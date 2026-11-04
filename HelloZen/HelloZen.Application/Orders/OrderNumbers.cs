using HelloZen.Application.Abstractions;
using HelloZen.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HelloZen.Application.Orders;

/// <summary>
/// Every order on a table's bill shares the bill's number, so staff and guests talk about one
/// number per visit; each new bill takes the venue's next one. Bills hold the numbers, unique per
/// venue, so two bills opened at the same moment can't share one.
/// </summary>
internal static class OrderNumbers
{
    public static async Task<int> NextAsync(IAppDbContext db, Guid venueId, CancellationToken ct)
    {
        var orders = await db.Orders.Where(o => o.VenueId == venueId).MaxAsync(o => (int?)o.Number, ct);
        var checks = await db.Checks.Where(c => c.VenueId == venueId).MaxAsync(c => c.Number, ct);
        return Math.Max(orders ?? Order.FirstNumber - 1, checks ?? Order.FirstNumber - 1) + 1;
    }

    /// <summary>The bill's next round: 1 for its first order.</summary>
    public static async Task<int> NextRoundAsync(IAppDbContext db, Guid checkId, CancellationToken ct) =>
        (await db.Orders.Where(o => o.CheckId == checkId).MaxAsync(o => (int?)o.Round, ct) ?? 0) + 1;
}
