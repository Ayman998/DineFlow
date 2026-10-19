namespace DineFlow.Services;

/// <summary>The tenant the current request acts for; null for platform-level requests.</summary>
public interface ITenantContext
{
    Guid? TenantId { get; }
}
