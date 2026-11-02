using HelloZen.Domain.Menus;
using HelloZen.Domain.Venues;

namespace HelloZen.Application.Menus;

/// <summary>Shapes loaded menu rows into the DTO tree, in sort order.</summary>
internal static class MenuTree
{
    /// <summary>
    /// Each menu with the categories of the items it links, at that menu's prices. Categories with
    /// none of its items are left out. <paramref name="forMerchant"/> adds each item's own price
    /// where a menu charges a different one.
    /// </summary>
    public static MenuTreeDto Build(
        Venue venue, IEnumerable<Menu> menus, IReadOnlyList<MenuCategory> categories,
        IReadOnlyList<MenuItem> items, IReadOnlyList<MenuItemLink> links, DateTime venueLocalNow, bool forMerchant = false)
    {
        var itemsById = items.ToDictionary(i => i.Id);
        var linksByMenu = links.ToLookup(l => l.MenuId);

        MenuDto Menu(Menu m)
        {
            var prices = linksByMenu[m.Id].ToDictionary(l => l.MenuItemId, l => l.PriceMinor);
            var shown = prices.Keys.Select(itemsById.GetValueOrDefault).OfType<MenuItem>();
            return ToMenu(m, venueLocalNow, Categories(categories, shown, hideEmpty: true, i => ToItem(i, prices[i.Id], forMerchant)));
        }

        return new MenuTreeDto(venue.Id, venue.Currency, menus.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).Select(Menu).ToList());
    }

    /// <summary>
    /// Top-level categories with their items and subcategories. With <paramref name="hideEmpty"/>,
    /// categories without any of <paramref name="items"/>, directly or in a subcategory, are left out.
    /// </summary>
    /// <param name="toItem">Shapes each item; by default at its own price.</param>
    public static IReadOnlyList<CategoryDto> Categories(
        IReadOnlyList<MenuCategory> categories, IEnumerable<MenuItem> items, bool hideEmpty, Func<MenuItem, ItemDto>? toItem = null)
    {
        toItem ??= i => ToItem(i);
        var itemsByCategory = items.ToLookup(i => i.CategoryId);
        var childrenByParent = categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);

        CategoryDto ToCategory(MenuCategory c) => new(
            c.Id, c.ParentId, c.Name, c.NameAr, c.SortOrder,
            itemsByCategory[c.Id].OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(toItem).ToList(),
            childrenByParent[c.Id].OrderBy(s => s.SortOrder).ThenBy(s => s.Name).Select(ToCategory).Where(Shown).ToList());

        bool Shown(CategoryDto c) => !hideEmpty || c.Items.Count > 0 || c.Subcategories.Count > 0;

        return categories.Where(c => c.ParentId is null)
            .OrderBy(c => c.SortOrder).ThenBy(c => c.Name)
            .Select(ToCategory).Where(Shown).ToList();
    }

    public static MenuDto ToMenu(Menu m, DateTime venueLocalNow, IReadOnlyList<CategoryDto> categories) => new(
        m.Id, m.Name, m.NameAr, m.SortOrder, m.IsPublished, m.Days.ToDays(),
        m.StartTime?.ToString("HH:mm"), m.EndTime?.ToString("HH:mm"),
        m.IsAvailableAt(venueLocalNow), categories);

    /// <summary>A category without its items and subcategories, e.g. after creating it.</summary>
    public static CategoryDto ToCategoryShell(MenuCategory c) =>
        new(c.Id, c.ParentId, c.Name, c.NameAr, c.SortOrder, [], []);

    /// <param name="menuPriceMinor">What a menu charges for it, when that menu sets its own price.</param>
    /// <param name="withItemPrice">Also give the item's own price when <paramref name="menuPriceMinor"/> replaces it.</param>
    public static ItemDto ToItem(MenuItem i, long? menuPriceMinor = null, bool withItemPrice = false) => new(
        i.Id, i.CategoryId, i.Name, i.NameAr, i.Description, i.DescriptionAr, menuPriceMinor ?? i.PriceMinor, i.ImageUrl,
        i.IsAvailable,
        Enum.GetValues<Allergen>().Where(a => a != Allergen.None && i.Allergens.HasFlag(a)).ToList(),
        i.Tags,
        i.SortOrder,
        i.ModifierGroups.OrderBy(g => g.SortOrder).Select(g => new ModifierGroupDto(
            g.Id, g.Name, g.NameAr, g.MinSelect, g.MaxSelect,
            g.Options.OrderBy(o => o.SortOrder).Select(o => new ModifierOptionDto(
                o.Id, o.Name, o.NameAr, o.PriceDeltaMinor, o.IsDefault, o.IsAvailable)).ToList())).ToList(),
        withItemPrice && menuPriceMinor is not null ? i.PriceMinor : null);
}
