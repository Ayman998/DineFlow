using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Infrastructure.Email;
using DineFlow.Infrastructure.Messaging;
using DineFlow.Infrastructure.Payments;
using DineFlow.Infrastructure.Persistence;
using DineFlow.Infrastructure.Pos;
using DineFlow.Infrastructure.Security;
using DineFlow.Infrastructure.Storage;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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

        services.AddOptions<PaymentOptions>()
            .Bind(configuration.GetSection(PaymentOptions.Section))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        var provider = configuration[$"{PaymentOptions.Section}:Provider"] ?? SandboxPaymentProvider.ProviderName;
        if (provider != SandboxPaymentProvider.ProviderName)
            throw new InvalidOperationException($"Unknown payment provider '{provider}'. Only '{SandboxPaymentProvider.ProviderName}' is built in so far.");
        services.AddOptions<SandboxOptions>()
            .Bind(configuration.GetSection(SandboxOptions.Section))
            .Validate(o => KeyBytes.IsStrong(o.SecretKey), "Payments:Sandbox:SecretKey must be base64 of at least 32 bytes.")
            .ValidateOnStart();
        services.AddSingleton<SandboxWebhookOutbox>();
        services.AddHostedService<SandboxWebhookDispatcher>();
        services.AddSingleton<SandboxPaymentProvider>();
        services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<SandboxPaymentProvider>());

        services.AddOptions<EmailOptions>().Bind(configuration.GetSection(EmailOptions.Section));
        services.AddSingleton<IEmailSender>(sp => new FileEmailSender(
            sp.GetRequiredService<IOptions<EmailOptions>>(), contentRootPath, sp.GetRequiredService<ILogger<FileEmailSender>>()));

        services.AddOptions<FeatureOptions>().Bind(configuration.GetSection(FeatureOptions.Section));

        // WhatsApp payment alerts: a local outbox until the WhatsApp Cloud API is configured.
        services.AddOptions<WhatsAppOptions>()
            .Bind(configuration.GetSection(WhatsAppOptions.Section))
            .Validate(o => o.Provider is WhatsAppOptions.Outbox or WhatsAppOptions.Meta, "WhatsApp:Provider must be 'outbox' or 'meta'.")
            .Validate(o => o.Provider != WhatsAppOptions.Meta || (o.MetaApi.PhoneNumberId.Length > 0 && o.MetaApi.AccessToken.Length > 0),
                "WhatsApp:MetaApi:PhoneNumberId and AccessToken are required when WhatsApp:Provider is 'meta'.")
            .ValidateOnStart();
        if (configuration[$"{WhatsAppOptions.Section}:Provider"] == WhatsAppOptions.Meta)
        {
            services.AddHttpClient<MetaWhatsAppSender>(c => c.Timeout = TimeSpan.FromSeconds(15));
            services.AddScoped<IWhatsAppSender>(sp => sp.GetRequiredService<MetaWhatsAppSender>());
        }
        else
        {
            services.AddSingleton<IWhatsAppSender>(sp => new OutboxWhatsAppSender(
                sp.GetRequiredService<IOptions<WhatsAppOptions>>(), contentRootPath, sp.GetRequiredService<ILogger<OutboxWhatsAppSender>>()));
        }
        services.AddSingleton<NotificationWorkerStatus>();
        services.AddHostedService<NotificationWorker>();

        // POS connectors (blueprint section 6). Access tokens are encrypted with Data Protection; the keys
        // live in App_Data/keys (git-ignored) here, and should be in shared, protected storage in production.
        services.AddDataProtection()
            .SetApplicationName("DineFlow")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(Path.Combine(contentRootPath, "App_Data/keys"))));
        services.AddSingleton<ISecretProtector, DataProtectionSecretProtector>();
        services.AddOptions<FoodicsOptions>().Bind(configuration.GetSection(FoodicsOptions.Section));
        services.AddHttpClient<FoodicsConnector>(c => c.Timeout = Timeout.InfiniteTimeSpan); // the connector applies its own timeout
        services.AddSingleton<SimulatedPosConnector>();
        services.AddScoped<IPosConnector>(sp => sp.GetRequiredService<SimulatedPosConnector>());
        services.AddScoped<IPosConnector>(sp => sp.GetRequiredService<FoodicsConnector>());
        services.AddScoped<IPosConnectorRegistry, PosConnectorRegistry>();
        services.AddHostedService<PosWorker>();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IQrTokenService, QrTokenService>();
        services.AddSingleton<ITokenIssuer, JwtTokenIssuer>();
        services.AddSingleton<IPasswordService, PasswordService>();

        services.AddHealthChecks()
            .AddDbContextCheck<AppDbContext>("database")
            .AddCheck<NotificationHealthCheck>("notifications");

        return services;
    }
}
