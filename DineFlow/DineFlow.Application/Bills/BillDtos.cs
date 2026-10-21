using System.ComponentModel.DataAnnotations;
using DineFlow.Application.Orders;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;

namespace DineFlow.Application.Bills;

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
/// <param name="Mine">Held, being paid or paid by this guest.</param>
public record BillUnitDto(
    Guid OrderLineId,
    int UnitIndex,
    int OrderNumber,
    string Name,
    string? NameAr,
    IReadOnlyList<string> Options,
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

public record TipSettingsDto(bool Enabled, IReadOnlyList<int> PresetsBasisPoints, int? MostCommonBasisPoints);

/// <param name="People">2–50 to split equally; null to stop splitting (only before anyone pays a share).</param>
public record SplitRequest([Range(2, Check.MaxSplitPeople)] int? People);

public record UnitRef([Required] Guid OrderLineId, [Range(0, OrderLine.MaxQuantity - 1)] int UnitIndex);

/// <param name="Units">Every item this guest wants to hold; anything not listed is let go.</param>
public record ReserveRequest([Required, MaxLength(500)] IReadOnlyList<UnitRef> Units);

/// <param name="Taken">Requested items another guest already holds or paid for.</param>
public record ReserveResultDto(BillDto Bill, IReadOnlyList<UnitRef> Taken);
