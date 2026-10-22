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
/// Paying all or part of the table's bill (blueprint 3.2, 7.x): the server works out the
/// guest's share (the rest, equal shares, their items or an amount), applies tip and promotion,
/// reserves that share so nobody else can pay it too, hands the guest to the provider's hosted
/// page, and only marks the payment paid when the provider's signed webhook says so.
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
        var existing = await FindRequestAsync(request, ct);
        if (existing is not null && existing.Status != PaymentStatus.Created) return await ToDtoAsync(existing, ct);

        if (!request.AcceptTerms)
            throw AppException.BadRequest("terms_required", "Please accept the Terms and Conditions to pay.");
        if (!provider.SupportedMethods.Contains(request.Method))
            throw AppException.BadRequest("method_unavailable", "That payment method isn't available here.");

        var table = await bills.LoadTableAsync(request.SessionId, ct);
        var venue = table.Venue;
        if (request.TipMinor > 0 && !venue.TipsEnabled)
            throw AppException.BadRequest("tips_disabled", "This venue doesn't take tips through the app.");

        // Reserve this guest's share. If another guest changes the bill at the same moment, the
        // check's revision makes one save fail, and that guest's numbers are worked out again.
        var payment = await Concurrency.RetryAsync(db, async () =>
        {
            if (await FindRequestAsync(request, ct) is { } again) return again;

            var check = await bills.OpenCheckAsync(table.Location.Id, tracked: true, ct)
                ?? throw AppException.Conflict("nothing_to_pay", "There's nothing left to pay on this bill.",
                    await bills.GetForSessionAsync(request.SessionId, ct));
            var state = await bills.LoadStateAsync(check, ct, trackClaims: true);
            BillDto Bill() => bills.ToDto(state, request.SessionId, venue, table.Location);

            if (state.Orders.Count == 0 || state.OpenMinor <= 0)
                throw AppException.Conflict("nothing_to_pay", "There's nothing left to pay on this bill.", Bill());

            var available = state.AvailableFor(request.SessionId);
            var held = state.HeldBy(request.SessionId);
            var principal = ShareOf(request, state, available, held, Bill);

            if (principal <= 0)
                throw AppException.Conflict("payment_in_progress", "Someone at your table is paying the rest of the bill right now. Please wait a moment.", Bill());
            if (principal != request.ExpectedAmountMinor)
                throw AppException.Conflict("bill_changed", "The bill has changed. Please check the new amount.", Bill());

            Guid? promoId = null;
            string? promoCode = null;
            long discount = 0;
            if (!string.IsNullOrWhiteSpace(request.PromoCode))
            {
                var (promo, evaluation) = await promotions.EvaluateAsync(request.PromoCode, principal, ct);
                (promoId, promoCode, discount) = (promo.Id, promo.Code, evaluation.DiscountMinor);
            }

            Payment created;
            try
            {
                created = new Payment(venue.TenantId, venue.Id, check.Id, request.SessionId, request.ClientRequestId,
                    provider.Name, request.Method, request.Mode, request.Shares, check.SplitPeople, check.Currency,
                    principal, discount, request.TipMinor, promoId, promoCode, paymentOptions.Value.TermsVersion, state.Now);
            }
            catch (DomainException ex)
            {
                throw AppException.BadRequest("invalid_payment", ex.Message);
            }

            if (request.Mode == PaymentMode.Items)
                foreach (var claim in held) claim.AttachPayment(created.Id, created.PayableUntil);
            else
                db.ItemClaims.RemoveRange(held); // paying an amount instead of the items this guest had picked

            check.Touch();
            db.Payments.Add(created);
            await db.SaveChangesAsync(ct);
            return created;
        });

        if (payment.Status != PaymentStatus.Created) return await ToDtoAsync(payment, ct);

        var guestApp = guestOptions.Value.AppBaseUrl.TrimEnd('/');
        var checkout = await provider.CreateCheckoutAsync(new ProviderCheckoutRequest(
            payment.Id, payment.Reference, payment.AmountMinor, payment.Currency, payment.Method,
            $"{venue.Tenant.DisplayName} · {venue.Name}",
            ReturnUrl: $"{guestApp}/pay/{payment.Id}",
            CancelUrl: $"{guestApp}/pay/{payment.Id}?cancelled=1"), ct);

        payment.AttachProvider(checkout.ProviderPaymentId, checkout.RedirectUrl, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await notifier.TableBillChangedAsync(payment.TenantId, table.Location.Id, "payment", ct);
        return await ToDtoAsync(payment, ct);
    }

    /// <summary>How much of the bill this request pays, before discount and tip.</summary>
    private static long ShareOf(CreatePaymentRequest request, CheckState state, long available, IReadOnlyList<ItemClaim> held, Func<BillDto> bill)
    {
        switch (request.Mode)
        {
            case PaymentMode.Full:
                return available;

            case PaymentMode.Custom:
                if (request.AmountMinor is not { } amount)
                    throw AppException.BadRequest("amount_required", "Enter the amount you want to pay.");
                if (amount > available)
                    throw AppException.Conflict("amount_too_high", "That's more than is left to pay.", bill());
                return amount;

            case PaymentMode.Equal:
                if (!state.CanSplitEquallyFor(request.SessionId))
                    throw AppException.Conflict("equal_unavailable", "Your table is paying by item, so the bill can't be split equally.", bill());
                if (state.SharesLeft is not { } left)
                    throw AppException.Conflict("split_not_set", "Choose how many people are splitting the bill first.", bill());
                if (request.Shares is not { } shares || shares > left)
                    throw AppException.Conflict("bill_changed", left switch
                    {
                        0 => "Every share has been paid or is being paid.",
                        1 => "Only one share is left to pay.",
                        _ => $"Only {left} shares are left to pay."
                    }, bill());
                return BillAllocation.EqualShare(available, left, shares);

            case PaymentMode.Items:
                // Items already held stay payable even if someone has since paid an amount: they were reserved.
                if (held.Count == 0)
                    throw AppException.Conflict("no_items_selected", "Your selection has expired. Select the items you're paying for again.", bill());
                return held.Sum(c => c.AmountMinor);

            default:
                throw AppException.BadRequest("invalid_payment", "Choose how you want to pay.");
        }
    }

    public async Task<PaymentDto> GetAsync(Guid sessionId, Guid paymentId, CancellationToken ct) =>
        await ToDtoAsync(await FindForSessionAsync(sessionId, paymentId, ct), ct);

    /// <summary>The guest came back from the provider's page without paying.</summary>
    public async Task<PaymentDto> CancelAsync(Guid sessionId, Guid paymentId, CancellationToken ct)
    {
        var payment = await FindForSessionAsync(sessionId, paymentId, ct);
        var wasFinal = payment.IsFinal;
        payment.Cancel(clock.GetUtcNow());
        if (!wasFinal) await ReturnItemsAsync(payment, ct);
        await db.SaveChangesAsync(ct);

        if (!wasFinal)
            await notifier.TableBillChangedAsync(payment.TenantId, (await LocationAsync(payment, ct)).Id, "payment", ct);
        return await ToDtoAsync(payment, ct);
    }

    /// <summary>
    /// Whether the provider may still complete this payment. The sandbox page asks before taking
    /// a card, so a payment that was cancelled or has lapsed can't go through.
    /// </summary>
    public async Task<bool> IsAwaitingProviderAsync(string providerName, string providerPaymentId, CancellationToken ct)
    {
        var payment = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(p => p.Provider == providerName && p.ProviderPaymentId == providerPaymentId, ct);
        return payment is not null && payment.IsInFlight(clock.GetUtcNow());
    }

    /// <summary>
    /// Applies a provider webhook (authoritative, blueprint 7.1). Unsigned or tampered events are
    /// rejected; repeated events are skipped; a success writes the ledger, marks the items paid,
    /// counts the promotion, closes the bill when nothing is left, and tells staff and the table.
    /// A failure gives the guest their items back to try again.
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
        if (webhook.ProviderRefundId is { } providerRefundId)
        {
            await ApplyRefundAsync(webhook, providerRefundId, now, ct);
            return;
        }

        var payment = await db.Payments.IgnoreQueryFilters()
            .SingleOrDefaultAsync(p => p.Provider == provider.Name && p.ProviderPaymentId == webhook.ProviderPaymentId, ct);

        db.ProcessedWebhooks.Add(new ProcessedWebhook(provider.Name, webhook.EventId, webhook.ProviderPaymentId, webhook.Status.ToString(), now));
        var wasFinal = payment?.IsFinal ?? true;
        var succeeded = payment?.Apply(webhook.Status, webhook.CardBrand, webhook.CardLast4, webhook.FailureReason, now) ?? false;
        var failed = !wasFinal && payment!.Status is PaymentStatus.Failed or PaymentStatus.Cancelled;

        if (succeeded)
        {
            db.LedgerEntries.AddRange(LedgerEntry.ForSucceededPayment(payment!, now));
            // If the payment arrived so late that its items had already gone to someone else, the
            // ledger still records the money; the bill can't go below zero and the extra is refunded.
            foreach (var claim in await ClaimsOfAsync(payment!, ct)) claim.MarkPaid();
            if (payment!.PromoCodeId is { } promoId)
            {
                var promo = await db.PromoCodes.IgnoreQueryFilters().SingleAsync(p => p.Id == promoId, ct);
                promo.Redeem();
            }
        }
        else if (failed)
        {
            await ReturnItemsAsync(payment!, ct);
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

        if (failed)
        {
            await notifier.TableBillChangedAsync(payment!.TenantId, (await LocationAsync(payment, ct)).Id, "payment", ct);
            return;
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
        var itemCount = await db.ItemClaims.IgnoreQueryFilters().CountAsync(c => c.PaymentId == payment.Id, ct);
        await notifier.PaymentSucceededAsync(new PaymentNotice(
            payment.TenantId, payment.VenueId, location.Id, location.Type.ToString(), location.Label, location.DiningArea?.Name,
            payment.Id, payment.Reference, payment.Currency, payment.AmountMinor, payment.TipMinor,
            totals.RemainingMinor, totals.RemainingMinor == 0,
            payment.Mode, payment.Shares, payment.SplitPeople, itemCount), ct);
    }

    /// <summary>The provider's verdict on a refund: a success writes the refund's own ledger entry (blueprint 7.2).</summary>
    private async Task ApplyRefundAsync(ProviderWebhookEvent webhook, string providerRefundId, DateTimeOffset now, CancellationToken ct)
    {
        var refund = await db.Refunds.IgnoreQueryFilters()
            .SingleOrDefaultAsync(r => r.Provider == provider.Name && r.ProviderRefundId == providerRefundId, ct);

        db.ProcessedWebhooks.Add(new ProcessedWebhook(provider.Name, webhook.EventId, webhook.ProviderPaymentId, $"Refund{webhook.Status}", now));
        var wasFinal = refund?.IsFinal ?? true;
        var status = webhook.Status == PaymentStatus.Succeeded ? RefundStatus.Succeeded : RefundStatus.Failed;
        var succeeded = refund?.Apply(status, webhook.FailureReason, now) ?? false;
        if (succeeded) db.LedgerEntries.Add(LedgerEntry.ForRefund(refund!, now));

        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            if (await IsDuplicateAsync(webhook.EventId, ct)) return;
            throw;
        }

        if (refund is not null && !wasFinal)
        {
            var reference = await db.Payments.IgnoreQueryFilters().Where(p => p.Id == refund.PaymentId).Select(p => p.Reference).SingleAsync(ct);
            await notifier.RefundUpdatedAsync(new RefundNotice(refund.TenantId, refund.VenueId, refund.PaymentId, refund.Id, reference,
                refund.Currency, refund.AmountMinor, refund.Status), ct);
        }
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
        var claims = await db.ItemClaims.AsNoTracking().Where(c => c.PaymentId == payment.Id).ToListAsync(ct);

        await email.SendAsync(Receipt.Build(to, venue, location, payment, orders, claims), ct);
    }

    private Task<Payment?> FindRequestAsync(CreatePaymentRequest request, CancellationToken ct) =>
        db.Payments.SingleOrDefaultAsync(p => p.GuestSessionId == request.SessionId && p.ClientRequestId == request.ClientRequestId, ct);

    private async Task<List<ItemClaim>> ClaimsOfAsync(Payment payment, CancellationToken ct) =>
        payment.Mode == PaymentMode.Items
            ? await db.ItemClaims.IgnoreQueryFilters().Where(c => c.PaymentId == payment.Id).ToListAsync(ct)
            : [];

    /// <summary>The payment didn't go through: the guest keeps their items selected to try again.</summary>
    private async Task ReturnItemsAsync(Payment payment, CancellationToken ct)
    {
        foreach (var claim in await ClaimsOfAsync(payment, ct)) claim.ReturnToHold(clock.GetUtcNow());
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

        var itemCount = p.Mode == PaymentMode.Items
            ? await db.ItemClaims.IgnoreQueryFilters().CountAsync(c => c.PaymentId == p.Id, ct)
            : 0;

        var canReview = false;
        int? rating = null;
        if (p.Status == PaymentStatus.Succeeded)
        {
            rating = await db.Reviews.IgnoreQueryFilters().Where(r => r.PaymentId == p.Id).Select(r => (int?)r.Rating).SingleOrDefaultAsync(ct);
            canReview = rating is null && await db.Venues.IgnoreQueryFilters().AnyAsync(v => v.Id == p.VenueId && v.ReviewsEnabled, ct);
        }

        return new PaymentDto(p.Id, p.Reference, p.Status, p.Method, p.Mode, p.Shares, p.SplitPeople, itemCount,
            p.Currency, p.BillAmountMinor, p.DiscountMinor, p.TipMinor, p.AmountMinor, p.PromoCode, p.CardBrand, p.CardLast4,
            p.FailureReason, p.Status == PaymentStatus.RequiresAction ? p.CheckoutUrl : null,
            location.Type.ToString(), location.Label, location.DiningArea?.Name,
            p.CreatedAt, p.SucceededAt, remaining, fullyPaid, offer, canReview, rating);
    }
}

/// <summary>
/// The receipt e-mail: venue, table, reference, what this payment covered and what was paid.
/// Paying by item lists those items; otherwise it lists the bill and the guest's part of it.
/// </summary>
internal static class Receipt
{
    public static EmailMessage Build(string to, Venue venue, ServiceLocation location, Payment payment,
        IReadOnlyList<Order> orders, IReadOnlyList<ItemClaim> paidItems)
    {
        string Money(long minor) => FormatMoney(minor, payment.Currency);
        var when = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var zone)
            ? TimeZoneInfo.ConvertTime(payment.SucceededAt ?? payment.CreatedAt, zone)
            : payment.SucceededAt ?? payment.CreatedAt;
        var place = $"{location.DisplayName}{(location.DiningArea is { } a ? $" · {a.Name}" : "")}";
        var method = payment.CardLast4 is { } last4 ? $"{payment.CardBrand ?? payment.Method.ToString()} •••• {last4}" : payment.Method.ToString();
        var lines = orders.OrderBy(o => o.Number).SelectMany(o => o.Lines.OrderBy(l => l.Position)).ToList();

        var rows = new List<(string Label, string Value)>();
        var totals = new List<(string Label, string Value)>();
        if (payment.Mode == PaymentMode.Items)
        {
            foreach (var group in paidItems.GroupBy(c => c.OrderLineId))
            {
                var name = lines.FirstOrDefault(l => l.Id == group.Key)?.Name ?? "Item";
                rows.Add(($"{group.Count()} × {name}", Money(group.Sum(c => c.AmountMinor))));
            }
            totals.Add(("Your items (incl. service and VAT)", Money(payment.BillAmountMinor)));
        }
        else
        {
            foreach (var line in lines)
                rows.Add(($"{line.Quantity} × {line.Name}", Money(line.LineTotalMinor)));

            // What the bill is made of, so the receipt shows the service charge and VAT.
            totals.Add(("Subtotal", Money(orders.Sum(o => o.SubtotalMinor))));
            var service = orders.Sum(o => o.ServiceChargeMinor);
            var tax = orders.Sum(o => o.TaxMinor);
            if (service > 0) totals.Add(("Service charge", Money(service)));
            if (tax > 0) totals.Add((orders.All(o => o.TaxIncluded) ? "VAT (included)" : "VAT", Money(tax)));
            var billTotal = orders.Sum(o => o.TotalMinor);
            totals.Add(("Bill total", Money(billTotal)));
            if (payment.BillAmountMinor != billTotal)
            {
                var share = payment.Mode switch
                {
                    PaymentMode.Equal => $"Your share ({payment.Shares} of {payment.SplitPeople} people)",
                    PaymentMode.Custom => "Your amount",
                    _ => "Your part of the bill"
                };
                totals.Add((share, Money(payment.BillAmountMinor)));
            }
        }
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
