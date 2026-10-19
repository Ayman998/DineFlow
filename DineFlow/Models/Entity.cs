namespace DineFlow.Models;

public abstract class Entity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public DateTimeOffset CreatedAt { get; protected set; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Marks an entity that belongs to a single tenant. The DbContext applies a global
/// query filter on TenantId, so one merchant can never read another merchant's rows.
/// </summary>
public interface ITenantOwned
{
    Guid TenantId { get; }
}
