using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Menus;

/// <summary>The live e-menu for a guest's table session (blueprint 9.1, P1-MENU-01..07).</summary>
public class GuestMenuService(IAppDbContext db, TimeProvider clock)
{
    public async Task<MenuTreeDto> GetMenuAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.GuestSessions.AsNoTracking().SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.IsExpired(clock.GetUtcNow()))
            throw AppException.Unauthorized("session_expired", "Your session has ended. Scan the QR code again.");

        var venue = await db.Venues.AsNoTracking().SingleAsync(v => v.Id == session.VenueId, ct);
        if (!venue.MenuEnabled)
            throw AppException.NotFound("menu_unavailable", "This venue doesn't offer the digital menu.");

        var menus = await db.Menus.AsNoTracking()
            .Where(m => m.VenueId == venue.Id && m.IsPublished)
            .ToListAsync(ct);
        var menuIds = menus.Select(m => m.Id).ToList();

        var categories = await db.MenuCategories.AsNoTracking()
            .Where(c => menuIds.Contains(c.MenuId))
            .ToListAsync(ct);
        var categoryIds = categories.Select(c => c.Id).ToList();

        var items = await db.MenuItems.AsNoTracking()
            .Where(i => categoryIds.Contains(i.CategoryId))
            .Include(i => i.ModifierGroups).ThenInclude(g => g.Options)
            .ToListAsync(ct);

        return MenuTree.Build(venue, menus, categories, items, VenueTime.LocalNow(clock, venue.TimeZone));
    }
}
