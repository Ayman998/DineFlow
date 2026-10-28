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
        if (provider is not (SandboxPaymentProvider.ProviderName or MastercardGatewayProvider.ProviderName))
            throw new InvalidOperationException($"Unknown payment provider '{provider}'. Use '{SandboxPaymentProvider.ProviderName}' or '{MastercardGatewayProvider.ProviderName}'.");

        // The sandbox's page and dispatcher are always registered (they're inert unless it's the provider);
        // its key is only required when it is.
        var sandboxOptions = services.AddOptions<SandboxOptions>().Bind(configuration.GetSection(SandboxOptions.Section));
        services.AddSingleton<SandboxWebhookOutbox>();
        services.AddHostedService<SandboxWebhookDispatcher>();
        services.AddSingleton<SandboxPaymentProvider>();

        // Mastercard Gateway (MPGS) Hosted Checkout: what Jordanian acquirers (MEPS, Network International, banks) provide.
        var mastercardOptions = services.AddOptions<MastercardGatewayOptions>().Bind(configuration.GetSection(MastercardGatewayOptions.Section));
        services.AddHttpClient<MastercardGatewayProvider>(c => c.Timeout = Timeout.InfiniteTimeSpan); // the provider applies its own timeout

        if (provider == MastercardGatewayProvider.ProviderName)
        {
            mastercardOptions
                .Validate(o => o.MerchantId.Length > 0 && o.ApiPassword.Length > 0, "Payments:Mastercard:MerchantId and ApiPassword are required.")
                .Validate(o => o.WebhookSecret.Length >= 16, "Payments:Mastercard:WebhookSecret is required (from Merchant Administration → Webhook Notifications).")
                .Validate(o => Uri.TryCreate(o.GatewayUrl, UriKind.Absolute, out var u) && u.Scheme == Uri.UriSchemeHttps, "Payments:Mastercard:GatewayUrl must be an https URL.")
                .Validate(o => o.NotificationUrl.Length == 0 || o.NotificationUrl.StartsWith("https://"), "Payments:Mastercard:NotificationUrl must be https (the gateway won't call http).")
                .ValidateOnStart();
            services.AddScoped<IPaymentProvider>(sp => sp.GetRequiredService<MastercardGatewayProvider>());
            // MPGS test merchant profiles are named TEST…, and the shared test host never charges cards.
            services.AddSingleton(sp =>
            {
                var o = sp.GetRequiredService<IOptions<MastercardGatewayOptions>>().Value;
                return new PaymentEnvironment(provider,
                    o.GatewayUrl.Contains("test-gateway", StringComparison.OrdinalIgnoreCase) || o.MerchantId.StartsWith("TEST", StringComparison.OrdinalIgnoreCase));
            });
        }
        else
        {
            sandboxOptions
                .Validate(o => KeyBytes.IsStrong(o.SecretKey), "Payments:Sandbox:SecretKey must be base64 of at least 32 bytes.")
                .ValidateOnStart();
            services.AddSingleton<IPaymentProvider>(sp => sp.GetRequiredService<SandboxPaymentProvider>());
            services.AddSingleton(new PaymentEnvironment(provider, IsTest: true));
        }
        // Asks the provider about payments still waiting, for providers whose webhooks aren't guaranteed.
        services.AddHostedService<PaymentStatusWorker>();
        // Re-reconciles recent days every hour as settlements arrive and POS posts catch up.
        services.AddHostedService<ReconciliationWorker>();

        services.AddOptions<EmailOptions>()
            .Bind(configuration.GetSection(EmailOptions.Section))
            .Validate(o => o.Provider is EmailOptions.File or EmailOptions.Smtp, "Email:Provider must be 'file' or 'smtp'.")
            .Validate(o => MimeKit.MailboxAddress.TryParse(o.From, out _), "Email:From must be an e-mail address, e.g. DineFlow <receipts@example.com>.")
            .ValidateOnStart();
        if (configuration[$"{EmailOptions.Section}:Provider"] == EmailOptions.Smtp)
        {
            services.AddOptions<SmtpOptions>()
                .Bind(configuration.GetSection($"{EmailOptions.Section}:Smtp"))
                .Validate(o => o.Host.Length > 0, "Email:Smtp:Host is required when Email:Provider is 'smtp'.")
                .Validate(o => o.Security.ToLowerInvariant() is "starttls" or "sslonconnect" or "none", "Email:Smtp:Security must be StartTls, SslOnConnect or None.")
                .Validate(o => o.Username.Length == 0 || o.Password.Length > 0, "Email:Smtp:Password is required with a username.")
                .ValidateOnStart();
            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else
        {
            services.AddSingleton<IEmailSender>(sp => new FileEmailSender(
                sp.GetRequiredService<IOptions<EmailOptions>>(), contentRootPath, sp.GetRequiredService<ILogger<FileEmailSender>>()));
        }

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

        // POS connectors (blueprint section 6). Access tokens are encrypted with Data Protection. The keys
        // live in DataProtection:KeysPath (App_Data/keys, git-ignored, by default); in a container that
        // must be a persistent volume, or every restart loses them and stored POS tokens can't be read.
        var keysPath = configuration["DataProtection:KeysPath"] is { Length: > 0 } configured ? configured : "App_Data/keys";
        services.AddDataProtection()
            .SetApplicationName("DineFlow")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.GetFullPath(Path.Combine(contentRootPath, keysPath))));
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
