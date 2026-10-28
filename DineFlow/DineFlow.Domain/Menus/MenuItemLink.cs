using DineFlow.Domain.Common;

namespace DineFlow.Domain.Menus;

/// <summary>Puts a catalog item on a menu. One item can be on any number of the venue's menus.</summary>
public class MenuItemLink : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid MenuId { get; private set; }
    public Guid MenuItemId { get; private set; }

    private MenuItemLink() { }

    public MenuItemLink(Guid tenantId, Guid menuId, Guid menuItemId)
    {
        TenantId = tenantId;
        MenuId = menuId;
        MenuItemId = menuItemId;
    }
}
