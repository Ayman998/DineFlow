namespace DineFlow.Services;

/// <summary>
/// Reads the tenant from the authenticated user's "tenant_id" claim.
/// Merchant JWTs and signed guest-session tokens both carry this claim (added in Sprint 1).
/// </summary>
public class ClaimsTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public const string ClaimType = "tenant_id";

    public Guid? TenantId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(ClaimType)?.Value, out var id) ? id : null;
}
