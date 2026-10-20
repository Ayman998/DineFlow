using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Infrastructure.Persistence;
using DineFlow.Infrastructure.Security;
using DineFlow.Infrastructure.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure;

public static class DependencyInjection
{
    /// <param name="contentRootPath">The API's content root; relative storage paths resolve against it.</param>
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services, IConfiguration configuration, string contentRootPath)
    {
        var connectionString = configuration.GetConnectionString("DineFlow")
            ?? throw new InvalidOperationException("Connection string 'DineFlow' is missing.");

        // Split queries keep menu loads (items → modifier groups → options) from multiplying rows.
        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString,
            sql => sql.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery)));
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());

        services.AddOptions<JwtOptions>()
            .Bind(configuration.GetSection(JwtOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => KeyBytes.IsStrong(o.SigningKey), "Jwt:SigningKey must be base64 of at least 32 bytes.")
            .ValidateOnStart();

        services.AddOptions<QrOptions>()
            .Bind(configuration.GetSection(QrOptions.Section))
            .ValidateDataAnnotations()
            .Validate(o => KeyBytes.IsStrong(o.TokenKey), "Qr:TokenKey must be base64 of at least 32 bytes.")
            .ValidateOnStart();

        services.AddOptions<GuestOptions>()
            .Bind(configuration.GetSection(GuestOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddOptions<StorageOptions>().Bind(configuration.GetSection(StorageOptions.Section));
        services.AddSingleton(sp => new LocalFileStorage(sp.GetRequiredService<IOptions<StorageOptions>>(), contentRootPath));
        services.AddSingleton<IFileStorage>(sp => sp.GetRequiredService<LocalFileStorage>());

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IQrTokenService, QrTokenService>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IPasswordService, PasswordService>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        return services;
    }
}
