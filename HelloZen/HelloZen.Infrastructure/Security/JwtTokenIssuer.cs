using HelloZen.Application.Abstractions;
using HelloZen.Domain.Sessions;
using HelloZen.Domain.Staff;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace HelloZen.Infrastructure.Security;

/// <summary>HMAC-signed JWTs for guests (one per session) and merchant staff.</summary>
public class JwtTokenIssuer(IOptions<JwtOptions> options, TimeProvider clock) : ITokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();
    private readonly SigningCredentials _credentials = new(
        new SymmetricSecurityKey(options.Value.SigningKeyBytes()), SecurityAlgorithms.HmacSha256);

    public IssuedToken IssueGuestToken(GuestSession session) =>
        Issue(session.ExpiresAt, new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = session.Id.ToString(),
            [HelloZenClaims.Scope] = HelloZenClaims.GuestScope,
            [HelloZenClaims.TenantId] = session.TenantId.ToString(),
            [HelloZenClaims.SessionId] = session.Id.ToString(),
            [HelloZenClaims.LocationId] = session.ServiceLocationId.ToString()
        });

    public IssuedToken IssueStaffToken(StaffUser user) =>
        Issue(clock.GetUtcNow() + options.Value.StaffTokenLifetime, new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Email] = user.Email,
            [HelloZenClaims.Scope] = HelloZenClaims.MerchantScope,
            [HelloZenClaims.TenantId] = user.TenantId.ToString(),
            [HelloZenClaims.Role] = user.Role.ToString()
        });

    private IssuedToken Issue(DateTimeOffset expiresAt, Dictionary<string, object> claims)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Value.Issuer,
            Audience = options.Value.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = expiresAt.UtcDateTime,
            Claims = claims,
            SigningCredentials = _credentials
        });
        return new IssuedToken(token, expiresAt);
    }
}
