using DineFlow.Application.Abstractions;

namespace DineFlow.Api.Tenancy;

/// <summary>
/// Reads the tenant from the bearer token's "tenant_id" claim. Merchant JWTs and guest
/// session tokens both carry it, so every query runs inside the caller's tenant.
/// </summary>
public class ClaimsTenantContext(IHttpContextAccessor httpContextAccessor) : ITenantContext
{
    public Guid? TenantId =>
        Guid.TryParse(httpContextAccessor.HttpContext?.User.FindFirst(DineFlowClaims.TenantId)?.Value, out var id) ? id : null;
}
