using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Infrastructure.Persistence;
using DineFlow.Infrastructure.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DineFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("DineFlow")
            ?? throw new InvalidOperationException("Connection string 'DineFlow' is missing.");

        services.AddDbContext<AppDbContext>(options => options.UseSqlServer(connectionString));
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

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IQrTokenService, QrTokenService>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IPasswordService, PasswordService>();

        services.AddHealthChecks().AddDbContextCheck<AppDbContext>("database");

        return services;
    }
}
