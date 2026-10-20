using DineFlow.Application.Common;
using DineFlow.Application.Menus;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Menus;
using DineFlow.Domain.Venues;

namespace DineFlow.Test.Application;

public class MenuServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static MenuRequest AllDay(string name = "All day", bool published = true) =>
        new(name, null, published, Enum.GetValues<DayOfWeek>(), null, null);

    private static ItemRequest Burger(Guid categoryId, params ModifierGroupRequest[] groups) => new(
        categoryId, "Classic burger", "برجر", "Beef, cheddar", null, 5500, null, IsAvailable: true,
        [Allergen.Gluten, Allergen.Milk], ["Chef's pick"], groups);

    private async Task<(VenueDto Venue, MenuDto Menu, CategoryDto Category)> CreateMenuAsync()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();
        var menu = await _host.Menus.CreateMenuAsync(venue.Id, AllDay(), default);
        var category = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, null, "Mains", "الأطباق"), default);
        return (venue, menu, category);
    }

    /// <summary>Starts a guest session at a table of the current venue and switches to guest mode.</summary>
    private async Task<Guid> StartGuestSessionAsync(Guid venueId)
    {
        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venueId, null, ServiceLocationType.Table, "1"), default);
        var tenantId = _host.CurrentTenant;
        _host.CurrentTenant = null;
        var resolved = await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(table.Qr!.Url), null, default);
        _host.CurrentTenant = tenantId; // the guest's token carries the tenant
        return resolved.Context.SessionId;
    }

    [Fact]
    public async Task Item_with_modifiers_round_trips_through_the_tree()
    {
        var (venue, _, category) = await CreateMenuAsync();

        await _host.Menus.CreateItemAsync(Burger(category.Id,
            new ModifierGroupRequest(null, "Doneness", null, 1, 1, [
                new(null, "Medium", null, 0, true), new(null, "Well done", null, 0, false)
            ])), default);

        var tree = await _host.Menus.GetTreeAsync(venue.Id, default);
        var item = tree.Menus.Single().Categories.Single().Items.Single();

        Assert.Equal("AED", tree.Currency);
        Assert.Equal(5500, item.PriceMinor);
        Assert.Equal([Allergen.Gluten, Allergen.Milk], item.Allergens);
        Assert.Equal(["Chef's pick"], item.Tags);
        var group = Assert.Single(item.ModifierGroups);
        Assert.Equal(["Medium", "Well done"], group.Options.Select(o => o.Name));
        Assert.True(group.Options[0].IsDefault);
    }

    [Fact]
    public async Task Updating_an_item_keeps_existing_modifier_ids_and_saves_changes()
    {
        var (venue, _, category) = await CreateMenuAsync();
        var created = await _host.Menus.CreateItemAsync(Burger(category.Id,
            new ModifierGroupRequest(null, "Size", null, 1, 1, [new(null, "Regular", null, 0, true), new(null, "Large", null, 400, false)])), default);
        var size = created.ModifierGroups.Single();

        await _host.Menus.UpdateItemAsync(created.Id, Burger(category.Id,
            new ModifierGroupRequest(size.Id, "Size", null, 1, 1, [
                new(size.Options[0].Id, "Regular", null, 0, true),
                new(size.Options[1].Id, "Large", null, 500, false),
                new(null, "Huge", null, 900, false)
            ]),
            new ModifierGroupRequest(null, "Extras", null, 0, 1, [new(null, "Cheese", null, 300, false)])), default);

        _host.Db.ChangeTracker.Clear();
        var item = (await _host.Menus.GetTreeAsync(venue.Id, default)).Menus.Single().Categories.Single().Items.Single();

        Assert.Equal(["Size", "Extras"], item.ModifierGroups.Select(g => g.Name));
        Assert.Equal(size.Id, item.ModifierGroups[0].Id);
        Assert.Equal(size.Options[1].Id, item.ModifierGroups[0].Options[1].Id);
        Assert.Equal([0L, 500L, 900L], item.ModifierGroups[0].Options.Select(o => o.PriceDeltaMinor));
    }

    [Fact]
    public async Task Subcategories_nest_one_level_only()
    {
        var (venue, menu, mains) = await CreateMenuAsync();
        var grills = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, mains.Id, "Grills", null), default);

        var tooDeep = await Assert.ThrowsAsync<AppException>(() =>
            _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, grills.Id, "Kebabs", null), default));
        var tree = await _host.Menus.GetTreeAsync(venue.Id, default);

        Assert.Equal("too_deep", tooDeep.Code);
        Assert.Equal("Grills", tree.Menus.Single().Categories.Single().Subcategories.Single().Name);
    }

    [Fact]
    public async Task Deleting_a_menu_removes_its_categories_and_items()
    {
        var (venue, menu, mains) = await CreateMenuAsync();
        var grills = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, mains.Id, "Grills", null), default);
        await _host.Menus.CreateItemAsync(Burger(grills.Id), default);

        await _host.Menus.DeleteMenuAsync(menu.Id, default);

        Assert.Empty((await _host.Menus.GetTreeAsync(venue.Id, default)).Menus);
        Assert.Empty(_host.Db.MenuCategories);
        Assert.Empty(_host.Db.MenuItems);
    }

    [Fact]
    public async Task Reorder_sets_the_order_of_siblings()
    {
        var (venue, _, category) = await CreateMenuAsync();
        var a = await _host.Menus.CreateItemAsync(Burger(category.Id) with { Name = "A" }, default);
        var b = await _host.Menus.CreateItemAsync(Burger(category.Id) with { Name = "B" }, default);
        var c = await _host.Menus.CreateItemAsync(Burger(category.Id) with { Name = "C" }, default);

        await _host.Menus.ReorderAsync(new ReorderRequest("items", [c.Id, a.Id, b.Id]), default);

        var items = (await _host.Menus.GetTreeAsync(venue.Id, default)).Menus.Single().Categories.Single().Items;
        Assert.Equal(["C", "A", "B"], items.Select(i => i.Name));
    }

    [Fact]
    public async Task Guests_see_only_published_menus_with_availability_in_venue_time()
    {
        // Tomorrow 06:00 UTC is 10:00 in Dubai (UTC+4): breakfast is on.
        var tomorrow = new DateTimeOffset(_host.Clock.GetUtcNow().UtcDateTime.Date.AddDays(1), TimeSpan.Zero);
        _host.Clock.SetUtcNow(tomorrow.AddHours(6));

        var (venue, _, _) = await CreateMenuAsync();
        await _host.Menus.CreateMenuAsync(venue.Id, AllDay("Draft", published: false), default);
        await _host.Menus.CreateMenuAsync(venue.Id,
            new MenuRequest("Breakfast", "الفطور", true, Enum.GetValues<DayOfWeek>(), "07:00", "11:30"), default);
        var sessionId = await StartGuestSessionAsync(venue.Id);

        var morning = await _host.GuestMenu.GetMenuAsync(sessionId, default);
        // 08:00 UTC is 12:00 in Dubai: breakfast is over.
        _host.Clock.Advance(TimeSpan.FromHours(2));
        var noon = await _host.GuestMenu.GetMenuAsync(sessionId, default);

        Assert.Equal(["All day", "Breakfast"], morning.Menus.Select(m => m.Name));
        Assert.True(morning.Menus.Single(m => m.Name == "Breakfast").AvailableNow);
        Assert.False(noon.Menus.Single(m => m.Name == "Breakfast").AvailableNow);
        Assert.Equal("07:00", noon.Menus.Single(m => m.Name == "Breakfast").StartTime);
    }

    [Fact]
    public async Task Guest_menu_requires_a_live_session_and_the_menu_feature()
    {
        var (venue, _, _) = await CreateMenuAsync();
        var sessionId = await StartGuestSessionAsync(venue.Id);

        _host.Clock.Advance(TimeSpan.FromHours(5)); // sessions last 4 hours
        var expired = await Assert.ThrowsAsync<AppException>(() => _host.GuestMenu.GetMenuAsync(sessionId, default));

        Assert.Equal("session_expired", expired.Code);
    }

    [Fact]
    public async Task Another_tenant_cannot_read_or_change_the_menu()
    {
        var (venue, menu, category) = await CreateMenuAsync();
        var item = await _host.Menus.CreateItemAsync(Burger(category.Id), default);

        await _host.CreateTenantAsync("Someone else");

        Assert.Equal("venue_not_found", (await Assert.ThrowsAsync<AppException>(() => _host.Menus.GetTreeAsync(venue.Id, default))).Code);
        Assert.Equal("menu_not_found", (await Assert.ThrowsAsync<AppException>(() => _host.Menus.UpdateMenuAsync(menu.Id, AllDay("Hacked"), default))).Code);
        Assert.Equal("item_not_found", (await Assert.ThrowsAsync<AppException>(() => _host.Menus.SetItemAvailabilityAsync(item.Id, false, default))).Code);
        Assert.Equal("category_not_found", (await Assert.ThrowsAsync<AppException>(() => _host.Menus.CreateItemAsync(Burger(category.Id), default))).Code);
    }

    [Fact]
    public async Task Invalid_schedule_and_modifier_input_becomes_a_400()
    {
        var (venue, _, category) = await CreateMenuAsync();

        var schedule = await Assert.ThrowsAsync<AppException>(() => _host.Menus.CreateMenuAsync(venue.Id,
            new MenuRequest("Lunch", null, true, [DayOfWeek.Monday], "12:00", null), default));
        var modifiers = await Assert.ThrowsAsync<AppException>(() => _host.Menus.CreateItemAsync(Burger(category.Id,
            new ModifierGroupRequest(null, "Size", null, 2, 1, [new(null, "Small", null, 0, false)])), default));

        Assert.Equal(400, schedule.StatusCode);
        Assert.Equal(400, modifiers.StatusCode);
    }
}

public class MenuImageServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0, 0, 0, 0, 0, 0, 0 }, "jpg")]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 0 }, "png")]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 1, 2, 3, 4, 0x57, 0x45, 0x42, 0x50 }, "webp")]
    [InlineData(new byte[] { 0x3C, 0x73, 0x76, 0x67, 0x20, 0x78, 0x6D, 0x6C, 0, 0, 0, 0 }, null)] // "<svg xml"
    public void Detects_images_by_content(byte[] header, string? expected)
    {
        Assert.Equal(expected, MenuImageService.DetectImageType(header));
    }

    [Fact]
    public async Task Upload_stores_a_valid_image_under_the_tenant()
    {
        var tenant = await _host.CreateTenantAsync();
        var png = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6 };

        var result = await _host.Images.UploadAsync(new MemoryStream(png), png.Length, default);

        Assert.StartsWith($"/media/{tenant.Id:N}/menu/", result.Url);
        Assert.EndsWith(".png", result.Url);
        Assert.Equal(png, _host.Files.Saved[result.Url]);
    }

    [Fact]
    public async Task Upload_rejects_non_images_and_oversized_files()
    {
        await _host.CreateTenantAsync();
        var html = "<html>hi</html>"u8.ToArray();

        var notImage = await Assert.ThrowsAsync<AppException>(() => _host.Images.UploadAsync(new MemoryStream(html), html.Length, default));
        var tooBig = await Assert.ThrowsAsync<AppException>(() => _host.Images.UploadAsync(Stream.Null, MenuImageService.MaxBytes + 1, default));

        Assert.Equal("image_type", notImage.Code);
        Assert.Equal("image_too_large", tooBig.Code);
        Assert.Empty(_host.Files.Saved);
    }
}
