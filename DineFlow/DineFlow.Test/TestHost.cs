using System.Security.Cryptography;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Auth;
using DineFlow.Application.Common;
using DineFlow.Application.Guests;
using DineFlow.Application.Menus;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Tenants;
using DineFlow.Infrastructure.Persistence;
using DineFlow.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DineFlow.Test;

/// <summary>
/// Wires the real application services to an in-memory SQLite database. Set
/// <see cref="CurrentTenant"/> to act as a signed-in merchant, or null to act as an anonymous guest.
/// </summary>
public sealed class TestHost : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly TestTenantContext _tenant = new();

    public TestHost()
    {
        _connection.Open();
        Db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _tenant);
        Db.Database.EnsureCreated();

        QrTokens = new QrTokenService(Options.Create(new QrOptions { TokenKey = RandomKey() }));
        Passwords = new PasswordService();
        var tokenIssuer = new JwtTokenIssuer(Options.Create(new JwtOptions
        {
            Issuer = "test", Audience = "test", SigningKey = RandomKey()
        }), Clock);
        var guestOptions = Options.Create(new GuestOptions { AppBaseUrl = "https://guest.test" });

        Guests = new GuestService(Db, QrTokens, tokenIssuer, guestOptions, Clock);
        Venues = new VenueService(Db, _tenant);
        Tables = new TableService(Db, _tenant, QrTokens, guestOptions, Clock);
        Auth = new AuthService(Db, Passwords, tokenIssuer);
        GuestMenu = new GuestMenuService(Db, Clock);
        Menus = new MenuAdminService(Db, _tenant, Clock);
        Images = new MenuImageService(Files, _tenant);
    }

    /// <summary>Controllable "now" for every service; starts at the real current time.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public InMemoryFileStorage Files { get; } = new();

    public AppDbContext Db { get; }
    public IQrTokenService QrTokens { get; }
    public IPasswordService Passwords { get; }
    public GuestService Guests { get; }
    public VenueService Venues { get; }
    public TableService Tables { get; }
    public AuthService Auth { get; }
    public GuestMenuService GuestMenu { get; }
    public MenuAdminService Menus { get; }
    public MenuImageService Images { get; }

    public Guid? CurrentTenant
    {
        get => _tenant.TenantId;
        set => _tenant.TenantId = value;
    }

    /// <summary>Creates an active tenant and signs in as it.</summary>
    public async Task<Tenant> CreateTenantAsync(string name = "Test Bistro")
    {
        var tenant = new Tenant($"{name} LLC", name, "AE");
        tenant.Activate();
        Db.Tenants.Add(tenant);
        await Db.SaveChangesAsync();
        CurrentTenant = tenant.Id;
        return tenant;
    }

    public Task<VenueDto> CreateVenueAsync(string name = "Downtown") =>
        Venues.CreateAsync(new VenueSettingsRequest(name, "AED", "Asia/Dubai", "en", null, "#0B3B38", true, true), default);

    /// <summary>The raw token at the end of a printed QR URL.</summary>
    public static string TokenFromUrl(string url) => url[(url.LastIndexOf('/') + 1)..];

    public void Dispose()
    {
        Db.Dispose();
        _connection.Dispose();
    }

    private static string RandomKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Keeps "uploaded" files in memory.</summary>
    public sealed class InMemoryFileStorage : IFileStorage
    {
        public Dictionary<string, byte[]> Saved { get; } = [];

        public async Task<string> SaveAsync(Guid tenantId, string folder, string extension, Stream content, CancellationToken ct)
        {
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, ct);
            var url = $"/media/{tenantId:N}/{folder}/{Guid.NewGuid():N}.{extension}";
            Saved[url] = copy.ToArray();
            return url;
        }
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
    }
}
