using DineFlow.Domain.Common;

namespace DineFlow.Domain.Venues;

/// <summary>A named part of a venue, such as "Main hall" or "Terrace".</summary>
public class DiningArea : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public int SortOrder { get; private set; }

    private DiningArea() { }

    public DiningArea(Guid tenantId, Guid venueId, string name, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Area name is required.");

        TenantId = tenantId;
        VenueId = venueId;
        Name = name.Trim();
        SortOrder = sortOrder;
    }
}
