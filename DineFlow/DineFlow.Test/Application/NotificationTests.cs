using DineFlow.Application.Abstractions;
using DineFlow.Application.Audit;
using DineFlow.Application.Common;
using DineFlow.Application.Menus;
using DineFlow.Application.Notifications;
using DineFlow.Application.Orders;
using DineFlow.Application.PaymentLinks;
using DineFlow.Application.Payments;
using DineFlow.Application.Reports;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

/// <summary>WhatsApp payment alerts, the notification outbox with retries, scheduled e-mail reports and the audit log (E15, E28).</summary>
public class NotificationTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>Wednesday 10 March 2027, 12:00 in Dubai (UTC+4). The test clock can only move forward.</summary>
    private static readonly DateTimeOffset Noon = new(2027, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(Guid TenantId, VenueDto Venue, ItemDto Burger, LocationDto Table7, StaffUser Manager);

    private async Task<Fixture> CreateAsync()
    {
        _host.Clock.SetUtcNow(Noon);
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(venue.Id, null, "Mains", null), default);
        var burger = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Burger", null, null, null, 5000, null, true, [], [], []), default);
        await _host.Menus.SetMenuItemsAsync(menu.Id, new MenuItemsRequest([burger.Id]), default);
        var table7 = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7"), default);
        var manager = new StaffUser(tenant.Id, "lina@demo.test", "Lina", StaffRole.Manager);
        _host.Db.StaffUsers.Add(manager);
        await _host.Db.SaveChangesAsync();
        return new Fixture(tenant.Id, venue, burger, table7, manager);
    }

    private async Task AlertsOnAsync(Fixture f, params string[] numbers)
    {
        await _host.AlertSettings.SetEnabledAsync(f.Venue.Id, true, f.Manager.Id, default);
        for (var i = 0; i < numbers.Length; i++)
            await _host.AlertSettings.AddContactAsync(f.Venue.Id, new AlertContactRequest($"Contact {i + 1}", numbers[i]), f.Manager.Id, default);
    }

    /// <summary>A guest at table 7 orders burgers and pays (all, or an amount), confirmed by the provider.</summary>
    private async Task<PaymentDto> OrderAndPayAsync(Fixture f, int burgers = 1, long? amount = null, long tip = 0)
    {
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        var session = (await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(f.Table7.Qr!.Url), null, default)).Context.SessionId;
        _host.CurrentTenant = tenant;

        var lines = new List<CartLineRequest> { new(f.Burger.Id, burgers, [], null) };
        var quote = await _host.CartQuotes.QuoteAsync(session, new CartRequest(lines), default);
        await _host.GuestOrders.PlaceAsync(session, new PlaceOrderRequest(Guid.NewGuid(), lines, null, quote.TotalMinor), default);
        var bill = await _host.Bills.GetForSessionAsync(session, default);
        var payment = await _host.Payments.CreateAsync(new CreatePaymentRequest(session, Guid.NewGuid(), PaymentMethod.Card,
            amount is null ? PaymentMode.Full : PaymentMode.Custom, null, amount, tip, null, amount ?? bill.AvailableMinor, true), default);
        await WebhookAsync(payment.Id);
        return payment;
    }

    private async Task WebhookAsync(Guid paymentId)
    {
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{paymentId:N}",
            PaymentStatus.Succeeded, "Visa", "4242", null));
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        try { await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default); }
        finally { _host.CurrentTenant = tenant; }
    }

    /// <summary>The venue's default language becomes Arabic, which staff alerts and report e-mails follow.</summary>
    private Task ArabicAsync(Fixture f) =>
        _host.Venues.UpdateSettingsAsync(f.Venue.Id, new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "ar", null, "#0B3B38", true, true), default);

    private Task<DeliveryPageDto> DeliveriesAsync(Fixture f, NotificationChannel? channel = null) =>
        _host.AlertSettings.DeliveriesAsync(new DeliveryQuery(f.Venue.Id, channel), default);

    // ---- WhatsApp payment alerts ----

    [Fact]
    public async Task Every_alert_number_gets_a_short_whatsapp_message_for_a_payment()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971 50 123 4567", "00971551112222");

        var payment = await OrderAndPayAsync(f, tip: 500);
        var queued = await DeliveriesAsync(f);
        var sent = await _host.Dispatcher.DispatchDueAsync(default);
        var after = await DeliveriesAsync(f);

        Assert.Equal(2, queued.Items.Count(d => d.Status == DeliveryStatus.Pending && d.Kind == NotificationKind.PaymentAlert));
        Assert.Equal(2, sent);
        Assert.Equal(["+971501234567", "+971551112222"], _host.WhatsApp.Sent.Select(m => m.To).Order());
        var message = _host.WhatsApp.Sent[0];
        Assert.Equal(["Test Bistro · Table 7", "AED 55.00 (tip AED 5.00)", "Bill fully paid"], message.Parameters.Take(3));
        Assert.Equal($"Orders #1001 · {payment.Reference} · 12:00", message.Parameters[3]);
        Assert.Equal($"Test Bistro · Table 7\nAED 55.00 (tip AED 5.00) paid\nBill fully paid\nOrders #1001 · {payment.Reference} · 12:00", message.Text);
        Assert.All(after.Items, d => Assert.Equal((DeliveryStatus.Sent, payment.Id), (d.Status, d.SubjectId!.Value)));
    }

    [Fact]
    public async Task A_part_payment_alert_says_what_is_left_to_pay()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567");

        await OrderAndPayAsync(f, burgers: 2, amount: 4000);
        await _host.Dispatcher.DispatchDueAsync(default);

        var message = Assert.Single(_host.WhatsApp.Sent);
        Assert.Equal(["AED 40.00 · part payment", "AED 60.00 left to pay"], message.Parameters.Skip(1).Take(2));
    }

    [Fact]
    public async Task A_payment_link_payment_sends_an_alert_too()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567");
        var link = await _host.PaymentLinks.CreateAsync(new CreatePaymentLinkRequest(f.Venue.Id, Guid.NewGuid(), 15_000, "Booking 42", false, false,
            24, ValidityUnit.Hours, null), f.Manager.Id, default);
        _host.CurrentTenant = null;
        var payment = await _host.LinkCheckout.CreatePaymentAsync(TestHost.TokenFromUrl(link.Summary.Url),
            new CreateLinkPaymentRequest(Guid.NewGuid(), PaymentMethod.Card, PaymentMode.Full, null, null, null, 0, 15_000, true), default);
        _host.CurrentTenant = f.TenantId;
        await WebhookAsync(payment.Id);
        await _host.Dispatcher.DispatchDueAsync(default);

        var message = Assert.Single(_host.WhatsApp.Sent);
        Assert.Equal([$"Test Bistro · Payment link {link.Summary.Code} (Booking 42)", "AED 150.00", "Link fully paid"], message.Parameters.Take(3));
        Assert.Equal(NotificationKind.LinkPaymentAlert, Assert.Single((await DeliveriesAsync(f)).Items).Kind);
    }

    [Fact]
    public async Task Alerts_are_in_arabic_with_the_arabic_template_when_the_venue_reads_arabic()
    {
        var f = await CreateAsync();
        await ArabicAsync(f);
        await AlertsOnAsync(f, "+971501234567");

        var payment = await OrderAndPayAsync(f, tip: 500);
        await _host.AlertSettings.SendTestAsync(f.Venue.Id, default);
        await _host.Dispatcher.DispatchDueAsync(default);

        var alert = Assert.Single(_host.WhatsApp.Sent, m => m.Parameters[1].StartsWith("AED 55.00"));
        var test = Assert.Single(_host.WhatsApp.Sent, m => m != alert);
        Assert.Equal(["Test Bistro · طاولة 7", "AED 55.00 (إكرامية AED 5.00)", "تم دفع الفاتورة بالكامل"], alert.Parameters.Take(3));
        Assert.Equal($"الطلبات #1001 · {payment.Reference} · 12:00", alert.Parameters[3]);
        Assert.StartsWith("Test Bistro · طاولة 7\nتم دفع AED 55.00", alert.Text);
        Assert.Equal(("ar", "ar"), (alert.Language, test.Language));
        Assert.Equal("تنبيهات الدفع تعمل", test.Parameters[2]);
    }

    [Fact]
    public async Task Messages_queued_before_alerts_had_a_language_still_go_out_in_english()
    {
        var f = await CreateAsync();
        _host.Db.NotificationDeliveries.Add(new NotificationDelivery(f.TenantId, f.Venue.Id, NotificationChannel.WhatsApp, NotificationKind.Test,
            "+971501234567", "test:old", null, null, "Test Bistro · Marina\nAn old test",
            null, """["Test Bistro · Marina","Test","Payment alerts are working","No money moved"]""", Noon));
        await _host.Db.SaveChangesAsync();

        await _host.Dispatcher.DispatchDueAsync(default);

        var message = Assert.Single(_host.WhatsApp.Sent);
        Assert.Equal(("en", "Payment alerts are working"), (message.Language, message.Parameters[2]));
    }

    [Fact]
    public async Task No_alerts_go_out_when_they_are_off_or_the_feature_is_disabled()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567");
        await _host.AlertSettings.SetEnabledAsync(f.Venue.Id, false, f.Manager.Id, default);
        await OrderAndPayAsync(f);

        await _host.AlertSettings.SetEnabledAsync(f.Venue.Id, true, f.Manager.Id, default);
        _host.Features.WhatsAppAlerts = false;
        await OrderAndPayAsync(f);

        Assert.Empty((await DeliveriesAsync(f)).Items);
    }

    [Fact]
    public async Task The_same_payment_is_never_alerted_twice()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567");
        await OrderAndPayAsync(f);
        var payment = await _host.Db.Payments.SingleAsync();

        var table = await _host.Db.ServiceLocations.Include(l => l.DiningArea).SingleAsync(l => l.Label == "7");

        await _host.Alerts.QueueTableAsync(payment, table, 0, default); // e.g. the event handled again

        Assert.Single((await DeliveriesAsync(f)).Items);
    }

    [Fact]
    public async Task Alert_numbers_must_be_international_and_unique()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971 (50) 123-4567");

        var local = await Assert.ThrowsAsync<AppException>(() =>
            _host.AlertSettings.AddContactAsync(f.Venue.Id, new AlertContactRequest("Omar", "050 123 4567"), f.Manager.Id, default));
        var twice = await Assert.ThrowsAsync<AppException>(() =>
            _host.AlertSettings.AddContactAsync(f.Venue.Id, new AlertContactRequest("Omar", "+971501234567"), f.Manager.Id, default));
        var settings = await _host.AlertSettings.GetAsync(f.Venue.Id, default);

        Assert.Equal((400, "invalid_contact"), (local.StatusCode, local.Code));
        Assert.Equal((409, "contact_exists"), (twice.StatusCode, twice.Code));
        Assert.Equal(("+971501234567", true, "test"), (Assert.Single(settings.Contacts).PhoneNumber, settings.Enabled, settings.Provider));
    }

    [Fact]
    public async Task A_test_message_goes_to_every_number()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567", "+971551112222");

        await _host.AlertSettings.SendTestAsync(f.Venue.Id, default);
        await _host.Dispatcher.DispatchDueAsync(default);

        Assert.Equal(2, _host.WhatsApp.Sent.Count);
        Assert.All((await DeliveriesAsync(f)).Items, d => Assert.Equal((NotificationKind.Test, DeliveryStatus.Sent), (d.Kind, d.Status)));
    }

    // ---- Retries ----

    [Fact]
    public async Task A_refused_message_is_retried_with_backoff_then_kept_as_failed_until_resent()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567");
        await OrderAndPayAsync(f);
        _host.WhatsApp.FailWith = "(#131026) Message undeliverable";

        await _host.Dispatcher.DispatchDueAsync(default);
        var first = Assert.Single((await DeliveriesAsync(f)).Items);
        var tooSoon = await _host.Dispatcher.DispatchDueAsync(default); // not due yet: waits 30 seconds
        for (var attempt = 2; attempt <= NotificationDelivery.MaxAttempts; attempt++)
        {
            _host.Clock.Advance(NotificationDelivery.Backoff(attempt - 1));
            await _host.Dispatcher.DispatchDueAsync(default);
        }
        var failed = await DeliveriesAsync(f);
        _host.Clock.Advance(TimeSpan.FromHours(1));
        await _host.Dispatcher.DispatchDueAsync(default);

        Assert.Equal((DeliveryStatus.Pending, 1, Noon.AddSeconds(30), "(#131026) Message undeliverable"),
            (first.Status, first.Attempts, first.NextAttemptAt!.Value, first.LastError));
        Assert.Equal(0, tooSoon);
        Assert.Equal((DeliveryStatus.Failed, NotificationDelivery.MaxAttempts, 1),
            (failed.Items[0].Status, failed.Items[0].Attempts, failed.Failed));
        Assert.Equal(NotificationDelivery.MaxAttempts, (await DeliveriesAsync(f)).Items[0].Attempts); // gave up: no more attempts

        _host.WhatsApp.FailWith = null;
        var resent = await _host.AlertSettings.ResendAsync(failed.Items[0].Id, f.Manager.Id, default);
        await _host.Dispatcher.DispatchDueAsync(default);

        Assert.Equal((DeliveryStatus.Pending, 0), (resent.Status, resent.Attempts));
        Assert.Equal(DeliveryStatus.Sent, (await DeliveriesAsync(f)).Items[0].Status);
        Assert.Single(_host.WhatsApp.Sent);
    }

    [Fact]
    public async Task Only_a_failed_message_can_be_resent()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567");
        await OrderAndPayAsync(f);
        var pending = Assert.Single((await DeliveriesAsync(f)).Items);

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.AlertSettings.ResendAsync(pending.Id, f.Manager.Id, default));

        Assert.Equal("not_failed", ex.Code);
    }

    // ---- Scheduled e-mail reports ----

    [Theory]
    [InlineData(ReportFrequency.Daily, "2027-03-11T04:00:00Z", "2027-03-10", "2027-03-11")]
    [InlineData(ReportFrequency.Weekly, "2027-03-15T04:00:00Z", "2027-03-08", "2027-03-15")]
    [InlineData(ReportFrequency.Monthly, "2027-04-01T04:00:00Z", "2027-03-01", "2027-04-01")]
    public async Task Reports_go_out_at_the_local_hour_and_cover_the_last_complete_period(
        ReportFrequency frequency, string nextRun, string periodStart, string periodEnd)
    {
        var f = await CreateAsync();

        var schedule = await _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, frequency, 8, ["owner@demo.test"]), f.Manager.Id, default);
        var stored = await _host.Db.ReportSchedules.SingleAsync();
        var period = stored.PeriodBefore(schedule.NextRunAt, TimeZoneInfo.FindSystemTimeZoneById("Asia/Dubai"));

        Assert.Equal(DateTimeOffset.Parse(nextRun), schedule.NextRunAt);
        Assert.Equal((DateTime.Parse(periodStart), DateTime.Parse(periodEnd)), period);
    }

    [Fact]
    public async Task The_report_email_is_in_arabic_when_the_venue_reads_arabic()
    {
        var f = await CreateAsync();
        await ArabicAsync(f);
        await OrderAndPayAsync(f, burgers: 2, tip: 500);
        await _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, ReportFrequency.Daily, 8, ["owner@demo.test"]), f.Manager.Id, default);

        _host.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 11, 4, 0, 0, TimeSpan.Zero));
        await _host.Reports.RunDueAsync(default);
        await _host.Dispatcher.DispatchDueAsync(default);

        var email = Assert.Single(_host.Emails.Sent);
        Assert.Equal("Test Bistro · Marina: التقرير اليومي عن الأربعاء 10 آذار 2027", email.Subject);
        Assert.Contains("الإيرادات", email.TextBody);
        Assert.Contains("AED 100.00", email.TextBody);
        Assert.Contains("dir=\"rtl\"", email.HtmlBody);
    }

    [Fact]
    public async Task A_due_daily_report_is_emailed_to_every_recipient_once()
    {
        var f = await CreateAsync();
        await OrderAndPayAsync(f, burgers: 2, tip: 500);
        await _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, ReportFrequency.Daily, 8, ["owner@demo.test", "finance@demo.test"]),
            f.Manager.Id, default);

        var early = await _host.Reports.RunDueAsync(default);
        _host.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 11, 4, 0, 0, TimeSpan.Zero));
        var ran = await _host.Reports.RunDueAsync(default);
        var again = await _host.Reports.RunDueAsync(default);
        await _host.Dispatcher.DispatchDueAsync(default);

        Assert.Equal((0, 1, 0), (early, ran, again));
        Assert.Equal(["finance@demo.test", "owner@demo.test"], _host.Emails.Sent.Select(e => e.To).Order());
        var email = _host.Emails.Sent[0];
        Assert.Equal("Test Bistro · Marina: daily report, Wed 10 Mar 2027", email.Subject);
        Assert.Contains("AED 100.00", email.HtmlBody);   // revenue
        Assert.Contains("2 × Burger", email.HtmlBody);   // best seller
        Assert.Contains("Revenue", email.TextBody);
        Assert.Equal(new DateTimeOffset(2027, 3, 12, 4, 0, 0, TimeSpan.Zero), (await _host.Db.ReportSchedules.SingleAsync()).NextRunAt);
        Assert.All((await DeliveriesAsync(f, NotificationChannel.Email)).Items, d => Assert.Equal((NotificationKind.Report, DeliveryStatus.Sent), (d.Kind, d.Status)));
    }

    [Fact]
    public async Task Two_workers_running_the_same_report_send_it_once()
    {
        var f = await CreateAsync();
        await _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, ReportFrequency.Daily, 8, ["owner@demo.test"]), f.Manager.Id, default);
        _host.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 11, 4, 0, 0, TimeSpan.Zero));
        using var other = _host.NewScope();

        _host.SaveHook.BeforeNextSave = () => other.Reports.RunDueAsync(default);
        await _host.Reports.RunDueAsync(default);

        Assert.Single(await _host.Db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync());
        Assert.Equal(new DateTimeOffset(2027, 3, 12, 4, 0, 0, TimeSpan.Zero),
            (await _host.Db.ReportSchedules.IgnoreQueryFilters().AsNoTracking().SingleAsync()).NextRunAt);
    }

    [Fact]
    public async Task The_preview_compares_with_the_period_before()
    {
        var f = await CreateAsync();
        await OrderAndPayAsync(f, burgers: 1);            // Wednesday
        _host.Clock.SetUtcNow(Noon.AddDays(1));
        await OrderAndPayAsync(f, burgers: 3, tip: 1000); // Thursday
        _host.Clock.SetUtcNow(Noon.AddDays(2));

        var report = await _host.Reports.PreviewAsync(f.Venue.Id, ReportFrequency.Daily, default);
        ReportMetricDto Metric(string key) => report.Metrics.Single(m => m.Key == key);

        Assert.Equal((new DateOnly(2027, 3, 11), "Thu 11 Mar 2027"), (report.PeriodStart, report.PeriodLabel));
        Assert.Equal((15_000m, 5000m, 2.0), (Metric("revenue").Value, Metric("revenue").Previous, Metric("revenue").Change!.Value));
        Assert.Equal((1000m, 0m, (double?)null), (Metric("tips").Value, Metric("tips").Previous, Metric("tips").Change));
        Assert.Equal(16_000m, Metric("net").Value);
        Assert.Equal(new ReportItemDto("Burger", 3, 15_000), Assert.Single(report.TopItems));
    }

    [Theory]
    [InlineData(8, new[] { "not-an-email" })]
    [InlineData(24, new[] { "owner@demo.test" })]
    [InlineData(8, new string[0])]
    public async Task Report_schedules_need_valid_recipients_and_hour(int hour, string[] recipients)
    {
        var f = await CreateAsync();

        var ex = await Assert.ThrowsAsync<AppException>(() =>
            _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, ReportFrequency.Daily, hour, recipients), f.Manager.Id, default));

        Assert.Equal((400, "invalid_schedule"), (ex.StatusCode, ex.Code));
    }

    [Fact]
    public async Task No_reports_run_while_the_feature_is_disabled()
    {
        var f = await CreateAsync();
        await _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, ReportFrequency.Daily, 8, ["owner@demo.test"]), f.Manager.Id, default);
        _host.Clock.SetUtcNow(new DateTimeOffset(2027, 3, 11, 4, 0, 0, TimeSpan.Zero));
        _host.Features.EmailReports = false;

        Assert.Equal(0, await _host.Reports.RunDueAsync(default));
        Assert.Empty(await _host.Db.NotificationDeliveries.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task Send_now_queues_the_latest_report_without_moving_the_schedule()
    {
        var f = await CreateAsync();
        var schedule = await _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, ReportFrequency.Weekly, 9, ["owner@demo.test"]), f.Manager.Id, default);

        var queued = await _host.Reports.SendNowAsync(schedule.Id, f.Manager.Id, default);
        await _host.Dispatcher.DispatchDueAsync(default);

        Assert.Equal(1, queued);
        Assert.Equal("Test Bistro · Marina: weekly report, 1–7 Mar 2027", Assert.Single(_host.Emails.Sent).Subject);
        Assert.Equal(schedule.NextRunAt, (await _host.Db.ReportSchedules.SingleAsync()).NextRunAt);
    }

    // ---- Audit ----

    [Fact]
    public async Task The_audit_log_shows_who_changed_alerts_and_reports()
    {
        var f = await CreateAsync();
        await AlertsOnAsync(f, "+971501234567");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var schedule = await _host.Reports.CreateAsync(new ReportScheduleRequest(f.Venue.Id, ReportFrequency.Daily, 8, ["owner@demo.test"]), f.Manager.Id, default);

        var all = await _host.AuditLog.ListAsync(new AuditQuery(), default);
        var alerts = await _host.AuditLog.ListAsync(new AuditQuery(Area: "alerts"), default);

        Assert.Equal(("report_schedule.created", "Created an e-mail report", "Lina", schedule.Id),
            (all.Items[0].Action, all.Items[0].Description, all.Items[0].StaffName, all.Items[0].ObjectId));
        Assert.Equal(["Added a WhatsApp alert number", "Turned WhatsApp payment alerts on"], alerts.Items.Select(i => i.Description));
        Assert.Equal(3, all.Total);
    }
}
