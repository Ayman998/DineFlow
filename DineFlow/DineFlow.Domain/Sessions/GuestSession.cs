using DineFlow.Domain.Common;

namespace DineFlow.Domain.Sessions;

/// <summary>
/// An anonymous guest visit started by scanning a location's QR code (blueprint P1-QR-02).
/// No account is needed; the guest holds a short-lived signed token that points here.
/// </summary>
public class GuestSession : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public Guid QrTokenId { get; private set; }
    public string Locale { get; private set; } = "en";
    public DateTimeOffset ExpiresAt { get; private set; }

    private GuestSession() { }

    public GuestSession(
        Guid tenantId, Guid venueId, Guid serviceLocationId, Guid qrTokenId,
        string locale, DateTimeOffset now, TimeSpan lifetime)
    {
        TenantId = tenantId;
        VenueId = venueId;
        ServiceLocationId = serviceLocationId;
        QrTokenId = qrTokenId;
        Locale = locale;
        CreatedAt = now;
        ExpiresAt = now + lifetime;
    }

    public bool IsExpired(DateTimeOffset now) => now >= ExpiresAt;
}
