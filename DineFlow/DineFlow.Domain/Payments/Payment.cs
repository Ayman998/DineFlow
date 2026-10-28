using DineFlow.Domain.Common;
using DineFlow.Domain.Venues;

namespace DineFlow.Domain.Payments;

/// <summary>
/// One guest payment against a table's check, or against a payment link (blueprint 7.1, E16).
/// The provider's verified webhook is the only thing that makes it <see cref="PaymentStatus.Succeeded"/>;
/// the app never trusts the browser for that. Terminal states never go back, so late or
/// duplicate webhooks are harmless.
/// </summary>
/// <remarks>
/// Amounts: <see cref="BillAmountMinor"/> is how much of the bill this settles,
/// <see cref="DiscountMinor"/> is the promotion's share of that, and the guest is charged
/// <see cref="AmountMinor"/> = bill amount − discount + tip.
/// </remarks>
public class Payment : Entity, ITenantOwned
{
    /// <summary>How long an unfinished payment keeps its part of the bill reserved.</summary>
    public static readonly TimeSpan InFlightLifetime = TimeSpan.FromMinutes(15);

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    /// <summary>The table's bill this pays; null for a payment-link payment.</summary>
    public Guid? CheckId { get; private set; }
    /// <summary>The guest at the table who paid; null for a payment-link payment.</summary>
    public Guid? GuestSessionId { get; private set; }
    /// <summary>The payment link this pays; null for a payment at the table.</summary>
    public Guid? PaymentLinkId { get; private set; }
    /// <summary>The guest app's id for this attempt; retries return the same payment (blueprint 7.3).</summary>
    public Guid ClientRequestId { get; private set; }
    /// <summary>Short reference shown on the receipt and the staff screen, e.g. DF-1A2B3C4D.</summary>
    public string Reference { get; private set; } = string.Empty;

    public string Provider { get; private set; } = string.Empty;
    public string? ProviderPaymentId { get; private set; }
    /// <summary>The provider's hosted payment page; returned again if the guest's request is retried.</summary>
    public string? CheckoutUrl { get; private set; }
    public PaymentMethod Method { get; private set; }
    /// <summary>How the guest chose what to pay: the rest of the bill, equal shares, items or an amount.</summary>
    public PaymentMode Mode { get; private set; }
    /// <summary>Equal split: how many shares this pays, out of <see cref="SplitPeople"/>.</summary>
    public int? Shares { get; private set; }
    public int? SplitPeople { get; private set; }

    public string Currency { get; private set; } = string.Empty;
    public long BillAmountMinor { get; private set; }
    public long DiscountMinor { get; private set; }
    public long TipMinor { get; private set; }
    public long AmountMinor { get; private set; }
    public Guid? PromoCodeId { get; private set; }
    public string? PromoCode { get; private set; }

    /// <summary>Terms and Conditions version the guest accepted by paying (P1-PAY-13).</summary>
    public string TermsVersion { get; private set; } = string.Empty;

    public PaymentStatus Status { get; private set; } = PaymentStatus.Created;
    /// <summary>Tokenised card metadata from the provider only; never the full number (P1-PAY-12).</summary>
    public string? CardBrand { get; private set; }
    public string? CardLast4 { get; private set; }
    public string? FailureReason { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? SucceededAt { get; private set; }

    private Payment() { }

    /// <summary>A guest at the table paying all or part of its check.</summary>
    public Payment(
        Guid tenantId, Guid venueId, Guid checkId, Guid guestSessionId, Guid clientRequestId,
        string provider, PaymentMethod method, PaymentMode mode, int? shares, int? splitPeople, string currency,
        long billAmountMinor, long discountMinor, long tipMinor,
        Guid? promoCodeId, string? promoCode, string termsVersion, DateTimeOffset now)
        : this(tenantId, venueId, clientRequestId, provider, method, mode, shares, splitPeople, currency,
            billAmountMinor, discountMinor, tipMinor, promoCodeId, promoCode, termsVersion, now)
    {
        CheckId = checkId;
        GuestSessionId = guestSessionId;
    }

    /// <summary>A customer paying all or part of a payment link. Links take no promotion codes and can't be paid by item.</summary>
    public static Payment ForLink(
        PaymentLink link, Guid clientRequestId, string provider, PaymentMethod method, PaymentMode mode, int? shares, int? splitPeople,
        long amountMinor, long tipMinor, string termsVersion, DateTimeOffset now)
    {
        if (mode == PaymentMode.Items) throw new DomainException("A payment link can't be paid by item.");
        return new Payment(link.TenantId, link.VenueId, clientRequestId, provider, method, mode, shares, splitPeople, link.Currency,
            amountMinor, 0, tipMinor, null, null, termsVersion, now) { PaymentLinkId = link.Id };
    }

    private Payment(
        Guid tenantId, Guid venueId, Guid clientRequestId,
        string provider, PaymentMethod method, PaymentMode mode, int? shares, int? splitPeople, string currency,
        long billAmountMinor, long discountMinor, long tipMinor,
        Guid? promoCodeId, string? promoCode, string termsVersion, DateTimeOffset now)
    {
        if (billAmountMinor <= 0) throw new DomainException("There's nothing to pay.");
        if (mode == PaymentMode.Equal && (shares is not > 0 || splitPeople is not > 1 || shares > splitPeople))
            throw new DomainException("An equal-split payment needs its shares and the number of people.");
        if (discountMinor < 0 || discountMinor > billAmountMinor) throw new DomainException("The discount can't be more than the bill.");
        if (tipMinor < 0) throw new DomainException("The tip can't be negative.");
        if (tipMinor > Venue.MaxTipMinor(billAmountMinor)) throw new DomainException("The tip can't be more than 20% of the bill.");

        TenantId = tenantId;
        VenueId = venueId;
        ClientRequestId = clientRequestId;
        Reference = $"DF-{Id.ToString("N")[..8].ToUpperInvariant()}";
        Provider = provider;
        Method = method;
        Mode = mode;
        Shares = mode == PaymentMode.Equal ? shares : null;
        SplitPeople = mode == PaymentMode.Equal ? splitPeople : null;
        Currency = currency;
        BillAmountMinor = billAmountMinor;
        DiscountMinor = discountMinor;
        TipMinor = tipMinor;
        AmountMinor = billAmountMinor - discountMinor + tipMinor;
        PromoCodeId = promoCodeId;
        PromoCode = promoCode;
        TermsVersion = termsVersion;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public bool IsFinal => Status is PaymentStatus.Succeeded or PaymentStatus.Failed or PaymentStatus.Cancelled;

    /// <summary>Started but not finished, and recent enough to still be completed.</summary>
    public bool IsInFlight(DateTimeOffset now) => !IsFinal && now - CreatedAt < InFlightLifetime;

    /// <summary>The last moment this payment can still complete.</summary>
    public DateTimeOffset PayableUntil => CreatedAt + InFlightLifetime;

    public void AttachProvider(string providerPaymentId, string checkoutUrl, DateTimeOffset now)
    {
        ProviderPaymentId = providerPaymentId;
        CheckoutUrl = checkoutUrl;
        Status = PaymentStatus.RequiresAction;
        UpdatedAt = now;
    }

    /// <summary>Applies a provider status update. Returns true only when this call made the payment succeed.</summary>
    public bool Apply(PaymentStatus status, string? cardBrand, string? last4, string? failureReason, DateTimeOffset now)
    {
        if (IsFinal || status == Status) return false;   // terminal states stick; repeats are no-ops
        if (status is PaymentStatus.Created) return false; // providers never move a payment backwards

        Status = status;
        UpdatedAt = now;
        CardBrand = cardBrand ?? CardBrand;
        CardLast4 = last4 ?? CardLast4;

        if (status == PaymentStatus.Failed) FailureReason = failureReason ?? "The payment was declined.";
        if (status != PaymentStatus.Succeeded) return false;

        SucceededAt = now;
        return true;
    }

    /// <summary>The guest left the payment page without paying.</summary>
    public void Cancel(DateTimeOffset now)
    {
        if (IsFinal) return;
        Status = PaymentStatus.Cancelled;
        UpdatedAt = now;
    }
}

public enum PaymentStatus
{
    /// <summary>Recorded here; the provider hasn't been asked yet.</summary>
    Created = 0,
    /// <summary>Waiting for the guest on the provider's page (card details, 3-D Secure, wallet).</summary>
    RequiresAction = 1,
    /// <summary>The provider accepted it; the final result is pending.</summary>
    Processing = 2,
    /// <summary>The provider confirmed the money (blueprint SUCCEEDED_PSP).</summary>
    Succeeded = 3,
    Failed = 4,
    Cancelled = 5
}

/// <summary>The split-payment selector (blueprint 4.3): full, equal, item or custom amount.</summary>
public enum PaymentMode
{
    /// <summary>Everything left that nobody else is paying for.</summary>
    Full = 0,
    Equal = 1,
    Items = 2,
    Custom = 3
}

public enum PaymentMethod
{
    Card = 0,
    ApplePay = 1,
    GooglePay = 2,
    StcPay = 3
}
