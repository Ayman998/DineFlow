using DineFlow.Application.Common;
using DineFlow.Application.Onboarding;
using DineFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Api.Common;

/// <summary>
/// One-off operator commands, run with the API's own configuration instead of starting the web server:
/// <code>
/// dotnet DineFlow.Api.dll migrate
/// dotnet DineFlow.Api.dll provision --business "Demo Bistro" --venue "Abdoun, Amman" --owner-email owner@example.com --owner-name "Owner Name"
/// </code>
/// In the Docker deployment: <c>docker compose run --rm api migrate</c> (see deploy/README.md).
/// </summary>
public static class AdminCommands
{
    public const string Usage = """
        Commands:
          migrate     Apply pending database migrations, then exit.
          provision   Create a business, its first venue with tables and QR codes, and the owner's account.
                      --business <name>        Name guests and staff see (required)
                      --venue <name>           Venue name, e.g. "Abdoun, Amman" (required)
                      --owner-email <address>  The owner's sign-in (required)
                      --owner-name <name>      (required)
                      --legal <name>           Legal name, if different from --business
                      --venue-ar <name>        Arabic venue name
                      --tables <n>             Numbered tables to create, 0-200 (default 10)
                      --tax <basis points>     Sales tax, 1600 = 16% (default 1600)
                      --service <basis points> Service charge, 1000 = 10% (default 1000)
                      --prices-exclude-tax     Menu prices are before tax (default: they include it)
        """;

    /// <returns>The exit code when <paramref name="args"/> names a command; null to start the web server.</returns>
    public static async Task<int?> TryRunAsync(WebApplication app, string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-')) return null;
        using var scope = app.Services.CreateScope();
        var sp = scope.ServiceProvider;

        switch (args[0])
        {
            case "migrate":
                var db = sp.GetRequiredService<AppDbContext>();
                var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
                await db.Database.MigrateAsync();
                Console.WriteLine(pending.Count == 0 ? "The database is up to date." : $"Applied {pending.Count} migration(s): {string.Join(", ", pending)}");
                return 0;

            case "provision":
                try
                {
                    var request = ParseProvision(args.Skip(1).ToArray());
                    var result = await sp.GetRequiredService<ProvisioningService>().ProvisionAsync(request, CancellationToken.None);
                    Console.WriteLine($"""
                        Created {request.BusinessName}, venue {request.VenueName} with {result.Tables} table(s).
                          Tenant id: {result.TenantId}
                          Venue id:  {result.VenueId}
                          Owner:     {result.OwnerEmail}
                          Temporary password: {result.TemporaryPassword}
                        Hand the password over privately. The owner changes it under Settings > Team > Your password.
                        """);
                    return 0;
                }
                catch (Exception ex) when (ex is AppException or ArgumentException)
                {
                    Console.Error.WriteLine(ex.Message);
                    Console.Error.WriteLine(Usage);
                    return 2;
                }

            default:
                Console.Error.WriteLine($"Unknown command '{args[0]}'.");
                Console.Error.WriteLine(Usage);
                return 2;
        }
    }

    public static ProvisionRequest ParseProvision(string[] args)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var flags = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--")) throw new ArgumentException($"Unexpected argument '{args[i]}'.");
            var name = args[i][2..];
            if (name == "prices-exclude-tax") { flags.Add(name); continue; }
            if (i + 1 >= args.Length) throw new ArgumentException($"--{name} needs a value.");
            values[name] = args[++i];
        }

        string Required(string name) => values.GetValueOrDefault(name) is { Length: > 0 } v ? v : throw new ArgumentException($"--{name} is required.");
        int Number(string name, int fallback) => values.GetValueOrDefault(name) is not { } v ? fallback
            : int.TryParse(v, out var n) ? n : throw new ArgumentException($"--{name} must be a whole number.");

        var known = new[] { "business", "venue", "owner-email", "owner-name", "legal", "venue-ar", "tables", "tax", "service" };
        if (values.Keys.FirstOrDefault(k => !known.Contains(k, StringComparer.OrdinalIgnoreCase)) is { } unknown)
            throw new ArgumentException($"Unknown option --{unknown}.");

        return new ProvisionRequest(
            BusinessName: Required("business"),
            VenueName: Required("venue"),
            OwnerEmail: Required("owner-email"),
            OwnerName: Required("owner-name"),
            LegalName: values.GetValueOrDefault("legal"),
            VenueNameAr: values.GetValueOrDefault("venue-ar"),
            Tables: Number("tables", 10),
            TaxBasisPoints: Number("tax", 1600),
            ServiceChargeBasisPoints: Number("service", 1000),
            PricesIncludeTax: !flags.Contains("prices-exclude-tax"));
    }
}
