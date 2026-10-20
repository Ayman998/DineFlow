using DineFlow.Domain.Common;

namespace DineFlow.Domain.Menus;

/// <summary>A section of a menu, e.g. "Mains". A category can have one level of subcategories.</summary>
public class MenuCategory : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid MenuId { get; private set; }
    /// <summary>Set for a subcategory; always points at a top-level category of the same menu.</summary>
    public Guid? ParentId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public int SortOrder { get; private set; }

    private MenuCategory() { }

    public MenuCategory(Guid tenantId, Guid venueId, Guid menuId, Guid? parentId, string name, string? nameAr, int sortOrder)
    {
        TenantId = tenantId;
        VenueId = venueId;
        MenuId = menuId;
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
