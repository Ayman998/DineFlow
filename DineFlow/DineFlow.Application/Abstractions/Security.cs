using DineFlow.Domain.Sessions;
using DineFlow.Domain.Staff;

namespace DineFlow.Application.Abstractions;

/// <summary>Creates and checks the opaque tokens printed in QR codes.</summary>
public interface IQrTokenService
{
    /// <summary>
    /// The raw token for a QR token row. It is derived from the row id with a server-side key,
    /// so it can be re-printed at any time without ever being stored.
    /// </summary>
    string CreateToken(Guid qrTokenId);

    /// <summary>
    /// The raw token in a payment link's URL (blueprint 14.3 "signed, non-guessable"). Derived from
    /// the link id the same way, but with its own purpose, so it can never match a QR token.
    /// </summary>
    string CreatePaymentLinkToken(Guid paymentLinkId);

    /// <summary>Lower-case hex SHA-256 of a raw token; this is what the database stores.</summary>
    string Hash(string token);

    /// <summary>Cheap shape check, so obviously bad tokens never reach the database.</summary>
    bool IsWellFormed(string token);
}

/// <summary>Issues the signed bearer tokens for guests and merchant staff.</summary>
public interface ITokenIssuer
{
    IssuedToken IssueGuestToken(GuestSession session);
    IssuedToken IssueStaffToken(StaffUser user);
}

public record IssuedToken(string Token, DateTimeOffset ExpiresAt);

public interface IPasswordService
{
    string Hash(StaffUser user, string password);
    bool Verify(StaffUser user, string password);
}

/// <summary>Claim names shared by the token issuer and the API's authorization policies.</summary>
public static class DineFlowClaims
{
    public const string TenantId = "tenant_id";
    public const string SessionId = "session_id";
    /// <summary>On guest tokens: the table, so everyone at it can be told when the bill changes.</summary>
    public const string LocationId = "location_id";
    public const string Role = "role";
    public const string Scope = "scope";

    public const string GuestScope = "guest";
    public const string MerchantScope = "merchant";
}
