using DineFlow.Api.Common;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace DineFlow.Api.Hubs;

/// <summary>
/// Live order feed for merchant staff screens (blueprint E10). Each connection joins its
/// tenant's group; events carry the venue id so a screen can ignore other branches.
/// Events: "orderPlaced" and "orderChanged", each with an <see cref="OrderDto"/>.
/// </summary>
[Authorize(Policy = Policies.Merchant)]
public class MerchantHub : Hub
{
    public const string Path = "/hubs/merchant";

    public static string Group(Guid tenantId) => $"tenant:{tenantId:N}";

    public override async Task OnConnectedAsync()
    {
        if (Guid.TryParse(Context.User?.FindFirst(DineFlowClaims.TenantId)?.Value, out var tenantId))
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(tenantId));
        await base.OnConnectedAsync();
    }
}

/// <summary>
/// Live updates for guests (P1-ORD-05): "orderChanged" for the guest's own orders, and
/// "billChanged" (a <see cref="BillChangedEvent"/>) for everyone at the same table, whenever an
/// order or payment changes what the table owes.
/// </summary>
[Authorize(Policy = Policies.Guest)]
public class GuestHub : Hub
{
    public const string Path = "/hubs/guest";

    public static string Group(Guid sessionId) => $"session:{sessionId:N}";

    public static string LocationGroup(Guid serviceLocationId) => $"location:{serviceLocationId:N}";

    public override async Task OnConnectedAsync()
    {
        if (Context.User?.GuestSessionId() is { } sessionId)
            await Groups.AddToGroupAsync(Context.ConnectionId, Group(sessionId));
        if (Guid.TryParse(Context.User?.FindFirst(DineFlowClaims.LocationId)?.Value, out var locationId))
            await Groups.AddToGroupAsync(Context.ConnectionId, LocationGroup(locationId));
        await base.OnConnectedAsync();
    }
}

/// <summary>Tells the table to reload its bill. Remaining and FullyPaid are set after a payment.</summary>
public record BillChangedEvent(Guid ServiceLocationId, string Reason, long? RemainingMinor = null, bool? FullyPaid = null);

/// <summary>Sends order events over SignalR. Failures are logged, never thrown at the caller.</summary>
public class SignalROrderNotifier(
    IHubContext<MerchantHub> merchants,
    IHubContext<GuestHub> guests,
    ITenantContext tenant,
    ILogger<SignalROrderNotifier> logger) : IOrderNotifier
{
    public Task OrderPlacedAsync(OrderDto order, CancellationToken ct) =>
        SendAsync(order, "orderPlaced", ct);

    public Task OrderChangedAsync(OrderDto order, CancellationToken ct) =>
        SendAsync(order, "orderChanged", ct);

    private async Task SendAsync(OrderDto order, string merchantEvent, CancellationToken ct)
    {
        try
        {
            if (tenant.TenantId is { } tenantId)
                await merchants.Clients.Group(MerchantHub.Group(tenantId)).SendAsync(merchantEvent, order, ct);
            await guests.Clients.Group(GuestHub.Group(order.GuestSessionId)).SendAsync("orderChanged", order, ct);
            await guests.Clients.Group(GuestHub.LocationGroup(order.ServiceLocationId))
                .SendAsync("billChanged", new BillChangedEvent(order.ServiceLocationId, "order"), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't push {Event} for order {OrderNumber}; screens will catch up on reload.", merchantEvent, order.Number);
        }
    }
}
