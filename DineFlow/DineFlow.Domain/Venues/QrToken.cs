using DineFlow.Domain.Common;

namespace DineFlow.Domain.Venues;

/// <summary>
/// The opaque token printed in a location's QR code (or NFC tag). Only a SHA-256 hash of the
/// token is stored, so a database leak cannot be turned into working QR codes. Rotating a
/// location retires its current token and issues a new version.
/// </summary>
public class QrToken : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public ServiceLocation ServiceLocation { get; private set; } = null!;
    public int Version { get; private set; }
    /// <summary>Lower-case hex SHA-256 of the raw token.</summary>
    public string TokenHash { get; private set; } = string.Empty;
    public QrTokenStatus Status { get; private set; } = QrTokenStatus.Active;
    public DateTimeOffset? RetiredAt { get; private set; }

    private QrToken() { }

    /// <param name="id">Chosen up front because the raw token is derived from it.</param>
    public QrToken(Guid id, Guid tenantId, Guid serviceLocationId, int version, string tokenHash)
    {
        Id = id;
        TenantId = tenantId;
        ServiceLocationId = serviceLocationId;
        Version = version;
        TokenHash = tokenHash;
    }

    public void Disable() => Status = QrTokenStatus.Disabled;

    public void Enable()
    {
        if (Status == QrTokenStatus.Retired) throw new DomainException("A retired QR code cannot be re-enabled.");
        Status = QrTokenStatus.Active;
    }

    public void Retire(DateTimeOffset at)
    {
        Status = QrTokenStatus.Retired;
        RetiredAt = at;
    }
}

public enum QrTokenStatus
{
    Active = 0,
    /// <summary>Temporarily off; can be enabled again.</summary>
    Disabled = 1,
    /// <summary>Replaced by a newer version after a rotation; never valid again.</summary>
    Retired = 2
}
