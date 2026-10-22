using System.Net.Mail;
using DineFlow.Domain.Common;

namespace DineFlow.Domain.Notifications;

/// <summary>
/// A report e-mailed to chosen people every day, week or month (blueprint P1-MER-09 "scheduled
/// email reports", 9.2 /v1/merchant/report-schedules/*). Each run covers the last complete period
/// in the venue's time zone: yesterday, last Monday to Sunday, or last calendar month.
/// </summary>
/// <remarks><see cref="Revision"/> is checked on save, so two workers can't send the same run twice.</remarks>
public class ReportSchedule : Entity, ITenantOwned
{
    public const int MaxRecipients = 10;

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public ReportFrequency Frequency { get; private set; }
    /// <summary>Local hour (0–23, venue time zone) the report goes out.</summary>
    public int SendAtHour { get; private set; }
    public List<string> Recipients { get; private set; } = [];
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset NextRunAt { get; private set; }
    public DateTimeOffset? LastRunAt { get; private set; }
    public Guid CreatedByStaffId { get; private set; }
    public int Revision { get; private set; }

    private ReportSchedule() { }

    public ReportSchedule(Guid tenantId, Guid venueId, ReportFrequency frequency, int sendAtHour, IEnumerable<string> recipients,
        Guid staffUserId, TimeZoneInfo zone, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        CreatedByStaffId = staffUserId;
        CreatedAt = now;
        Update(frequency, sendAtHour, recipients, true, zone, now);
    }

    public void Update(ReportFrequency frequency, int sendAtHour, IEnumerable<string> recipients, bool isActive, TimeZoneInfo zone, DateTimeOffset now)
    {
        if (sendAtHour is < 0 or > 23) throw new DomainException("Choose an hour between 00:00 and 23:00.");
        var list = recipients.Select(r => r.Trim()).Where(r => r.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (list.Count == 0) throw new DomainException("Add at least one e-mail address.");
        if (list.Count > MaxRecipients) throw new DomainException($"Send to {MaxRecipients} addresses or fewer.");
        foreach (var address in list)
            if (address.Length > 254 || !MailAddress.TryCreate(address, out var parsed) || parsed.Address != address)
                throw new DomainException($"\"{address}\" isn't a valid e-mail address.");

        Frequency = frequency;
        SendAtHour = sendAtHour;
        Recipients = list;
        IsActive = isActive;
        NextRunAt = NextRunAfter(now, zone);
        Revision++;
    }

    public bool IsDue(DateTimeOffset now) => IsActive && NextRunAt <= now;

    /// <summary>Records a run and schedules the next one.</summary>
    public void Ran(DateTimeOffset runAt, TimeZoneInfo zone)
    {
        LastRunAt = runAt;
        NextRunAt = NextRunAfter(runAt, zone);
        Revision++;
    }

    /// <summary>The next send time strictly after <paramref name="after"/>, in UTC.</summary>
    public DateTimeOffset NextRunAfter(DateTimeOffset after, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(after, zone).DateTime;
        var today = local.Date.AddHours(SendAtHour);
        var candidate = Frequency switch
        {
            ReportFrequency.Weekly => today.AddDays(((int)DayOfWeek.Monday - (int)local.DayOfWeek + 7) % 7),
            ReportFrequency.Monthly => new DateTime(local.Year, local.Month, 1).AddHours(SendAtHour),
            _ => today
        };
        if (candidate <= local)
            candidate = Frequency switch
            {
                ReportFrequency.Weekly => candidate.AddDays(7),
                ReportFrequency.Monthly => candidate.AddMonths(1),
                _ => candidate.AddDays(1)
            };
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(candidate, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
    }

    /// <summary>The last complete period before <paramref name="runAt"/> as local dates: start inclusive, end exclusive.</summary>
    public (DateTime Start, DateTime End) PeriodBefore(DateTimeOffset runAt, TimeZoneInfo zone) => PeriodBefore(Frequency, runAt, zone);

    /// <inheritdoc cref="PeriodBefore(DateTimeOffset, TimeZoneInfo)"/>
    public static (DateTime Start, DateTime End) PeriodBefore(ReportFrequency frequency, DateTimeOffset runAt, TimeZoneInfo zone)
    {
        var date = TimeZoneInfo.ConvertTime(runAt, zone).Date;
        var thisMonday = date.AddDays(-(((int)date.DayOfWeek + 6) % 7));
        return frequency switch
        {
            ReportFrequency.Weekly => (thisMonday.AddDays(-7), thisMonday),
            ReportFrequency.Monthly => (new DateTime(date.Year, date.Month, 1).AddMonths(-1), new DateTime(date.Year, date.Month, 1)),
            _ => (date.AddDays(-1), date)
        };
    }
}

public enum ReportFrequency
{
    Daily = 0,
    /// <summary>Mondays, covering Monday to Sunday of the week before.</summary>
    Weekly = 1,
    /// <summary>The 1st, covering the month before.</summary>
    Monthly = 2
}
