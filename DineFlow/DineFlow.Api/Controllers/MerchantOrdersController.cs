using DineFlow.Api.Common;
using DineFlow.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

/// <summary>The standalone order queue (blueprint 9.2: GET/POST /v1/merchant/orders/*). Any staff role.</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
public class MerchantOrdersController(MerchantOrderService orders) : ControllerBase
{
    [HttpGet("v1/merchant/venues/{venueId:guid}/orders")]
    public Task<IReadOnlyList<OrderDto>> List(Guid venueId, [FromQuery] OrderFilter filter, CancellationToken ct) =>
        orders.ListAsync(venueId, filter, ct);

    /// <summary>How many orders are in each active state, for the navigation badge.</summary>
    [HttpGet("v1/merchant/venues/{venueId:guid}/orders/counts")]
    public Task<OrderCountsDto> Counts(Guid venueId, CancellationToken ct) => orders.CountsAsync(venueId, ct);

    [HttpGet("v1/merchant/orders/{orderId:guid}")]
    public Task<OrderDto> Get(Guid orderId, CancellationToken ct) => orders.GetAsync(orderId, ct);

    /// <summary>Accept, reject (with a reason), start preparing, mark ready or mark served.</summary>
    [HttpPost("v1/merchant/orders/{orderId:guid}/status")]
    public Task<OrderDto> ChangeStatus(Guid orderId, ChangeOrderStatusRequest request, CancellationToken ct) =>
        orders.ChangeStatusAsync(orderId, request, ct);
}
