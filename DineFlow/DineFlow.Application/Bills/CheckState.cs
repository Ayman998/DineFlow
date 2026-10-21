using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;

namespace DineFlow.Application.Bills;

/// <summary>A unit on the bill with the order line it comes from.</summary>
internal sealed record UnitOnBill(BillUnit Unit, Order Order, OrderLine Line);

/// <summary>
/// Where a check stands at one moment (blueprint "Split/Allocation Ledger"): what was ordered,
/// what the ledger says is paid, which payments are still in progress and which items guests
/// hold. Every split decision is made from this, so amounts can't add up to more than the bill.
/// </summary>
/// <remarks>
/// Open = total − paid. Payments in progress and other guests' item holds are reserved; what a
/// guest can pay is open − reserved. Splitting equally and by item don't mix on one bill: once
/// an amount (full, equal share or custom) has been paid or is being paid, items can't be
/// picked any more, and once someone holds or pays items, the bill can't be shared equally.
/// </remarks>
internal sealed class CheckState(
    Check check,
    IReadOnlyList<Order> orders,
    long paidMinor,
    IReadOnlyList<Payment> payments,
    IReadOnlyList<ItemClaim> claims,
    DateTimeOffset now)
{
    public Check Check { get; } = check;
    public IReadOnlyList<Order> Orders { get; } = orders;
    public IReadOnlyList<UnitOnBill> Units { get; } =
        orders.SelectMany(o => BillAllocation.Units(o).Select(u => new UnitOnBill(u, o, o.Lines.Single(l => l.Id == u.OrderLineId)))).ToList();
    public IReadOnlyList<Payment> Payments { get; } = payments;
    public IReadOnlyList<ItemClaim> Claims { get; } = claims;
    public DateTimeOffset Now { get; } = now;

    public long TotalMinor => Orders.Sum(o => o.TotalMinor);
    public long PaidMinor { get; } = paidMinor;
    /// <summary>Left to pay. Never negative, even if a late payment overpaid the bill.</summary>
    public long OpenMinor => Math.Max(0, TotalMinor - PaidMinor);

    public IEnumerable<Payment> InFlight => Payments.Where(p => p.IsInFlight(Now));
    public long InFlightMinor => InFlight.Sum(p => p.BillAmountMinor);
    public IEnumerable<ItemClaim> ActiveClaims => Claims.Where(c => c.IsActive(Now));

    public IReadOnlyList<ItemClaim> HeldBy(Guid sessionId) =>
        ActiveClaims.Where(c => c.Status == ItemClaimStatus.Held && c.GuestSessionId == sessionId).ToList();

    /// <summary>Being paid by anyone, or held by other guests: not available to <paramref name="sessionId"/>.</summary>
    public long ReservedFor(Guid sessionId) =>
        InFlightMinor + ActiveClaims.Where(c => c.Status == ItemClaimStatus.Held && c.GuestSessionId != sessionId).Sum(c => c.AmountMinor);

    public long AvailableFor(Guid sessionId) => Math.Max(0, OpenMinor - ReservedFor(sessionId));

    private bool Counts(Payment p) => p.Status == PaymentStatus.Succeeded || p.IsInFlight(Now);

    /// <summary>Equal shares paid or being paid.</summary>
    public int SharesTaken => Payments.Where(p => p.Mode == PaymentMode.Equal && Counts(p)).Sum(p => p.Shares ?? 0);
    public int? SharesLeft => Check.SplitPeople is { } people ? Math.Max(0, people - SharesTaken) : null;

    /// <summary>An amount not tied to items has been paid or is being paid.</summary>
    public bool AmountPaymentsStarted => Payments.Any(p => p.Mode != PaymentMode.Items && Counts(p));

    public bool CanSplitByItem => !AmountPaymentsStarted;

    /// <summary>Nobody else holds items, and no item has been paid or is being paid.</summary>
    public bool CanSplitEquallyFor(Guid sessionId) =>
        !ActiveClaims.Any(c => c.Status != ItemClaimStatus.Held || c.GuestSessionId != sessionId);

    public ItemClaim? ActiveClaimOn(BillUnit unit) => ActiveClaims.FirstOrDefault(c => c.Matches(unit));
}
