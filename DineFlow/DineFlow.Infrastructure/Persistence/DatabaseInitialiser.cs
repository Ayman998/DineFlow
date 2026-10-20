using DineFlow.Application.Abstractions;
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

        if (!config.GetValue<bool>("Seed:Enabled") || await db.Tenants.AnyAsync(ct)) return;

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

    private static async Task SeedDemoTenantAsync(
        AppDbContext db, IQrTokenService qrTokens, IPasswordService passwords,
        string ownerEmail, string ownerPassword, CancellationToken ct)
    {
        var tenant = new Tenant("Demo Bistro LLC", "Demo Bistro", "AE");
        tenant.Activate();
        db.Tenants.Add(tenant);

        var venue = new Venue(tenant.Id, "Dubai Marina", "AED", "Asia/Dubai", "en");
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
