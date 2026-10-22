using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Common;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Reports;

/// <summary>
/// Scheduled e-mail reports (blueprint P1-MER-09, 9.2 /v1/merchant/report-schedules/*): who gets
/// the venue's daily, weekly or monthly report and when, a preview, "send now", and the background
/// run that queues each report when it's due. Sending goes through the notification outbox, so
/// failures are retried and show up in the delivery log.
/// </summary>
public class ReportService(
    IAppDbContext db,
    ITenantContext tenant,
    ReportBuilder builder,
    IOptions<FeatureOptions> features,
    TimeProvider clock)
{
    public async Task<IReadOnlyList<ReportScheduleDto>> ListAsync(Guid venueId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var schedules = await db.ReportSchedules.AsNoTracking().Where(s => s.VenueId == venue.Id).OrderBy(s => s.CreatedAt).ToListAsync(ct);
        var staffIds = schedules.Select(s => s.CreatedByStaffId).Distinct().ToList();
        var staff = await db.StaffUsers.AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
        return schedules.Select(s => ToDto(s, staff.GetValueOrDefault(s.CreatedByStaffId))).ToList();
    }

    public async Task<ReportScheduleDto> CreateAsync(ReportScheduleRequest request, Guid staffUserId, CancellationToken ct)
    {
        var venue = await VenueAsync(request.VenueId, ct);
        ReportSchedule schedule;
        try
        {
            schedule = new ReportSchedule(venue.TenantId, venue.Id, request.Frequency, request.SendAtHour, request.Recipients,
                staffUserId, ReportBuilder.Zone(venue), clock.GetUtcNow());
            if (!request.IsActive) schedule.Update(request.Frequency, request.SendAtHour, request.Recipients, false, ReportBuilder.Zone(venue), clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw AppException.BadRequest("invalid_schedule", ex.Message);
        }

        db.ReportSchedules.Add(schedule);
        Audit(staffUserId, "report_schedule.created", schedule.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(schedule.Id, ct);
    }

    public async Task<ReportScheduleDto> UpdateAsync(Guid scheduleId, ReportScheduleRequest request, Guid staffUserId, CancellationToken ct)
    {
        var schedule = await FindAsync(scheduleId, ct);
        var venue = await VenueAsync(schedule.VenueId, ct);
        try { schedule.Update(request.Frequency, request.SendAtHour, request.Recipients, request.IsActive, ReportBuilder.Zone(venue), clock.GetUtcNow()); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_schedule", ex.Message); }

        Audit(staffUserId, "report_schedule.updated", schedule.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(schedule.Id, ct);
    }

    public async Task DeleteAsync(Guid scheduleId, Guid staffUserId, CancellationToken ct)
    {
        var schedule = await FindAsync(scheduleId, ct);
        db.ReportSchedules.Remove(schedule);
        Audit(staffUserId, "report_schedule.deleted", schedule.Id);
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The report the venue would get for the last complete day, week or month.</summary>
    public async Task<ReportDto> PreviewAsync(Guid venueId, ReportFrequency frequency, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var (start, end) = ReportSchedule.PeriodBefore(frequency, clock.GetUtcNow(), ReportBuilder.Zone(venue));
        return await builder.BuildAsync(venue, frequency, start, end, ct);
    }

    /// <summary>Queues the latest report to the schedule's recipients straight away, outside the schedule.</summary>
    public async Task<int> SendNowAsync(Guid scheduleId, Guid staffUserId, CancellationToken ct)
    {
        var schedule = await FindAsync(scheduleId, ct);
        var venue = await VenueAsync(schedule.VenueId, ct);
        var now = clock.GetUtcNow();
        var (start, end) = schedule.PeriodBefore(now, ReportBuilder.Zone(venue));
        await QueueAsync(schedule, venue, start, end, $"report-now:{Guid.NewGuid():N}", now, ct);
        Audit(staffUserId, "report_schedule.sent_now", schedule.Id);
        await db.SaveChangesAsync(ct);
        return schedule.Recipients.Count;
    }

    /// <summary>
    /// Background: queues every report that's due and schedules its next run. A run that another
    /// worker got to first is skipped (the schedule's revision changed), and a report already queued
    /// for that period isn't queued again.
    /// </summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        if (!features.Value.EmailReports) return 0;
        var now = clock.GetUtcNow();
        var due = await db.ReportSchedules.IgnoreQueryFilters().AsNoTracking()
            .Where(s => s.IsActive && s.NextRunAt <= now).OrderBy(s => s.NextRunAt).Select(s => s.Id).Take(50).ToListAsync(ct);

        var ran = 0;
        foreach (var id in due)
        {
            db.ChangeTracker.Clear();
            var schedule = await db.ReportSchedules.IgnoreQueryFilters().SingleAsync(s => s.Id == id, ct);
            var venue = await db.Venues.IgnoreQueryFilters().SingleAsync(v => v.Id == schedule.VenueId, ct);
            var zone = ReportBuilder.Zone(venue);
            // The period is the one before the scheduled time, so a run that was delayed still reports the right day.
            var (start, end) = schedule.PeriodBefore(schedule.NextRunAt, zone);
            await QueueAsync(schedule, venue, start, end, $"report:{schedule.Id:N}:{start:yyyyMMdd}", now, ct);
            schedule.Ran(now, zone);
            try
            {
                await db.SaveChangesAsync(ct);
                ran++;
            }
            catch (DbUpdateException)
            {
                // Another worker ran it (the revision changed), or this period's report was already queued
                // (the dedupe key refused it). Either way the report is on its way; make sure the schedule moves on.
                db.ChangeTracker.Clear();
                var again = await db.ReportSchedules.IgnoreQueryFilters().SingleAsync(s => s.Id == id, ct);
                if (again.IsDue(now))
                {
                    again.Ran(now, zone);
                    try { await db.SaveChangesAsync(ct); }
                    catch (DbUpdateException) { /* someone else moved it on in the meantime */ }
                }
            }
        }
        db.ChangeTracker.Clear();
        return ran;
    }

    // ---- Helpers ----

    private async Task QueueAsync(ReportSchedule schedule, Venue venue, DateTime start, DateTime end, string dedupeKey, DateTimeOffset now, CancellationToken ct)
    {
        var report = await builder.BuildAsync(venue, schedule.Frequency, start, end, ct);
        var (subject, text, html) = ReportBuilder.Render(report);
        foreach (var to in schedule.Recipients)
            db.NotificationDeliveries.Add(new NotificationDelivery(schedule.TenantId, schedule.VenueId, NotificationChannel.Email, NotificationKind.Report,
                to, dedupeKey, schedule.Id, subject, text, html, null, now));
    }

    private async Task<ReportScheduleDto> GetAsync(Guid scheduleId, CancellationToken ct)
    {
        var schedule = await db.ReportSchedules.AsNoTracking().SingleAsync(s => s.Id == scheduleId, ct);
        var by = await db.StaffUsers.AsNoTracking().Where(s => s.Id == schedule.CreatedByStaffId).Select(s => s.DisplayName).SingleOrDefaultAsync(ct);
        return ToDto(schedule, by);
    }

    private static ReportScheduleDto ToDto(ReportSchedule s, string? createdBy) =>
        new(s.Id, s.Frequency, s.SendAtHour, s.Recipients, s.IsActive, s.NextRunAt, s.LastRunAt, createdBy);

    private async Task<ReportSchedule> FindAsync(Guid scheduleId, CancellationToken ct) =>
        await db.ReportSchedules.SingleOrDefaultAsync(s => s.Id == scheduleId, ct)
        ?? throw AppException.NotFound("schedule_not_found", "Report schedule not found.");

    private async Task<Venue> VenueAsync(Guid venueId, CancellationToken ct) =>
        await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId, ct) ?? throw AppException.NotFound("venue_not_found", "Venue not found.");

    private void Audit(Guid staffUserId, string action, Guid objectId) =>
        db.AuditEntries.Add(new AuditEntry(tenant.RequireTenantId(), staffUserId, action, nameof(ReportSchedule), objectId, clock.GetUtcNow()));
}
