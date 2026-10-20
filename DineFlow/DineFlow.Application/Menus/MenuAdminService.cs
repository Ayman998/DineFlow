using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Common;
using DineFlow.Domain.Menus;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Menus;

/// <summary>
/// Menu management for the signed-in merchant (blueprint 4.4 "Menu"): menus with schedules,
/// categories and subcategories, items with prices, allergens, tags, availability and modifiers.
/// Every query is limited to the merchant's tenant by the DbContext filter.
/// </summary>
public class MenuAdminService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public async Task<MenuTreeDto> GetTreeAsync(Guid venueId, CancellationToken ct)
    {
        var venue = await FindVenueAsync(venueId, ct);
        var menus = await db.Menus.AsNoTracking().Where(m => m.VenueId == venueId).ToListAsync(ct);
        var categories = await db.MenuCategories.AsNoTracking().Where(c => c.VenueId == venueId).ToListAsync(ct);
        var items = await db.MenuItems.AsNoTracking()
            .Where(i => i.VenueId == venueId)
            .Include(i => i.ModifierGroups).ThenInclude(g => g.Options)
            .ToListAsync(ct);

        return MenuTree.Build(venue, menus, categories, items, LocalNow(venue));
    }

    // ---- Menus ----

    public async Task<MenuDto> CreateMenuAsync(Guid venueId, MenuRequest request, CancellationToken ct)
    {
        var venue = await FindVenueAsync(venueId, ct);
        var nextOrder = await db.Menus.Where(m => m.VenueId == venueId).Select(m => (int?)m.SortOrder).MaxAsync(ct) ?? -1;

        var menu = Apply(() =>
        {
            var m = new Menu(tenant.RequireTenantId(), venueId, request.Name, request.NameAr, nextOrder + 1);
            SetMenuDetails(m, request);
            return m;
        });

        db.Menus.Add(menu);
        await db.SaveChangesAsync(ct);
        return MenuTree.ToMenu(menu, LocalNow(venue), []);
    }

    public async Task<MenuDto> UpdateMenuAsync(Guid menuId, MenuRequest request, CancellationToken ct)
    {
        var menu = await FindMenuAsync(menuId, ct);
        Apply(() =>
        {
            menu.Rename(request.Name, request.NameAr);
            SetMenuDetails(menu, request);
            return menu;
        });

        await db.SaveChangesAsync(ct);
        var venue = await db.Venues.AsNoTracking().SingleAsync(v => v.Id == menu.VenueId, ct);
        return MenuTree.ToMenu(menu, LocalNow(venue), []);
    }

    public async Task DeleteMenuAsync(Guid menuId, CancellationToken ct)
    {
        var menu = await FindMenuAsync(menuId, ct);
        var categories = await db.MenuCategories.Where(c => c.MenuId == menuId).ToListAsync(ct);

        // Subcategories point at their parent with no cascade, so EF deletes children first.
        db.MenuCategories.RemoveRange(categories);
        db.Menus.Remove(menu);
        await db.SaveChangesAsync(ct);
    }

    // ---- Categories ----

    public async Task<CategoryDto> CreateCategoryAsync(CreateCategoryRequest request, CancellationToken ct)
    {
        var menu = await FindMenuAsync(request.MenuId, ct);

        if (request.ParentId is { } parentId)
        {
            var parent = await db.MenuCategories.SingleOrDefaultAsync(c => c.Id == parentId, ct);
            if (parent is null || parent.MenuId != menu.Id)
                throw AppException.BadRequest("parent_not_found", "The parent category isn't in this menu.");
            if (parent.ParentId is not null)
                throw AppException.BadRequest("too_deep", "Subcategories can't have their own subcategories.");
        }

        var nextOrder = await db.MenuCategories
            .Where(c => c.MenuId == menu.Id && c.ParentId == request.ParentId)
            .Select(c => (int?)c.SortOrder).MaxAsync(ct) ?? -1;

        var category = Apply(() => new MenuCategory(
            tenant.RequireTenantId(), menu.VenueId, menu.Id, request.ParentId, request.Name, request.NameAr, nextOrder + 1));

        db.MenuCategories.Add(category);
        await db.SaveChangesAsync(ct);
        return MenuTree.ToCategoryShell(category);
    }

    public async Task<CategoryDto> UpdateCategoryAsync(Guid categoryId, UpdateCategoryRequest request, CancellationToken ct)
    {
        var category = await FindCategoryAsync(categoryId, ct);
        Apply(() =>
        {
            category.Rename(request.Name, request.NameAr);
            return category;
        });

        await db.SaveChangesAsync(ct);
        return MenuTree.ToCategoryShell(category);
    }

    /// <summary>Deletes the category, its subcategories and all their items.</summary>
    public async Task DeleteCategoryAsync(Guid categoryId, CancellationToken ct)
    {
        var category = await FindCategoryAsync(categoryId, ct);
        var children = await db.MenuCategories.Where(c => c.ParentId == categoryId).ToListAsync(ct);

        db.MenuCategories.RemoveRange(children);
        db.MenuCategories.Remove(category);
        await db.SaveChangesAsync(ct);
    }

    // ---- Items ----

    public async Task<ItemDto> CreateItemAsync(ItemRequest request, CancellationToken ct)
    {
        var category = await FindCategoryAsync(request.CategoryId, ct);
        var nextOrder = await db.MenuItems.Where(i => i.CategoryId == category.Id)
            .Select(i => (int?)i.SortOrder).MaxAsync(ct) ?? -1;

        var item = new MenuItem(tenant.RequireTenantId(), category.VenueId, category.Id, nextOrder + 1);
        Apply(() =>
        {
            ApplyItem(item, request, category);
            return item;
        });

        db.MenuItems.Add(item);
        await db.SaveChangesAsync(ct);
        return MenuTree.ToItem(item);
    }

    public async Task<ItemDto> UpdateItemAsync(Guid itemId, ItemRequest request, CancellationToken ct)
    {
        var item = await db.MenuItems
            .Include(i => i.ModifierGroups).ThenInclude(g => g.Options)
            .SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw AppException.NotFound("item_not_found", "Menu item not found.");

        var category = await FindCategoryAsync(request.CategoryId, ct);
        if (category.VenueId != item.VenueId)
            throw AppException.BadRequest("category_not_found", "That category belongs to a different venue.");

        Apply(() =>
        {
            ApplyItem(item, request, category);
            return item;
        });

        await db.SaveChangesAsync(ct);
        return MenuTree.ToItem(item);
    }

    /// <summary>Marks an item sold out or back in stock (any staff role can do this).</summary>
    public async Task<ItemDto> SetItemAvailabilityAsync(Guid itemId, bool isAvailable, CancellationToken ct)
    {
        var item = await db.MenuItems
            .Include(i => i.ModifierGroups).ThenInclude(g => g.Options)
            .SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw AppException.NotFound("item_not_found", "Menu item not found.");

        item.SetAvailable(isAvailable);
        await db.SaveChangesAsync(ct);
        return MenuTree.ToItem(item);
    }

    public async Task DeleteItemAsync(Guid itemId, CancellationToken ct)
    {
        var item = await db.MenuItems.SingleOrDefaultAsync(i => i.Id == itemId, ct)
            ?? throw AppException.NotFound("item_not_found", "Menu item not found.");

        db.MenuItems.Remove(item);
        await db.SaveChangesAsync(ct);
    }

    // ---- Ordering ----

    /// <summary>Sets the display order of sibling menus, categories or items to the order of <paramref name="request"/>.Ids.</summary>
    public async Task ReorderAsync(ReorderRequest request, CancellationToken ct)
    {
        var ids = request.Ids.Distinct().ToList();
        if (ids.Count != request.Ids.Count) throw AppException.BadRequest("invalid_order", "Each id may appear only once.");

        switch (request.Kind)
        {
            case "menus":
                var menus = await db.Menus.Where(m => ids.Contains(m.Id)).ToListAsync(ct);
                EnsureSiblings(ids, menus, m => m.Id, m => m.VenueId);
                foreach (var m in menus) m.SetSortOrder(ids.IndexOf(m.Id));
                break;
            case "categories":
                var categories = await db.MenuCategories.Where(c => ids.Contains(c.Id)).ToListAsync(ct);
                EnsureSiblings(ids, categories, c => c.Id, c => (c.MenuId, c.ParentId));
                foreach (var c in categories) c.SetSortOrder(ids.IndexOf(c.Id));
                break;
            default:
                var items = await db.MenuItems.Where(i => ids.Contains(i.Id)).ToListAsync(ct);
                EnsureSiblings(ids, items, i => i.Id, i => i.CategoryId);
                foreach (var i in items) i.SetSortOrder(ids.IndexOf(i.Id));
                break;
        }

        await db.SaveChangesAsync(ct);
    }

    // ---- Helpers ----

    private static void SetMenuDetails(Menu menu, MenuRequest request)
    {
        menu.SetSchedule(MenuDaysExtensions.FromDays(request.Days), ParseTime(request.StartTime), ParseTime(request.EndTime));
        menu.SetPublished(request.IsPublished);
    }

    private static void ApplyItem(MenuItem item, ItemRequest request, MenuCategory category)
    {
        var allergens = (request.Allergens ?? []).Aggregate(Allergen.None, (all, a) => all | a);
        item.Update(category.Id, request.Name, request.NameAr, request.Description, request.DescriptionAr,
            request.PriceMinor, request.ImageUrl, allergens, request.Tags ?? []);
        item.SetAvailable(request.IsAvailable);
        item.SetModifierGroups((request.ModifierGroups ?? []).Select(g => new ModifierGroupSpec(
            g.Id, g.Name, g.NameAr, g.MinSelect, g.MaxSelect,
            g.Options.Select(o => new ModifierOptionSpec(o.Id, o.Name, o.NameAr, o.PriceDeltaMinor, o.IsDefault, o.IsAvailable)).ToList()))
            .ToList());
    }

    private static TimeOnly? ParseTime(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null
        : TimeOnly.TryParseExact(value, "HH:mm", out var time) ? time
        : throw AppException.BadRequest("invalid_time", $"'{value}' isn't a valid time. Use HH:mm, e.g. 07:30.");

    private static void EnsureSiblings<T, TParent>(List<Guid> ids, List<T> found, Func<T, Guid> id, Func<T, TParent> parent)
    {
        if (found.Count != ids.Count) throw AppException.NotFound("not_found", "Some of those entries no longer exist. Reload and try again.");
        if (found.Select(parent).Distinct().Count() > 1)
            throw AppException.BadRequest("invalid_order", "Only entries in the same list can be reordered together.");
    }

    private DateTime LocalNow(Venue venue) => VenueTime.LocalNow(clock, venue.TimeZone);

    private static T Apply<T>(Func<T> change)
    {
        try { return change(); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_menu", ex.Message); }
    }

    private async Task<Venue> FindVenueAsync(Guid venueId, CancellationToken ct) =>
        await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == venueId, ct)
        ?? throw AppException.NotFound("venue_not_found", "Venue not found.");

    private async Task<Menu> FindMenuAsync(Guid menuId, CancellationToken ct) =>
        await db.Menus.SingleOrDefaultAsync(m => m.Id == menuId, ct)
        ?? throw AppException.NotFound("menu_not_found", "Menu not found.");

    private async Task<MenuCategory> FindCategoryAsync(Guid categoryId, CancellationToken ct) =>
        await db.MenuCategories.SingleOrDefaultAsync(c => c.Id == categoryId, ct)
        ?? throw AppException.NotFound("category_not_found", "Category not found.");
}
