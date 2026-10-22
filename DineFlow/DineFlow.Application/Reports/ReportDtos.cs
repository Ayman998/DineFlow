using System.ComponentModel.DataAnnotations;
using DineFlow.Domain.Notifications;

namespace DineFlow.Application.Reports;

/// <param name="SendAtHour">Local hour (venue time zone) the report goes out.</param>
public record ReportScheduleRequest(
    [Required] Guid VenueId,
    ReportFrequency Frequency,
    [Range(0, 23)] int SendAtHour,
    [Required, MinLength(1), MaxLength(ReportSchedule.MaxRecipients)] IReadOnlyList<string> Recipients,
    bool IsActive = true);

public record ReportScheduleDto(
    Guid Id,
    ReportFrequency Frequency,
    int SendAtHour,
    IReadOnlyList<string> Recipients,
    bool IsActive,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastRunAt,
    string? CreatedBy);

public enum ReportUnit { Money, Count, Rating }

/// <param name="Change">Relative change from the previous period (0.25 = up 25%); null when the previous value was 0.</param>
public record ReportMetricDto(string Key, string Label, ReportUnit Unit, decimal Value, decimal Previous, double? Change);

public record ReportItemDto(string Name, int Quantity, long TotalMinor);

/// <summary>
/// One venue's report for a period (blueprint P1-MER-09): money, orders, payments, refunds and
/// reviews, each against the period before, and the best-selling items. Dates are local to the
/// venue; the end is exclusive.
/// </summary>
public record ReportDto(
    string BrandName,
    string VenueName,
    string Currency,
    ReportFrequency Frequency,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    string PeriodLabel,
    IReadOnlyList<ReportMetricDto> Metrics,
    IReadOnlyList<ReportItemDto> TopItems);
