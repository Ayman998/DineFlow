using DineFlow.Domain.Common;

namespace DineFlow.Domain.Venues;

/// <summary>A named part of a venue, such as "Main hall" or "Terrace".</summary>
public class DiningArea : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    /// <summary>What Arabic-reading guests see instead of <see cref="Name"/>, e.g. "التراس"; optional.</summary>
    public string? NameAr { get; private set; }
    public int SortOrder { get; private set; }
    /// <summary>
    /// False while the whole area is closed (e.g. the terrace in winter): its QR codes stop
    /// opening and its tables take no new orders, but bills already running there can still be paid.
    /// </summary>
    public bool IsActive { get; private set; } = true;

    private DiningArea() { }

    public DiningArea(Guid tenantId, Guid venueId, string name, int sortOrder, string? nameAr = null)
    {
        TenantId = tenantId;
        VenueId = venueId;
        SortOrder = sortOrder;
        Rename(name, nameAr);
    }

    public void Rename(string name, string? nameAr)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Area name is required.");

        Name = name.Trim();
        NameAr = Text.Optional(nameAr, 80, "Arabic name");
    }

    public void SetActive(bool active) => IsActive = active;
}
