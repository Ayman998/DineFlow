using DineFlow.Domain.Common;

namespace DineFlow.Domain.Bills;

/// <summary>
/// A guest's hold on one bill unit when splitting by item (blueprint AllocationReservation):
/// while it's active nobody else can select or pay for that unit. A hold lapses after
/// <see cref="HoldLifetime"/> unless refreshed; while it's being paid it lasts as long as the
/// payment can still complete; once paid it's permanent and links the payment to the item.
/// The database allows one claim per unit, so two guests can never hold the same one.
/// </summary>
public class ItemClaim : Entity, ITenantOwned
{
    public static readonly TimeSpan HoldLifetime = TimeSpan.FromMinutes(10);

    public Guid TenantId { get; private set; }
    public Guid CheckId { get; private set; }
    public Guid OrderLineId { get; private set; }
    public int UnitIndex { get; private set; }
    /// <summary>The unit's price including its share of service charge and tax.</summary>
    public long AmountMinor { get; private set; }
    public Guid GuestSessionId { get; private set; }
    public Guid? PaymentId { get; private set; }
    public ItemClaimStatus Status { get; private set; } = ItemClaimStatus.Held;
    /// <summary>When the hold lapses; null once paid.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    private ItemClaim() { }

    public ItemClaim(Guid tenantId, Guid checkId, BillUnit unit, Guid guestSessionId, DateTimeOffset now)
    {
        TenantId = tenantId;
        CheckId = checkId;
        OrderLineId = unit.OrderLineId;
        UnitIndex = unit.UnitIndex;
        AmountMinor = unit.AmountMinor;
        GuestSessionId = guestSessionId;
        CreatedAt = now;
        ExpiresAt = now + HoldLifetime;
    }

    public bool IsActive(DateTimeOffset now) => Status == ItemClaimStatus.Paid || ExpiresAt > now;

    public bool Matches(BillUnit unit) => OrderLineId == unit.OrderLineId && UnitIndex == unit.UnitIndex;

    /// <summary>The guest is still choosing: keep holding it.</summary>
    public void Refresh(DateTimeOffset now)
    {
        if (Status == ItemClaimStatus.Held) ExpiresAt = now + HoldLifetime;
    }

    /// <summary>The previous hold lapsed, so the unit goes to another guest (the row is reused: one claim per unit).</summary>
    public void HoldFor(Guid guestSessionId, long amountMinor, DateTimeOffset now)
    {
        if (IsActive(now)) throw new DomainException("This item is held by another guest.");
        GuestSessionId = guestSessionId;
        AmountMinor = amountMinor;
        Status = ItemClaimStatus.Held;
        PaymentId = null;
        ExpiresAt = now + HoldLifetime;
    }

    /// <summary>A payment now covers this unit; it stays held for as long as that payment can complete.</summary>
    public void AttachPayment(Guid paymentId, DateTimeOffset payableUntil)
    {
        if (Status != ItemClaimStatus.Held) throw new DomainException("This item is already being paid for.");
        Status = ItemClaimStatus.Paying;
        PaymentId = paymentId;
        ExpiresAt = payableUntil;
    }

    public void MarkPaid()
    {
        Status = ItemClaimStatus.Paid;
        ExpiresAt = null;
    }

    /// <summary>The payment failed or was cancelled: give the guest the item back to try again.</summary>
    public void ReturnToHold(DateTimeOffset now)
    {
        if (Status != ItemClaimStatus.Paying) return;
        Status = ItemClaimStatus.Held;
        PaymentId = null;
        ExpiresAt = now + HoldLifetime;
    }
}

public enum ItemClaimStatus
{
    /// <summary>Selected by a guest who hasn't paid yet.</summary>
    Held = 0,
    /// <summary>Covered by a payment that is still in progress.</summary>
    Paying = 1,
    Paid = 2
}
