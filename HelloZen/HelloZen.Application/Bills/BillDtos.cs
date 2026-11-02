using System.ComponentModel.DataAnnotations;
using HelloZen.Application.Orders;
using HelloZen.Domain.Bills;
using HelloZen.Domain.Orders;
using HelloZen.Domain.Payments;
using HelloZen.Domain.Venues;

namespace HelloZen.Application.Bills;

public enum BillStatus
{
    /// <summary>Nothing has been ordered at this table yet.</summary>
    NoOrders,
    Open,
    /// <summary>The table's last bill has been settled.</summary>
    Paid
}

/// <summary>
/// The table's bill as one guest sees it (blueprint 9.1 GET …/bill, 3.2): everything ordered,
/// what's been paid, what others are paying right now, and what this guest can pay. All
/// amounts include taxes and charges.
/// </summary>
public record BillDto(
    Guid? CheckId,
    BillStatus Status,
    string Currency,
    string LocationType,
    string LocationLabel,
    string? AreaName,
    string? AreaNameAr,
    IReadOnlyList<BillOrderDto> Orders,
    long SubtotalMinor,
    long ServiceChargeMinor,
    long TaxMinor,
    bool TaxIncluded,
    long TotalMinor,
    long PaidMinor,
    /// <summary>Left to pay on the bill.</summary>
    long RemainingMinor,
    /// <summary>Part of what's left that other guests are paying or have selected right now.</summary>
    long ReservedMinor,
    /// <summary>What this guest can pay now: left to pay minus what others are paying or holding.</summary>
    long AvailableMinor,
    /// <summary>A payment on this bill (anyone's) is in progress.</summary>
    bool PaymentInProgress,
    SplitDto Split,
    IReadOnlyList<BillUnitDto> Units,
    TipSettingsDto Tips,
    IReadOnlyList<PaymentMethod> Methods,
    string TermsVersion,
    string TermsUrl);

public record BillOrderDto(Guid Id, int Number, OrderStatus Status, IReadOnlyList<OrderLineDto> Lines, long TotalMinor);

/// <param name="People">How many people the table splits between equally; null when not splitting.</param>
/// <param name="SharesTaken">Equal shares paid or being paid.</param>
/// <param name="SharesLeft">Equal shares still to pay; null when not splitting.</param>
/// <param name="EqualAvailable">This guest can split equally (nobody is paying by item).</param>
/// <param name="ItemsAvailable">This guest can pick items (nobody has paid an amount).</param>
/// <param name="SelectedMinor">The items this guest holds, inclusive of taxes and charges.</param>
public record SplitDto(int? People, int SharesTaken, int? SharesLeft, bool EqualAvailable, bool ItemsAvailable, long SelectedMinor);

/// <summary>One portion of an order line, e.g. the first of two Cokes, for splitting by item.</summary>
/// <param name="OptionsAr">The Arabic option names, in the same order as <c>Options</c> (null where there is none).</param>
/// <param name="Mine">Held, being paid or paid by this guest.</param>
public record BillUnitDto(
    Guid OrderLineId,
    int UnitIndex,
    int OrderNumber,
    string Name,
    string? NameAr,
    IReadOnlyList<string> Options,
    IReadOnlyList<string?> OptionsAr,
    long AmountMinor,
    BillUnitStatus Status,
    bool Mine);

public enum BillUnitStatus
{
    Open,
    /// <summary>Held by a guest while they choose (this guest when <c>Mine</c>).</summary>
    Selected,
    /// <summary>Covered by a payment in progress.</summary>
    Paying,
    Paid
}

/// <param name="MaxBasisPoints">The most a guest can tip, as a share of what they're paying (20%).</param>
public record TipSettingsDto(bool Enabled, IReadOnlyList<int> PresetsBasisPoints, int? MostCommonBasisPoints, int MaxBasisPoints = Venue.MaxTipBasisPoints)
{
    /// <summary>The venue's tip choices, leaving out any saved before tips were capped at 20%.</summary>
    public static TipSettingsDto For(Venue venue)
    {
        var presets = venue.TipPresetsBasisPoints.Where(p => p <= Venue.MaxTipBasisPoints).ToList();
        var common = venue.MostCommonTipBasisPoints is { } c && presets.Contains(c) ? c : (int?)null;
        return new(venue.TipsEnabled, presets, common);
    }
}

/// <param name="People">2–50 to split equally; null to stop splitting (only before anyone pays a share).</param>
public record SplitRequest([Range(2, Check.MaxSplitPeople)] int? People);

public record UnitRef([Required] Guid OrderLineId, [Range(0, OrderLine.MaxQuantity - 1)] int UnitIndex);

/// <param name="Units">Every item this guest wants to hold; anything not listed is let go.</param>
public record ReserveRequest([Required, MaxLength(500)] IReadOnlyList<UnitRef> Units);

/// <param name="Taken">Requested items another guest already holds or paid for.</param>
public record ReserveResultDto(BillDto Bill, IReadOnlyList<UnitRef> Taken);

// ---- Moving items between tables (staff) ----

/// <summary>A table's open bill line by line, for staff choosing what to move to another table.</summary>
/// <param name="CheckId">Null when the table has no open bill.</param>
/// <param name="PaidMinor">Already paid towards the bill (ledger), which has to stay covered by what's left on it.</param>
/// <param name="PaymentInProgress">A guest is paying right now; nothing can move until that finishes.</param>
public record TableBillDto(
    Guid LocationId,
    Guid? CheckId,
    string Currency,
    long TotalMinor,
    long PaidMinor,
    bool PaymentInProgress,
    IReadOnlyList<TableBillLineDto> Lines);

/// <param name="MovableQuantity">How many of the line can move: 0 when guests have paid, or are paying, for it by item.</param>
public record TableBillLineDto(
    Guid OrderLineId,
    Guid OrderId,
    int OrderNumber,
    OrderStatus OrderStatus,
    string Name,
    string? NameAr,
    IReadOnlyList<OrderModifierDto> Modifiers,
    string? Note,
    int Quantity,
    long UnitPriceMinor,
    long LineTotalMinor,
    int MovableQuantity);

public record TransferLineRequest([Required] Guid OrderLineId, [Range(1, OrderLine.MaxQuantity)] int Quantity);

/// <param name="ToLocationId">The table the items go to, in the same branch.</param>
/// <param name="Lines">Which items and how many of each; null or empty moves the whole bill.</param>
public record TransferRequest([Required] Guid ToLocationId, [MaxLength(500)] IReadOnlyList<TransferLineRequest>? Lines);

/// <param name="WholeBill">Everything on the bill moved, not just some items.</param>
/// <param name="Items">How many items (units) moved.</param>
/// <param name="OrderNumbers">The orders now at the new table, including any split off for the items that moved.</param>
public record TransferResultDto(Guid FromLocationId, Guid ToLocationId, bool WholeBill, int Items, IReadOnlyList<int> OrderNumbers);
