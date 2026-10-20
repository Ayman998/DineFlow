using DineFlow.Domain.Common;

namespace DineFlow.Domain.Promotions;

/// <summary>
/// A promotion or voucher code guests can enter at checkout (blueprint P1-PAY-08, P1-RWD-02),
/// e.g. WELCOME10 for 10% off. Validation happens on the server only; each successful payment
/// that used it counts as a redemption.
/// </summary>
public class PromoCode : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = string.Empty;
    /// <summary>Guest-facing description, e.g. "10% off your next meal".</summary>
    public string Headline { get; private set; } = string.Empty;
    public DiscountType Type { get; private set; }
    /// <summary>Percent in basis points (1000 = 10%) or a fixed amount in minor units.</summary>
    public long Value { get; private set; }
    /// <summary>Upper limit for a percentage discount, in minor units.</summary>
    public long? MaxDiscountMinor { get; private set; }
    public long? MinBillMinor { get; private set; }
    public DateTimeOffset? StartsAt { get; private set; }
    public DateTimeOffset? EndsAt { get; private set; }
    public int? MaxRedemptions { get; private set; }
    public int RedemptionCount { get; private set; }
    public bool IsActive { get; private set; } = true;
    /// <summary>Show this code on the payment confirmation as an offer for the next visit (P1-REV-01).</summary>
    public bool ShowAfterPayment { get; private set; }

    private PromoCode() { }

    public PromoCode(Guid tenantId, string code)
    {
        TenantId = tenantId;
        Code = Normalise(code);
        if (Code.Length is < 3 or > 20 || !Code.All(c => char.IsAsciiLetterOrDigit(c) || c == '-'))
            throw new DomainException("Codes are 3–20 letters, digits or dashes, e.g. WELCOME10.");
    }

    public static string Normalise(string code) => code.Trim().ToUpperInvariant();

    public void Update(
        string headline, DiscountType type, long value, long? maxDiscountMinor, long? minBillMinor,
        DateTimeOffset? startsAt, DateTimeOffset? endsAt, int? maxRedemptions, bool isActive, bool showAfterPayment)
    {
        if (type == DiscountType.Percent && value is < 1 or > 10_000)
            throw new DomainException("A percentage discount must be between 0.01% and 100%.");
        if (type == DiscountType.Amount && value < 1)
            throw new DomainException("A fixed discount must be more than zero.");
        if (startsAt.HasValue && endsAt.HasValue && endsAt <= startsAt)
            throw new DomainException("The end date must be after the start date.");
        if (maxRedemptions is < 1) throw new DomainException("The redemption limit must be at least 1.");
        if (maxDiscountMinor is < 1 || minBillMinor is < 0) throw new DomainException("Limits can't be negative.");

        Headline = Text.Required(headline, 120, "Headline");
        Type = type;
        Value = value;
        MaxDiscountMinor = type == DiscountType.Percent ? maxDiscountMinor : null;
        MinBillMinor = minBillMinor;
        StartsAt = startsAt;
        EndsAt = endsAt;
        MaxRedemptions = maxRedemptions;
        IsActive = isActive;
        ShowAfterPayment = showAfterPayment;
    }

    /// <summary>Active, within its dates and not used up: could be redeemed at <paramref name="now"/>, whatever the bill.</summary>
    public bool IsAvailableAt(DateTimeOffset now) =>
        IsActive && !(StartsAt > now) && !(EndsAt <= now) && !(MaxRedemptions is { } max && RedemptionCount >= max);

    /// <summary>The discount this code gives on <paramref name="billMinor"/> now, or why it can't be used.</summary>
    public PromoEvaluation Evaluate(long billMinor, DateTimeOffset now)
    {
        if (!IsActive) return PromoEvaluation.Rejected("promo_inactive", "This code isn't active.");
        if (StartsAt > now) return PromoEvaluation.Rejected("promo_not_started", "This code isn't valid yet.");
        if (EndsAt <= now) return PromoEvaluation.Rejected("promo_expired", "This code has expired.");
        if (MaxRedemptions is { } max && RedemptionCount >= max)
            return PromoEvaluation.Rejected("promo_used_up", "This code has been fully used.");
        if (MinBillMinor is { } min && billMinor < min)
            return PromoEvaluation.Rejected("promo_min_spend", "Your bill is below this code's minimum spend.");

        var discount = Type == DiscountType.Percent
            ? (long)Math.Round(billMinor * (decimal)Value / 10_000m, MidpointRounding.AwayFromZero)
            : Value;
        if (MaxDiscountMinor is { } cap) discount = Math.Min(discount, cap);
        return PromoEvaluation.Accepted(Math.Min(discount, billMinor));
    }

    public void Redeem() => RedemptionCount++;
}

public enum DiscountType
{
    Percent = 0,
    Amount = 1
}

public readonly record struct PromoEvaluation(bool IsValid, long DiscountMinor, string? Code, string? Message)
{
    public static PromoEvaluation Accepted(long discountMinor) => new(true, discountMinor, null, null);
    public static PromoEvaluation Rejected(string code, string message) => new(false, 0, code, message);
}
