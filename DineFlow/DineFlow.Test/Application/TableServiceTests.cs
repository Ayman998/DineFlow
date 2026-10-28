using DineFlow.Application.Common;
using DineFlow.Application.Tables;
using DineFlow.Domain.Venues;

namespace DineFlow.Test.Application;

public class TableServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    [Fact]
    public async Task New_location_gets_a_version_1_active_qr_pointing_at_the_guest_app()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();

        var counter = await _host.Tables.CreateLocationAsync(
            new CreateLocationRequest(venue.Id, null, ServiceLocationType.Counter, "1"), default);

        Assert.Equal("Counter 1", counter.DisplayName);
        Assert.NotNull(counter.Qr);
        Assert.Equal(1, counter.Qr.Version);
        Assert.Equal("Active", counter.Qr.Status);
        Assert.StartsWith("https://guest.test/q/", counter.Qr.Url);
    }

    [Fact]
    public async Task Bulk_create_numbers_tables_and_skips_existing_ones()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();
        await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "3"), default);

        var created = await _host.Tables.BulkCreateTablesAsync(new BulkCreateTablesRequest(venue.Id, null, 1, 5), default);
        var plan = await _host.Tables.GetFloorPlanAsync(venue.Id, default);

        Assert.Equal(["1", "2", "4", "5"], created.Select(t => t.Label));
        Assert.Equal(["1", "2", "3", "4", "5"], plan.Unassigned.Select(t => t.Label));
    }

    [Fact]
    public async Task Duplicate_label_is_a_conflict()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();
        var request = new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7");
        await _host.Tables.CreateLocationAsync(request, default);

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Tables.CreateLocationAsync(request, default));

        Assert.Equal("location_exists", ex.Code);
    }

    [Fact]
    public async Task Floor_plan_groups_locations_by_area()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();
        var terrace = await _host.Tables.CreateAreaAsync(venue.Id, new CreateAreaRequest("Terrace"), default);
        await _host.Tables.BulkCreateTablesAsync(new BulkCreateTablesRequest(venue.Id, terrace.Id, 9, 10), default);

        var plan = await _host.Tables.GetFloorPlanAsync(venue.Id, default);

        var area = Assert.Single(plan.Areas);
        Assert.Equal("Terrace", area.Name);
        Assert.Equal(["Table 9", "Table 10"], area.Locations.Select(l => l.DisplayName));
        Assert.Empty(plan.Unassigned);
    }

    [Fact]
    public async Task Closing_an_area_stops_its_qr_codes_and_reopening_keeps_tables_disabled_one_by_one()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();
        var terrace = await _host.Tables.CreateAreaAsync(venue.Id, new CreateAreaRequest("Terrace"), default);
        var hall = await _host.Tables.CreateAreaAsync(venue.Id, new CreateAreaRequest("Main hall"), default);
        var tables = await _host.Tables.BulkCreateTablesAsync(new BulkCreateTablesRequest(venue.Id, terrace.Id, 1, 2), default);
        var inHall = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, hall.Id, ServiceLocationType.Table, "3"), default);
        await _host.Tables.SetQrEnabledAsync(tables[1].Id, enabled: false, default);

        var closed = await _host.Tables.SetAreaActiveAsync(venue.Id, terrace.Id, active: false, default);
        var scan = await Assert.ThrowsAsync<AppException>(() =>
            _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(tables[0].Qr!.Url), null, default));
        await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(inHall.Qr!.Url), null, default);

        var reopened = await _host.Tables.SetAreaActiveAsync(venue.Id, terrace.Id, active: true, default);
        await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(tables[0].Qr!.Url), null, default);

        Assert.False(closed.IsActive);
        Assert.Equal("venue_unavailable", scan.Code);
        Assert.True(reopened.IsActive);
        Assert.Equal(["Active", "Disabled"], reopened.Locations.Select(l => l.Qr!.Status));
        Assert.True((await _host.Tables.GetFloorPlanAsync(venue.Id, default)).Areas.All(a => a.IsActive));
    }

    [Fact]
    public async Task Guests_reading_arabic_get_the_areas_arabic_name()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();
        var terrace = await _host.Tables.CreateAreaAsync(venue.Id, new CreateAreaRequest("Terrace"), default);
        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, terrace.Id, ServiceLocationType.Table, "4"), default);

        var renamed = await _host.Tables.UpdateAreaAsync(venue.Id, terrace.Id, new UpdateAreaRequest(" Terrace ", " التراس "), default);
        var guest = await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(table.Qr!.Url), "ar", default);
        var cleared = await _host.Tables.UpdateAreaAsync(venue.Id, terrace.Id, new UpdateAreaRequest("Garden", ""), default);

        Assert.Equal(("Terrace", "التراس"), (renamed.Name, renamed.NameAr));
        Assert.Equal(["Table 4"], renamed.Locations.Select(l => l.DisplayName));
        Assert.Equal(("Terrace", "التراس"), (guest.Context.Location.AreaName, guest.Context.Location.AreaNameAr));
        Assert.Equal(("Garden", (string?)null), (cleared.Name, cleared.NameAr));
    }

    [Fact]
    public async Task An_area_of_another_venue_cannot_be_closed()
    {
        await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync();
        var other = await _host.CreateVenueAsync("Marina");
        var terrace = await _host.Tables.CreateAreaAsync(other.Id, new CreateAreaRequest("Terrace"), default);

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Tables.SetAreaActiveAsync(venue.Id, terrace.Id, false, default));

        Assert.Equal("area_not_found", ex.Code);
    }

    [Fact]
    public async Task One_tenant_cannot_see_or_change_another_tenants_venue()
    {
        await _host.CreateTenantAsync("Tenant A");
        var venueA = await _host.CreateVenueAsync();
        var tableA = await _host.Tables.CreateLocationAsync(
            new CreateLocationRequest(venueA.Id, null, ServiceLocationType.Table, "1"), default);

        await _host.CreateTenantAsync("Tenant B"); // now signed in as B

        var floor = await Assert.ThrowsAsync<AppException>(() => _host.Tables.GetFloorPlanAsync(venueA.Id, default));
        var rotate = await Assert.ThrowsAsync<AppException>(() => _host.Tables.RotateQrAsync(tableA.Id, default));
        var addTable = await Assert.ThrowsAsync<AppException>(() => _host.Tables.CreateLocationAsync(
            new CreateLocationRequest(venueA.Id, null, ServiceLocationType.Table, "2"), default));

        Assert.Equal("venue_not_found", floor.Code);
        Assert.Equal("location_not_found", rotate.Code);
        Assert.Equal("venue_not_found", addTable.Code);
        Assert.Empty(await _host.Venues.ListAsync(default));
    }
}
