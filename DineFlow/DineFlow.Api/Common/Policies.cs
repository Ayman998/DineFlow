using System.Security.Claims;
using DineFlow.Application.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;

namespace DineFlow.Api.Common;

public static class Policies
{
    /// <summary>Any signed-in merchant user.</summary>
    public const string Merchant = "merchant";
    /// <summary>Merchant owners and managers: settings, tables and QR changes.</summary>
    public const string MerchantManager = "merchant-manager";
    /// <summary>A guest holding a session token from a QR scan.</summary>
    public const string Guest = "guest";

    /// <summary>Rate-limit policy for anonymous guest endpoints such as QR resolution.</summary>
    public const string PublicRateLimit = "public";
    /// <summary>Stricter rate limit for merchant sign-in.</summary>
    public const string LoginRateLimit = "login";
}

public static class ClaimsPrincipalExtensions
{
    public static Guid UserId(this ClaimsPrincipal user) =>
        Guid.Parse(user.FindFirstValue(JwtRegisteredClaimNames.Sub)
            ?? throw new InvalidOperationException("Token has no subject."));

    public static Guid? GuestSessionId(this ClaimsPrincipal user) =>
        Guid.TryParse(user.FindFirstValue(DineFlowClaims.SessionId), out var id) ? id : null;
}
