using System.Globalization;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Finance;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Dashboard;

/// <summary>
/// The merchant overview (blueprint 4.4, P1-MER-08/09, mockup "Merchant overview"): revenue, tips,
/// orders, refunds and failures for today and a chosen period, the last week against the week
/// before, a revenue and tips trend, and what needs attention. Days and weeks follow the
/// venue's time zone.
/// </summary>
public class OverviewService(IAppDbContext db, ITenantContext tenant, TableStatusService tables, TimeProvider clock)
{
    private const int MaxTrendPoints = 400;

    public async Task<OverviewDto> GetAsync(Guid venueId, OverviewPeriod period, TrendGranularity? granularity, CancellationToken ct)
    {
        tenant.RequireTenantId();
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == venueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var tz) ? tz : TimeZoneInfo.Utc;

        var now = clock.GetUtcNow();
        var to = now.AddSeconds(1);
        var localToday = TimeZoneInfo.ConvertTime(now, zone).Date;
        var today = Utc(localToday, zone);
        var from = period switch
        {
            OverviewPeriod.Week => Utc(localToday.AddDays(-6), zone),
            OverviewPeriod.Month => Utc(new DateTime(localToday.Year, localToday.Month, 1), zone),
            OverviewPeriod.Quarter => Utc(new DateTime(localToday.Year, (localToday.Month - 1) / 3 * 3 + 1, 1), zone),
            _ => today
        };
        var grain = granularity ?? period switch
        {
            OverviewPeriod.Today => TrendGranularity.Hourly,
            OverviewPeriod.Quarter => TrendGranularity.Weekly,
            _ => TrendGranularity.Daily
        };

        var weekAgo = now - TimeSpan.FromDays(7);
        var twoWeeksAgo = now - TimeSpan.FromDays(14);
        var revenue = await LedgerAsync(venueId, weekAgo, to, LedgerEntryType.BillPayment, ct);
        var previousRevenue = await LedgerAsync(venueId, twoWeeksAgo, weekAgo, LedgerEntryType.BillPayment, ct);
        var week = new WeekComparisonDto(revenue, previousRevenue,
            previousRevenue == 0 ? null : Math.Round((double)(revenue - previousRevenue) / previousRevenue, 4),
            await OrdersAsync(venueId, weekAgo, to, ct), await OrdersAsync(venueId, twoWeeksAgo, weekAgo, ct));

        return new OverviewDto(period, from, to, venue.Currency, venue.TimeZone,
            await KpisAsync(venueId, from, to, ct), await KpisAsync(venueId, today, to, ct), week,
            grain, await TrendAsync(venueId, from, to, grain, zone, ct),
            await AttentionAsync(venueId, today, now, zone, ct));
    }

    private async Task<OverviewKpisDto> KpisAsync(Guid venueId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) => new(
        await LedgerAsync(venueId, from, to, LedgerEntryType.BillPayment, ct),
        await LedgerAsync(venueId, from, to, LedgerEntryType.Tip, ct),
        await OrdersAsync(venueId, from, to, ct),
        await LedgerAsync(venueId, from, to, LedgerEntryType.Refund, ct),
        await db.Payments.AsNoTracking().CountAsync(p => p.VenueId == venueId && p.Status == PaymentStatus.Succeeded
            && p.SucceededAt >= from && p.SucceededAt < to, ct),
        await db.Payments.AsNoTracking().CountAsync(p => p.VenueId == venueId && p.Status == PaymentStatus.Failed
            && p.UpdatedAt >= from && p.UpdatedAt < to, ct));

    private async Task<long> LedgerAsync(Guid venueId, DateTimeOffset from, DateTimeOffset to, LedgerEntryType type, CancellationToken ct) =>
        await db.LedgerEntries.AsNoTracking()
            .Where(e => e.VenueId == venueId && e.Type == type && e.CreatedAt >= from && e.CreatedAt < to)
            .SumAsync(e => (long?)e.AmountMinor, ct) ?? 0;

    private Task<int> OrdersAsync(Guid venueId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct) =>
        db.Orders.AsNoTracking().CountAsync(o => o.VenueId == venueId && o.Status != OrderStatus.Rejected
            && o.CreatedAt >= from && o.CreatedAt < to, ct);

    /// <summary>Revenue and tips per hour, day, week (from the period's start) or month, in the venue's time zone.</summary>
    private async Task<IReadOnlyList<TrendPointDto>> TrendAsync(
        Guid venueId, DateTimeOffset from, DateTimeOffset to, TrendGranularity grain, TimeZoneInfo zone, CancellationToken ct)
    {
        var starts = new List<DateTimeOffset>();
        var local = TimeZoneInfo.ConvertTime(from, zone).DateTime;
        while (Utc(local, zone) < to)
        {
            starts.Add(Utc(local, zone));
            if (starts.Count > MaxTrendPoints)
                throw AppException.BadRequest("too_many_points", "Choose a coarser view for this period, e.g. daily or weekly.");
            local = grain switch
            {
                TrendGranularity.Hourly => local.AddHours(1),
                TrendGranularity.Daily => local.AddDays(1),
                TrendGranularity.Weekly => local.AddDays(7),
                _ => local.AddMonths(1)
            };
        }

        var entries = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.VenueId == venueId && (e.Type == LedgerEntryType.BillPayment || e.Type == LedgerEntryType.Tip)
                && e.CreatedAt >= from && e.CreatedAt < to)
            .Select(e => new { e.CreatedAt, e.Type, e.AmountMinor })
            .ToListAsync(ct);

        var revenue = new long[starts.Count];
        var tips = new long[starts.Count];
        foreach (var e in entries)
        {
            var i = starts.FindLastIndex(s => s <= e.CreatedAt);
            if (i < 0) continue;
            if (e.Type == LedgerEntryType.Tip) tips[i] += e.AmountMinor;
            else revenue[i] += e.AmountMinor;
        }
        return starts.Select((s, i) => new TrendPointDto(s, revenue[i], tips[i])).ToList();
    }

    /// <summary>The "Needs attention" list: failures, tables with money left, waiting orders, unhappy guests, pending refunds.</summary>
    private async Task<IReadOnlyList<AttentionDto>> AttentionAsync(Guid venueId, DateTimeOffset today, DateTimeOffset now, TimeZoneInfo zone, CancellationToken ct)
    {
        var venue = await db.Venues.AsNoTracking().SingleAsync(v => v.Id == venueId, ct);
        string Money(long minor) => FormatMoney(minor, venue.Currency);
        var items = new List<AttentionDto>();

        var failures = await db.Payments.AsNoTracking()
            .Where(p => p.VenueId == venueId && p.Status == PaymentStatus.Failed && p.UpdatedAt >= today)
            .OrderByDescending(p => p.UpdatedAt).Select(p => new { p.CheckId, p.PaymentLinkId, p.UpdatedAt }).ToListAsync(ct);
        if (failures.Count > 0)
        {
            var last = failures[0];
            var linkCode = last.PaymentLinkId is { } linkId
                ? await db.PaymentLinks.AsNoTracking().Where(l => l.Id == linkId).Select(l => l.Code).SingleAsync(ct)
                : null;
            var location = linkCode is not null ? null
                : await db.Checks.AsNoTracking().Where(c => c.Id == last.CheckId)
                    .Join(db.ServiceLocations.AsNoTracking(), c => c.ServiceLocationId, l => l.Id, (c, l) => l)
                    .SingleOrDefaultAsync(ct);
            var label = linkCode is not null ? "Payment link " + linkCode : location?.DisplayName;
            var time = TimeZoneInfo.ConvertTime(last.UpdatedAt, zone).ToString("HH:mm", CultureInfo.InvariantCulture);
            items.Add(new("payment", failures.Count == 1 ? "1 payment failure today" : $"{failures.Count} payment failures today",
                $"Last: {label} · {time}", failures.Count,
                "payment_failures", new { linkCode, locationType = location?.Type.ToString(), label = location?.Label, time }));
        }

        // Payments the POS hasn't got yet after a while (blueprint "POS offline for an extended period"), or that it refused.
        var stuckSince = now - PosCommand.AttentionAfter;
        var stuck = await db.PosCommands.AsNoTracking().Where(c => c.VenueId == venueId
            && (c.Status == PosCommandStatus.Failed || (c.Status == PosCommandStatus.Pending && c.CreatedAt <= stuckSince)))
            .GroupBy(c => c.Status).Select(g => new { g.Key, Count = g.Count() }).ToListAsync(ct);
        if (stuck.Sum(s => s.Count) is var stuckCount and > 0)
        {
            var refused = stuck.Where(s => s.Key == PosCommandStatus.Failed).Sum(s => s.Count);
            items.Add(new("pos", stuckCount == 1 ? "1 item not in the POS yet" : $"{stuckCount} items not in the POS yet",
                refused > 0 ? $"{refused} refused by the POS: fix and retry under Settings → POS" : "Waiting for the POS to come back; nobody is charged again", stuckCount,
                "pos_stuck", new { refused }));
        }

        // Reconciliation: money that doesn't add up between DineFlow, the acquirer and the POS.
        var discrepancies = await db.Discrepancies.AsNoTracking().CountAsync(d => d.VenueId == venueId && d.Status == DiscrepancyStatus.Open, ct);
        if (discrepancies > 0)
            items.Add(new("reconciliation", discrepancies == 1 ? "1 reconciliation difference" : $"{discrepancies} reconciliation differences",
                "Between DineFlow, the acquirer's settlements and the POS", discrepancies, "reconciliation"));

        var waiting = await db.Orders.AsNoTracking().CountAsync(o => o.VenueId == venueId && o.Status == OrderStatus.Submitted
            && o.CreatedAt <= now - TimeSpan.FromMinutes(5), ct);
        if (waiting > 0)
            items.Add(new("orders", waiting == 1 ? "1 order waiting to be accepted" : $"{waiting} orders waiting to be accepted",
                "Placed more than 5 minutes ago", waiting, "orders_waiting", new { minutes = 5 }));

        var tableList = await tables.ListAsync(venueId, ct);
        var partial = tableList.Where(t => t.State == TableState.PartiallyPaid).ToList();
        if (partial.Count > 0)
            items.Add(new("tables", partial.Count == 1 ? "1 table partially paid" : $"{partial.Count} tables partially paid",
                $"Open balance {Money(partial.Sum(t => t.RemainingMinor))}", partial.Count,
                "tables_partially_paid", new { openMinor = partial.Sum(t => t.RemainingMinor) }));
        var unpaid = tableList.Where(t => t.State is TableState.Unpaid or TableState.Attention).ToList();
        if (unpaid.Count > 0)
            items.Add(new("tables", unpaid.Count == 1 ? "1 table with an unpaid bill" : $"{unpaid.Count} tables with unpaid bills",
                $"Open balance {Money(unpaid.Sum(t => t.RemainingMinor))}", unpaid.Count,
                "tables_unpaid", new { openMinor = unpaid.Sum(t => t.RemainingMinor) }));

        var weekAgo = now - TimeSpan.FromDays(7);
        var unhappy = await db.Reviews.AsNoTracking().CountAsync(r => r.VenueId == venueId && r.Rating <= 3
            && r.FollowedUpAt == null && r.CreatedAt >= weekAgo, ct);
        if (unhappy > 0)
            items.Add(new("review", unhappy == 1 ? "1 new 1–3 star feedback" : $"{unhappy} new 1–3 star feedbacks", "Follow up with guests", unhappy,
                "unhappy_feedback"));

        var refunds = await db.Refunds.AsNoTracking().CountAsync(r => r.VenueId == venueId && r.Status == RefundStatus.Pending, ct);
        if (refunds > 0)
            items.Add(new("refund", refunds == 1 ? "1 refund waiting for the provider" : $"{refunds} refunds waiting for the provider",
                "They complete when the provider confirms", refunds, "refunds_pending"));

        return items;
    }

    /// <summary>A local date and time in the venue's zone, as UTC.</summary>
    private static DateTimeOffset Utc(DateTime local, TimeZoneInfo zone)
    {
        var unspecified = DateTime.SpecifyKind(local, DateTimeKind.Unspecified);
        return new DateTimeOffset(unspecified, zone.GetUtcOffset(unspecified)).ToUniversalTime();
    }

    private static string FormatMoney(long minor, string currency)
    {
        var digits = currency is "KWD" or "BHD" or "OMR" or "JOD" ? 3 : 2;
        return $"{currency} {(minor / (decimal)Math.Pow(10, digits)).ToString($"N{digits}", CultureInfo.InvariantCulture)}";
    }
}
