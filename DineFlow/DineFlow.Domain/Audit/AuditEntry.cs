using DineFlow.Domain.Common;

namespace DineFlow.Domain.Audit;

/// <summary>
/// An append-only record of a sensitive action (blueprint AuditLog, section 11), e.g. a staff
/// member viewing a guest's contact details. Never edited or deleted by the application.
/// </summary>
public class AuditEntry : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid StaffUserId { get; private set; }
    /// <summary>What happened, e.g. "review.contact.viewed".</summary>
    public string Action { get; private set; } = string.Empty;
    public string ObjectType { get; private set; } = string.Empty;
    public Guid ObjectId { get; private set; }

    private AuditEntry() { }

    public AuditEntry(Guid tenantId, Guid staffUserId, string action, string objectType, Guid objectId, DateTimeOffset now)
    {
        TenantId = tenantId;
        StaffUserId = staffUserId;
        Action = action;
        ObjectType = objectType;
        ObjectId = objectId;
        CreatedAt = now;
    }
}
