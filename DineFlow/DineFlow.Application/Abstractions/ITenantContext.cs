using DineFlow.Application.Common;

namespace DineFlow.Application.Abstractions;

/// <summary>The tenant the current request acts for; null for platform-level requests.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}

public static class TenantContextExtensions
{
    /// <summary>The current tenant, or a 401 when the request is not signed in to one.</summary>
    public static Guid RequireTenantId(this ITenantContext context) =>
        context.TenantId ?? throw AppException.Unauthorized("tenant_required", "Sign in to a merchant account first.");
}
