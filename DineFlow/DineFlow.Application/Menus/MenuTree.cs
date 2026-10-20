using DineFlow.Domain.Menus;
using DineFlow.Domain.Venues;

namespace DineFlow.Application.Menus;

/// <summary>Shapes loaded menu rows into the DTO tree, in sort order.</summary>
internal static class MenuTree
{
    public static MenuTreeDto Build(
        Venue venue, IEnumerable<Menu> menus, IReadOnlyList<MenuCategory> categories,
        IReadOnlyList<MenuItem> items, DateTime venueLocalNow)
    {
        var itemsByCategory = items.ToLookup(i => i.CategoryId);
        var childrenByParent = categories.Where(c => c.ParentId is not null).ToLookup(c => c.ParentId!.Value);

        CategoryDto ToCategory(MenuCategory c) => new(
            c.Id, c.MenuId, c.ParentId, c.Name, c.NameAr, c.SortOrder,
            itemsByCategory[c.Id].OrderBy(i => i.SortOrder).ThenBy(i => i.Name).Select(ToItem).ToList(),
            childrenByParent[c.Id].OrderBy(s => s.SortOrder).Select(ToCategory).ToList());

        return new MenuTreeDto(
            venue.Id,
            venue.Currency,
            menus.OrderBy(m => m.SortOrder).ThenBy(m => m.Name).Select(m => ToMenu(m, venueLocalNow,
                categories.Where(c => c.MenuId == m.Id && c.ParentId is null)
                    .OrderBy(c => c.SortOrder)
                    .Select(ToCategory)
                    .ToList()))
                .ToList());
    }

    public static MenuDto ToMenu(Menu m, DateTime venueLocalNow, IReadOnlyList<CategoryDto> categories) => new(
        m.Id, m.Name, m.NameAr, m.SortOrder, m.IsPublished, m.Days.ToDays(),
        m.StartTime?.ToString("HH:mm"), m.EndTime?.ToString("HH:mm"),
        m.IsAvailableAt(venueLocalNow), categories);

    /// <summary>A category without its items and subcategories, e.g. after creating it.</summary>
    public static CategoryDto ToCategoryShell(MenuCategory c) =>
        new(c.Id, c.MenuId, c.ParentId, c.Name, c.NameAr, c.SortOrder, [], []);

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
