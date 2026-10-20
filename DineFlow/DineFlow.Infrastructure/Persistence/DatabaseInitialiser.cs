using DineFlow.Application.Abstractions;
using DineFlow.Domain.Promotions;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DineFlow.Infrastructure.Persistence;

public static class DatabaseInitialiser
{
    /// <summary>
    /// Applies pending migrations and, when <c>Seed:Enabled</c> is true and the database is empty,
    /// creates a demo tenant to click around with. Meant for local development only.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        using var scope = services.CreateScope();
        var sp = scope.ServiceProvider;
        var db = sp.GetRequiredService<AppDbContext>();
        var logger = sp.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(DatabaseInitialiser));
        var config = sp.GetRequiredService<IConfiguration>();

        await db.Database.MigrateAsync(ct);

        if (!config.GetValue<bool>("Seed:Enabled")) return;

        if (!await db.Tenants.AnyAsync(ct))
        {
            var email = config["Seed:Owner:Email"];
            var password = config["Seed:Owner:Password"];
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("Seed:Enabled is true but Seed:Owner:Email/Password are not set; skipping demo data.");
                return;
            }

            await SeedDemoTenantAsync(db, sp.GetRequiredService<IQrTokenService>(), sp.GetRequiredService<IPasswordService>(),
                email, password, ct);
            logger.LogInformation("Seeded demo tenant 'Demo Bistro' with owner {Email}.", email);
        }

        await SeedDemoMenuAsync(db, logger, ct);
        await SeedDemoPromotionsAsync(db, logger, ct);
    }

    /// <summary>Two demo codes: WELCOME10 to try at checkout, NEXT15 offered on the payment confirmation.</summary>
    private static async Task SeedDemoPromotionsAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var demoTenantId = await db.Tenants.Where(t => t.LegalName == DemoTenantLegalName).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct);
        if (demoTenantId is not { } tenantId || await db.PromoCodes.IgnoreQueryFilters().AnyAsync(p => p.TenantId == tenantId, ct)) return;

        var welcome = new PromoCode(tenantId, "WELCOME10");
        welcome.Update("10% off your bill", DiscountType.Percent, 1000, maxDiscountMinor: 5000, minBillMinor: null,
            startsAt: null, endsAt: null, maxRedemptions: null, isActive: true, showAfterPayment: false);
        var next = new PromoCode(tenantId, "NEXT15");
        next.Update("15% off your next visit", DiscountType.Percent, 1500, maxDiscountMinor: 7500, minBillMinor: 2000,
            startsAt: null, endsAt: null, maxRedemptions: null, isActive: true, showAfterPayment: true);

        db.PromoCodes.AddRange(welcome, next);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo promotion codes.");
    }

    /// <summary>
    /// Gives the demo venue a menu if it has none, including demo databases created before menus
    /// existed. Startup has no tenant, so these queries bypass the tenant filter.
    /// </summary>
    private static async Task SeedDemoMenuAsync(AppDbContext db, ILogger logger, CancellationToken ct)
    {
        var demoTenantId = await db.Tenants.Where(t => t.LegalName == DemoTenantLegalName).Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct);
        if (demoTenantId is null) return;

        var venue = await db.Venues.IgnoreQueryFilters().FirstOrDefaultAsync(v => v.TenantId == demoTenantId, ct);
        if (venue is null || await db.Menus.IgnoreQueryFilters().AnyAsync(m => m.VenueId == venue.Id, ct)) return;

        DemoMenuSeed.Add(db, venue);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Seeded demo menus for venue {Venue}.", venue.Name);
    }

    private const string DemoTenantLegalName = "Demo Bistro LLC";

    private static async Task SeedDemoTenantAsync(
        AppDbContext db, IQrTokenService qrTokens, IPasswordService passwords,
        string ownerEmail, string ownerPassword, CancellationToken ct)
    {
        var tenant = new Tenant(DemoTenantLegalName, "Demo Bistro", "AE");
        tenant.Activate();
        db.Tenants.Add(tenant);

        var venue = new Venue(tenant.Id, "Dubai Marina", "AED", "Asia/Dubai", "en");
        venue.SetCharges(serviceChargeBasisPoints: 1000, taxBasisPoints: 500, pricesIncludeTax: true);
        db.Venues.Add(venue);

        var mainHall = new DiningArea(tenant.Id, venue.Id, "Main hall", 0);
        var terrace = new DiningArea(tenant.Id, venue.Id, "Terrace", 1);
        db.DiningAreas.AddRange(mainHall, terrace);

        void AddLocation(DiningArea? area, ServiceLocationType type, string label)
        {
            var location = new ServiceLocation(tenant.Id, venue.Id, area?.Id, type, label);
            var qrId = Guid.NewGuid();
            db.ServiceLocations.Add(location);
            db.QrTokens.Add(new QrToken(qrId, tenant.Id, location.Id, 1, qrTokens.Hash(qrTokens.CreateToken(qrId))));
        }

        for (var n = 1; n <= 8; n++) AddLocation(mainHall, ServiceLocationType.Table, n.ToString());
        for (var n = 9; n <= 12; n++) AddLocation(terrace, ServiceLocationType.Table, n.ToString());
        AddLocation(null, ServiceLocationType.Counter, "1");

        var owner = new StaffUser(tenant.Id, ownerEmail, "Demo Owner", StaffRole.Owner);
        owner.SetPasswordHash(passwords.Hash(owner, ownerPassword));
        db.StaffUsers.Add(owner);

        await db.SaveChangesAsync(ct);
    }
}
