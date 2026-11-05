using HelloZen.Api.Common;
using HelloZen.Application.Abstractions;
using HelloZen.Application.Auth;
using HelloZen.Application.Common;
using HelloZen.Application.Menus;
using HelloZen.Application.Onboarding;
using HelloZen.Application.Orders;
using HelloZen.Application.Payments;
using HelloZen.Application.Staff;
using HelloZen.Domain.Payments;
using HelloZen.Domain.Staff;
using HelloZen.Infrastructure.Email;
using HelloZen.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace HelloZen.Test.Application;

/// <summary>Sprint 12: provisioning a venue, the team, passwords, the go-live checklist and production settings.</summary>
public class PilotReadinessTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static ProvisionRequest Hashem(string email = "owner@hashem.test", int tables = 6) =>
        new("Hashem Kitchen", "Abdoun, Amman", email, "Lina Haddad", VenueNameAr: "عبدون، عمّان", Tables: tables);

    /// <summary>Provisions a venue and signs in as its owner, the way the operator and then the owner would.</summary>
    private async Task<(ProvisionResult Result, StaffUser Owner)> ProvisionAsync(int tables = 6)
    {
        var result = await _host.Provisioning.ProvisionAsync(Hashem(tables: tables), default);
        _host.CurrentTenant = result.TenantId;
        return (result, await _host.Db.StaffUsers.SingleAsync(u => u.Email == result.OwnerEmail));
    }

    // ── Provisioning ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Provision_creates_a_Jordanian_venue_with_tables_and_an_owner_who_can_sign_in()
    {
        var result = await _host.Provisioning.ProvisionAsync(Hashem(), default);

        var venue = await _host.Db.Venues.IgnoreQueryFilters().SingleAsync(v => v.Id == result.VenueId);
        Assert.Equal(("JOD", "Asia/Amman", "ar", 1600, 1000), (venue.Currency, venue.TimeZone, venue.DefaultLocale, venue.TaxBasisPoints, venue.ServiceChargeBasisPoints));
        Assert.Equal("عبدون، عمّان", venue.NameAr);
        Assert.Equal(6, await _host.Db.QrTokens.IgnoreQueryFilters().CountAsync(q => q.TenantId == result.TenantId));
        Assert.Equal(14, result.TemporaryPassword.Length);

        var login = await _host.Auth.LoginAsync(new LoginRequest("OWNER@hashem.test", result.TemporaryPassword), default);
        Assert.Equal(("Owner", "Hashem Kitchen"), (login.Profile.Role, login.Profile.TenantName));
    }

    [Fact]
    public async Task Provision_refuses_an_email_that_already_signs_in()
    {
        await _host.Provisioning.ProvisionAsync(Hashem(), default);

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Provisioning.ProvisionAsync(Hashem(" Owner@Hashem.test "), default));

        Assert.Equal(("email_taken", 409), (ex.Code, ex.StatusCode));
        Assert.Equal(1, await _host.Db.Tenants.CountAsync());
    }

    [Fact]
    public void Provision_command_line_is_parsed_with_Amman_defaults()
    {
        var request = AdminCommands.ParseProvision(["--business", "Hashem Kitchen", "--venue", "Abdoun", "--owner-email", "o@h.test",
            "--owner-name", "Lina", "--tables", "12"]);
        var included = AdminCommands.ParseProvision(["--business", "X", "--venue", "Y", "--owner-email", "o@h.test", "--owner-name", "Z", "--prices-include-tax"]);

        Assert.Equal(("Hashem Kitchen", 12, "JOD", 1600, false), (request.BusinessName, request.Tables, request.Currency, request.TaxBasisPoints, request.PricesIncludeTax));
        Assert.True(included.PricesIncludeTax);
        Assert.Contains("--owner-name", Assert.Throws<ArgumentException>(() => AdminCommands.ParseProvision(["--business", "X", "--venue", "Y", "--owner-email", "o@h.test"])).Message);
        Assert.Contains("--colour", Assert.Throws<ArgumentException>(() => AdminCommands.ParseProvision(["--colour", "red"])).Message);
    }

    // ── Passwords ────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_replaces_the_temporary_password()
    {
        var (result, owner) = await ProvisionAsync();

        var wrong = await Assert.ThrowsAsync<AppException>(() =>
            _host.Auth.ChangePasswordAsync(owner.Id, new ChangePasswordRequest("not it", "a much better password"), default));
        Assert.Equal("wrong_password", wrong.Code);

        await _host.Auth.ChangePasswordAsync(owner.Id, new ChangePasswordRequest(result.TemporaryPassword, "a much better password"), default);

        _host.CurrentTenant = null;
        await Assert.ThrowsAsync<AppException>(() => _host.Auth.LoginAsync(new LoginRequest(result.OwnerEmail, result.TemporaryPassword), default));
        Assert.NotNull(await _host.Auth.LoginAsync(new LoginRequest(result.OwnerEmail, "a much better password"), default));
        Assert.Equal("account.password_changed", (await _host.Db.AuditEntries.IgnoreQueryFilters().SingleAsync()).Action);
    }

    [Fact]
    public async Task Operator_lets_an_owner_who_lost_the_temporary_password_back_in()
    {
        var (result, _) = await ProvisionAsync();
        _host.CurrentTenant = null;

        var temporary = await _host.Provisioning.ResetPasswordAsync(" OWNER@hashem.test ", "a password I chose", default);

        Assert.Null(temporary);
        await Assert.ThrowsAsync<AppException>(() => _host.Auth.LoginAsync(new LoginRequest(result.OwnerEmail, result.TemporaryPassword), default));
        Assert.NotNull(await _host.Auth.LoginAsync(new LoginRequest(result.OwnerEmail, "a password I chose"), default));
        Assert.Equal("account.password_reset_by_operator", (await _host.Db.AuditEntries.IgnoreQueryFilters().SingleAsync()).Action);

        // Without a chosen password the operator gets a temporary one to hand over.
        var another = await _host.Provisioning.ResetPasswordAsync(result.OwnerEmail, null, default);
        Assert.Equal(14, another!.Length);
        Assert.NotNull(await _host.Auth.LoginAsync(new LoginRequest(result.OwnerEmail, another), default));
    }

    [Fact]
    public async Task Provision_can_take_the_owners_password_instead_of_a_temporary_one()
    {
        var result = await _host.Provisioning.ProvisionAsync(Hashem() with { OwnerPassword = "the owner's own password" }, default);

        Assert.NotNull(await _host.Auth.LoginAsync(new LoginRequest(result.OwnerEmail, "the owner's own password"), default));
        var weak = await Assert.ThrowsAsync<AppException>(() =>
            _host.Provisioning.ProvisionAsync(Hashem("other@hashem.test") with { OwnerPassword = "short" }, default));
        Assert.Equal("weak_password", weak.Code);
        Assert.Equal(1, await _host.Db.Tenants.CountAsync());
    }

    [Fact]
    public async Task Operator_reset_refuses_unknown_accounts_and_short_passwords()
    {
        var (result, _) = await ProvisionAsync();

        var unknown = await Assert.ThrowsAsync<AppException>(() => _host.Provisioning.ResetPasswordAsync("nobody@hashem.test", "long enough password", default));
        var weak = await Assert.ThrowsAsync<AppException>(() => _host.Provisioning.ResetPasswordAsync(result.OwnerEmail, "short", default));

        Assert.Equal(("staff_not_found", "weak_password"), (unknown.Code, weak.Code));
        Assert.Equal("owner@hashem.test", AdminCommands.ParseResetPassword(["--email", "owner@hashem.test"]));
        Assert.Throws<ArgumentException>(() => AdminCommands.ParseResetPassword(["--mail", "owner@hashem.test"]));
    }

    // ── Team ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Owner_adds_a_waiter_who_signs_in_with_the_temporary_password()
    {
        var (_, owner) = await ProvisionAsync();

        var added = await _host.Team.AddAsync(new AddStaffRequest(" Sami@Hashem.test ", "Sami", StaffRole.Staff), owner.Id, default);

        Assert.Equal(("sami@hashem.test", StaffRole.Staff, true, false), (added.Member.Email, added.Member.Role, added.Member.IsActive, added.Member.IsYou));
        var team = await _host.Team.ListAsync(owner.Id, default);
        Assert.Equal(["Lina Haddad", "Sami"], team.Select(m => m.DisplayName));
        Assert.True(team[0].IsYou);

        _host.CurrentTenant = null;
        var login = await _host.Auth.LoginAsync(new LoginRequest("sami@hashem.test", added.TemporaryPassword), default);
        Assert.Equal("Staff", login.Profile.Role);
        Assert.Equal("staff.added", (await _host.Db.AuditEntries.IgnoreQueryFilters().SingleAsync()).Action);
    }

    [Fact]
    public async Task Managers_manage_staff_but_not_managers_or_owners()
    {
        var (_, owner) = await ProvisionAsync();
        var manager = (await _host.Team.AddAsync(new AddStaffRequest("rami@hashem.test", "Rami", StaffRole.Manager), owner.Id, default)).Member;

        var waiter = await _host.Team.AddAsync(new AddStaffRequest("sami@hashem.test", "Sami", StaffRole.Staff), manager.Id, default);
        Assert.Equal(StaffRole.Staff, waiter.Member.Role);

        var promote = await Assert.ThrowsAsync<AppException>(() =>
            _host.Team.UpdateAsync(waiter.Member.Id, new UpdateStaffRequest("Sami", StaffRole.Manager, true), manager.Id, default));
        Assert.Equal(("role_not_allowed", 403), (promote.Code, promote.StatusCode));
        var addManager = await Assert.ThrowsAsync<AppException>(() =>
            _host.Team.AddAsync(new AddStaffRequest("noor@hashem.test", "Noor", StaffRole.Manager), manager.Id, default));
        Assert.Equal("role_not_allowed", addManager.Code);
        await Assert.ThrowsAsync<AppException>(() => _host.Team.ResetPasswordAsync(owner.Id, manager.Id, default));
    }

    [Fact]
    public async Task Nobody_changes_their_own_role_or_switches_themselves_off()
    {
        var (_, owner) = await ProvisionAsync();
        var self = await Assert.ThrowsAsync<AppException>(() =>
            _host.Team.UpdateAsync(owner.Id, new UpdateStaffRequest("Lina", StaffRole.Owner, false), owner.Id, default));
        Assert.Equal("cannot_change_self", self.Code);

        // A second owner may step the first down; the remaining owner then can't step themselves down too.
        var second = (await _host.Team.AddAsync(new AddStaffRequest("omar@hashem.test", "Omar", StaffRole.Owner), owner.Id, default)).Member;
        await _host.Team.UpdateAsync(owner.Id, new UpdateStaffRequest("Lina", StaffRole.Manager, true), second.Id, default);
        var last = await Assert.ThrowsAsync<AppException>(() =>
            _host.Team.UpdateAsync(second.Id, new UpdateStaffRequest("Omar", StaffRole.Manager, true), second.Id, default));
        Assert.Equal("cannot_change_self", last.Code);
    }

    [Fact]
    public async Task Reset_password_issues_a_new_temporary_password()
    {
        var (_, owner) = await ProvisionAsync();
        var waiter = await _host.Team.AddAsync(new AddStaffRequest("sami@hashem.test", "Sami", StaffRole.Staff), owner.Id, default);

        var reset = await _host.Team.ResetPasswordAsync(waiter.Member.Id, owner.Id, default);

        Assert.NotEqual(waiter.TemporaryPassword, reset.TemporaryPassword);
        _host.CurrentTenant = null;
        await Assert.ThrowsAsync<AppException>(() => _host.Auth.LoginAsync(new LoginRequest("sami@hashem.test", waiter.TemporaryPassword), default));
        Assert.NotNull(await _host.Auth.LoginAsync(new LoginRequest("sami@hashem.test", reset.TemporaryPassword), default));
    }

    // ── Go-live checklist ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Go_live_checklist_follows_what_actually_happened()
    {
        var (result, owner) = await ProvisionAsync(tables: 4);

        var fresh = await _host.GoLive.ChecklistAsync(result.VenueId, default);
        Assert.False(fresh.Ready);
        Assert.True(fresh.TestMode);
        Assert.Equal(4, Check(fresh, "tables.qr").Count);
        Assert.True(Check(fresh, "venue.tax").Done);
        Assert.All(["tables.scanned", "menu.published", "team.staff", "payments.provider", "payments.test_payment", "payments.webhook", "payments.live"],
            key => Assert.False(Check(fresh, key).Done));

        // A menu, a waiter, and a guest who scans table 1 and pays through the sandbox.
        var mansaf = await MenuAsync(result);
        await _host.Team.AddAsync(new AddStaffRequest("sami@hashem.test", "Sami", StaffRole.Staff), owner.Id, default);
        await PayAsync(result, mansaf);

        var after = await _host.GoLive.ChecklistAsync(result.VenueId, default);
        Assert.All(["tables.scanned", "menu.published", "team.staff", "payments.test_payment", "payments.webhook"], key => Assert.True(Check(after, key).Done, key));
        Assert.False(Check(after, "payments.provider").Done); // still the sandbox
        Assert.False(after.Ready);

        // On Mastercard's live gateway the sandbox payment no longer counts as a test of it.
        _host.PaymentEnvironment = new PaymentEnvironment("mastercard", IsTest: false);
        var live = await _host.GoLive.ChecklistAsync(result.VenueId, default);
        Assert.True(Check(live, "payments.provider").Done);
        Assert.True(Check(live, "payments.live").Done);
        Assert.False(Check(live, "payments.test_payment").Done);
    }

    [Fact]
    public async Task Webhook_check_needs_the_gateways_own_notification_not_a_status_check()
    {
        var (result, _) = await ProvisionAsync();
        var mansaf = await MenuAsync(result);

        // HelloZen asked the gateway before its webhook arrived: the payment is paid, but that
        // doesn't show the notification URL and secret work.
        var outcome = await PayAsync(result, mansaf, viaWebhook: false);
        var before = await _host.GoLive.ChecklistAsync(result.VenueId, default);
        Assert.True(Check(before, "payments.test_payment").Done);
        Assert.False(Check(before, "payments.webhook").Done);

        // The same event then arrives by webhook: nothing is applied twice, and the check turns green.
        await WebhookAsync(outcome);
        var after = await _host.GoLive.ChecklistAsync(result.VenueId, default);
        Assert.True(Check(after, "payments.webhook").Done);
        Assert.Equal(1, await _host.Db.LedgerEntries.CountAsync()); // one sale, no tip, applied once
    }

    private static GoLiveCheckDto Check(GoLiveDto list, string key) => list.Checks.Single(c => c.Key == key);

    private async Task<Guid> MenuAsync(ProvisionResult venue)
    {
        var menu = await _host.Menus.CreateMenuAsync(venue.VenueId, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(venue.VenueId, null, "Mains", null), default);
        var mansaf = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Mansaf", null, null, null, 12_500, null, true, [], [], []), default);
        await _host.Menus.SetMenuItemsAsync(menu.Id, new MenuItemsRequest([mansaf.Id]), default);
        return mansaf.Id;
    }

    private async Task WebhookAsync(ProviderWebhookEvent outcome)
    {
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null; // webhooks arrive without a tenant
        var (body, signature) = _host.Sandbox.CreateWebhook(outcome);
        await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default);
        _host.CurrentTenant = tenant;
    }

    /// <summary>A guest scans table 1, orders and pays in full; the sandbox's verdict arrives by webhook or a status check.</summary>
    private async Task<ProviderWebhookEvent> PayAsync(ProvisionResult venue, Guid itemId, bool viaWebhook = true)
    {
        var plan = await _host.Tables.GetFloorPlanAsync(venue.VenueId, default);
        var table = plan.Areas.SelectMany(a => a.Locations).First(l => l.Label == "1");
        _host.CurrentTenant = null;
        var session = (await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(table.Qr!.Url), null, default)).Context.SessionId;
        _host.CurrentTenant = venue.TenantId;
        var lines = new List<CartLineRequest> { new(itemId, 1, [], null) };
        var quote = await _host.CartQuotes.QuoteAsync(session, new CartRequest(lines), default);
        await _host.GuestOrders.PlaceAsync(session, new PlaceOrderRequest(Guid.NewGuid(), lines, null, quote.TotalMinor), default);
        var bill = await _host.Bills.GetForSessionAsync(session, default);
        var payment = await _host.Payments.CreateAsync(new CreatePaymentRequest(session, Guid.NewGuid(), PaymentMethod.Card, PaymentMode.Full,
            null, null, 0, null, bill.AvailableMinor, true), default);
        var outcome = new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", PaymentStatus.Succeeded, "Visa", "4242", null);
        _host.CurrentTenant = venue.TenantId;
        if (viaWebhook) await WebhookAsync(outcome);
        else await _host.Payments.ApplyEventAsync(outcome, default);
        return outcome;
    }

    // ── Production settings ──────────────────────────────────────────────────────────────

    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value)).Build();

    [Fact]
    public void Production_refuses_the_development_defaults()
    {
        var checks = ProductionChecks.Run(Config(("Guest:AppBaseUrl", "http://localhost:4200"), ("Seed:Enabled", "true")), isDevelopment: false);

        Assert.Equal(4, checks.Errors.Count);
        Assert.Contains(checks.Errors, e => e.Contains("sandbox"));
        Assert.Contains(checks.Errors, e => e.Contains("Seed:Enabled"));
        Assert.Contains(checks.Errors, e => e.Contains("Guest:AppBaseUrl"));
        Assert.Contains(checks.Errors, e => e.Contains("Email:Provider"));
        Assert.Empty(ProductionChecks.Run(Config(), isDevelopment: true).Errors);
    }

    [Fact]
    public void Production_with_Mastercard_and_smtp_starts_and_warns_about_the_test_gateway()
    {
        var checks = ProductionChecks.Run(Config(
            ("Payments:Provider", "mastercard"),
            ("Payments:Mastercard:GatewayUrl", "https://test-gateway.mastercard.com"),
            ("Payments:TermsUrl", "https://hashem.jo/terms"),
            ("Guest:AppBaseUrl", "https://dine.hashem.jo"),
            ("Email:Provider", "smtp"),
            ("WhatsApp:Provider", "meta"),
            ("Legal:LegalName", "Hashem Kitchen LLC"),
            ("Legal:Address", "Abdoun, Amman, Jordan"),
            ("Legal:Email", "support@hashem.jo"),
            ("Legal:Phone", "+962 6 555 0100")), isDevelopment: false);

        Assert.Empty(checks.Errors);
        Assert.Contains("test gateway", Assert.Single(checks.Warnings));
    }

    [Fact]
    public void Terms_on_the_guest_apps_own_page_need_no_warning_but_missing_company_details_do()
    {
        var checks = ProductionChecks.Run(Config(
            ("Payments:Provider", "mastercard"),
            ("Payments:Mastercard:GatewayUrl", "https://na.gateway.mastercard.com"),
            ("Payments:TermsUrl", "/terms"),
            ("Guest:AppBaseUrl", "https://app.hello-zen.com"),
            ("Email:Provider", "smtp"),
            ("WhatsApp:Provider", "meta"),
            ("Legal:LegalName", "helloZen LLC"),
            ("Legal:Email", "support@hello-zen.com")), isDevelopment: false);

        var warning = Assert.Single(checks.Warnings);
        Assert.Contains("Legal:Address, Legal:Phone", warning);
        Assert.DoesNotContain("TermsUrl", warning);

        var placeholder = ProductionChecks.Run(Config(("Payments:TermsUrl", "https://hellozen.example/terms")), isDevelopment: false);
        Assert.Contains(placeholder.Warnings, w => w.Contains("Payments:TermsUrl"));
    }

    [Fact]
    public void Legal_details_leave_out_what_isnt_set()
    {
        var info = new LegalOptions { LegalName = " helloZen LLC ", Email = "support@hello-zen.com", Phone = "  " }.ToDto();

        Assert.Equal(new LegalInfoDto("helloZen LLC", null, "support@hello-zen.com", null, null), info);
    }

    [Fact]
    public void Smtp_message_has_text_and_html_parts()
    {
        var mime = SmtpEmailSender.Build(new EmailOptions { From = "HelloZen <receipts@hashem.jo>" },
            new EmailMessage("guest@example.com", "إيصالك", "Thank you", "<p>Thank you</p>"));

        Assert.Equal("receipts@hashem.jo", mime.From.Mailboxes.Single().Address);
        Assert.Equal("إيصالك", mime.Subject);
        Assert.Equal("Thank you", mime.TextBody?.Trim());
        Assert.Equal("<p>Thank you</p>", mime.HtmlBody?.Trim());
    }
}
