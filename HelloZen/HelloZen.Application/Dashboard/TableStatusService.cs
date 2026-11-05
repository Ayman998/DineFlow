using HelloZen.Application.Abstractions;
using HelloZen.Application.Common;
using HelloZen.Domain.Bills;
using HelloZen.Domain.Orders;
using HelloZen.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace HelloZen.Application.Dashboard;

/// <summary>
/// The live table view (blueprint 4.4 "Tables", P1-MER-10, mockup "Merchant table view"): every
/// table with what's been paid against its bill and a state staff can act on. A table is flagged
/// once guests place an order, not when they scan the QR code to look at the menu: Ordering while
/// food is on its way, then Unpaid once it's all served. A table whose bill is paid in full is
/// available again straight away.
/// </summary>
public class TableStatusService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    /// <summary>A bill paid in full this recently shows as the free table's last activity.</summary>
    public static readonly TimeSpan RecentlyPaid = TimeSpan.FromMinutes(60);
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
        var paidSince = now - RecentlyPaid;
        var checks = await db.Checks.AsNoTracking()
            .Where(c => c.VenueId == venueId && c.Status == CheckStatus.Open)
            .ToListAsync(ct);
        var checkIds = checks.Select(c => c.Id).ToList();
        // When each table's last bill was paid in full, if that was recent.
        var closedAt = await db.Checks.AsNoTracking()
            .Where(c => c.VenueId == venueId && c.Status == CheckStatus.Closed && c.ClosedAt >= paidSince)
            .GroupBy(c => c.ServiceLocationId)
            .Select(g => new { LocationId = g.Key, Last = g.Max(c => c.ClosedAt) })
            .ToListAsync(ct);

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CheckId != null && checkIds.Contains(o.CheckId.Value) && o.Status != OrderStatus.Rejected)
            .Select(o => new { o.Id, CheckId = o.CheckId!.Value, o.Number, o.Round, o.Status, o.CreatedAt, o.TotalMinor })
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

        long Ledger(Guid checkId, params LedgerEntryType[] types) =>
            ledger.Where(e => e.CheckId == checkId && types.Contains(e.Type)).Sum(e => e.Sum);

        var result = locations.Select(location =>
        {
            var open = checks.FirstOrDefault(c => c.ServiceLocationId == location.Id);
            var shown = open is null ? [] : orders.Where(o => o.CheckId == open.Id).OrderBy(o => o.Number).ThenBy(o => o.Round).ToList();
            var check = shown.Count > 0 ? open : null;

            var total = shown.Sum(o => o.TotalMinor);
            var paid = check is null ? 0 : Ledger(check.Id, LedgerEntryType.BillPayment, LedgerEntryType.Discount);
            var tips = check is null ? 0 : Ledger(check.Id, LedgerEntryType.Tip);
            var remaining = Math.Max(0, total - paid);
            // The latest attempt decides (a success wins a tie with a failure from the same moment).
            var lastPayment = check is null ? null : payments.Where(p => p.CheckId == check.Id)
                .MaxBy(p => (p.CreatedAt, p.Status == PaymentStatus.Failed ? 0 : 1));
            var paidAt = closedAt.FirstOrDefault(c => c.LocationId == location.Id)?.Last;

            TableState state;
            string? attention = null;
            DateTimeOffset? lastActivity;
            if (check is not null)
            {
                var onItsWay = shown.Any(o => o.Status is not (OrderStatus.Completed or OrderStatus.Closed));
                state = remaining == 0 ? TableState.Paid
                    : paid > 0 ? TableState.PartiallyPaid
                    : onItsWay ? TableState.Ordering
                    : TableState.Unpaid;
                if (remaining > 0 && lastPayment is { Status: PaymentStatus.Failed } failed && failed.UpdatedAt >= now - FailedPaymentAttention)
                {
                    state = TableState.Attention;
                    attention = "A payment just failed. The guests may need help paying.";
                }
                lastActivity = Latest(shown.Max(o => (DateTimeOffset?)o.CreatedAt), lastPayment?.UpdatedAt);
            }
            else
            {
                state = TableState.Available;
                lastActivity = paidAt;
            }

            return new TableStatusDto(location.Id, location.Type.ToString(), location.Label, location.DisplayName, location.DiningArea?.Name,
                state, attention, check?.Currency ?? venue.Currency, total, paid, tips, remaining, lastActivity, check?.Id,
                shown.Select(o => new TableOrderDto(o.Id, o.Number, o.Round, o.Status, o.CreatedAt, o.TotalMinor)).ToList());
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
