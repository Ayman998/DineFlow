using HelloZen.Application.Abstractions;
using HelloZen.Application.Bills;
using HelloZen.Application.Common;
using HelloZen.Application.Pos;
using HelloZen.Domain.Bills;
using HelloZen.Domain.Common;
using HelloZen.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace HelloZen.Application.Orders;

public record OrderCountsDto(int New, int Ready);

/// <summary>
/// The standalone order queue (blueprint 4.4 "Orders", P1-ORD-03/04): staff see orders as they
/// arrive and move them New → Ready → Served, or reject them.
/// </summary>
public class MerchantOrderService(IAppDbContext db, IOrderNotifier notifier, BillService bills, PosSync pos, TimeProvider clock)
{
    public const int PageSize = 100;

    /// <summary>Weeks run Saturday to Friday, as they do in Jordan; the portal labels them the same way.</summary>
    public const DayOfWeek WeekStart = DayOfWeek.Saturday;

    /// <summary>Not ready yet. Accepted and Preparing are only on orders taken on before the three-step flow.</summary>
    private static readonly OrderStatus[] NewStatuses = [OrderStatus.Submitted, OrderStatus.Accepted, OrderStatus.Preparing];

    private static readonly OrderStatus[] KitchenStatuses = [.. NewStatuses, OrderStatus.Ready];

    public async Task<IReadOnlyList<OrderDto>> ListAsync(Guid venueId, OrderQuery request, CancellationToken ct)
    {
        var timeZone = await EnsureVenueAsync(venueId, ct);
        var servedSince = VenueTime.StartOfToday(clock, timeZone);
        IQueryable<Order> query = db.Orders.AsNoTracking().Where(o => o.VenueId == venueId).Include(o => o.Lines);

        if (request.Period != OrderPeriod.All)
        {
            var (from, to) = PeriodRange(request.Period, request.Date ?? DateOnly.FromDateTime(VenueTime.LocalNow(clock, timeZone)), timeZone);
            query = query.Where(o => o.CreatedAt >= from && o.CreatedAt < to);
        }
        if (request.LocationId is { } locationId) query = query.Where(o => o.ServiceLocationId == locationId);
        if (request.Number is { } number) query = query.Where(o => o.Number == number);

        // The kitchen works oldest first; served, closed and rejected orders are looked back on, newest first.
        var skip = (request.Page - 1) * PageSize;
        // A table's later round shares its first one's number, so they go by when they were placed.
        Task<List<Order>> Oldest(IQueryable<Order> orders) => orders
            .OrderBy(o => o.CreatedAt).ThenBy(o => o.Number).ThenBy(o => o.Round).Skip(skip).Take(PageSize).ToListAsync(ct);
        Task<List<Order>> Newest(IQueryable<Order> orders) => orders
            .OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Number).ThenByDescending(o => o.Round).Skip(skip).Take(PageSize).ToListAsync(ct);

        var orders = request.Filter switch
        {
            OrderFilter.New => await Oldest(query.Where(o => NewStatuses.Contains(o.Status))),
            OrderFilter.Ready => await Oldest(query.Where(o => o.Status == OrderStatus.Ready)),
            OrderFilter.Served => await Newest(query.Where(o => o.Status == OrderStatus.Completed)),
            OrderFilter.Closed => await Newest(query.Where(o => o.Status == OrderStatus.Closed)),
            OrderFilter.Rejected => await Newest(query.Where(o => o.Status == OrderStatus.Rejected)),
            OrderFilter.All => await Newest(query),
            // The board: everything not served yet, then what was served since midnight at the venue.
            _ => [
                .. await Oldest(query.Where(o => KitchenStatuses.Contains(o.Status))),
                .. await Newest(query.Where(o => o.Status == OrderStatus.Completed && o.UpdatedAt >= servedSince))
            ]
        };
        return orders.Select(o => o.ToDto()).ToList();
    }

    public async Task<OrderCountsDto> CountsAsync(Guid venueId, CancellationToken ct)
    {
        await EnsureVenueAsync(venueId, ct);
        var counts = await db.Orders
            .Where(o => o.VenueId == venueId && KitchenStatuses.Contains(o.Status))
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        return new OrderCountsDto(NewStatuses.Sum(s => counts.GetValueOrDefault(s)), counts.GetValueOrDefault(OrderStatus.Ready));
    }

    public async Task<OrderDto> GetAsync(Guid orderId, CancellationToken ct) =>
        (await FindAsync(orderId, tracked: false, ct)).ToDto();

    /// <param name="staffUserId">Who made the change; recorded when accepting and serving, for staff ratings.</param>
    public async Task<OrderDto> ChangeStatusAsync(Guid orderId, ChangeOrderStatusRequest request, CancellationToken ct, Guid? staffUserId = null)
    {
        var order = await FindAsync(orderId, tracked: true, ct);
        if (order.Status == request.Status) return order.ToDto(); // e.g. two staff tapping "Mark ready" at once
        var wasNew = order.Status == OrderStatus.Submitted;

        if (!order.CanTransitionTo(request.Status))
            throw AppException.Conflict("invalid_transition", $"Order #{order.Number} is {order.Status} and can't become {request.Status}.");

        // Rejecting takes the order off the table's bill: make sure nobody has paid for it.
        if (request.Status == OrderStatus.Rejected) await bills.PrepareRejectionAsync(order, ct);

        var now = clock.GetUtcNow();
        try { order.TransitionTo(request.Status, request.Reason, now, staffUserId); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_request", ex.Message); }

        // Served after the table had already paid in full: there's nothing left to do for it.
        if (order.Status == OrderStatus.Completed && order.CheckId is { } checkId
            && await db.Checks.AnyAsync(c => c.Id == checkId && c.Status == CheckStatus.Closed, ct))
            order.Close(now);

        await db.SaveChangesAsync(ct);
        // Once staff take a new order on (marking it ready, or accepting it), it goes to the POS
        // (kitchen) when the venue has one that takes orders.
        if (wasNew && order.Status != OrderStatus.Rejected) await pos.OrderAcceptedAsync(order, ct);
        var dto = order.ToDto();
        await notifier.OrderChangedAsync(dto, ct);
        return dto;
    }

    /// <summary>The day, week, month, quarter or year at the venue that <paramref name="date"/> falls in, as UTC instants (end exclusive).</summary>
    internal static (DateTimeOffset From, DateTimeOffset To) PeriodRange(OrderPeriod period, DateOnly date, string timeZoneId)
    {
        var week = date.AddDays(-(((int)date.DayOfWeek - (int)WeekStart + 7) % 7));
        var month = new DateOnly(date.Year, date.Month, 1);
        var quarter = new DateOnly(date.Year, (date.Month - 1) / 3 * 3 + 1, 1);
        var (start, end) = period switch
        {
            OrderPeriod.Day => (date, date.AddDays(1)),
            OrderPeriod.Week => (week, week.AddDays(7)),
            OrderPeriod.Month => (month, month.AddMonths(1)),
            OrderPeriod.Quarter => (quarter, quarter.AddMonths(3)),
            _ => (new DateOnly(date.Year, 1, 1), new DateOnly(date.Year + 1, 1, 1))
        };
        return (VenueTime.StartOf(start, timeZoneId), VenueTime.StartOf(end, timeZoneId));
    }

    private async Task<Order> FindAsync(Guid orderId, bool tracked, CancellationToken ct)
    {
        var query = db.Orders.Include(o => o.Lines).Where(o => o.Id == orderId);
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");
    }

    /// <returns>The venue's time zone.</returns>
    private async Task<string> EnsureVenueAsync(Guid venueId, CancellationToken ct) =>
        await db.Venues.Where(v => v.Id == venueId).Select(v => v.TimeZone).SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
}
