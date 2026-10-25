using DineFlow.Domain.Common;

namespace DineFlow.Domain.Venues;

/// <summary>
/// Somewhere a guest can order and pay from: a table, counter, hotel room or pool/beach spot
/// (blueprint P1-QR-05). Each one gets its own QR code.
/// </summary>
public class ServiceLocation : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Venue Venue { get; private set; } = null!;
    public Guid? DiningAreaId { get; private set; }
    public DiningArea? DiningArea { get; private set; }

    public ServiceLocationType Type { get; private set; }
    /// <summary>Short label shown to guests and staff, e.g. "12" or "204".</summary>
    public string Label { get; private set; } = string.Empty;
    /// <summary>The matching table or room reference in the POS, once a connector is linked.</summary>
    public string? ExternalRef { get; private set; }
    public bool IsActive { get; private set; } = true;

    private ServiceLocation() { }

    public ServiceLocation(Guid tenantId, Guid venueId, Guid? diningAreaId, ServiceLocationType type, string label)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new DomainException("Location label is required.");

        TenantId = tenantId;
        VenueId = venueId;
        DiningAreaId = diningAreaId;
        Type = type;
        Label = label.Trim();
    }

    /// <summary>Maps this location to its table in the POS; null unmaps it.</summary>
    public void SetExternalRef(string? externalRef) => ExternalRef = Text.Optional(externalRef, 100, "POS table");

    /// <summary>Guest-facing name, e.g. "Table 12" or "Room 204".</summary>
    public string DisplayName => Type switch
    {
        ServiceLocationType.Table => $"Table {Label}",
        ServiceLocationType.Counter => $"Counter {Label}",
        ServiceLocationType.HotelRoom => $"Room {Label}",
        ServiceLocationType.PoolBeach => $"Pool/Beach {Label}",
        _ => Label
    };
}

public enum ServiceLocationType
{
    Table = 0,
    Counter = 1,
    HotelRoom = 2,
    PoolBeach = 3
}
