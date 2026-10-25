using DineFlow.Application.Abstractions;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Pos;

/// <summary>
/// Queues what the POS needs to hear about (blueprint E9): an accepted order, a confirmed payment.
/// Only queues; <see cref="PosDispatcher"/> does the talking, so the order screen and the payment
/// webhook never wait for, or fail because of, the POS.
/// </summary>
public class PosSync(IAppDbContext db, IPosConnectorRegistry connectors, TimeProvider clock)
{
    /// <summary>Staff accepted an order at a venue with a POS that takes orders: send it to the POS (kitchen).</summary>
    public async Task OrderAcceptedAsync(Order order, CancellationToken ct)
    {
        if (order.CheckId is not { } checkId) return;
        var connection = await ActiveConnectionAsync(order.VenueId, PosCapabilities.OrderCreate, ct);
        if (connection is null) return;

        await AddAsync(PosCommand.SendOrder(order.TenantId, order.VenueId, checkId, order.Id, connection.Provider, clock.GetUtcNow()), ct);
    }

    /// <summary>A payment at a table went through: post it to the POS (links aren't on a POS check, so they're not posted).</summary>
    public async Task PaymentSucceededAsync(Payment payment, CancellationToken ct)
    {
        if (payment.CheckId is not { } checkId) return;
        var connection = await ActiveConnectionAsync(payment.VenueId, PosCapabilities.PaymentPosting, ct);
        if (connection is null) return;

        await AddAsync(PosCommand.PostPayment(payment.TenantId, payment.VenueId, checkId, payment.Id, connection.Provider, clock.GetUtcNow()), ct);
    }

    private async Task<PosConnection?> ActiveConnectionAsync(Guid venueId, PosCapabilities needs, CancellationToken ct)
    {
        // Called from webhooks too, where there's no tenant: look the venue up directly.
        var connection = await db.PosConnections.IgnoreQueryFilters().AsNoTracking()
            .SingleOrDefaultAsync(c => c.VenueId == venueId && c.IsActive, ct);
        return connection is not null && connectors.Find(connection.Provider)?.Capabilities.HasFlag(needs) == true ? connection : null;
    }

    private async Task AddAsync(PosCommand command, CancellationToken ct)
    {
        db.PosCommands.Add(command);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Already queued (the unique key refused a duplicate), e.g. the same event handled twice.
            db.ChangeTracker.Clear();
        }
    }
}
