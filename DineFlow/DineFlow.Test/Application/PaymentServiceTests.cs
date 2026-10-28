using DineFlow.Application.Abstractions;
using DineFlow.Application.Bills;
using DineFlow.Application.Common;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Payments;
using DineFlow.Application.Promotions;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Promotions;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

public class PaymentServiceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>
    /// A venue with no service charge or tax (so totals are easy to read), a 50.00 burger, table 7
    /// with one guest who has ordered two burgers (100.00). Leaves the host acting as that guest.
    /// </summary>
    private async Task<Fixture> CreateAsync(bool tipsEnabled = true)
    {
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        await _host.Venues.UpdateSettingsAsync(venue.Id,
            new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "en", null, "#0B3B38", true, true, TipsEnabled: tipsEnabled), default);

        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, null, "Mains", null), default);
        var burger = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Burger", null, null, null, 5000, null, true, [], [], []), default);

        await _host.Promotions.CreateAsync(new PromoCodeRequest("WELCOME10", "10% off", DiscountType.Percent, 1000, null, null, null, null, null, true, false), default);
        await _host.Promotions.CreateAsync(new PromoCodeRequest("NEXT15", "15% off your next visit", DiscountType.Percent, 1500, null, null, null, null, null, true, true), default);

        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7"), default);
        var f = new Fixture(tenant.Id, venue, burger, TestHost.TokenFromUrl(table.Qr!.Url), Guid.Empty);
        f = f with { SessionId = await NewGuestAsync(f) };
        await OrderAsync(f, f.SessionId, quantity: 2);
        return f;
    }

    private sealed record Fixture(Guid TenantId, VenueDto Venue, ItemDto Burger, string QrToken, Guid SessionId);

    /// <summary>Another guest scans the same table's QR code.</summary>
    private async Task<Guid> NewGuestAsync(Fixture f)
    {
        _host.CurrentTenant = null;
        var session = await _host.Guests.ResolveQrAsync(f.QrToken, null, default);
        _host.CurrentTenant = f.TenantId; // the guest's token carries the tenant
        return session.Context.SessionId;
    }

    private async Task<OrderDto> OrderAsync(Fixture f, Guid sessionId, int quantity)
    {
        var line = new CartLineRequest(f.Burger.Id, quantity, [], null);
        return await _host.GuestOrders.PlaceAsync(sessionId, new PlaceOrderRequest(Guid.NewGuid(), [line], null, quantity * 5000L), default);
    }

    private Task<PaymentDto> StartPaymentAsync(Guid sessionId, long expectedRemaining, long tip = 0, string? promo = null,
        Guid? clientRequestId = null, bool acceptTerms = true) =>
        _host.Payments.CreateAsync(new CreatePaymentRequest(sessionId, clientRequestId ?? Guid.NewGuid(), PaymentMethod.Card,
            PaymentMode.Full, null, null, tip, promo, expectedRemaining, acceptTerms), default);

    private static string ProviderId(PaymentDto payment) => $"sbx_{payment.Id:N}";

    /// <summary>The provider's signed webhook arrives. Webhooks carry no tenant.</summary>
    private async Task WebhookAsync(PaymentDto payment, PaymentStatus status, string? eventId = null, string? failure = null)
    {
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent(
            eventId ?? $"evt_{Guid.NewGuid():N}", ProviderId(payment), status, "Visa", "4242", failure));
        await DeliverAsync(body, signature);
    }

    private async Task DeliverAsync(string body, string signature)
    {
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        try
        {
            await _host.Payments.HandleWebhookAsync("sandbox", body,
                new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default);
        }
        finally
        {
            _host.CurrentTenant = tenant;
        }
    }

    private Task<BillDto> BillAsync(Guid sessionId) => _host.Bills.GetForSessionAsync(sessionId, default);

    // ---- Bill ----

    [Fact]
    public async Task The_bill_shows_every_order_at_the_table_and_what_is_left()
    {
        var f = await CreateAsync();
        var friend = await NewGuestAsync(f);
        await OrderAsync(f, friend, quantity: 1);

        var bill = await BillAsync(friend);

        Assert.Equal(BillStatus.Open, bill.Status);
        Assert.Equal(("Table", "7"), (bill.LocationType, bill.LocationLabel));
        Assert.Equal(2, bill.Orders.Count);
        Assert.Equal(15_000, bill.TotalMinor);
        Assert.Equal(0, bill.PaidMinor);
        Assert.Equal(15_000, bill.RemainingMinor);
        Assert.Equal([500, 1000, 1500], bill.Tips.PresetsBasisPoints);
        Assert.Equal(1000, bill.Tips.MostCommonBasisPoints);
        Assert.Contains(PaymentMethod.ApplePay, bill.Methods);
        Assert.Single(await _host.Db.Checks.ToListAsync());
    }

    [Fact]
    public async Task A_table_with_no_orders_has_nothing_to_pay()
    {
        var f = await CreateAsync();
        var other = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(f.Venue.Id, null, ServiceLocationType.Table, "8"), default);
        var guest = await NewGuestAsync(f with { QrToken = TestHost.TokenFromUrl(other.Qr!.Url) });

        var bill = await BillAsync(guest);

        Assert.Equal(BillStatus.NoOrders, bill.Status);
        Assert.Null(bill.CheckId);
        await Assert.ThrowsAsync<AppException>(() => StartPaymentAsync(guest, expectedRemaining: 1));
    }

    [Fact]
    public async Task Rejected_orders_are_not_billed()
    {
        var f = await CreateAsync();
        var second = await OrderAsync(f, f.SessionId, quantity: 1);
        await _host.MerchantOrders.ChangeStatusAsync(second.Id, new ChangeOrderStatusRequest(OrderStatus.Rejected, "Out of buns"), default);

        var bill = await BillAsync(f.SessionId);

        Assert.Single(bill.Orders);
        Assert.Equal(10_000, bill.RemainingMinor);
    }

    // ---- Paying ----

    [Fact]
    public async Task Paying_in_full_with_a_tip_and_promotion_settles_the_bill()
    {
        var f = await CreateAsync();

        var payment = await StartPaymentAsync(f.SessionId, expectedRemaining: 10_000, tip: 1_000, promo: " welcome10 ");

        Assert.Equal(PaymentStatus.RequiresAction, payment.Status);
        Assert.Equal((10_000, 1_000, 1_000, 10_000), (payment.BillAmountMinor, payment.DiscountMinor, payment.TipMinor, payment.AmountMinor));
        Assert.Equal("WELCOME10", payment.PromoCode);
        Assert.StartsWith("DF-", payment.Reference);

        // The hosted page link is signed and carries the amount to charge.
        var link = new Uri(payment.CheckoutUrl!);
        var token = Uri.UnescapeDataString(link.Query.TrimStart('?')["t=".Length..]);
        var checkout = _host.Sandbox.ReadCheckout(ProviderId(payment), token);
        Assert.Equal((10_000, "AED"), (checkout!.AmountMinor, checkout.Currency));
        Assert.Equal($"https://guest.test/pay/{payment.Id}", checkout.ReturnUrl);
        Assert.True((await BillAsync(f.SessionId)).PaymentInProgress);

        await WebhookAsync(payment, PaymentStatus.Succeeded);

        var paid = await _host.Payments.GetAsync(f.SessionId, payment.Id, default);
        Assert.Equal(PaymentStatus.Succeeded, paid.Status);
        Assert.Equal(("Visa", "4242"), (paid.CardBrand, paid.CardLast4));
        Assert.Equal((0L, true), (paid.RemainingMinor, paid.FullyPaid));
        Assert.Equal("NEXT15", paid.NextVisitOffer?.Code);
        Assert.Null(paid.CheckoutUrl);

        var ledger = await _host.Db.LedgerEntries.ToDictionaryAsync(e => e.Type, e => e.AmountMinor);
        Assert.Equal(new Dictionary<LedgerEntryType, long>
        {
            [LedgerEntryType.BillPayment] = 9_000, [LedgerEntryType.Discount] = 1_000, [LedgerEntryType.Tip] = 1_000
        }, ledger);

        Assert.Equal(1, (await _host.Db.PromoCodes.SingleAsync(p => p.Code == "WELCOME10")).RedemptionCount);
        Assert.Equal(CheckStatus.Closed, (await _host.Db.Checks.SingleAsync()).Status);

        var bill = await BillAsync(f.SessionId);
        Assert.Equal(BillStatus.Paid, bill.Status);
        Assert.Equal((10_000, 0), (bill.PaidMinor, bill.RemainingMinor));

        var notice = Assert.Single(_host.PaymentNotifier.Notices);
        Assert.Equal((f.TenantId, payment.Id, 10_000L, 1_000L, true), (notice.TenantId, notice.PaymentId, notice.AmountMinor, notice.TipMinor, notice.FullyPaid));
    }

    [Fact]
    public async Task Retrying_with_the_same_request_id_returns_the_same_payment()
    {
        var f = await CreateAsync();
        var requestId = Guid.NewGuid();

        var first = await StartPaymentAsync(f.SessionId, 10_000, clientRequestId: requestId);
        var retry = await StartPaymentAsync(f.SessionId, 10_000, clientRequestId: requestId);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(first.CheckoutUrl, retry.CheckoutUrl);
        Assert.Single(await _host.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Nothing_is_charged_when_the_bill_changed_or_terms_were_not_accepted()
    {
        var f = await CreateAsync();

        var changed = await Assert.ThrowsAsync<AppException>(() => StartPaymentAsync(f.SessionId, expectedRemaining: 5_000));
        var terms = await Assert.ThrowsAsync<AppException>(() => StartPaymentAsync(f.SessionId, 10_000, acceptTerms: false));

        Assert.Equal((409, "bill_changed"), (changed.StatusCode, changed.Code));
        Assert.Equal(10_000, Assert.IsType<BillDto>(changed.Details).RemainingMinor);
        Assert.Equal("terms_required", terms.Code);
        Assert.Empty(await _host.Db.Payments.ToListAsync());
    }

    [Fact]
    public async Task Tips_are_refused_when_the_venue_turned_them_off()
    {
        var f = await CreateAsync(tipsEnabled: false);

        var ex = await Assert.ThrowsAsync<AppException>(() => StartPaymentAsync(f.SessionId, 10_000, tip: 500));

        Assert.Equal("tips_disabled", ex.Code);
        Assert.False((await BillAsync(f.SessionId)).Tips.Enabled);
    }

    [Fact]
    public async Task Tips_are_capped_at_20_percent_of_the_amount_paid()
    {
        var f = await CreateAsync();

        var ex = await Assert.ThrowsAsync<AppException>(() => StartPaymentAsync(f.SessionId, 10_000, tip: 2_001));
        var payment = await StartPaymentAsync(f.SessionId, 10_000, tip: 2_000);

        Assert.Equal((400, "tip_too_high"), (ex.StatusCode, ex.Code));
        Assert.Equal(2_000, payment.TipMinor);
        Assert.Equal(2_000, (await BillAsync(f.SessionId)).Tips.MaxBasisPoints);
    }

    [Fact]
    public async Task Only_one_guest_at_a_table_can_be_paying_at_a_time()
    {
        var f = await CreateAsync();
        var friend = await NewGuestAsync(f);
        await StartPaymentAsync(f.SessionId, 10_000);

        var blocked = await Assert.ThrowsAsync<AppException>(() => StartPaymentAsync(friend, 10_000));
        Assert.Equal((409, "payment_in_progress"), (blocked.StatusCode, blocked.Code));

        // An abandoned payment stops blocking the table after a while.
        _host.Clock.Advance(Payment.InFlightLifetime + TimeSpan.FromSeconds(1));
        var later = await StartPaymentAsync(friend, 10_000);
        Assert.Equal(PaymentStatus.RequiresAction, later.Status);
    }

    [Fact]
    public async Task Cancelling_frees_the_bill_for_another_attempt()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000);

        var cancelled = await _host.Payments.CancelAsync(f.SessionId, payment.Id, default);

        Assert.Equal(PaymentStatus.Cancelled, cancelled.Status);
        Assert.False((await BillAsync(f.SessionId)).PaymentInProgress);
        Assert.Equal(PaymentStatus.RequiresAction, (await StartPaymentAsync(f.SessionId, 10_000)).Status);
    }

    [Fact]
    public async Task A_declined_payment_leaves_the_bill_open()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000, promo: "WELCOME10");

        await WebhookAsync(payment, PaymentStatus.Failed, failure: "Your card was declined.");

        var failed = await _host.Payments.GetAsync(f.SessionId, payment.Id, default);
        Assert.Equal((PaymentStatus.Failed, "Your card was declined."), (failed.Status, failed.FailureReason));
        Assert.Null(failed.NextVisitOffer);
        Assert.Equal(10_000, (await BillAsync(f.SessionId)).RemainingMinor);
        Assert.Empty(await _host.Db.LedgerEntries.ToListAsync());
        Assert.Equal(0, (await _host.Db.PromoCodes.SingleAsync(p => p.Code == "WELCOME10")).RedemptionCount);
        Assert.Empty(_host.PaymentNotifier.Notices);
    }

    [Fact]
    public async Task Ordering_after_the_bill_is_settled_opens_a_new_bill()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000);
        await WebhookAsync(payment, PaymentStatus.Succeeded);

        await OrderAsync(f, f.SessionId, quantity: 1);

        var bill = await BillAsync(f.SessionId);
        Assert.Equal(BillStatus.Open, bill.Status);
        Assert.Equal((5_000, 0, 5_000), (bill.TotalMinor, bill.PaidMinor, bill.RemainingMinor));
        Assert.Equal(2, await _host.Db.Checks.CountAsync());
    }

    // ---- Webhooks ----

    [Fact]
    public async Task A_repeated_webhook_is_applied_once()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000, promo: "WELCOME10");
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent(
            "evt_1", ProviderId(payment), PaymentStatus.Succeeded, "Visa", "4242", null));

        await DeliverAsync(body, signature);
        await DeliverAsync(body, signature);

        Assert.Equal(2, await _host.Db.LedgerEntries.CountAsync());   // bill payment + discount
        Assert.Equal(1, (await _host.Db.PromoCodes.SingleAsync(p => p.Code == "WELCOME10")).RedemptionCount);
        Assert.Single(_host.PaymentNotifier.Notices);
    }

    [Fact]
    public async Task A_late_failure_event_cannot_undo_a_success()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000);

        await WebhookAsync(payment, PaymentStatus.Succeeded);
        await WebhookAsync(payment, PaymentStatus.Failed, failure: "Timeout");

        Assert.Equal(PaymentStatus.Succeeded, (await _host.Payments.GetAsync(f.SessionId, payment.Id, default)).Status);
        Assert.Equal(1, await _host.Db.LedgerEntries.CountAsync());
    }

    [Fact]
    public async Task Unsigned_tampered_or_replayed_webhooks_change_nothing()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000);
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent(
            "evt_1", ProviderId(payment), PaymentStatus.Succeeded, "Visa", "4242", null));

        var tampered = await Assert.ThrowsAsync<AppException>(() => DeliverAsync(body.Replace("4242", "1111"), signature));
        var forged = await Assert.ThrowsAsync<AppException>(() => DeliverAsync(body, "t=1,v1=00"));
        _host.Clock.Advance(TimeSpan.FromMinutes(6));
        var replayed = await Assert.ThrowsAsync<AppException>(() => DeliverAsync(body, signature));

        Assert.All([tampered, forged, replayed], ex => Assert.Equal((400, "invalid_webhook"), (ex.StatusCode, ex.Code)));
        Assert.Equal(PaymentStatus.RequiresAction, (await _host.Db.Payments.SingleAsync()).Status);
        Assert.Empty(await _host.Db.LedgerEntries.ToListAsync());
        Assert.Empty(await _host.Db.ProcessedWebhooks.ToListAsync());
    }

    [Fact]
    public async Task The_hosted_page_link_cannot_be_altered()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000);
        var token = Uri.UnescapeDataString(new Uri(payment.CheckoutUrl!).Query["?t=".Length..]);

        Assert.NotNull(_host.Sandbox.ReadCheckout(ProviderId(payment), token));
        Assert.Null(_host.Sandbox.ReadCheckout($"sbx_{Guid.NewGuid():N}", token));            // another payment
        Assert.Null(_host.Sandbox.ReadCheckout(ProviderId(payment), "x" + token));            // tampered
        _host.Clock.Advance(Payment.InFlightLifetime + TimeSpan.FromSeconds(1));
        Assert.Null(_host.Sandbox.ReadCheckout(ProviderId(payment), token));                  // expired
    }

    // ---- Promotions ----

    [Fact]
    public async Task Promotion_codes_are_checked_on_the_server()
    {
        var f = await CreateAsync();
        await _host.Promotions.CreateAsync(new PromoCodeRequest("BIGSPEND", "20 off over 200", DiscountType.Amount, 2000, null, 20_000, null, null, null, true, false), default);
        await _host.Promotions.CreateAsync(new PromoCodeRequest("OLD", "Expired", DiscountType.Percent, 1000, null, null,
            _host.Clock.GetUtcNow().AddDays(-10), _host.Clock.GetUtcNow().AddDays(-1), null, true, false), default);
        await _host.Promotions.CreateAsync(new PromoCodeRequest("CAPPED", "50% off, up to 10", DiscountType.Percent, 5000, 1000, null, null, null, null, true, false), default);

        var welcome = await _host.Promotions.ValidateAsync(f.SessionId, "welcome10", null, default);
        var capped = await _host.Promotions.ValidateAsync(f.SessionId, "CAPPED", null, default);

        Assert.Equal(("WELCOME10", 1_000L), (welcome.Code, welcome.DiscountMinor));
        Assert.Equal(1_000, capped.DiscountMinor);
        Assert.Equal("promo_min_spend", (await Assert.ThrowsAsync<AppException>(() => _host.Promotions.ValidateAsync(f.SessionId, "BIGSPEND", null, default))).Code);
        Assert.Equal("promo_expired", (await Assert.ThrowsAsync<AppException>(() => _host.Promotions.ValidateAsync(f.SessionId, "OLD", null, default))).Code);
        Assert.Equal("promo_invalid", (await Assert.ThrowsAsync<AppException>(() => _host.Promotions.ValidateAsync(f.SessionId, "NOPE", null, default))).Code);
    }

    [Fact]
    public async Task A_code_with_a_redemption_limit_runs_out()
    {
        var f = await CreateAsync();
        await _host.Promotions.CreateAsync(new PromoCodeRequest("ONCE", "One time", DiscountType.Amount, 500, null, null, null, null, 1, true, false), default);
        var payment = await StartPaymentAsync(f.SessionId, 10_000, promo: "ONCE");
        await WebhookAsync(payment, PaymentStatus.Succeeded);
        await OrderAsync(f, f.SessionId, quantity: 1);

        var ex = await Assert.ThrowsAsync<AppException>(() => StartPaymentAsync(f.SessionId, 5_000, promo: "ONCE"));

        Assert.Equal("promo_used_up", ex.Code);
        Assert.Equal(10_000 - 500, (await _host.Db.LedgerEntries.SingleAsync(e => e.Type == LedgerEntryType.BillPayment)).AmountMinor);
    }

    [Fact]
    public async Task Another_merchants_codes_do_not_work_here()
    {
        var f = await CreateAsync();
        await _host.CreateTenantAsync("Rival Cafe");
        await _host.Promotions.CreateAsync(new PromoCodeRequest("RIVAL50", "Half off", DiscountType.Percent, 5000, null, null, null, null, null, true, false), default);
        _host.CurrentTenant = f.TenantId;

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Promotions.ValidateAsync(f.SessionId, "RIVAL50", null, default));

        Assert.Equal("promo_invalid", ex.Code);
    }

    [Fact]
    public async Task Merchants_cannot_create_duplicate_or_impossible_codes()
    {
        await _host.CreateTenantAsync();
        await _host.Promotions.CreateAsync(new PromoCodeRequest("SUMMER", "Summer", DiscountType.Percent, 1000, null, null, null, null, null, true, false), default);

        var duplicate = await Assert.ThrowsAsync<AppException>(() => _host.Promotions.CreateAsync(
            new PromoCodeRequest("summer", "Again", DiscountType.Percent, 1000, null, null, null, null, null, true, false), default));
        var overHundred = await Assert.ThrowsAsync<AppException>(() => _host.Promotions.CreateAsync(
            new PromoCodeRequest("TOOMUCH", "Free money", DiscountType.Percent, 15_000, null, null, null, null, null, true, false), default));

        Assert.Equal((409, "promo_exists"), (duplicate.StatusCode, duplicate.Code));
        Assert.Equal((400, "invalid_promo"), (overHundred.StatusCode, overHundred.Code));
    }

    // ---- Receipts ----

    [Fact]
    public async Task A_receipt_is_sent_only_once_the_payment_went_through()
    {
        var f = await CreateAsync();
        var payment = await StartPaymentAsync(f.SessionId, 10_000, tip: 1_000);

        var early = await Assert.ThrowsAsync<AppException>(() => _host.Payments.SendReceiptAsync(f.SessionId, payment.Id, "guest@example.com", null, default));
        await WebhookAsync(payment, PaymentStatus.Succeeded);
        await _host.Payments.SendReceiptAsync(f.SessionId, payment.Id, "guest@example.com", null, default);

        Assert.Equal("not_paid", early.Code);
        var mail = Assert.Single(_host.Emails.Sent);
        Assert.Equal("guest@example.com", mail.To);
        Assert.Contains(payment.Reference, mail.Subject);
        Assert.Contains("2 × Burger", mail.TextBody);
        Assert.Contains("AED 110.00", mail.TextBody);
    }

    [Fact]
    public async Task The_receipt_follows_the_language_the_guest_reads()
    {
        var f = await CreateAsync();
        _host.CurrentTenant = null;
        var arabicGuest = (await _host.Guests.ResolveQrAsync(f.QrToken, "ar", default)).Context.SessionId;
        _host.CurrentTenant = f.TenantId;
        var payment = await StartPaymentAsync(arabicGuest, 10_000, tip: 1_000);
        await WebhookAsync(payment, PaymentStatus.Succeeded);
        await _host.Venues.UpdateSettingsAsync(f.Venue.Id,
            new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "en", null, "#0B3B38", true, true, NameAr: "المارينا"), default);

        await _host.Payments.SendReceiptAsync(arabicGuest, payment.Id, "guest@example.com", null, default); // the session's language
        await _host.Payments.SendReceiptAsync(arabicGuest, payment.Id, "guest@example.com", "en", default); // switched to English since

        var (arabic, english) = (_host.Emails.Sent[0], _host.Emails.Sent[1]);
        Assert.Equal($"إيصالك من Test Bistro ({payment.Reference})", arabic.Subject);
        Assert.StartsWith("Test Bistro · المارينا", arabic.TextBody);
        Assert.Contains("طاولة 7", arabic.TextBody);
        Assert.Contains("2 × Burger", arabic.TextBody);
        Assert.Contains("الإكرامية", arabic.TextBody);
        Assert.Contains("تم الدفع عبر Visa •••• 4242", arabic.TextBody);
        Assert.Contains("dir=\"rtl\"", arabic.HtmlBody);
        Assert.StartsWith("Your receipt from", english.Subject);
        Assert.StartsWith("Test Bistro · Marina", english.TextBody);
        Assert.Contains("Table 7", english.TextBody);
    }

    [Fact]
    public async Task Guests_cannot_see_other_guests_payments()
    {
        var f = await CreateAsync();
        var friend = await NewGuestAsync(f);
        var payment = await StartPaymentAsync(f.SessionId, 10_000);

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Payments.GetAsync(friend, payment.Id, default));

        Assert.Equal(404, ex.StatusCode);
    }
}
