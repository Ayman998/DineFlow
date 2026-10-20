namespace DineFlow.Application.Common;

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
}
