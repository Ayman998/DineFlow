using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Orders;

/// <summary>Placing and following orders from a guest's table session (P1-ORD-02, P1-ORD-05).</summary>
public class GuestOrderService(IAppDbContext db, CartQuoteService quotes, IOrderNotifier notifier, TimeProvider clock)
{
    private const int NumberAttempts = 3;

    /// <summary>
    /// Places the order exactly once per <see cref="PlaceOrderRequest.ClientRequestId"/>: a retry
    /// after a timeout returns the order that was already placed instead of creating another.
    /// </summary>
    public async Task<OrderDto> PlaceAsync(Guid sessionId, PlaceOrderRequest request, CancellationToken ct)
    {
        if (await FindExistingAsync(sessionId, request.ClientRequestId, ct) is { } existing) return existing;

        var table = await quotes.LoadTableAsync(sessionId, ct);
        var priced = await quotes.PriceAsync(table, request.Lines, ct);

        if (!priced.Quote.CanOrder)
            throw AppException.Conflict("cart_invalid", "Some items can't be ordered right now.", priced.Quote);
        if (request.ExpectedTotalMinor is { } expected && expected != priced.Quote.TotalMinor)
            throw AppException.Conflict("price_changed", "Prices have changed since you added these items. Please check your order.", priced.Quote);

        var venue = table.Venue;
        var location = table.Location;

        for (var attempt = 1; ; attempt++)
        {
            var number = (await db.Orders.Where(o => o.VenueId == venue.Id).MaxAsync(o => (int?)o.Number, ct) ?? Order.FirstNumber - 1) + 1;
            var order = new Order(
                venue.TenantId, venue.Id, location.Id, sessionId, request.ClientRequestId, number,
                location.Type.ToString(), location.Label, location.DiningArea?.Name,
                venue.Currency, venue.ServiceChargeBasisPoints, venue.TaxBasisPoints, venue.PricesIncludeTax,
                priced.CreateLines(), request.Note, clock.GetUtcNow());

            // The order joins the table's open bill; the first order at a table opens one.
            var check = await db.Checks.SingleOrDefaultAsync(c => c.ServiceLocationId == location.Id && c.Status == CheckStatus.Open, ct);
            if (check is null)
            {
                check = new Check(venue.TenantId, venue.Id, location.Id, venue.Currency, clock.GetUtcNow());
                db.Checks.Add(check);
            }
            order.AssignToCheck(check.Id);

            db.Orders.Add(order);
            try
            {
                await db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException) when (attempt < NumberAttempts)
            {
                // Either the same submission won a race (return it), or another order took this number or
                // opened the table's bill at the same moment (try again with the latest of both).
                db.ChangeTracker.Clear();
                if (await FindExistingAsync(sessionId, request.ClientRequestId, ct) is { } raced) return raced;
                continue;
            }

            var dto = order.ToDto();
            await notifier.OrderPlacedAsync(dto, ct);
            return dto;
        }
    }

    /// <summary>All orders from this table session, newest first, for the guest's order tracker.</summary>
    public async Task<IReadOnlyList<OrderDto>> ListAsync(Guid sessionId, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.GuestSessionId == sessionId)
            .Include(o => o.Lines)
            .OrderByDescending(o => o.Number)
            .ToListAsync(ct);
        return orders.Select(o => o.ToDto()).ToList();
    }

    private async Task<OrderDto?> FindExistingAsync(Guid sessionId, Guid clientRequestId, CancellationToken ct)
    {
        var order = await db.Orders.AsNoTracking()
            .Include(o => o.Lines)
            .SingleOrDefaultAsync(o => o.GuestSessionId == sessionId && o.ClientRequestId == clientRequestId, ct);
        return order?.ToDto();
    }
}
