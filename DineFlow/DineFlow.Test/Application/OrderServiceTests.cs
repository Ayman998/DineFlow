using DineFlow.Application.Common;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

public class OrderServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// A venue with 10% service and 5% VAT included in prices, an all-day menu with a burger
    /// (doneness required, up to 2 extras) and a sold-out soup, a breakfast-only item, and a
    /// guest session at table 7. Leaves the host acting as that guest's tenant.
    /// </summary>
    private async Task<Fixture> CreateAsync()
    {
        // 06:00 UTC tomorrow is 10:00 in Dubai: breakfast (07:00-11:30) is being served.
        _host.Clock.SetUtcNow(new DateTimeOffset(_host.Clock.GetUtcNow().UtcDateTime.Date.AddDays(1).AddHours(6), TimeSpan.Zero));

        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        await _host.Venues.UpdateSettingsAsync(venue.Id,
            new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "en", null, "#0B3B38", true, true, 1000, 500, true), default);

        var allDay = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(venue.Id, null, "Mains", null), default);
        var burger = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Burger", "برجر", null, null, 5500, null, true, [], [], [
            new ModifierGroupRequest(null, "Doneness", null, 1, 1, [new(null, "Medium", null, 0, false), new(null, "Well done", null, 0, false)]),
            new ModifierGroupRequest(null, "Extras", null, 0, 2, [new(null, "Cheese", null, 400, false), new(null, "Bacon", null, 600, false), new(null, "Egg", null, 300, false)])
        ]), default);
        var soup = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Soup", null, null, null, 2400, null, false, [], [], []), default);
        await _host.Menus.SetMenuItemsAsync(allDay.Id, new MenuItemsRequest([burger.Id, soup.Id]), default);

        var breakfast = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("Breakfast", null, true, Enum.GetValues<DayOfWeek>(), "07:00", "11:30"), default);
        var plates = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(venue.Id, null, "Plates", null), default);
        var shakshuka = await _host.Menus.CreateItemAsync(new ItemRequest(plates.Id, "Shakshuka", null, null, null, 3800, null, true, [], [], []), default);
        await _host.Menus.SetMenuItemsAsync(breakfast.Id, new MenuItemsRequest([shakshuka.Id]), default);

        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7"), default);
        var tenantId = _host.CurrentTenant;
        _host.CurrentTenant = null;
        var session = await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(table.Qr!.Url), null, default);
        _host.CurrentTenant = tenantId; // the guest's token carries the tenant

        return new Fixture(venue, session.Context.SessionId, burger, soup, shakshuka);
    }

    private sealed record Fixture(VenueDto Venue, Guid SessionId, ItemDto Burger, ItemDto Soup, ItemDto Shakshuka)
    {
        public Guid Medium => Burger.ModifierGroups[0].Options[0].Id;
        public Guid Cheese => Burger.ModifierGroups[1].Options[0].Id;
        public Guid Bacon => Burger.ModifierGroups[1].Options[1].Id;
        public Guid Egg => Burger.ModifierGroups[1].Options[2].Id;

        public CartLineRequest TwoBurgersWithCheese => new(Burger.Id, 2, [Medium, Cheese], "No pickles");
    }

    [Fact]
    public async Task Quote_prices_options_and_charges_on_the_server()
    {
        var f = await CreateAsync();

        var quote = await _host.CartQuotes.QuoteAsync(f.SessionId, new CartRequest([f.TwoBurgersWithCheese]), default);

        Assert.True(quote.CanOrder);
        var line = Assert.Single(quote.Lines);
        Assert.Equal(5_900, line.UnitPriceMinor);
        Assert.Equal(11_800, quote.SubtotalMinor);
        Assert.Equal(1_180, quote.ServiceChargeMinor);    // 10%
        Assert.Equal(618, quote.TaxMinor);                // 5% VAT inside 129.80
        Assert.True(quote.TaxIncluded);
        Assert.Equal(12_980, quote.TotalMinor);
        Assert.Equal(["Medium", "Cheese"], line.Modifiers.Select(m => m.Name));
    }

    [Fact]
    public async Task Quote_reports_every_problem_by_line()
    {
        var f = await CreateAsync();
        _host.Clock.Advance(TimeSpan.FromHours(2)); // 12:00 in Dubai: breakfast is over

        var quote = await _host.CartQuotes.QuoteAsync(f.SessionId, new CartRequest([
            new(f.Burger.Id, 1, [], null),                         // 0: doneness missing
            new(f.Burger.Id, 1, [f.Medium, f.Cheese, f.Bacon, f.Egg], null), // 1: three extras, max two
            new(f.Soup.Id, 1, [], null),                           // 2: sold out
            new(f.Shakshuka.Id, 1, [], null),                      // 3: breakfast closed
            new(f.Burger.Id, 1, [f.Medium, Guid.NewGuid()], null), // 4: option no longer exists
            new(Guid.NewGuid(), 1, [], null)                       // 5: item no longer exists
        ]), default);

        Assert.False(quote.CanOrder);
        Assert.Equal(
            [(0, "invalid_choices"), (1, "invalid_choices"), (2, "item_unavailable"), (3, "menu_closed"), (4, "option_not_found"), (5, "item_not_found")],
            quote.Issues.Select(i => (i.Index, i.Code)));
    }

    [Fact]
    public async Task An_item_on_several_menus_can_be_ordered_while_any_of_them_is_served()
    {
        var f = await CreateAsync();
        _host.Clock.Advance(TimeSpan.FromHours(2)); // 12:00 in Dubai: breakfast is over
        var shakshuka = new CartRequest([new(f.Shakshuka.Id, 1, [], null)]);

        var breakfastOnly = await _host.CartQuotes.QuoteAsync(f.SessionId, shakshuka, default);
        var allDay = (await _host.Menus.GetTreeAsync(f.Venue.Id, default)).Menus.Single(m => m.Name == "All day");
        await _host.Menus.SetMenuItemsAsync(allDay.Id, new MenuItemsRequest([f.Burger.Id, f.Soup.Id, f.Shakshuka.Id]), default);
        var alsoAllDay = await _host.CartQuotes.QuoteAsync(f.SessionId, shakshuka, default);

        Assert.Equal("Shakshuka is on the Breakfast menu, served 07:00–11:30.", breakfastOnly.Issues.Single().Message);
        Assert.True(alsoAllDay.CanOrder);
    }

    [Fact]
    public async Task Placing_an_order_saves_priced_snapshots_and_notifies()
    {
        var f = await CreateAsync();

        var order = await _host.GuestOrders.PlaceAsync(f.SessionId,
            new PlaceOrderRequest(Guid.NewGuid(), [f.TwoBurgersWithCheese], "Birthday table", 12_980), default);

        Assert.Equal(Order.FirstNumber, order.Number);
        Assert.Equal(OrderStatus.Submitted, order.Status);
        Assert.Equal(("Table", "7"), (order.LocationType, order.LocationLabel));
        Assert.Equal(12_980, order.TotalMinor);
        Assert.Equal("Birthday table", order.Note);
        Assert.Equal("No pickles", order.Lines.Single().Note);
        Assert.Equal(["Medium", "Cheese"], order.Lines.Single().Modifiers.Select(m => m.Name));
        Assert.Equal(("placed", order.Id), (_host.Notifier.Events.Single().Event, _host.Notifier.Events.Single().Order.Id));
    }

    [Fact]
    public async Task Retrying_with_the_same_request_id_never_creates_a_second_order()
    {
        var f = await CreateAsync();
        var request = new PlaceOrderRequest(Guid.NewGuid(), [f.TwoBurgersWithCheese], null, null);

        var first = await _host.GuestOrders.PlaceAsync(f.SessionId, request, default);
        var retry = await _host.GuestOrders.PlaceAsync(f.SessionId, request, default);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(1, await _host.Db.Orders.CountAsync());
        Assert.Single(_host.Notifier.Events);
    }

    [Fact]
    public async Task Orders_are_numbered_per_venue()
    {
        var f = await CreateAsync();

        var a = await _host.GuestOrders.PlaceAsync(f.SessionId, new PlaceOrderRequest(Guid.NewGuid(), [f.TwoBurgersWithCheese], null, null), default);
        var b = await _host.GuestOrders.PlaceAsync(f.SessionId, new PlaceOrderRequest(Guid.NewGuid(), [new(f.Shakshuka.Id, 1, [], null)], null, null), default);

        Assert.Equal([Order.FirstNumber, Order.FirstNumber + 1], new[] { a.Number, b.Number });
        Assert.Equal([b.Id, a.Id], (await _host.GuestOrders.ListAsync(f.SessionId, default)).Select(o => o.Id));
    }

    [Fact]
    public async Task A_changed_total_or_invalid_cart_is_not_ordered()
    {
        var f = await CreateAsync();

        var changed = await Assert.ThrowsAsync<AppException>(() => _host.GuestOrders.PlaceAsync(f.SessionId,
            new PlaceOrderRequest(Guid.NewGuid(), [f.TwoBurgersWithCheese], null, ExpectedTotalMinor: 11_800), default));
        var invalid = await Assert.ThrowsAsync<AppException>(() => _host.GuestOrders.PlaceAsync(f.SessionId,
            new PlaceOrderRequest(Guid.NewGuid(), [new(f.Soup.Id, 1, [], null)], null, null), default));

        Assert.Equal(("price_changed", 409), (changed.Code, changed.StatusCode));
        Assert.Equal(12_980, Assert.IsType<CartQuoteDto>(changed.Details).TotalMinor);
        Assert.Equal("cart_invalid", invalid.Code);
        Assert.Equal(0, await _host.Db.Orders.CountAsync());
    }

    [Fact]
    public async Task Staff_move_orders_through_the_queue_and_guests_are_told()
    {
        var f = await CreateAsync();
        var order = await _host.GuestOrders.PlaceAsync(f.SessionId, new PlaceOrderRequest(Guid.NewGuid(), [f.TwoBurgersWithCheese], null, null), default);

        var counts = await _host.MerchantOrders.CountsAsync(f.Venue.Id, default);
        await _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted, null), default);
        var again = await _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted, null), default);
        var skip = await Assert.ThrowsAsync<AppException>(() =>
            _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Rejected, "Too late"), default));
        await _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Completed, null), default);

        Assert.Equal(new OrderCountsDto(1, 0, 0, 0), counts);
        Assert.Equal(OrderStatus.Accepted, again.Status); // a second tap is harmless
        Assert.Equal("invalid_transition", skip.Code);
        Assert.Empty(await _host.MerchantOrders.ListAsync(f.Venue.Id, OrderFilter.Active, default));
        Assert.Single(await _host.MerchantOrders.ListAsync(f.Venue.Id, OrderFilter.Completed, default));
        Assert.Equal(["placed", "changed", "changed"], _host.Notifier.Events.Select(e => e.Event));
    }

    [Fact]
    public async Task Another_tenant_cannot_see_or_change_orders()
    {
        var f = await CreateAsync();
        var order = await _host.GuestOrders.PlaceAsync(f.SessionId, new PlaceOrderRequest(Guid.NewGuid(), [f.TwoBurgersWithCheese], null, null), default);

        await _host.CreateTenantAsync("Someone else");

        Assert.Equal("venue_not_found", (await Assert.ThrowsAsync<AppException>(() => _host.MerchantOrders.ListAsync(f.Venue.Id, OrderFilter.Active, default))).Code);
        Assert.Equal("order_not_found", (await Assert.ThrowsAsync<AppException>(() =>
            _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted, null), default))).Code);
    }

    [Fact]
    public async Task An_expired_session_cannot_order()
    {
        var f = await CreateAsync();
        _host.Clock.Advance(TimeSpan.FromHours(5));

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.GuestOrders.PlaceAsync(f.SessionId,
            new PlaceOrderRequest(Guid.NewGuid(), [f.TwoBurgersWithCheese], null, null), default));

        Assert.Equal("session_expired", ex.Code);
    }
}
