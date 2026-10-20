using DineFlow.Api.Common;
using DineFlow.Application.Common;
using DineFlow.Application.Guests;
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
        if (User.GuestSessionId() != sessionId)
            throw AppException.NotFound("session_not_found", "Session not found.");
        return guests.GetSessionAsync(sessionId, ct);
    }
}
