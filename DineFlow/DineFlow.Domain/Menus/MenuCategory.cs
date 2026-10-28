using DineFlow.Domain.Common;

namespace DineFlow.Domain.Menus;

/// <summary>
/// A group of items in the venue's catalog, e.g. "Mains". A category can have one level of
/// subcategories. A menu shows a category when it links at least one of its items.
/// </summary>
public class MenuCategory : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    /// <summary>Set for a subcategory; always points at a top-level category of the same venue.</summary>
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public int SortOrder { get; private set; }

    private MenuCategory() { }

    public MenuCategory(Guid tenantId, Guid venueId, Guid? parentId, string name, string? nameAr, int sortOrder)
    {
        TenantId = tenantId;
        VenueId = venueId;
        ParentId = parentId;
        SortOrder = sortOrder;
        Rename(name, nameAr);
    }

    public void Rename(string name, string? nameAr)
    {
        Name = Text.Required(name, 80, "Category name");
        NameAr = Text.Optional(nameAr, 80);
    }

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;
}
