using System.Globalization;
using System.Net;
using System.Text;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Bills;
using DineFlow.Application.Common;
using DineFlow.Application.Promotions;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Common;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Payments;

/// <summary>
/// Paying the table's bill (blueprint 3.2 "Pay full", 7.x): the server checks what's owed, applies
/// tip and promotion, hands the guest to the provider's hosted page, and only marks the payment
/// paid when the provider's signed webhook says so.
/// </summary>
public class PaymentService(
    IAppDbContext db,
    BillService bills,
    PromotionService promotions,
    IPaymentProvider provider,
    IPaymentNotifier notifier,
    IEmailSender email,
    IOptions<PaymentOptions> paymentOptions,
    IOptions<GuestOptions> guestOptions,
    TimeProvider clock)
{
    public async Task<PaymentDto> CreateAsync(CreatePaymentRequest request, CancellationToken ct)
    {
        // A retry of a request that already got through returns the same payment and page.
        var existing = await db.Payments.SingleOrDefaultAsync(
            p => p.GuestSessionId == request.SessionId && p.ClientRequestId == request.ClientRequestId, ct);
        if (existing is not null) return await ToDtoAsync(existing, ct);

        if (!request.AcceptTerms)
            throw AppException.BadRequest("terms_required", "Please accept the Terms and Conditions to pay.");
        if (!provider.SupportedMethods.Contains(request.Method))
            throw AppException.BadRequest("method_unavailable", "That payment method isn't available here.");

        var table = await bills.LoadTableAsync(request.SessionId, ct);
        var bill = await bills.GetForSessionAsync(request.SessionId, ct);
        if (bill.Status != BillStatus.Open || bill.CheckId is not { } checkId || bill.RemainingMinor <= 0)
            throw AppException.Conflict("nothing_to_pay", "There's nothing left to pay on this bill.", bill);
        if (bill.RemainingMinor != request.ExpectedRemainingMinor)
            throw AppException.Conflict("bill_changed", "The bill has changed. Please check the new total.", bill);
        if (bill.PaymentInProgress)
            throw AppException.Conflict("payment_in_progress", "Someone at your table is already paying. Please wait a moment.", bill);

        var venue = table.Venue;
        if (request.TipMinor > 0 && !venue.TipsEnabled)
            throw AppException.BadRequest("tips_disabled", "This venue doesn't take tips through the app.");

        Guid? promoId = null;
        string? promoCode = null;
        long discount = 0;
        if (!string.IsNullOrWhiteSpace(request.PromoCode))
        {
            var (promo, evaluation) = await promotions.EvaluateAsync(request.PromoCode, bill.RemainingMinor, ct);
            (promoId, promoCode, discount) = (promo.Id, promo.Code, evaluation.DiscountMinor);
        }

        var now = clock.GetUtcNow();
        Payment payment;
        try
        {
            payment = new Payment(venue.TenantId, venue.Id, checkId, request.SessionId, request.ClientRequestId,
                provider.Name, request.Method, bill.Currency, bill.RemainingMinor, discount, request.TipMinor,
                promoId, promoCode, paymentOptions.Value.TermsVersion, now);
        }
        catch (DomainException ex)
        {
            throw AppException.BadRequest("invalid_payment", ex.Message);
        }

        db.Payments.Add(payment);
        await db.SaveChangesAsync(ct);

        // Two guests can get past the check above at the same moment; the earlier one wins.
        var inFlightSince = now - Payment.InFlightLifetime;
        var earlier = await db.Payments.AnyAsync(p => p.CheckId == checkId && p.Id != payment.Id
            && (p.Status == PaymentStatus.Created || p.Status == PaymentStatus.RequiresAction || p.Status == PaymentStatus.Processing)
            && p.CreatedAt > inFlightSince && p.CreatedAt <= payment.CreatedAt, ct);
        if (earlier)
        {
            payment.Cancel(now);
            await db.SaveChangesAsync(ct);
            throw AppException.Conflict("payment_in_progress", "Someone at your table is already paying. Please wait a moment.");
        }

        var guestApp = guestOptions.Value.AppBaseUrl.TrimEnd('/');
        var checkout = await provider.CreateCheckoutAsync(new ProviderCheckoutRequest(
            payment.Id, payment.Reference, payment.AmountMinor, payment.Currency, payment.Method,
            $"{venue.Tenant.DisplayName} · {venue.Name}",
            ReturnUrl: $"{guestApp}/pay/{payment.Id}",
            CancelUrl: $"{guestApp}/pay/{payment.Id}?cancelled=1"), ct);

        payment.AttachProvider(checkout.ProviderPaymentId, checkout.RedirectUrl, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(payment, ct);
    }

    public async Task<PaymentDto> GetAsync(Guid sessionId, Guid paymentId, CancellationToken ct) =>
        await ToDtoAsync(await FindForSessionAsync(sessionId, paymentId, ct), ct);

    /// <summary>The guest came back from the provider's page without paying.</summary>
    public async Task<PaymentDto> CancelAsync(Guid sessionId, Guid paymentId, CancellationToken ct)
    {
        var payment = await FindForSessionAsync(sessionId, paymentId, ct);
        payment.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(payment, ct);
    }

    /// <summary>
    /// Applies a provider webhook (authoritative, blueprint 7.1). Unsigned or tampered events are
    /// rejected; repeated events are skipped; a success writes the ledger, counts the promotion,
    /// closes the bill when nothing is left, and tells staff and the table.
    /// </summary>
    public async Task HandleWebhookAsync(string providerName, string body, IReadOnlyDictionary<string, string> headers, CancellationToken ct)
    {
        if (!string.Equals(providerName, provider.Name, StringComparison.OrdinalIgnoreCase))
            throw AppException.NotFound("provider_not_found", "Unknown payment provider.");

        ProviderWebhookEvent webhook;
        try { webhook = provider.ParseWebhook(body, headers); }
        catch (InvalidWebhookException ex) { throw AppException.BadRequest("invalid_webhook", ex.Message); }

        // Webhooks carry no tenant, so these lookups bypass the tenant filter.
        if (await db.ProcessedWebhooks.AnyAsync(w => w.Provider == provider.Name && w.EventId == webhook.EventId, ct)) return;

        var now = clock.GetUtcNow();
        var payment = await db.Payments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.Provider == provider.Name && p.ProviderPaymentId == webhook.ProviderPaymentId, ct);

        db.ProcessedWebhooks.Add(new ProcessedWebhook(provider.Name, webhook.EventId, webhook.ProviderPaymentId, webhook.Status.ToString(), now));
        var succeeded = payment?.Apply(webhook.Status, webhook.CardBrand, webhook.CardLast4, webhook.FailureReason, now) ?? false;

        if (succeeded)
        {
            db.LedgerEntries.AddRange(LedgerEntry.ForSucceededPayment(payment!, now));
            if (payment!.PromoCodeId is { } promoId)
            {
                var promo = await db.PromoCodes.IgnoreQueryFilters().SingleAsync(p => p.Id == promoId, ct);
                promo.Redeem();
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await IsDuplicateAsync(webhook.EventId, ct)) return; // handled in parallel; that one won
            throw;
        }

        if (!succeeded) return;

        var totals = await bills.TotalsAsync(payment!.CheckId, ct);
        if (totals.RemainingMinor == 0)
        {
            var check = await db.Checks.IgnoreQueryFilters().SingleAsync(c => c.Id == payment.CheckId, ct);
            check.Close(now);
            await db.SaveChangesAsync(ct);
        }

        var location = await LocationAsync(payment, ct);
        await notifier.PaymentSucceededAsync(new PaymentNotice(
            payment.TenantId, payment.VenueId, location.Id, location.Type.ToString(), location.Label, location.DiningArea?.Name,
            payment.Id, payment.Reference, payment.Currency, payment.AmountMinor, payment.TipMinor,
            totals.RemainingMinor, totals.RemainingMinor == 0), ct);
    }

    /// <summary>E-mails the receipt for a successful payment (P1-REC-01/02).</summary>
    public async Task SendReceiptAsync(Guid sessionId, Guid paymentId, string to, CancellationToken ct)
    {
        var payment = await FindForSessionAsync(sessionId, paymentId, ct);
        if (payment.Status != PaymentStatus.Succeeded)
            throw AppException.Conflict("not_paid", "A receipt is available once the payment has gone through.");

        var venue = await db.Venues.Include(v => v.Tenant).SingleAsync(v => v.Id == payment.VenueId, ct);
        var location = await LocationAsync(payment, ct);
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CheckId == payment.CheckId && o.Status != OrderStatus.Rejected)
            .Include(o => o.Lines).OrderBy(o => o.Number).ToListAsync(ct);

        await email.SendAsync(Receipt.Build(to, venue, location, payment, orders), ct);
    }

    private async Task<bool> IsDuplicateAsync(string eventId, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        return await db.ProcessedWebhooks.AnyAsync(w => w.Provider == provider.Name && w.EventId == eventId, ct);
    }

    private async Task<Payment> FindForSessionAsync(Guid sessionId, Guid paymentId, CancellationToken ct) =>
        await db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId && p.GuestSessionId == sessionId, ct)
        ?? throw AppException.NotFound("payment_not_found", "Payment not found.");

    private async Task<ServiceLocation> LocationAsync(Payment payment, CancellationToken ct)
    {
        var locationId = await db.Checks.IgnoreQueryFilters()
            .Where(c => c.Id == payment.CheckId).Select(c => c.ServiceLocationId).SingleAsync(ct);
        return await db.ServiceLocations.IgnoreQueryFilters().Include(l => l.DiningArea).SingleAsync(l => l.Id == locationId, ct);
    }

    private async Task<PaymentDto> ToDtoAsync(Payment p, CancellationToken ct)
    {
        var location = await LocationAsync(p, ct);
        long? remaining = null;
        bool? fullyPaid = null;
        NextVisitOfferDto? offer = null;

        if (p.Status == PaymentStatus.Succeeded)
        {
            var totals = await bills.TotalsAsync(p.CheckId, ct);
            remaining = totals.RemainingMinor;
            fullyPaid = totals.RemainingMinor == 0;
            offer = await promotions.NextVisitOfferAsync(ct);
        }

        return new PaymentDto(p.Id, p.Reference, p.Status, p.Method, p.Currency, p.BillAmountMinor, p.DiscountMinor,
            p.TipMinor, p.AmountMinor, p.PromoCode, p.CardBrand, p.CardLast4, p.FailureReason,
            p.Status == PaymentStatus.RequiresAction ? p.CheckoutUrl : null,
            location.Type.ToString(), location.Label, location.DiningArea?.Name,
            p.CreatedAt, p.SucceededAt, remaining, fullyPaid, offer);
    }
}

/// <summary>The receipt e-mail: venue, table, reference, what was ordered and what was paid.</summary>
internal static class Receipt
{
    public static EmailMessage Build(string to, Venue venue, ServiceLocation location, Payment payment, IReadOnlyList<Order> orders)
    {
        string Money(long minor) => FormatMoney(minor, payment.Currency);
        var when = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(payment.SucceededAt ?? payment.CreatedAt, zone)
            : payment.SucceededAt ?? payment.CreatedAt;
        var place = $"{location.DisplayName}{(location.DiningArea is { } a ? $" · {a.Name}" : "")}";
        var method = payment.CardLast4 is { } last4 ? $"{payment.CardBrand ?? payment.Method.ToString()} •••• {last4}" : payment.Method.ToString();

        var rows = new List<(string Label, string Value)>();
        foreach (var line in orders.SelectMany(o => o.Lines))
            rows.Add(($"{line.Quantity} × {line.Name}", Money(line.LineTotalMinor)));

        // What the bill is made of, so the receipt shows the service charge and VAT.
        var totals = new List<(string Label, string Value)> { ("Subtotal", Money(orders.Sum(o => o.SubtotalMinor))) };
        var service = orders.Sum(o => o.ServiceChargeMinor);
        var tax = orders.Sum(o => o.TaxMinor);
        if (service > 0) totals.Add(("Service charge", Money(service)));
        if (tax > 0) totals.Add((orders.All(o => o.TaxIncluded) ? "VAT (included)" : "VAT", Money(tax)));
        totals.Add(("Bill", Money(payment.BillAmountMinor)));
        if (payment.DiscountMinor > 0) totals.Add(($"Promotion {payment.PromoCode}", $"−{Money(payment.DiscountMinor)}"));
        if (payment.TipMinor > 0) totals.Add(("Tip", Money(payment.TipMinor)));
        totals.Add(("Paid", Money(payment.AmountMinor)));

        var text = new StringBuilder()
            .AppendLine($"{venue.Tenant.DisplayName} · {venue.Name}")
            .AppendLine(place)
            .AppendLine($"Receipt {payment.Reference} · {when:dd MMM yyyy HH:mm}")
            .AppendLine();
        foreach (var (label, value) in rows) text.AppendLine($"{label,-40} {value}");
        text.AppendLine();
        foreach (var (label, value) in totals) text.AppendLine($"{label,-40} {value}");
        text.AppendLine().AppendLine($"Paid with {method}. Includes all taxes and charges.");

        static string E(string s) => WebUtility.HtmlEncode(s);
        static string Rows(IEnumerable<(string Label, string Value)> items, bool bold = false) => string.Concat(items.Select(r =>
            $"<tr><td style=\"padding:4px 0\">{E(r.Label)}</td><td style=\"padding:4px 0;text-align:right{(bold ? ";font-weight:700" : "")}\">{E(r.Value)}</td></tr>"));

        var html = $"""
            <div style="font-family:Segoe UI,Arial,sans-serif;max-width:480px;color:#101828">
              <h2 style="margin:0">{E(venue.Tenant.DisplayName)} · {E(venue.Name)}</h2>
              <p style="color:#475467;margin:4px 0 16px">{E(place)} · Receipt {E(payment.Reference)} · {when:dd MMM yyyy HH:mm}</p>
              <table style="width:100%;border-collapse:collapse">{Rows(rows)}</table>
              <hr style="border:0;border-top:1px solid #E4E7EC">
              <table style="width:100%;border-collapse:collapse">{Rows(totals, bold: true)}</table>
              <p style="color:#475467">Paid with {E(method)}. Includes all taxes and charges.</p>
            </div>
            """;

        return new EmailMessage(to, $"Your receipt from {venue.Tenant.DisplayName} ({payment.Reference})", text.ToString(), html);
    }

    private static string FormatMoney(long minor, string currency)
    {
        var digits = currency is "KWD" or "BHD" or "OMR" or "JOD" ? 3 : 2;
        return $"{currency} {(minor / (decimal)Math.Pow(10, digits)).ToString($"N{digits}", CultureInfo.InvariantCulture)}";
    }
}
