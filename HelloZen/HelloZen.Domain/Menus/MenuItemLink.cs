using HelloZen.Domain.Common;

namespace HelloZen.Domain.Menus;

/// <summary>
/// Puts a catalog item on a menu. One item can be on any number of the venue's menus, and each
/// menu can charge its own price for it.
/// </summary>
public class MenuItemLink : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid MenuId { get; private set; }
    public Guid MenuItemId { get; private set; }
    /// <summary>What this menu charges for the item, before options; null means the item's own price.</summary>
    public long? PriceMinor { get; private set; }

    private MenuItemLink() { }

    public MenuItemLink(Guid tenantId, Guid menuId, Guid menuItemId)
    {
        TenantId = tenantId;
        MenuId = menuId;
        MenuItemId = menuItemId;
    }

    public void SetPrice(long? priceMinor)
    {
        if (priceMinor < 0) throw new DomainException("Price can't be negative.");
        PriceMinor = priceMinor;
    }
}
