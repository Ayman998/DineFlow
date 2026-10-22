using DineFlow.Domain.Common;

namespace DineFlow.Domain.Payments;

/// <summary>
/// A link the merchant sends a customer so they can pay a set amount away from the table, e.g. to
/// prepay a booking or an order (blueprint E16, P1-LNK-01..05). The public URL carries a signed,
/// non-guessable token; only its SHA-256 hash is stored. The link stays open until it is paid in
/// full, it expires or the merchant deactivates it. Payments against it are ordinary
/// <see cref="Payment"/>s, confirmed only by the provider's webhook.
/// </summary>
/// <remarks>
/// <see cref="Revision"/> is checked on every save, like <c>Check.Revision</c>: starting a payment
/// bumps it, so two customers paying at the same moment can't both spend what's left.
/// </remarks>
public class PaymentLink : Entity, ITenantOwned
{
    public const int MaxReferenceLength = 60;
    public const long MaxAmountMinor = 100_000_000;
    public static readonly TimeSpan MinValidity = TimeSpan.FromHours(1);
    public static readonly TimeSpan MaxValidity = TimeSpan.FromDays(90);

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    /// <summary>The merchant app's id for this request; a retry returns the same link.</summary>
    public Guid ClientRequestId { get; private set; }
    /// <summary>Short identifier shown to the customer and staff, e.g. PL-1A2B3C4D.</summary>
    public string Code { get; private set; } = string.Empty;
    /// <summary>Lower-case hex SHA-256 of the raw token in the link's URL.</summary>
    public string TokenHash { get; private set; } = string.Empty;

    /// <summary>What the customer is asked to pay, inclusive of taxes and charges, before any tip.</summary>
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    /// <summary>The merchant's own reference, e.g. a booking number.</summary>
    public string? Reference { get; private set; }
    /// <summary>Several people can each pay part of the amount.</summary>
    public bool AllowSplit { get; private set; }
    /// <summary>The customer is offered a tip on top of the amount.</summary>
    public bool RequestTip { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    /// <summary>Who was told about the link by e-mail, if anyone.</summary>
    public string? CustomerEmail { get; private set; }
    public DateTimeOffset? CustomerNotifiedAt { get; private set; }

    /// <summary>Stored as Active, Paid or Cancelled; <see cref="StatusAt"/> also reports Expired.</summary>
    public PaymentLinkStatus Status { get; private set; } = PaymentLinkStatus.Active;
    public Guid CreatedByStaffId { get; private set; }
    public DateTimeOffset? PaidAt { get; private set; }
    public DateTimeOffset? CancelledAt { get; private set; }
    public Guid? CancelledByStaffId { get; private set; }
    /// <summary>Optimistic-concurrency token; see the remarks.</summary>
    public int Revision { get; private set; }

    private PaymentLink() { }

    /// <param name="id">Chosen up front because the raw token is derived from it.</param>
    public PaymentLink(
        Guid id, Guid tenantId, Guid venueId, Guid clientRequestId, string currency, long amountMinor, string? reference,
        bool allowSplit, bool requestTip, TimeSpan validFor, string? customerEmail, Guid staffUserId, string tokenHash, DateTimeOffset now)
    {
        if (amountMinor < 1) throw new DomainException("Enter the amount to collect.");
        if (amountMinor > MaxAmountMinor) throw new DomainException("That amount is too large for a payment link.");
        if (validFor < MinValidity || validFor > MaxValidity) throw new DomainException("A link can be active for 1 hour up to 90 days.");

        Id = id;
        TenantId = tenantId;
        VenueId = venueId;
        ClientRequestId = clientRequestId;
        Code = $"PL-{id.ToString("N")[..8].ToUpperInvariant()}";
        TokenHash = tokenHash;
        Currency = currency;
        AmountMinor = amountMinor;
        Reference = Text.Optional(reference, MaxReferenceLength, "Reference");
        AllowSplit = allowSplit;
        RequestTip = requestTip;
        ExpiresAt = now + validFor;
        CustomerEmail = string.IsNullOrWhiteSpace(customerEmail) ? null : customerEmail.Trim();
        CreatedByStaffId = staffUserId;
        CreatedAt = now;
    }

    /// <summary>Where the link stands at <paramref name="now"/>: an open link past its expiry reads as Expired.</summary>
    public PaymentLinkStatus StatusAt(DateTimeOffset now) =>
        Status == PaymentLinkStatus.Active && now >= ExpiresAt ? PaymentLinkStatus.Expired : Status;

    /// <summary>A customer can start a new payment.</summary>
    public bool IsPayable(DateTimeOffset now) => StatusAt(now) == PaymentLinkStatus.Active;

    /// <summary>Marks a change to what's left to pay, so a concurrent payment on the same link can't also save.</summary>
    public void Touch() => Revision++;

    /// <summary>Everything has been paid: the link closes by itself (P1-LNK-02).</summary>
    public void MarkPaid(DateTimeOffset now)
    {
        if (Status != PaymentLinkStatus.Active) return;
        Status = PaymentLinkStatus.Paid;
        PaidAt = now;
        Touch();
    }

    /// <summary>The merchant turns the link off; nobody can start a new payment on it.</summary>
    public void Cancel(Guid staffUserId, DateTimeOffset now)
    {
        if (Status == PaymentLinkStatus.Paid) throw new DomainException("This link has been paid in full, so there's nothing to deactivate.");
        if (Status == PaymentLinkStatus.Cancelled) return;
        Status = PaymentLinkStatus.Cancelled;
        CancelledAt = now;
        CancelledByStaffId = staffUserId;
        Touch();
    }

    /// <summary>Records that the customer was e-mailed the link, at this address.</summary>
    public void Notified(string email, DateTimeOffset now)
    {
        CustomerEmail = email.Trim();
        CustomerNotifiedAt = now;
    }
}

public enum PaymentLinkStatus
{
    /// <summary>Open for payment.</summary>
    Active = 0,
    /// <summary>Paid in full; closed automatically.</summary>
    Paid = 1,
    /// <summary>Past its expiry without being paid in full. Never stored; see <see cref="PaymentLink.StatusAt"/>.</summary>
    Expired = 2,
    /// <summary>Deactivated by the merchant.</summary>
    Cancelled = 3
}
