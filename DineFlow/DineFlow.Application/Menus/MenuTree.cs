using DineFlow.Domain.Menus;
using DineFlow.Domain.Venues;

namespace DineFlow.Application.Menus;

/// <summary>Shapes loaded menu rows into the DTO tree, in sort order.</summary>
internal static class MenuTree
{
    /// <summary>Each menu with the categories of the items it links. Categories with none of its items are left out.</summary>
    public static MenuTreeDto Build(
        Venue venue, IEnumerable<Menu> menus, IReadOnlyList<MenuCategory> categories,
        IReadOnlyList<MenuItem> items, IReadOnlyList<MenuItemLink> links, DateTime venueLocalNow)
    {
        var itemsById = items.ToDictionary(i => i.Id);
        var itemIdsByMenu = links.ToLookup(l => l.MenuId, l => l.MenuItemId);

        return new MenuTreeDto(
            venue.Id,
            venue.Currency,
            menus.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).Select(m => ToMenu(m, venueLocalNow,
                Categories(categories, itemIdsByMenu[m.Id].Select(itemsById.GetValueOrDefault).OfType<MenuItem>(), hideEmpty: true)))
                .ToList());
    }

    /// <summary>
    /// Top-level categories with their items and subcategories. With <paramref name="hideEmpty"/>,
    /// categories without any of <paramref name="items"/>, directly or in a subcategory, are left out.
    /// </summary>
    public static IReadOnlyList<CategoryDto> Categories(IReadOnlyList<MenuCategory> categories, IEnumerable<MenuItem> items, bool hideEmpty)
    {
        var itemsByCategory = items.ToLookup(i => i.CategoryId);
        var childrenByParent = categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);

        CategoryDto ToCategory(MenuCategory c) => new(
            c.Id, c.ParentId, c.Name, c.NameAr, c.SortOrder,
            itemsByCategory[c.Id].OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(ToItem).ToList(),
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

    public static ItemDto ToItem(MenuItem i) => new(
        i.Id, i.CategoryId, i.Name, i.NameAr, i.Description, i.DescriptionAr, i.PriceMinor, i.ImageUrl,
        i.IsAvailable,
        Enum.GetValues<Allergen>().Where(a => a != Allergen.None && i.Allergens.HasFlag(a)).ToList(),
        i.Tags,
        i.SortOrder,
        i.ModifierGroups.OrderBy(g => g.SortOrder).Select(g => new ModifierGroupDto(
            g.Id, g.Name, g.NameAr, g.MinSelect, g.MaxSelect,
            g.Options.OrderBy(o => o.SortOrder).Select(o => new ModifierOptionDto(
                o.Id, o.Name, o.NameAr, o.PriceDeltaMinor, o.IsDefault, o.IsAvailable)).ToList())).ToList());
}
