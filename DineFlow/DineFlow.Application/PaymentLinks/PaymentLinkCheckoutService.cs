using DineFlow.Application.Abstractions;
using DineFlow.Application.Bills;
using DineFlow.Application.Common;
using DineFlow.Application.Payments;
using DineFlow.Domain.Common;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.PaymentLinks;

/// <summary>
/// The customer's side of a payment link (blueprint 4.2 "Payment Link Checkout", 9.1
/// /v1/public/payment-links/{token}). Whoever has the link can see it and pay it; there's no
/// table or session. The server works out what's owed, reserves it while the customer is on the
/// provider's page, and only the provider's webhook marks anything paid (see PaymentService).
/// </summary>
public class PaymentLinkCheckoutService(
    IAppDbContext db,
    IQrTokenService tokens,
    IPaymentProvider provider,
    IEmailSender email,
    IOptions<PaymentOptions> paymentOptions,
    IOptions<GuestOptions> guestOptions,
    TimeProvider clock)
{
    public async Task<PublicPaymentLinkDto> GetAsync(string token, CancellationToken ct)
    {
        var (link, venue) = await LoadAsync(token, ct);
        return ToDto(link, venue, await LinkTotals.LoadAsync(db, link, clock.GetUtcNow(), ct));
    }

    /// <summary>
    /// Starts paying what's left, equal shares or an amount. Safe to retry with the same
    /// clientRequestId. Returns the provider page to send the customer to (checkoutUrl), or 409
    /// with the current link in "details" when it changed, expired or was paid.
    /// </summary>
    public async Task<LinkPaymentDto> CreatePaymentAsync(string token, CreateLinkPaymentRequest request, CancellationToken ct)
    {
        var (found, venue) = await LoadAsync(token, ct);
        var existing = await FindRequestAsync(found.Id, request.ClientRequestId, ct);
        if (existing is not null && existing.Status != PaymentStatus.Created) return await ToPaymentDtoAsync(found, existing, ct);

        if (!request.AcceptTerms)
            throw AppException.BadRequest("terms_required", "Please accept the Terms and Conditions to pay.");
        if (!provider.SupportedMethods.Contains(request.Method))
            throw AppException.BadRequest("method_unavailable", "That payment method isn't available here.");
        if (request.Mode is not (PaymentMode.Full or PaymentMode.Equal or PaymentMode.Custom))
            throw AppException.BadRequest("invalid_payment", "Choose how you want to pay.");
        if (request.Mode != PaymentMode.Full && !found.AllowSplit)
            throw AppException.BadRequest("split_disabled", "This link has to be paid in full.");
        if (request.TipMinor > 0 && !TipsOffered(found, venue))
            throw AppException.BadRequest("tips_disabled", "This link doesn't take a tip.");

        // Reserve this customer's amount. If someone else pays the same link at the same moment,
        // the link's revision makes one save fail, and that customer's numbers are worked out again.
        var payment = await Concurrency.RetryAsync(db, async () =>
        {
            if (await FindRequestAsync(found.Id, request.ClientRequestId, ct) is { } again) return again;

            var link = await db.PaymentLinks.IgnoreQueryFilters().SingleAsync(l => l.Id == found.Id, ct);
            var now = clock.GetUtcNow();
            var totals = await LinkTotals.LoadAsync(db, link, now, ct);
            PublicPaymentLinkDto Current() => ToDto(link, venue, totals);

            switch (link.StatusAt(now))
            {
                case PaymentLinkStatus.Paid: throw AppException.Conflict("link_paid", "This link has already been paid in full.", Current());
                case PaymentLinkStatus.Expired: throw AppException.Conflict("link_expired", "This payment link has expired.", Current());
                case PaymentLinkStatus.Cancelled: throw AppException.Conflict("link_cancelled", "This payment link is no longer active.", Current());
            }

            var principal = AmountOf(request, link, totals, Current);
            if (principal <= 0)
                throw AppException.Conflict("payment_in_progress", "Someone is paying the rest of this link right now. Please wait a moment.", Current());
            if (principal != request.ExpectedAmountMinor)
                throw AppException.Conflict("amount_changed", "The amount to pay has changed. Please check the new amount.", Current());

            Payment created;
            try
            {
                created = Payment.ForLink(link, request.ClientRequestId, provider.Name, request.Method, request.Mode,
                    request.Mode == PaymentMode.Equal ? request.Shares : null, request.Mode == PaymentMode.Equal ? request.People : null,
                    principal, request.TipMinor, paymentOptions.Value.TermsVersion, now);
            }
            catch (DomainException ex)
            {
                throw AppException.BadRequest("invalid_payment", ex.Message);
            }

            link.Touch();
            db.Payments.Add(created);
            await db.SaveChangesAsync(ct);
            return created;
        });

        if (payment.Status != PaymentStatus.Created) return await ToPaymentDtoAsync(found, payment, ct);

        var page = $"{guestOptions.Value.AppBaseUrl.TrimEnd('/')}/l/{token}";
        var checkout = await provider.CreateCheckoutAsync(new ProviderCheckoutRequest(
            payment.Id, payment.Reference, payment.AmountMinor, payment.Currency, payment.Method,
            $"{venue.Tenant.DisplayName} · {venue.Name}",
            ReturnUrl: $"{page}?payment={payment.Id}",
            CancelUrl: $"{page}?payment={payment.Id}&cancelled=1"), ct);

        payment.AttachProvider(checkout.ProviderPaymentId, checkout.RedirectUrl, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await ToPaymentDtoAsync(found, payment, ct);
    }

    /// <summary>Payment status for the page the provider sends the customer back to; the app polls until it's final.</summary>
    public async Task<LinkPaymentDto> GetPaymentAsync(string token, Guid paymentId, CancellationToken ct)
    {
        var (link, _) = await LoadAsync(token, ct);
        return await ToPaymentDtoAsync(link, await FindPaymentAsync(link, paymentId, ct), ct);
    }

    /// <summary>The customer came back from the provider's page without paying; what they'd reserved is free again.</summary>
    public async Task<LinkPaymentDto> CancelPaymentAsync(string token, Guid paymentId, CancellationToken ct)
    {
        var (link, _) = await LoadAsync(token, ct);
        var payment = await FindPaymentAsync(link, paymentId, ct);
        payment.Cancel(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await ToPaymentDtoAsync(link, payment, ct);
    }

    /// <summary>E-mails the receipt for a successful payment (P1-REC-01).</summary>
    public async Task SendReceiptAsync(string token, Guid paymentId, string to, CancellationToken ct)
    {
        var (link, venue) = await LoadAsync(token, ct);
        var payment = await FindPaymentAsync(link, paymentId, ct);
        if (payment.Status != PaymentStatus.Succeeded)
            throw AppException.Conflict("not_paid", "A receipt is available once the payment has gone through.");

        await email.SendAsync(Receipt.BuildForLink(to, venue, link, payment), ct);
    }

    // ---- Helpers ----

    /// <summary>How much of the link this request pays, before tip.</summary>
    private static long AmountOf(CreateLinkPaymentRequest request, PaymentLink link, LinkTotals totals, Func<PublicPaymentLinkDto> current)
    {
        switch (request.Mode)
        {
            case PaymentMode.Custom:
                if (request.AmountMinor is not { } amount)
                    throw AppException.BadRequest("amount_required", "Enter the amount you want to pay.");
                if (amount > totals.AvailableMinor)
                    throw AppException.Conflict("amount_too_high", "That's more than is left to pay.", current());
                return amount;

            case PaymentMode.Equal:
                if (request.People is not { } people || request.Shares is not { } shares || people < 2 || shares < 1 || shares > people)
                    throw AppException.BadRequest("invalid_split", "Choose how many people are splitting and how many you're paying for.");
                return Math.Min(totals.AvailableMinor, LinkTotals.EqualShare(link.AmountMinor, people, shares));

            default:
                return totals.AvailableMinor;
        }
    }

    private static bool TipsOffered(PaymentLink link, Venue venue) => link.RequestTip && venue.TipsEnabled;

    /// <summary>The link behind a token. Public: there's no tenant yet, so this bypasses the tenant filter.</summary>
    private async Task<(PaymentLink Link, Venue Venue)> LoadAsync(string token, CancellationToken ct)
    {
        var hash = tokens.IsWellFormed(token) ? tokens.Hash(token) : null;
        var link = hash is null ? null
            : await db.PaymentLinks.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(l => l.TokenHash == hash, ct);
        if (link is null) throw AppException.NotFound("link_not_found", "This payment link isn't valid. Check you have the whole link.");

        var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking().Include(v => v.Tenant).SingleAsync(v => v.Id == link.VenueId, ct);
        if (!venue.IsActive)
            throw AppException.Conflict("venue_unavailable", "This venue isn't taking payments right now. Please contact them.");
        return (link, venue);
    }

    private Task<Payment?> FindRequestAsync(Guid linkId, Guid clientRequestId, CancellationToken ct) =>
        db.Payments.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.PaymentLinkId == linkId && p.ClientRequestId == clientRequestId, ct);

    private async Task<Payment> FindPaymentAsync(PaymentLink link, Guid paymentId, CancellationToken ct) =>
        await db.Payments.IgnoreQueryFilters().SingleOrDefaultAsync(p => p.Id == paymentId && p.PaymentLinkId == link.Id, ct)
        ?? throw AppException.NotFound("payment_not_found", "Payment not found.");

    private PublicPaymentLinkDto ToDto(PaymentLink link, Venue venue, LinkTotals totals)
    {
        var tips = TipsOffered(link, venue)
            ? new TipSettingsDto(true, venue.TipPresetsBasisPoints, venue.MostCommonTipBasisPoints)
            : new TipSettingsDto(false, [], null);
        var status = link.StatusAt(clock.GetUtcNow());
        var open = status == PaymentLinkStatus.Active;

        return new PublicPaymentLinkDto(link.Code, link.Reference, status,
            new LinkVenueDto(venue.Tenant.DisplayName, venue.Name, venue.LogoUrl, venue.BrandColor, venue.DefaultLocale),
            link.Currency, link.AmountMinor, totals.PaidMinor, totals.RemainingMinor, open ? totals.AvailableMinor : 0,
            open && totals.InFlightMinor > 0, link.AllowSplit, tips, provider.SupportedMethods,
            paymentOptions.Value.TermsVersion, paymentOptions.Value.TermsUrl, link.ExpiresAt);
    }

    private async Task<LinkPaymentDto> ToPaymentDtoAsync(PaymentLink link, Payment p, CancellationToken ct)
    {
        var fresh = await db.PaymentLinks.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == link.Id, ct);
        var totals = await LinkTotals.LoadAsync(db, fresh, clock.GetUtcNow(), ct);
        return new LinkPaymentDto(p.Id, p.Reference, p.Status, p.Method, p.Mode, p.Shares, p.SplitPeople, p.Currency,
            p.BillAmountMinor, p.TipMinor, p.AmountMinor, p.CardBrand, p.CardLast4, p.FailureReason,
            p.Status == PaymentStatus.RequiresAction ? p.CheckoutUrl : null, p.CreatedAt, p.SucceededAt,
            totals.RemainingMinor, totals.RemainingMinor == 0);
    }
}
