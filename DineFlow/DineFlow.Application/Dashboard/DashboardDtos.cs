using DineFlow.Domain.Orders;

namespace DineFlow.Application.Dashboard;

/// <summary>The period the overview's headline numbers cover, in the venue's time zone.</summary>
public enum OverviewPeriod { Today, Week, Month, Quarter }

public enum TrendGranularity { Hourly, Daily, Weekly, Monthly }

/// <param name="RevenueMinor">What guests paid towards bills (after promotions, before refunds), excluding tips.</param>
public record OverviewKpisDto(long RevenueMinor, long TipsMinor, int Orders, long RefundsMinor, int Payments, int PaymentFailures);

/// <summary>The last 7 days against the 7 before (blueprint "% change versus the previous week").</summary>
/// <param name="RevenueChange">Relative change, e.g. 0.0557 for +5.57%; null when last week had none.</param>
public record WeekComparisonDto(long RevenueMinor, long PreviousRevenueMinor, double? RevenueChange, int Orders, int PreviousOrders);

/// <param name="Start">Start of the bucket in UTC (the client formats it in the venue's time zone).</param>
public record TrendPointDto(DateTimeOffset Start, long RevenueMinor, long TipsMinor);

/// <summary>Something staff should look at, e.g. failed payments or tables with money left to pay.</summary>
/// <param name="Kind">"payment", "tables", "review", "refund" or "orders".</param>
public record AttentionDto(string Kind, string Title, string Detail, int Count);

public record OverviewDto(
    OverviewPeriod Period,
    DateTimeOffset From,
    DateTimeOffset To,
    string Currency,
    string TimeZone,
    OverviewKpisDto Selected,
    OverviewKpisDto Today,
    WeekComparisonDto Week,
    TrendGranularity Granularity,
    IReadOnlyList<TrendPointDto> Trend,
    IReadOnlyList<AttentionDto> Attention);

/// <summary>A table's state on the live view (blueprint 4.4 "Tables", P1-MER-10).</summary>
public enum TableState
{
    /// <summary>Nobody is at the table (no recent scan, nothing ordered).</summary>
    Available,
    /// <summary>Guests have scanned the QR code but haven't ordered yet.</summary>
    Ordering,
    Unpaid,
    PartiallyPaid,
    /// <summary>The table's bill has been settled recently.</summary>
    Paid,
    /// <summary>Something needs staff, e.g. a payment just failed.</summary>
    Attention
}

public record TableOrderDto(Guid Id, int Number, OrderStatus Status, DateTimeOffset CreatedAt, long TotalMinor);

public record TableStatusDto(
    Guid LocationId,
    string LocationType,
    string Label,
    string DisplayName,
    string? AreaName,
    TableState State,
    string? AttentionReason,
    string Currency,
    long TotalMinor,
    long PaidMinor,
    long TipsMinor,
    long RemainingMinor,
    DateTimeOffset? LastActivityAt,
    Guid? CheckId,
    IReadOnlyList<TableOrderDto> Orders);
