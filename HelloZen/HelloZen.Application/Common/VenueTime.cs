namespace HelloZen.Application.Common;

public static class VenueTime
{
    /// <summary>The current wall-clock time at the venue, e.g. for menu schedules.</summary>
    public static DateTime LocalNow(TimeProvider clock, string timeZoneId)
    {
        var utcNow = clock.GetUtcNow();
        return TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var zone)
            ? TimeZoneInfo.ConvertTime(utcNow, zone).DateTime
            : utcNow.UtcDateTime;
    }

    /// <summary>Midnight at the venue today, e.g. for "served today".</summary>
    public static DateTimeOffset StartOfToday(TimeProvider clock, string timeZoneId)
    {
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(timeZoneId, out var found) ? found : TimeZoneInfo.Utc;
        var midnight = TimeZoneInfo.ConvertTime(clock.GetUtcNow(), zone).Date;
        return new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(midnight, zone), TimeSpan.Zero);
    }
}
