using DineFlow.Application.Orders;

namespace DineFlow.Application.Abstractions;

/// <summary>
/// Pushes order events to whoever is watching: the merchant's staff screens and the guest's
/// phone (blueprint E10, P1-STAFF-01). Called after the change is saved; delivery is best effort,
/// and screens reload the queue when they reconnect.
/// </summary>
public interface IOrderNotifier
{
    Task OrderPlacedAsync(OrderDto order, CancellationToken ct);
    Task OrderChangedAsync(OrderDto order, CancellationToken ct);
}
