using System.Security.Cryptography;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Audit;
using DineFlow.Application.Auth;
using DineFlow.Application.Bills;
using DineFlow.Application.Dashboard;
using DineFlow.Application.Common;
using DineFlow.Application.Guests;
using DineFlow.Application.Menus;
using DineFlow.Application.Notifications;
using DineFlow.Application.Orders;
using DineFlow.Application.PaymentLinks;
using DineFlow.Application.Payments;
using DineFlow.Application.Promotions;
using DineFlow.Application.Reports;
using DineFlow.Application.Reviews;
using DineFlow.Application.Tables;
using DineFlow.Application.Transactions;
using DineFlow.Application.Venues;
using DineFlow.Domain.Tenants;
using DineFlow.Infrastructure.Payments;
using DineFlow.Infrastructure.Persistence;
using DineFlow.Infrastructure.Security;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
        Db = new SqliteAppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).AddInterceptors(SaveHook).Options, _tenant);
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
        CartQuotes = new CartQuoteService(Db, Clock);
        GuestOrders = new GuestOrderService(Db, CartQuotes, Notifier, Clock);
        var paymentOptions = Options.Create(new PaymentOptions());
        Sandbox = new SandboxPaymentProvider(Options.Create(new SandboxOptions { SecretKey = RandomKey() }), guestOptions, Clock);
        Bills = new BillService(Db, Sandbox, PaymentNotifier, paymentOptions, Clock);
        MerchantOrders = new MerchantOrderService(Db, Notifier, Bills, Clock);
        Promotions = new PromotionService(Db, _tenant, Bills, Clock);
        var features = Options.Create(Features);
        Alerts = new PaymentAlerts(Db, features, Clock);
        Payments = new PaymentService(Db, Bills, Promotions, Sandbox, PaymentNotifier, Emails, Alerts, paymentOptions, guestOptions, Clock);
        GuestReviews = new GuestReviewService(Db, ReviewNotifier, Clock);
        MerchantReviews = new MerchantReviewService(Db, _tenant, Clock);
        Transactions = new TransactionService(Db, _tenant, Sandbox, Clock);
        TableStatus = new TableStatusService(Db, _tenant, Clock);
        Overview = new OverviewService(Db, _tenant, TableStatus, Clock);
        PaymentLinks = new PaymentLinkService(Db, _tenant, QrTokens, Emails, guestOptions, Clock);
        LinkCheckout = new PaymentLinkCheckoutService(Db, QrTokens, Sandbox, Emails, paymentOptions, guestOptions, Clock);
        AlertSettings = new AlertService(Db, _tenant, WhatsApp, Clock);
        Dispatcher = new NotificationDispatcher(Db, WhatsApp, Emails, Clock);
        Reports = new ReportService(Db, _tenant, new ReportBuilder(Db), features, Clock);
        AuditLog = new AuditService(Db, _tenant);
    }

    /// <summary>Runs something just before the main context's next save, e.g. another guest's request.</summary>
    public SaveInterceptor SaveHook { get; } = new();

    /// <summary>Controllable "now" for every service; starts at the real current time.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UtcNow);
    public InMemoryFileStorage Files { get; } = new();
    public RecordingNotifier Notifier { get; } = new();
    public RecordingPaymentNotifier PaymentNotifier { get; } = new();
    public RecordingEmailSender Emails { get; } = new();
    public RecordingReviewNotifier ReviewNotifier { get; } = new();

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
    public CartQuoteService CartQuotes { get; }
    public GuestOrderService GuestOrders { get; }
    public MerchantOrderService MerchantOrders { get; }
    public SandboxPaymentProvider Sandbox { get; }
    public BillService Bills { get; }
    public PromotionService Promotions { get; }
    public PaymentService Payments { get; }
    public GuestReviewService GuestReviews { get; }
    public MerchantReviewService MerchantReviews { get; }
    public TransactionService Transactions { get; }
    public TableStatusService TableStatus { get; }
    public OverviewService Overview { get; }
    public PaymentLinkService PaymentLinks { get; }
    public PaymentLinkCheckoutService LinkCheckout { get; }
    /// <summary>Feature switches; change them before the service under test runs.</summary>
    public FeatureOptions Features { get; } = new();
    public RecordingWhatsAppSender WhatsApp { get; } = new();
    public PaymentAlerts Alerts { get; }
    public AlertService AlertSettings { get; }
    public NotificationDispatcher Dispatcher { get; }
    public ReportService Reports { get; }
    public AuditService AuditLog { get; }

    /// <summary>
    /// Another request running at the same time as the main one: its own DbContext (and so its own
    /// view of the data) on the same database, with the bill and payment services on top.
    /// </summary>
    public Scope NewScope()
    {
        var db = new SqliteAppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options, _tenant);
        var paymentOptions = Options.Create(new PaymentOptions());
        var bills = new BillService(db, Sandbox, PaymentNotifier, paymentOptions, Clock);
        var promotions = new PromotionService(db, _tenant, bills, Clock);
        var payments = new PaymentService(db, bills, promotions, Sandbox, PaymentNotifier, Emails, new PaymentAlerts(db, Options.Create(Features), Clock), paymentOptions,
            Options.Create(new GuestOptions { AppBaseUrl = "https://guest.test" }), Clock);
        return new Scope(db, bills, payments, new TransactionService(db, _tenant, Sandbox, Clock),
            new PaymentLinkCheckoutService(db, QrTokens, Sandbox, Emails, paymentOptions, Options.Create(new GuestOptions { AppBaseUrl = "https://guest.test" }), Clock),
            new ReportService(db, _tenant, new ReportBuilder(db), Options.Create(Features), Clock));
    }

    public sealed record Scope(AppDbContext Db, BillService Bills, PaymentService Payments, TransactionService Transactions,
        PaymentLinkCheckoutService LinkCheckout, ReportService Reports) : IDisposable
    {
        public void Dispose() => Db.Dispose();
    }

    public sealed class SaveInterceptor : SaveChangesInterceptor
    {
        public Func<Task>? BeforeNextSave { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (BeforeNextSave is { } run)
            {
                BeforeNextSave = null;
                await run();
            }
            return result;
        }
    }

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

    /// <summary>Remembers the order events that would have been pushed to screens.</summary>
    public sealed class RecordingNotifier : IOrderNotifier
    {
        public List<(string Event, OrderDto Order)> Events { get; } = [];

        public Task OrderPlacedAsync(OrderDto order, CancellationToken ct)
        {
            Events.Add(("placed", order));
            return Task.CompletedTask;
        }

        public Task OrderChangedAsync(OrderDto order, CancellationToken ct)
        {
            Events.Add(("changed", order));
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingPaymentNotifier : IPaymentNotifier
    {
        public List<PaymentNotice> Notices { get; } = [];
        public List<(Guid LocationId, string Reason)> TableChanges { get; } = [];

        public Task PaymentSucceededAsync(PaymentNotice notice, CancellationToken ct)
        {
            Notices.Add(notice);
            return Task.CompletedTask;
        }

        public Task TableBillChangedAsync(Guid tenantId, Guid serviceLocationId, string reason, CancellationToken ct)
        {
            TableChanges.Add((serviceLocationId, reason));
            return Task.CompletedTask;
        }

        public List<RefundNotice> Refunds { get; } = [];

        public Task RefundUpdatedAsync(RefundNotice notice, CancellationToken ct)
        {
            Refunds.Add(notice);
            return Task.CompletedTask;
        }

        public List<PaymentLinkNotice> LinkPayments { get; } = [];

        public Task PaymentLinkPaidAsync(PaymentLinkNotice notice, CancellationToken ct)
        {
            LinkPayments.Add(notice);
            return Task.CompletedTask;
        }
    }

    public sealed class RecordingReviewNotifier : IReviewNotifier
    {
        public List<ReviewNotice> Notices { get; } = [];

        public Task ReviewSubmittedAsync(ReviewNotice notice, CancellationToken ct)
        {
            Notices.Add(notice);
            return Task.CompletedTask;
        }
    }

    /// <summary>Remembers WhatsApp messages; set <see cref="FailWith"/> to make the provider refuse them.</summary>
    public sealed class RecordingWhatsAppSender : IWhatsAppSender
    {
        public string Name => "test";
        public List<WhatsAppMessage> Sent { get; } = [];
        public string? FailWith { get; set; }

        public Task<string> SendAsync(WhatsAppMessage message, CancellationToken ct)
        {
            if (FailWith is { } error) throw new HttpRequestException(error);
            Sent.Add(message);
            return Task.FromResult($"wamid.{Sent.Count}");
        }
    }

    public sealed class RecordingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// SQLite can't compare or sort DateTimeOffset values, which SQL Server can. Storing them as
    /// numbers here lets the same queries run. Every time DineFlow stores is UTC, so order is kept.
    /// </summary>
    private sealed class SqliteAppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenant) : AppDbContext(options, tenant)
    {
        protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
        {
            base.ConfigureConventions(configurationBuilder);
            configurationBuilder.Properties<DateTimeOffset>().HaveConversion<DateTimeOffsetToBinaryConverter>();
        }
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid? TenantId { get; set; }
    }
}
