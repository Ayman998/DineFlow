using DineFlow.Api.Common;
using DineFlow.Application.Common;
using DineFlow.Application.Guests;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineFlow.Api.Controllers;

/// <summary>Guest-facing endpoints used by the QR web app (blueprint 9.1).</summary>
[ApiController]
[Route("v1/public")]
public class PublicController(GuestService guests) : ControllerBase
{
    /// <summary>Health ping for the guest app's status indicator.</summary>
    [HttpGet("ping")]
    [AllowAnonymous]
    public IActionResult Ping() => Ok(new { service = "DineFlow.Api", time = DateTimeOffset.UtcNow });

    /// <summary>
    /// Resolves a scanned QR token and starts a guest session. Returns the session token the
    /// guest app sends as a bearer token on later calls.
    /// </summary>
    [HttpGet("qr/{token}")]
    [AllowAnonymous]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<QrResolutionDto> ResolveQr(string token, [FromQuery] string? locale, CancellationToken ct) =>
        guests.ResolveQrAsync(token, locale, ct);

    /// <summary>Reloads the caller's own session, e.g. after a page refresh.</summary>
    [HttpGet("sessions/{sessionId:guid}")]
    [Authorize(Policy = Policies.Guest)]
    public Task<GuestContextDto> GetSession(Guid sessionId, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return guests.GetSessionAsync(sessionId, ct);
    }

    /// <summary>The venue's published menus for this session, with what can be ordered right now.</summary>
    [HttpGet("sessions/{sessionId:guid}/menu")]
    [Authorize(Policy = Policies.Guest)]
    public Task<MenuTreeDto> GetMenu(Guid sessionId, [FromServices] GuestMenuService menus, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return menus.GetMenuAsync(sessionId, ct);
    }

    /// <summary>Prices the cart on the server and reports anything that can't be ordered right now.</summary>
    [HttpPost("sessions/{sessionId:guid}/cart/validate")]
    [Authorize(Policy = Policies.Guest)]
    public Task<CartQuoteDto> ValidateCart(Guid sessionId, CartRequest request, [FromServices] CartQuoteService quotes, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return quotes.QuoteAsync(sessionId, request, ct);
    }

    /// <summary>
    /// Places an order. Safe to retry with the same clientRequestId. Returns 409 with an updated
    /// quote in "details" when items changed or the total differs from what the guest saw.
    /// </summary>
    [HttpPost("sessions/{sessionId:guid}/orders")]
    [Authorize(Policy = Policies.Guest)]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<OrderDto> PlaceOrder(Guid sessionId, PlaceOrderRequest request, [FromServices] GuestOrderService orders, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return orders.PlaceAsync(sessionId, request, ct);
    }

    [HttpGet("sessions/{sessionId:guid}/orders")]
    [Authorize(Policy = Policies.Guest)]
    public Task<IReadOnlyList<OrderDto>> ListOrders(Guid sessionId, [FromServices] GuestOrderService orders, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return orders.ListAsync(sessionId, ct);
    }

    private void EnsureOwnSession(Guid sessionId)
    {
        if (User.GuestSessionId() != sessionId)
            throw AppException.NotFound("session_not_found", "Session not found.");
    }
}
