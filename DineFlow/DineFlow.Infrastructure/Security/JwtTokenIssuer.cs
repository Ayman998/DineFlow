using DineFlow.Application.Abstractions;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Staff;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace DineFlow.Infrastructure.Security;

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
            [DineFlowClaims.Scope] = DineFlowClaims.GuestScope,
            [DineFlowClaims.TenantId] = session.TenantId.ToString(),
            [DineFlowClaims.SessionId] = session.Id.ToString(),
            [DineFlowClaims.LocationId] = session.ServiceLocationId.ToString()
        });

    public IssuedToken IssueStaffToken(StaffUser user) =>
        Issue(clock.GetUtcNow() + options.Value.StaffTokenLifetime, new Dictionary<string, object>
        {
            [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
            [JwtRegisteredClaimNames.Email] = user.Email,
            [DineFlowClaims.Scope] = DineFlowClaims.MerchantScope,
            [DineFlowClaims.TenantId] = user.TenantId.ToString(),
            [DineFlowClaims.Role] = user.Role.ToString()
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
