using DineFlow.Domain.Common;

namespace DineFlow.Domain.Bills;

/// <summary>
/// The open bill (tab) at one service location (blueprint "Bill/Check"). Every order placed at
/// the table joins the open check; payments settle it; once nothing is left to pay it closes
/// and the next order opens a new one. In standalone mode DineFlow is the bill's source of truth;
/// with a POS connector this becomes a mirror of the POS check.
/// </summary>
/// <remarks>
/// <see cref="Revision"/> is checked on every save: anything that takes money off what's
/// available to pay (starting a payment, holding items, changing the split) bumps it, so when
/// two guests act at the same moment, one save fails and is retried with fresh numbers instead
/// of both spending the same balance (blueprint 3.2 "Concurrency").
/// </remarks>
public class Check : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public CheckStatus Status { get; private set; } = CheckStatus.Open;
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset? ClosedAt { get; private set; }
    /// <summary>Optimistic-concurrency token; see the remarks.</summary>
    public int Revision { get; private set; }
    /// <summary>How many people the table is splitting the bill between equally; null when not splitting.</summary>
    public int? SplitPeople { get; private set; }

    public const int MaxSplitPeople = 50;

    private Check() { }

    public Check(Guid tenantId, Guid venueId, Guid serviceLocationId, string currency, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        ServiceLocationId = serviceLocationId;
        Currency = currency;
        CreatedAt = now;
    }

    /// <summary>Marks a change to what's available to pay, so a concurrent change to the same bill can't also save.</summary>
    public void Touch() => Revision++;

    /// <summary>
    /// Sets how many people the bill is split between. It can't drop below the shares already
    /// paid or being paid plus one, and can only be removed while nobody has paid a share.
    /// </summary>
    public void SetSplitPeople(int? people, int sharesTaken)
    {
        if (people is null)
        {
            if (sharesTaken > 0) throw new DomainException("Someone has already paid a share, so the split can't be removed. Pay the rest in full instead.");
        }
        else
        {
            if (people is < 2 or > MaxSplitPeople) throw new DomainException($"Split between 2 and {MaxSplitPeople} people.");
            if (people <= sharesTaken)
                throw new DomainException($"{sharesTaken} {(sharesTaken == 1 ? "share has" : "shares have")} already been paid, so split between at least {sharesTaken + 1} people.");
        }

        SplitPeople = people;
        Touch();
    }

    public void Close(DateTimeOffset now)
    {
        if (Status == CheckStatus.Closed) return;
        Status = CheckStatus.Closed;
        ClosedAt = now;
    }
}

public enum CheckStatus
{
    Open = 0,
    Closed = 1
}
