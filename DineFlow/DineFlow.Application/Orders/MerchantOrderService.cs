using DineFlow.Application.Abstractions;
using DineFlow.Application.Bills;
using DineFlow.Application.Common;
using DineFlow.Application.Pos;
using DineFlow.Domain.Common;
using DineFlow.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Orders;

public record OrderCountsDto(int New, int Accepted, int Preparing, int Ready);

/// <summary>
/// The standalone order queue (blueprint 4.4 "Orders", P1-ORD-03/04): staff see orders as they
/// arrive and move them through accepted → preparing → ready → served, or reject them.
/// </summary>
public class MerchantOrderService(IAppDbContext db, IOrderNotifier notifier, BillService bills, PosSync pos, TimeProvider clock)
{
    private const int PageSize = 100;

    private static readonly OrderStatus[] ActiveStatuses =
        [OrderStatus.Submitted, OrderStatus.Accepted, OrderStatus.Preparing, OrderStatus.Ready];

    public async Task<IReadOnlyList<OrderDto>> ListAsync(Guid venueId, OrderFilter filter, CancellationToken ct)
    {
        await EnsureVenueAsync(venueId, ct);
        var query = db.Orders.AsNoTracking().Where(o => o.VenueId == venueId).Include(o => o.Lines);

        var orders = filter switch
        {
            // The kitchen works oldest first.
            OrderFilter.Active => await query.Where(o => ActiveStatuses.Contains(o.Status)).OrderBy(o => o.Number).Take(PageSize).ToListAsync(ct),
            OrderFilter.New => await query.Where(o => o.Status == OrderStatus.Submitted).OrderBy(o => o.Number).Take(PageSize).ToListAsync(ct),
            OrderFilter.Completed => await query.Where(o => o.Status == OrderStatus.Completed).OrderByDescending(o => o.Number).Take(PageSize).ToListAsync(ct),
            _ => await query.Where(o => o.Status == OrderStatus.Rejected).OrderByDescending(o => o.Number).Take(PageSize).ToListAsync(ct)
        };
        return orders.Select(o => o.ToDto()).ToList();
    }

    public async Task<OrderCountsDto> CountsAsync(Guid venueId, CancellationToken ct)
    {
        await EnsureVenueAsync(venueId, ct);
        var counts = await db.Orders
            .Where(o => o.VenueId == venueId && ActiveStatuses.Contains(o.Status))
            .GroupBy(o => o.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.Status, x => x.Count, ct);

        return new OrderCountsDto(
            counts.GetValueOrDefault(OrderStatus.Submitted), counts.GetValueOrDefault(OrderStatus.Accepted),
            counts.GetValueOrDefault(OrderStatus.Preparing), counts.GetValueOrDefault(OrderStatus.Ready));
    }

    public async Task<OrderDto> GetAsync(Guid orderId, CancellationToken ct) =>
        (await FindAsync(orderId, tracked: false, ct)).ToDto();

    /// <param name="staffUserId">Who made the change; recorded when accepting and serving, for staff ratings.</param>
    public async Task<OrderDto> ChangeStatusAsync(Guid orderId, ChangeOrderStatusRequest request, CancellationToken ct, Guid? staffUserId = null)
    {
        var order = await FindAsync(orderId, tracked: true, ct);
        if (order.Status == request.Status) return order.ToDto(); // e.g. two staff tapping "Accept" at once

        if (!order.CanTransitionTo(request.Status))
            throw AppException.Conflict("invalid_transition", $"Order #{order.Number} is {order.Status} and can't become {request.Status}.");

        // Rejecting takes the order off the table's bill: make sure nobody has paid for it.
        if (request.Status == OrderStatus.Rejected) await bills.PrepareRejectionAsync(order, ct);

        try { order.TransitionTo(request.Status, request.Reason, clock.GetUtcNow(), staffUserId); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_request", ex.Message); }

        await db.SaveChangesAsync(ct);
        // Accepted orders go to the POS (kitchen) when the venue has one that takes orders.
        if (order.Status == OrderStatus.Accepted) await pos.OrderAcceptedAsync(order, ct);
        var dto = order.ToDto();
        await notifier.OrderChangedAsync(dto, ct);
        return dto;
    }

    private async Task<Order> FindAsync(Guid orderId, bool tracked, CancellationToken ct)
    {
        var query = db.Orders.Include(o => o.Lines).Where(o => o.Id == orderId);
        return await (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct)
            ?? throw AppException.NotFound("order_not_found", "Order not found.");
    }

    private async Task EnsureVenueAsync(Guid venueId, CancellationToken ct)
    {
        if (!await db.Venues.AnyAsync(v => v.Id == venueId, ct))
            throw AppException.NotFound("venue_not_found", "Venue not found.");
    }
}
