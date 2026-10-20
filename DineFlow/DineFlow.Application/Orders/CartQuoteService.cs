using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Menus;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Orders;

/// <summary>
/// Prices a guest's cart on the server (P1-ORD-01, blueprint 3.3 "server-side price and
/// availability validation"): every item, option, quantity and menu schedule is checked against
/// the live menu, and the total is calculated here, never trusted from the phone.
/// </summary>
public class CartQuoteService(IAppDbContext db, TimeProvider clock)
{
    public async Task<CartQuoteDto> QuoteAsync(Guid sessionId, CartRequest request, CancellationToken ct)
    {
        var table = await LoadTableAsync(sessionId, ct);
        return (await PriceAsync(table, request.Lines, ct)).Quote;
    }

    /// <summary>The guest's session, venue and location, checked to be open for ordering.</summary>
    internal async Task<TableContext> LoadTableAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.GuestSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.IsExpired(clock.GetUtcNow()))
            throw AppException.Unauthorized("session_expired", "Your session has ended. Scan the QR code again.");

        var venue = await db.Venues.SingleAsync(v => v.Id == session.VenueId, ct);
        var location = await db.ServiceLocations.Include(l => l.DiningArea).SingleAsync(l => l.Id == session.ServiceLocationId, ct);

        if (!venue.IsActive || !location.IsActive)
            throw AppException.Gone("venue_unavailable", "This venue isn't taking orders right now.");
        if (!venue.MenuEnabled)
            throw AppException.NotFound("menu_unavailable", "This venue doesn't take orders from the digital menu.");

        return new TableContext(session, venue, location);
    }

    internal async Task<PricedCart> PriceAsync(TableContext table, IReadOnlyList<CartLineRequest> lines, CancellationToken ct)
    {
        var itemIds = lines.Select(l => l.ItemId).Distinct().ToList();
        var items = await db.MenuItems
            .Where(i => itemIds.Contains(i.Id) && i.VenueId == table.Venue.Id)
            .Include(i => i.ModifierGroups).ThenInclude(g => g.Options)
            .ToDictionaryAsync(i => i.Id, ct);

        var categoryIds = items.Values.Select(i => i.CategoryId).Distinct().ToList();
        var menuByCategory = await db.MenuCategories
            .Where(c => categoryIds.Contains(c.Id))
            .Join(db.Menus, c => c.MenuId, m => m.Id, (c, m) => new { CategoryId = c.Id, Menu = m })
            .ToDictionaryAsync(x => x.CategoryId, x => x.Menu, ct);

        var localNow = VenueTime.LocalNow(clock, table.Venue.TimeZone);
        var quoteLines = new List<QuoteLineDto>();
        var issues = new List<CartIssueDto>();
        var orderLines = new List<Func<OrderLine>>();

        for (var index = 0; index < lines.Count; index++)
        {
            var line = lines[index];
            void Issue(string code, string message) => issues.Add(new CartIssueDto(index, code, message));

            if (!items.TryGetValue(line.ItemId, out var item))
            {
                Issue("item_not_found", "This item is no longer on the menu.");
                continue;
            }

            var menu = menuByCategory.GetValueOrDefault(item.CategoryId);
            if (!item.IsAvailable)
                Issue("item_unavailable", $"{item.Name} is sold out right now.");
            else if (menu is null || !menu.IsAvailableAt(localNow))
                Issue("menu_closed", menu?.StartTime is { } start
                    ? $"{item.Name} is on the {menu.Name} menu, served {start:HH\\:mm}–{menu.EndTime:HH\\:mm}."
                    : $"{item.Name} isn't being served right now.");

            var modifiers = ResolveOptions(item, line.OptionIds ?? [], Issue);
            var unitPrice = item.PriceMinor + modifiers.Sum(m => m.PriceDeltaMinor);
            var note = string.IsNullOrWhiteSpace(line.Note) ? null : line.Note.Trim();

            quoteLines.Add(new QuoteLineDto(index, item.Id, item.Name, item.NameAr, line.Quantity,
                unitPrice, unitPrice * line.Quantity, modifiers.Select(m => m.ToDto()).ToList(), note));

            var tenantId = table.Venue.TenantId;
            orderLines.Add(() => new OrderLine(tenantId, item.Id, item.Name, item.NameAr, item.PriceMinor, line.Quantity, modifiers, note));
        }

        var venue = table.Venue;
        var price = OrderPricing.Calculate(quoteLines.Sum(l => l.LineTotalMinor),
            venue.ServiceChargeBasisPoints, venue.TaxBasisPoints, venue.PricesIncludeTax);

        var quote = new CartQuoteDto(venue.Currency, quoteLines,
            price.SubtotalMinor, price.ServiceChargeMinor, venue.ServiceChargeBasisPoints,
            price.TaxMinor, venue.TaxBasisPoints, price.TaxIncluded, price.TotalMinor, issues);

        return new PricedCart(quote, () => orderLines.Select(create => create()).ToList());
    }

    /// <summary>Checks the chosen options against the item's groups and returns them in menu order.</summary>
    private static List<OrderLineModifier> ResolveOptions(MenuItem item, IReadOnlyList<Guid> optionIds, Action<string, string> issue)
    {
        var chosen = optionIds.ToHashSet();
        if (chosen.Count != optionIds.Count) issue("invalid_choices", $"An option was chosen twice for {item.Name}.");

        var known = item.ModifierGroups.SelectMany(g => g.Options).Select(o => o.Id).ToHashSet();
        if (!chosen.IsSubsetOf(known))
            issue("option_not_found", $"The choices for {item.Name} have changed. Please choose again.");

        var result = new List<OrderLineModifier>();
        foreach (var group in item.ModifierGroups.OrderBy(g => g.SortOrder))
        {
            var picked = group.Options.Where(o => chosen.Contains(o.Id)).OrderBy(o => o.SortOrder).ToList();

            if (picked.Count < group.MinSelect || picked.Count > group.MaxSelect)
                issue("invalid_choices", group.MinSelect == group.MaxSelect
                    ? $"Choose {group.MaxSelect} for {group.Name} on {item.Name}."
                    : $"Choose {group.MinSelect}–{group.MaxSelect} for {group.Name} on {item.Name}.");

            foreach (var option in picked)
            {
                if (!option.IsAvailable) issue("option_unavailable", $"{option.Name} is sold out right now.");
                result.Add(new OrderLineModifier(option.Id, group.Name, group.NameAr, option.Name, option.NameAr, option.PriceDeltaMinor));
            }
        }

        return result;
    }
}

internal sealed record TableContext(GuestSession Session, Venue Venue, ServiceLocation Location);

/// <param name="CreateLines">Builds fresh order lines; called again if saving has to be retried.</param>
internal sealed record PricedCart(CartQuoteDto Quote, Func<IReadOnlyList<OrderLine>> CreateLines);
