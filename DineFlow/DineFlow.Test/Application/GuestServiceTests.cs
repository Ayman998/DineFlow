using DineFlow.Application.Common;
using DineFlow.Application.Tables;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

public class GuestServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private async Task<(LocationDto Table, string Token)> CreateTableAsync(string label = "12")
    {
        await _host.CreateTenantAsync("Demo Bistro");
        var venue = await _host.CreateVenueAsync("Dubai Marina");
        var area = await _host.Tables.CreateAreaAsync(venue.Id, new CreateAreaRequest("Main hall"), default);
        var table = await _host.Tables.CreateLocationAsync(
            new CreateLocationRequest(venue.Id, area.Id, ServiceLocationType.Table, label), default);
        return (table, TestHost.TokenFromUrl(table.Qr!.Url));
    }

    [Fact]
    public async Task Scanning_a_valid_qr_starts_a_session_with_the_venue_and_table()
    {
        var (_, token) = await CreateTableAsync();
        _host.CurrentTenant = null; // guests scan anonymously

        var result = await _host.Guests.ResolveQrAsync(token, locale: "ar", default);

        Assert.False(string.IsNullOrEmpty(result.SessionToken));
        Assert.Equal("Demo Bistro", result.Context.Venue.BrandName);
        Assert.Equal("Dubai Marina", result.Context.Venue.BranchName);
        Assert.Equal("Table 12", result.Context.Location.DisplayName);
        Assert.Equal("Main hall", result.Context.Location.AreaName);
        Assert.Equal("ar", result.Context.Locale);
        Assert.True(result.Context.Features.Menu && result.Context.Features.PayAtTable);
        Assert.Equal(1, await _host.Db.Set<GuestSession>().IgnoreQueryFilters().CountAsync());
    }

    [Fact]
    public async Task Unsupported_locale_falls_back_to_the_venue_default()
    {
        var (_, token) = await CreateTableAsync();
        _host.CurrentTenant = null;

        var result = await _host.Guests.ResolveQrAsync(token, locale: "fr", default);

        Assert.Equal("en", result.Context.Locale);
    }

    [Fact]
    public async Task Unknown_or_malformed_tokens_are_invalid()
    {
        await CreateTableAsync();
        _host.CurrentTenant = null;

        var unknown = await Assert.ThrowsAsync<AppException>(() =>
            _host.Guests.ResolveQrAsync(new string('a', 27), null, default));
        var malformed = await Assert.ThrowsAsync<AppException>(() =>
            _host.Guests.ResolveQrAsync("not-a-token", null, default));

        Assert.Equal("qr_invalid", unknown.Code);
        Assert.Equal("qr_invalid", malformed.Code);
    }

    [Fact]
    public async Task Disabled_qr_is_invalid_until_enabled_again()
    {
        var (table, token) = await CreateTableAsync();
        await _host.Tables.SetQrEnabledAsync(table.Id, enabled: false, default);

        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Guests.ResolveQrAsync(token, null, default));
        Assert.Equal("qr_invalid", ex.Code);

        _host.CurrentTenant = tenant;
        await _host.Tables.SetQrEnabledAsync(table.Id, enabled: true, default);
        _host.CurrentTenant = null;
        await _host.Guests.ResolveQrAsync(token, null, default);
    }

    [Fact]
    public async Task Rotating_a_qr_kills_the_old_code_and_the_new_one_works()
    {
        var (table, oldToken) = await CreateTableAsync();

        var rotated = await _host.Tables.RotateQrAsync(table.Id, default);
        var newToken = TestHost.TokenFromUrl(rotated.Qr!.Url);
        _host.CurrentTenant = null;

        Assert.Equal(2, rotated.Qr.Version);
        Assert.NotEqual(oldToken, newToken);
        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Guests.ResolveQrAsync(oldToken, null, default));
        Assert.Equal("qr_invalid", ex.Code);
        await _host.Guests.ResolveQrAsync(newToken, null, default);
    }

    [Fact]
    public async Task Suspended_tenant_makes_the_venue_unavailable()
    {
        var (_, token) = await CreateTableAsync();
        var tenant = await _host.Db.Tenants.SingleAsync();
        tenant.Suspend();
        await _host.Db.SaveChangesAsync();
        _host.CurrentTenant = null;

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Guests.ResolveQrAsync(token, null, default));

        Assert.Equal("venue_unavailable", ex.Code);
        Assert.Equal(410, ex.StatusCode);
    }

    [Fact]
    public async Task A_guest_can_reload_their_own_session()
    {
        var (_, token) = await CreateTableAsync();
        _host.CurrentTenant = null;
        var resolved = await _host.Guests.ResolveQrAsync(token, null, default);

        // The guest's bearer token carries the tenant, so their requests run inside it.
        _host.CurrentTenant = (await _host.Db.Tenants.SingleAsync()).Id;
        var context = await _host.Guests.GetSessionAsync(resolved.Context.SessionId, default);

        Assert.Equal(resolved.Context.SessionId, context.SessionId);
        Assert.Equal("Table 12", context.Location.DisplayName);
    }
}
