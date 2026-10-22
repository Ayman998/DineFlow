using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Dashboard;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Payments;
using DineFlow.Application.Reviews;
using DineFlow.Application.Tables;
using DineFlow.Application.Transactions;
using DineFlow.Application.Venues;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

/// <summary>Transactions, refunds, the overview and the live table view (blueprint E11, E12).</summary>
public class FinanceTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>Noon in Dubai on 10 March 2027, so "today" has room either side (the test clock can only move forward).</summary>
    private static readonly DateTimeOffset Noon = new(2027, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(Guid TenantId, VenueDto Venue, ItemDto Burger, ItemDto Coke, LocationDto Table7, LocationDto Table8, StaffUser Manager);

    private async Task<Fixture> CreateAsync(DateTimeOffset? at = null)
    {
        _host.Clock.SetUtcNow(at ?? Noon);
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, null, "Mains", null), default);
        var burger = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Burger", null, null, null, 5000, null, true, [], [], []), default);
        var coke = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Coke", null, null, null, 1250, null, true, [], [], []), default);
        var table7 = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7"), default);
        var table8 = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "8"), default);
        var manager = new StaffUser(tenant.Id, "lina@demo.test", "Lina", StaffRole.Manager);
        _host.Db.StaffUsers.Add(manager);
        await _host.Db.SaveChangesAsync();
        return new Fixture(tenant.Id, venue, burger, coke, table7, table8, manager);
    }

    private async Task<Guid> GuestAsync(Fixture f, LocationDto table)
    {
        _host.CurrentTenant = null;
        var session = await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(table.Qr!.Url), null, default);
        _host.CurrentTenant = f.TenantId;
        return session.Context.SessionId;
    }

    private async Task<OrderDto> OrderAsync(Guid sessionId, params (ItemDto Item, int Quantity)[] lines)
    {
        var request = lines.Select(l => new CartLineRequest(l.Item.Id, l.Quantity, [], null)).ToList();
        var quote = await _host.CartQuotes.QuoteAsync(sessionId, new CartRequest(request), default);
        return await _host.GuestOrders.PlaceAsync(sessionId, new PlaceOrderRequest(Guid.NewGuid(), request, null, quote.TotalMinor), default);
    }

    private async Task<PaymentDto> PayAsync(Guid sessionId, long? amount = null, long tip = 0, PaymentStatus outcome = PaymentStatus.Succeeded)
    {
        var bill = await _host.Bills.GetForSessionAsync(sessionId, default);
        var mode = amount is null ? PaymentMode.Full : PaymentMode.Custom;
        var payment = await _host.Payments.CreateAsync(new CreatePaymentRequest(sessionId, Guid.NewGuid(), PaymentMethod.Card,
            mode, null, amount, tip, null, amount ?? bill.AvailableMinor, true), default);
        await WebhookAsync(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", outcome, "Visa", "4242",
            outcome == PaymentStatus.Failed ? "Declined" : null));
        return await _host.Payments.GetAsync(sessionId, payment.Id, default);
    }

    private async Task WebhookAsync(ProviderWebhookEvent webhook)
    {
        var (body, signature) = _host.Sandbox.CreateWebhook(webhook);
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        try
        {
            await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default);
        }
        finally
        {
            _host.CurrentTenant = tenant;
        }
    }

    private Task<TransactionDetailDto> RefundAsync(Fixture f, PaymentDto payment, long amount, Guid? requestId = null) =>
        _host.Transactions.RefundAsync(payment.Id, new RefundRequest(requestId ?? Guid.NewGuid(), amount, "Wrong item"), f.Manager.Id, default);

    /// <summary>The provider confirms (or declines) a refund.</summary>
    private Task ConfirmRefundAsync(PaymentDto payment, TransactionDetailDto detail, Guid refundId, PaymentStatus outcome = PaymentStatus.Succeeded) =>
        WebhookAsync(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", outcome, null, null,
            outcome == PaymentStatus.Failed ? "Card account closed" : null, $"sbxr_{refundId:N}"));

    // ---- Refunds ----

    [Fact]
    public async Task A_full_refund_waits_for_the_provider_then_goes_in_the_ledger()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 1));
        var payment = await PayAsync(guest, tip: 500);

        var pending = await RefundAsync(f, payment, 5500);
        var refund = Assert.Single(pending.Refunds);
        var again = await Assert.ThrowsAsync<AppException>(() => RefundAsync(f, payment, 1));

        Assert.Equal((RefundStatus.Pending, 0L), (refund.Status, pending.RefundableMinor));
        Assert.True(pending.Summary.RefundPending);
        Assert.Equal("refund_not_allowed", again.Code);
        Assert.Equal("sbxr_" + refund.Id.ToString("N"), (await _host.Db.Refunds.SingleAsync()).ProviderRefundId);
        Assert.Contains(await _host.Db.AuditEntries.ToListAsync(), a => a.Action == "payment.refund.requested" && a.ObjectId == payment.Id);

        await ConfirmRefundAsync(payment, pending, refund.Id);
        var done = await _host.Transactions.GetAsync(payment.Id, default);

        Assert.Equal((TransactionState.Refunded, 5500L, RefundStatus.Succeeded), (done.Summary.State, done.Summary.RefundedMinor, done.Refunds.Single().Status));
        var entry = await _host.Db.LedgerEntries.SingleAsync(e => e.Type == LedgerEntryType.Refund);
        Assert.Equal((5500L, payment.Id), (entry.AmountMinor, entry.PaymentId));
        Assert.Equal(RefundStatus.Succeeded, Assert.Single(_host.PaymentNotifier.Refunds).Status);
        Assert.Equal(CheckStatus.Closed, (await _host.Db.Checks.SingleAsync()).Status); // the bill stays settled
    }

    [Fact]
    public async Task Partial_refunds_never_add_up_to_more_than_was_paid()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 1), (f.Coke, 1));
        var payment = await PayAsync(guest); // 62.50

        var first = await RefundAsync(f, payment, 1250);
        await ConfirmRefundAsync(payment, first, first.Refunds.Single().Id);
        var partly = await _host.Transactions.GetAsync(payment.Id, default);
        var second = await RefundAsync(f, payment, 5000);
        var tooMuch = await Assert.ThrowsAsync<AppException>(() => RefundAsync(f, payment, 1));

        Assert.Equal((TransactionState.PartiallyRefunded, 5000L), (partly.Summary.State, partly.RefundableMinor));
        Assert.Equal(0, second.RefundableMinor);
        Assert.Equal("refund_not_allowed", tooMuch.Code);
    }

    [Fact]
    public async Task Retrying_a_refund_request_returns_the_same_refund()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 1));
        var payment = await PayAsync(guest);
        var requestId = Guid.NewGuid();

        await RefundAsync(f, payment, 1000, requestId);
        var retry = await RefundAsync(f, payment, 1000, requestId);

        Assert.Single(retry.Refunds);
        Assert.Equal(1, await _host.Db.Refunds.CountAsync());
    }

    [Fact]
    public async Task A_declined_refund_can_be_tried_again()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 1));
        var payment = await PayAsync(guest);

        var pending = await RefundAsync(f, payment, 5000);
        await ConfirmRefundAsync(payment, pending, pending.Refunds.Single().Id, PaymentStatus.Failed);
        var declined = await _host.Transactions.GetAsync(payment.Id, default);

        Assert.Equal((RefundStatus.Failed, "Card account closed"), (declined.Refunds.Single().Status, declined.Refunds.Single().FailureReason));
        Assert.Equal((TransactionState.Succeeded, 5000L), (declined.Summary.State, declined.RefundableMinor));
        Assert.Empty(await _host.Db.LedgerEntries.Where(e => e.Type == LedgerEntryType.Refund).ToListAsync());
        Assert.Equal(2, (await RefundAsync(f, payment, 5000)).Refunds.Count);
    }

    [Fact]
    public async Task Only_successful_payments_can_be_refunded()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 1));
        var failed = await PayAsync(guest, outcome: PaymentStatus.Failed);

        var ex = await Assert.ThrowsAsync<AppException>(() => RefundAsync(f, failed, 100));

        Assert.Equal("refund_not_allowed", ex.Code);
    }

    [Fact]
    public async Task One_of_several_split_payments_can_be_partly_refunded()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        var friend = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 2)); // 100.00
        var mine = await PayAsync(guest, amount: 4000);
        var theirs = await PayAsync(friend);    // the remaining 60.00

        var refunded = await RefundAsync(f, theirs, 1500);
        await ConfirmRefundAsync(theirs, refunded, refunded.Refunds.Single().Id);

        Assert.Equal(TransactionState.Succeeded, (await _host.Transactions.GetAsync(mine.Id, default)).Summary.State);
        Assert.Equal((TransactionState.PartiallyRefunded, 4500L), ((await _host.Transactions.GetAsync(theirs.Id, default)).Summary.State,
            (await _host.Transactions.GetAsync(theirs.Id, default)).RefundableMinor));
        var page = await _host.Transactions.ListAsync(new TransactionQuery(f.Venue.Id), default);
        Assert.Equal((2, 10_000L, 1500L, 8500L), (page.Totals.Count, page.Totals.GrossMinor, page.Totals.RefundsMinor, page.Totals.NetMinor));
    }

    [Fact]
    public async Task Two_managers_refunding_at_the_same_moment_cannot_refund_twice()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 1));
        var payment = await PayAsync(guest);
        using var other = _host.NewScope();

        _host.SaveHook.BeforeNextSave = () =>
            other.Transactions.RefundAsync(payment.Id, new RefundRequest(Guid.NewGuid(), 5000, "Duplicate charge"), f.Manager.Id, default);
        var ex = await Assert.ThrowsAsync<AppException>(() => RefundAsync(f, payment, 5000));

        Assert.Equal("refund_not_allowed", ex.Code);
        Assert.Equal(1, await _host.Db.Refunds.IgnoreQueryFilters().CountAsync());
    }

    // ---- Transactions ----

    [Fact]
    public async Task Transactions_can_be_filtered_and_searched()
    {
        var f = await CreateAsync();
        var at7 = await GuestAsync(f, f.Table7);
        await OrderAsync(at7, (f.Burger, 1));
        var declined = await PayAsync(at7, outcome: PaymentStatus.Failed);
        var paid7 = await PayAsync(at7, tip: 500);
        var at8 = await GuestAsync(f, f.Table8);
        await OrderAsync(at8, (f.Coke, 2));
        var paid8 = await PayAsync(at8);
        var refund = await RefundAsync(f, paid8, 2500);
        await ConfirmRefundAsync(paid8, refund, refund.Refunds.Single().Id);
        Task<TransactionPageDto> List(TransactionFilter status = TransactionFilter.All, string? search = null) =>
            _host.Transactions.ListAsync(new TransactionQuery(f.Venue.Id, Status: status, Search: search), default);

        var all = await List();
        Assert.Equal(3, all.Total);
        Assert.Equal((2, 8000L, 500L, 2500L, 5500L), (all.Totals.Count, all.Totals.GrossMinor, all.Totals.TipsMinor, all.Totals.RefundsMinor, all.Totals.NetMinor));
        Assert.Equal([declined.Id], (await List(TransactionFilter.Failed)).Items.Select(t => t.Id));
        Assert.Equal([paid8.Id], (await List(TransactionFilter.Refunded)).Items.Select(t => t.Id));
        Assert.Equal(2, (await List(TransactionFilter.Succeeded)).Total);
        Assert.Equal([paid7.Id], (await List(search: paid7.Reference.ToLowerInvariant())).Items.Select(t => t.Id));
        Assert.Equal([paid8.Id], (await List(search: "8")).Items.Select(t => t.Id));
        Assert.Equal([paid7.Id], (await List(search: "55.00")).Items.Select(t => t.Id));

        var row = all.Items.Single(t => t.Id == paid7.Id);
        Assert.Equal(("Table", "7"), (row.LocationType, row.LocationLabel));
        Assert.Equal([Order.FirstNumber], row.OrderNumbers);
        Assert.Equal(TransactionState.Refunded, all.Items.Single(t => t.Id == paid8.Id).State);
    }

    [Fact]
    public async Task A_transaction_shows_its_whole_story_in_order()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        _host.Clock.Advance(TimeSpan.FromMinutes(2));
        await OrderAsync(guest, (f.Burger, 1));
        _host.Clock.Advance(TimeSpan.FromMinutes(30));
        var payment = await PayAsync(guest, tip: 500);
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var result = await _host.GuestReviews.SubmitAsync(guest, new SubmitReviewRequest(payment.Id, 4, null), default);
        _host.Clock.Advance(TimeSpan.FromHours(1));
        var refund = await RefundAsync(f, payment, 500);
        await ConfirmRefundAsync(payment, refund, refund.Refunds.Single().Id);

        var detail = await _host.Transactions.GetAsync(payment.Id, default);

        Assert.Equal(["session", "order", "payment", "provider", "ledger", "review", "refund", "provider", "ledger"], detail.Timeline.Select(e => e.Kind));
        Assert.Contains("Lina", detail.Timeline.Single(e => e.Kind == "refund").Text);
        Assert.Equal((4, result.Id), (detail.ReviewRating, detail.ReviewId));
        Assert.Equal(("Burger", 1), (detail.Orders.Single().Lines.Single().Name, detail.Orders.Single().Lines.Single().Quantity));
        Assert.Equal((5000L, 500L, 5500L, 5000L), (detail.BillAmountMinor, detail.TipMinor, detail.AmountMinor, detail.RefundableMinor));
    }

    // ---- Overview ----

    [Fact]
    public async Task The_overview_counts_today_and_the_period_and_compares_with_last_week()
    {
        var f = await CreateAsync(Noon - TimeSpan.FromDays(8));
        // Last week: one burger.
        var lastWeek = await GuestAsync(f, f.Table8);
        await OrderAsync(lastWeek, (f.Burger, 1));
        await PayAsync(lastWeek);
        // Today: two burgers with a tip, a failed attempt, and a refund.
        _host.Clock.SetUtcNow(Noon);
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 2));
        await PayAsync(guest, outcome: PaymentStatus.Failed);
        var paid = await PayAsync(guest, tip: 1000);
        var refund = await RefundAsync(f, paid, 2000);
        await ConfirmRefundAsync(paid, refund, refund.Refunds.Single().Id);

        var overview = await _host.Overview.GetAsync(f.Venue.Id, OverviewPeriod.Month, null, default);

        Assert.Equal(new OverviewKpisDto(10_000, 1000, 1, 2000, 1, 1), overview.Today);
        Assert.Equal((10_000L, 5000L, 1.0, 1, 1), (overview.Week.RevenueMinor, overview.Week.PreviousRevenueMinor, overview.Week.RevenueChange, overview.Week.Orders, overview.Week.PreviousOrders));
        Assert.Equal(15_000, overview.Selected.RevenueMinor); // both are in March
        Assert.Equal(TrendGranularity.Daily, overview.Granularity);
        Assert.Equal(10, overview.Trend.Count);                // 1–10 March
        Assert.Equal(overview.Selected.RevenueMinor, overview.Trend.Sum(p => p.RevenueMinor));
        Assert.Equal(10_000, overview.Trend[^1].RevenueMinor);
        Assert.Contains(overview.Attention, a => a.Kind == "payment" && a.Title == "1 payment failure today" && a.Detail.StartsWith("Last: Table 7"));
    }

    [Fact]
    public async Task Needs_attention_lists_open_balances_waiting_orders_unhappy_guests_and_pending_refunds()
    {
        var f = await CreateAsync();
        var guest = await GuestAsync(f, f.Table7);
        await OrderAsync(guest, (f.Burger, 2));
        await PayAsync(guest, amount: 3000);                // partially paid: 70.00 left
        var other = await GuestAsync(f, f.Table8);
        await OrderAsync(other, (f.Coke, 1));
        var paid = await PayAsync(other);
        await _host.GuestReviews.SubmitAsync(other, new SubmitReviewRequest(paid.Id, 2, "Slow"), default);
        await RefundAsync(f, paid, 1250);                   // still pending
        _host.Clock.Advance(TimeSpan.FromMinutes(6));        // the orders have now waited too long

        var overview = await _host.Overview.GetAsync(f.Venue.Id, OverviewPeriod.Today, null, default);

        Assert.Equal(["orders", "tables", "review", "refund"], overview.Attention.Select(a => a.Kind));
        Assert.Equal("2 orders waiting to be accepted", overview.Attention[0].Title);
        Assert.Equal(("1 table partially paid", "Open balance AED 70.00"), (overview.Attention[1].Title, overview.Attention[1].Detail));
        Assert.Equal(TrendGranularity.Hourly, overview.Granularity);
    }

    // ---- Live tables ----

    [Fact]
    public async Task Tables_show_what_has_been_paid_against_the_bill()
    {
        var f = await CreateAsync();
        Task<TableStatusDto> Table(string label) =>
            _host.TableStatus.ListAsync(f.Venue.Id, default).ContinueWith(t => t.Result.Single(x => x.Label == label));

        Assert.Equal(TableState.Available, (await Table("7")).State);

        var guest = await GuestAsync(f, f.Table7);
        Assert.Equal(TableState.Ordering, (await Table("7")).State);

        var order = await OrderAsync(guest, (f.Burger, 2));
        var unpaid = await Table("7");
        Assert.Equal((TableState.Unpaid, 10_000L, 0L, 10_000L), (unpaid.State, unpaid.TotalMinor, unpaid.PaidMinor, unpaid.RemainingMinor));
        Assert.Equal(order.Number, unpaid.Orders.Single().Number);

        await PayAsync(guest, amount: 3000, outcome: PaymentStatus.Failed);
        var attention = await Table("7");
        Assert.Equal(TableState.Attention, attention.State);
        Assert.NotNull(attention.AttentionReason);

        await PayAsync(guest, amount: 3000, tip: 400);
        var partly = await Table("7");
        Assert.Equal((TableState.PartiallyPaid, 3000L, 400L, 7000L), (partly.State, partly.PaidMinor, partly.TipsMinor, partly.RemainingMinor));

        await PayAsync(guest);
        var paid = await Table("7");
        Assert.Equal((TableState.Paid, 10_000L, 0L), (paid.State, paid.PaidMinor, paid.RemainingMinor));

        _host.Clock.Advance(TableStatusService.PaidVisibleFor + TimeSpan.FromMinutes(1));
        Assert.Equal(TableState.Available, (await Table("7")).State);
        Assert.Equal(TableState.Available, (await Table("8")).State);
    }
}
