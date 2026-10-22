using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Dashboard;

/// <summary>
/// The live table view (blueprint 4.4 "Tables", P1-MER-10, mockup "Merchant table view"): every
/// table with what's been paid against its bill and a state staff can act on.
/// </summary>
public class TableStatusService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    /// <summary>A settled table keeps showing "Paid" this long, while the guests may still be there.</summary>
    public static readonly TimeSpan PaidVisibleFor = TimeSpan.FromHours(2);
    /// <summary>A scan this recent with nothing ordered yet means guests are choosing.</summary>
    public static readonly TimeSpan OrderingWindow = TimeSpan.FromMinutes(60);
    /// <summary>A failed payment this recent, with money still owed, needs a member of staff.</summary>
    public static readonly TimeSpan FailedPaymentAttention = TimeSpan.FromMinutes(30);

    public async Task<IReadOnlyList<TableStatusDto>> ListAsync(Guid venueId, CancellationToken ct)
    {
        tenant.RequireTenantId();
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == venueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var now = clock.GetUtcNow();

        var locations = await db.ServiceLocations.AsNoTracking().Include(l => l.DiningArea)
            .Where(l => l.VenueId == venueId && l.IsActive).ToListAsync(ct);
        var paidSince = now - PaidVisibleFor;
        var checks = await db.Checks.AsNoTracking()
            .Where(c => c.VenueId == venueId && (c.Status == CheckStatus.Open || c.ClosedAt >= paidSince))
            .ToListAsync(ct);
        var checkIds = checks.Select(c => c.Id).ToList();

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CheckId != null && checkIds.Contains(o.CheckId.Value) && o.Status != OrderStatus.Rejected)
            .Select(o => new { o.Id, CheckId = o.CheckId!.Value, o.Number, o.Status, o.CreatedAt, o.TotalMinor })
            .ToListAsync(ct);
        var ledger = await db.LedgerEntries.AsNoTracking()
            .Where(e => e.CheckId != null && checkIds.Contains(e.CheckId.Value))
            .GroupBy(e => new { e.CheckId, e.Type })
            .Select(g => new { g.Key.CheckId, g.Key.Type, Sum = g.Sum(e => e.AmountMinor) })
            .ToListAsync(ct);
        var payments = await db.Payments.AsNoTracking()
            .Where(p => p.CheckId != null && checkIds.Contains(p.CheckId.Value))
            .Select(p => new { p.CheckId, p.Status, p.CreatedAt, p.UpdatedAt })
            .ToListAsync(ct);
        var scannedSince = now - OrderingWindow;
        var locationIds = locations.Select(l => l.Id).ToList();
        var scans = await db.GuestSessions.AsNoTracking()
            .Where(s => locationIds.Contains(s.ServiceLocationId) && s.CreatedAt >= scannedSince)
            .GroupBy(s => s.ServiceLocationId)
            .Select(g => new { LocationId = g.Key, Last = g.Max(s => s.CreatedAt) })
            .ToListAsync(ct);

        long Ledger(Guid checkId, params LedgerEntryType[] types) =>
            ledger.Where(e => e.CheckId == checkId && types.Contains(e.Type)).Sum(e => e.Sum);

        var result = locations.Select(location =>
        {
            var open = checks.FirstOrDefault(c => c.ServiceLocationId == location.Id && c.Status == CheckStatus.Open);
            var openOrders = open is null ? [] : orders.Where(o => o.CheckId == open.Id).ToList();
            var check = openOrders.Count > 0
                ? open
                : checks.Where(c => c.ServiceLocationId == location.Id && c.Status == CheckStatus.Closed).MaxBy(c => c.ClosedAt);
            var shown = check is null ? [] : orders.Where(o => o.CheckId == check.Id).OrderBy(o => o.Number).ToList();

            var total = shown.Sum(o => o.TotalMinor);
            var paid = check is null ? 0 : Ledger(check.Id, LedgerEntryType.BillPayment, LedgerEntryType.Discount);
            var tips = check is null ? 0 : Ledger(check.Id, LedgerEntryType.Tip);
            var remaining = Math.Max(0, total - paid);
            // The latest attempt decides (a success wins a tie with a failure from the same moment).
            var lastPayment = check is null ? null : payments.Where(p => p.CheckId == check.Id)
                .MaxBy(p => (p.CreatedAt, p.Status == PaymentStatus.Failed ? 0 : 1));
            var scan = scans.FirstOrDefault(s => s.LocationId == location.Id)?.Last;

            TableState state;
            string? attention = null;
            DateTimeOffset? lastActivity;
            if (check is not null && check.Status == CheckStatus.Open)
            {
                state = remaining == 0 ? TableState.Paid : paid == 0 ? TableState.Unpaid : TableState.PartiallyPaid;
                if (remaining > 0 && lastPayment is { Status: PaymentStatus.Failed } failed && failed.UpdatedAt >= now - FailedPaymentAttention)
                {
                    state = TableState.Attention;
                    attention = "A payment just failed. The guests may need help paying.";
                }
                lastActivity = Latest(shown.Max(o => (DateTimeOffset?)o.CreatedAt), lastPayment?.UpdatedAt);
            }
            else if (check is not null)
            {
                state = TableState.Paid;
                lastActivity = check.ClosedAt;
            }
            else
            {
                state = scan is not null ? TableState.Ordering : TableState.Available;
                lastActivity = scan;
            }

            return new TableStatusDto(location.Id, location.Type.ToString(), location.Label, location.DisplayName, location.DiningArea?.Name,
                state, attention, check?.Currency ?? venue.Currency, total, paid, tips, remaining, lastActivity, check?.Id,
                shown.Select(o => new TableOrderDto(o.Id, o.Number, o.Status, o.CreatedAt, o.TotalMinor)).ToList());
        });

        // Areas in the order the venue set; then tables numerically (2 before 10).
        var areaOrder = locations.ToDictionary(l => l.Id, l => l.DiningArea?.SortOrder ?? int.MaxValue);
        return result
            .OrderBy(t => areaOrder[t.LocationId]).ThenBy(t => t.AreaName).ThenBy(t => t.LocationType)
            .ThenBy(t => int.TryParse(t.Label, out var n) ? n : int.MaxValue).ThenBy(t => t.Label)
            .ToList();
    }

    private static DateTimeOffset? Latest(DateTimeOffset? a, DateTimeOffset? b) => a > b ? a : b ?? a;
}
