using System.Text.Json;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Dashboard;
using DineFlow.Application.PaymentLinks;
using DineFlow.Application.Transactions;
using DineFlow.Application.Venues;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Staff;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

/// <summary>Payment links: create, share, pay (in full or split), expiry, deactivation and tracking (blueprint E16).</summary>
public class PaymentLinkTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>Noon in Dubai on 10 March 2027 (the test clock can only move forward).</summary>
    private static readonly DateTimeOffset Noon = new(2027, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(Guid TenantId, VenueDto Venue, StaffUser Staff);

    private async Task<Fixture> CreateAsync()
    {
        _host.Clock.SetUtcNow(Noon);
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        var staff = new StaffUser(tenant.Id, "sara@demo.test", "Sara", StaffRole.Staff);
        _host.Db.StaffUsers.Add(staff);
        await _host.Db.SaveChangesAsync();
        return new Fixture(tenant.Id, venue, staff);
    }

    private Task<PaymentLinkDetailDto> LinkAsync(Fixture f, long amount = 5000, bool split = false, bool tip = false,
        int validFor = 24, ValidityUnit unit = ValidityUnit.Hours, string? email = null, string? reference = "BOOK-42", Guid? requestId = null) =>
        _host.PaymentLinks.CreateAsync(new CreatePaymentLinkRequest(f.Venue.Id, requestId ?? Guid.NewGuid(), amount, reference, split, tip,
            validFor, unit, email), f.Staff.Id, default);

    private static string TokenOf(PaymentLinkDetailDto link) => TestHost.TokenFromUrl(link.Summary.Url);

    /// <summary>Runs something as the anonymous customer who opened the link.</summary>
    private async Task<T> AsCustomer<T>(Func<Task<T>> action)
    {
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        try { return await action(); }
        finally { _host.CurrentTenant = tenant; }
    }

    private static CreateLinkPaymentRequest Pay(PaymentMode mode, long expected, long? amount = null, int? people = null, int? shares = null,
        long tip = 0, Guid? requestId = null) =>
        new(requestId ?? Guid.NewGuid(), PaymentMethod.Card, mode, people, shares, amount, tip, expected, true);

    /// <summary>Starts a payment on the link without finishing it at the provider.</summary>
    private Task<LinkPaymentDto> StartAsync(string token, CreateLinkPaymentRequest request) =>
        AsCustomer(() => _host.LinkCheckout.CreatePaymentAsync(token, request, default));

    /// <summary>Starts a payment and has the provider confirm (or decline) it.</summary>
    private async Task<LinkPaymentDto> PayAsync(string token, CreateLinkPaymentRequest request, PaymentStatus outcome = PaymentStatus.Succeeded)
    {
        var payment = await StartAsync(token, request);
        await WebhookAsync(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", outcome, "Visa", "4242",
            outcome == PaymentStatus.Failed ? "Declined" : null));
        return await AsCustomer(() => _host.LinkCheckout.GetPaymentAsync(token, payment.Id, default));
    }

    private Task<PublicPaymentLinkDto> ViewAsync(string token) => AsCustomer(() => _host.LinkCheckout.GetAsync(token, default));

    private async Task WebhookAsync(ProviderWebhookEvent webhook)
    {
        var (body, signature) = _host.Sandbox.CreateWebhook(webhook);
        await AsCustomer(async () =>
        {
            await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default);
            return true;
        });
    }

    // ---- Creating and sharing ----

    [Fact]
    public async Task A_new_link_has_a_signed_url_an_expiry_and_an_audit_record()
    {
        var f = await CreateAsync();
        var requestId = Guid.NewGuid();
        var link = await LinkAsync(f, validFor: 48, requestId: requestId);
        var again = await LinkAsync(f, validFor: 48, requestId: requestId);
        var days = await LinkAsync(f, validFor: 3, unit: ValidityUnit.Days);

        var token = TokenOf(link);
        var stored = await _host.Db.PaymentLinks.SingleAsync(l => l.Id == link.Summary.Id);
        Assert.Equal(link.Summary.Id, again.Summary.Id); // a retried request returns the same link
        Assert.Equal(2, await _host.Db.PaymentLinks.CountAsync());
        Assert.Matches("^PL-[0-9A-F]{8}$", link.Summary.Code);
        Assert.StartsWith("https://guest.test/l/", link.Summary.Url);
        Assert.True(_host.QrTokens.IsWellFormed(token));
        Assert.Equal(_host.QrTokens.Hash(token), stored.TokenHash); // only the hash is stored
        Assert.Equal((PaymentLinkStatus.Active, Noon.AddHours(48)), (link.Summary.Status, link.Summary.ExpiresAt));
        Assert.Equal(Noon.AddDays(3), days.Summary.ExpiresAt);
        Assert.Equal(("Sara", "BOOK-42", 5000L), (link.CreatedBy, link.Summary.Reference, link.RemainingMinor));
        Assert.Contains(await _host.Db.AuditEntries.ToListAsync(), a => a.Action == "payment_link.created" && a.ObjectId == link.Summary.Id);
    }

    [Theory]
    [InlineData(0L, 24, ValidityUnit.Hours)]
    [InlineData(5000L, 0, ValidityUnit.Hours)]
    [InlineData(5000L, 91, ValidityUnit.Days)]
    public async Task Links_need_an_amount_and_a_validity_from_an_hour_to_90_days(long amount, int validFor, ValidityUnit unit)
    {
        var f = await CreateAsync();

        var ex = await Assert.ThrowsAsync<AppException>(() => LinkAsync(f, amount, validFor: validFor, unit: unit));

        Assert.Equal((400, "invalid_payment_link"), (ex.StatusCode, ex.Code));
    }

    [Fact]
    public async Task The_customer_is_emailed_the_link_and_it_can_be_sent_again()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f, email: "omar@example.com");

        var first = Assert.Single(_host.Emails.Sent);
        var resent = await _host.PaymentLinks.NotifyAsync(link.Summary.Id, "layla@example.com", default);
        await _host.PaymentLinks.CancelAsync(link.Summary.Id, f.Staff.Id, default);
        var closed = await Assert.ThrowsAsync<AppException>(() => _host.PaymentLinks.NotifyAsync(link.Summary.Id, "omar@example.com", default));

        Assert.Equal("omar@example.com", first.To);
        Assert.Contains(link.Summary.Url, first.TextBody);
        Assert.Contains("AED 50.00", first.HtmlBody);
        Assert.Equal(Noon, link.CustomerNotifiedAt);
        Assert.Equal(("layla@example.com", 2), (resent.Summary.CustomerEmail, _host.Emails.Sent.Count));
        Assert.Equal("payment_link_closed", closed.Code);
    }

    // ---- Paying ----

    [Fact]
    public async Task Paying_in_full_closes_the_link()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f);
        var token = TokenOf(link);

        var before = await ViewAsync(token);
        var payment = await PayAsync(token, Pay(PaymentMode.Full, 5000));
        var after = await ViewAsync(token);
        var again = await Assert.ThrowsAsync<AppException>(() => StartAsync(token, Pay(PaymentMode.Full, 5000)));
        var detail = await _host.PaymentLinks.GetAsync(link.Summary.Id, default);

        Assert.Equal((PaymentLinkStatus.Active, 5000L, false, "Marina"), (before.Status, before.AvailableMinor, before.Tips.Enabled, before.Venue.BranchName));
        Assert.Equal((PaymentStatus.Succeeded, 5000L, 0L, true), (payment.Status, payment.AmountMinor, payment.RemainingMinor, payment.FullyPaid));
        Assert.Equal((PaymentLinkStatus.Paid, 5000L, 0L), (after.Status, after.PaidMinor, after.RemainingMinor));
        Assert.Equal((409, "link_paid"), (again.StatusCode, again.Code));
        Assert.Equal((PaymentLinkStatus.Paid, Noon, 1), (detail.Summary.Status, detail.Summary.PaidAt, detail.Summary.PaymentCount));
        Assert.Contains(detail.Timeline, e => e.Text == "Paid in full; the link closed" && e.Code == "link_paid");
        var paidStep = JsonSerializer.SerializeToElement(Assert.Single(detail.Timeline, e => e.Code == "payment_paid").Params);
        Assert.Equal((payment.Reference, 5000L), (paidStep.GetProperty("reference").GetString(), paidStep.GetProperty("amountMinor").GetInt64()));

        var ledger = await _host.Db.LedgerEntries.SingleAsync();
        Assert.Equal((LedgerEntryType.BillPayment, 5000L, (Guid?)null), (ledger.Type, ledger.AmountMinor, ledger.CheckId));
        var notice = Assert.Single(_host.PaymentNotifier.LinkPayments);
        Assert.Equal((link.Summary.Code, 0L, true), (notice.Code, notice.RemainingMinor, notice.FullyPaid));
    }

    [Fact]
    public async Task A_split_link_can_be_paid_in_parts_and_equal_shares_cover_it_exactly()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f, amount: 10_000, split: true);
        var token = TokenOf(link);

        var first = await PayAsync(token, Pay(PaymentMode.Equal, 3334, people: 3, shares: 1)); // 100.00 / 3 rounds up
        var partly = await ViewAsync(token);
        var rest = await PayAsync(token, Pay(PaymentMode.Equal, 6666, people: 3, shares: 2)); // capped at what's left
        var paid = await ViewAsync(token);

        Assert.Equal((3334L, 3, 1), (first.BillAmountMinor, first.SplitPeople!.Value, first.Shares!.Value));
        Assert.Equal((PaymentLinkStatus.Active, 6666L), (partly.Status, partly.RemainingMinor));
        Assert.Equal((6666L, true), (rest.BillAmountMinor, rest.FullyPaid));
        Assert.Equal((PaymentLinkStatus.Paid, 10_000L), (paid.Status, paid.PaidMinor));
    }

    [Fact]
    public async Task A_custom_amount_can_be_paid_but_never_more_than_is_left()
    {
        var f = await CreateAsync();
        var token = TokenOf(await LinkAsync(f, amount: 10_000, split: true));

        await PayAsync(token, Pay(PaymentMode.Custom, 2500, amount: 2500));
        var tooMuch = await Assert.ThrowsAsync<AppException>(() => StartAsync(token, Pay(PaymentMode.Custom, 8000, amount: 8000)));
        var view = await ViewAsync(token);

        Assert.Equal((409, "amount_too_high"), (tooMuch.StatusCode, tooMuch.Code));
        Assert.Equal(7500L, ((PublicPaymentLinkDto)tooMuch.Details!).AvailableMinor);
        Assert.Equal((PaymentLinkStatus.Active, 2500L, 7500L), (view.Status, view.PaidMinor, view.RemainingMinor));
    }

    [Fact]
    public async Task Splitting_and_tips_are_only_offered_when_the_merchant_asked_for_them()
    {
        var f = await CreateAsync();
        var plain = TokenOf(await LinkAsync(f));
        var withTip = TokenOf(await LinkAsync(f, tip: true));

        var split = await Assert.ThrowsAsync<AppException>(() => StartAsync(plain, Pay(PaymentMode.Custom, 1000, amount: 1000)));
        var tipped = await Assert.ThrowsAsync<AppException>(() => StartAsync(plain, Pay(PaymentMode.Full, 5000, tip: 500)));
        var offered = await ViewAsync(withTip);
        var payment = await PayAsync(withTip, Pay(PaymentMode.Full, 5000, tip: 500));
        var after = await ViewAsync(withTip);

        Assert.Equal("split_disabled", split.Code);
        Assert.Equal("tips_disabled", tipped.Code);
        Assert.Equal((true, 1000), (offered.Tips.Enabled, offered.Tips.MostCommonBasisPoints!.Value));
        Assert.Equal((5500L, 500L), (payment.AmountMinor, payment.TipMinor));
        Assert.Equal((PaymentLinkStatus.Paid, 5000L), (after.Status, after.PaidMinor)); // the tip doesn't count towards the amount
        Assert.Equal(500L, (await _host.Db.LedgerEntries.SingleAsync(e => e.Type == LedgerEntryType.Tip)).AmountMinor);
    }

    [Fact]
    public async Task A_payment_in_progress_holds_its_amount_until_it_is_cancelled_or_lapses()
    {
        var f = await CreateAsync();
        var token = TokenOf(await LinkAsync(f, amount: 10_000, split: true));

        var first = await StartAsync(token, Pay(PaymentMode.Custom, 6000, amount: 6000));
        var during = await ViewAsync(token);
        var blocked = await Assert.ThrowsAsync<AppException>(() => StartAsync(token, Pay(PaymentMode.Custom, 5000, amount: 5000)));
        await AsCustomer(() => _host.LinkCheckout.CancelPaymentAsync(token, first.Id, default));
        var afterCancel = await ViewAsync(token);
        await StartAsync(token, Pay(PaymentMode.Full, 10_000));
        _host.Clock.Advance(Payment.InFlightLifetime);
        var afterLapse = await ViewAsync(token);

        Assert.NotNull(first.CheckoutUrl);
        Assert.Equal((true, 4000L), (during.PaymentInProgress, during.AvailableMinor));
        Assert.Equal("amount_too_high", blocked.Code);
        Assert.Equal((false, 10_000L), (afterCancel.PaymentInProgress, afterCancel.AvailableMinor));
        Assert.Equal((false, 10_000L), (afterLapse.PaymentInProgress, afterLapse.AvailableMinor));
    }

    [Fact]
    public async Task Two_customers_paying_the_whole_link_at_the_same_moment_cannot_both_pay_it()
    {
        var f = await CreateAsync();
        var token = TokenOf(await LinkAsync(f));
        using var other = _host.NewScope();
        LinkPaymentDto? otherPayment = null;

        _host.CurrentTenant = null;
        _host.SaveHook.BeforeNextSave = async () =>
            otherPayment = await other.LinkCheckout.CreatePaymentAsync(token, Pay(PaymentMode.Full, 5000), default);
        var ex = await Assert.ThrowsAsync<AppException>(() => _host.LinkCheckout.CreatePaymentAsync(token, Pay(PaymentMode.Full, 5000), default));

        Assert.NotNull(otherPayment);
        Assert.Equal((409, "payment_in_progress"), (ex.StatusCode, ex.Code));
        Assert.Single(await _host.Db.Payments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task A_retried_payment_request_returns_the_same_payment()
    {
        var f = await CreateAsync();
        var token = TokenOf(await LinkAsync(f));
        var requestId = Guid.NewGuid();

        var first = await StartAsync(token, Pay(PaymentMode.Full, 5000, requestId: requestId));
        var again = await StartAsync(token, Pay(PaymentMode.Full, 5000, requestId: requestId));

        Assert.Equal(first.Id, again.Id);
        Assert.Single(await _host.Db.Payments.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task A_declined_payment_leaves_the_link_open()
    {
        var f = await CreateAsync();
        var token = TokenOf(await LinkAsync(f));

        var declined = await PayAsync(token, Pay(PaymentMode.Full, 5000), PaymentStatus.Failed);
        var view = await ViewAsync(token);

        Assert.Equal((PaymentStatus.Failed, "Declined"), (declined.Status, declined.FailureReason));
        Assert.Equal((PaymentLinkStatus.Active, 5000L), (view.Status, view.AvailableMinor));
        Assert.Empty(_host.PaymentNotifier.LinkPayments);
    }

    // ---- Expiry, deactivation and bad links ----

    [Fact]
    public async Task An_expired_link_takes_no_new_payments()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f, validFor: 2);
        var token = TokenOf(link);

        _host.Clock.Advance(TimeSpan.FromHours(2));
        var view = await ViewAsync(token);
        var ex = await Assert.ThrowsAsync<AppException>(() => StartAsync(token, Pay(PaymentMode.Full, 5000)));
        var expired = await _host.PaymentLinks.ListAsync(new PaymentLinkQuery(f.Venue.Id, PaymentLinkFilter.Expired), default);

        Assert.Equal((PaymentLinkStatus.Expired, 0L), (view.Status, view.AvailableMinor));
        Assert.Equal((409, "link_expired"), (ex.StatusCode, ex.Code));
        Assert.Equal((1, 0), (expired.Counts.Expired, expired.Counts.Active));
        Assert.Equal(link.Summary.Id, Assert.Single(expired.Items).Id);
    }

    [Fact]
    public async Task A_deactivated_link_takes_no_more_money_even_from_a_payment_under_way()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f);
        var token = TokenOf(link);
        var started = await StartAsync(token, Pay(PaymentMode.Full, 5000));

        var cancelled = await _host.PaymentLinks.CancelAsync(link.Summary.Id, f.Staff.Id, default);
        var view = await ViewAsync(token);
        var ex = await Assert.ThrowsAsync<AppException>(() => StartAsync(token, Pay(PaymentMode.Full, 5000)));
        var awaiting = await _host.Payments.IsAwaitingProviderAsync("sandbox", $"sbx_{started.Id:N}", default);

        Assert.Equal((PaymentLinkStatus.Cancelled, "Sara"), (cancelled.Summary.Status, cancelled.CancelledBy));
        Assert.Equal(PaymentLinkStatus.Cancelled, view.Status);
        Assert.Equal("link_cancelled", ex.Code);
        Assert.False(awaiting);
        Assert.Contains(await _host.Db.AuditEntries.ToListAsync(), a => a.Action == "payment_link.cancelled");
    }

    [Fact]
    public async Task A_paid_link_cannot_be_deactivated()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f);
        await PayAsync(TokenOf(link), Pay(PaymentMode.Full, 5000));

        var ex = await Assert.ThrowsAsync<AppException>(() => _host.PaymentLinks.CancelAsync(link.Summary.Id, f.Staff.Id, default));

        Assert.Equal((409, "payment_link_paid"), (ex.StatusCode, ex.Code));
    }

    [Theory]
    [InlineData("not-a-token")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task An_unknown_or_malformed_token_is_not_found(string token)
    {
        await CreateAsync();

        var ex = await Assert.ThrowsAsync<AppException>(() => ViewAsync(token));

        Assert.Equal((404, "link_not_found"), (ex.StatusCode, ex.Code));
    }

    [Fact]
    public async Task A_link_token_is_never_the_same_as_a_qr_token_for_the_same_id()
    {
        var id = Guid.NewGuid();

        Assert.NotEqual(_host.QrTokens.CreateToken(id), _host.QrTokens.CreatePaymentLinkToken(id));
        Assert.Equal(_host.QrTokens.CreatePaymentLinkToken(id), _host.QrTokens.CreatePaymentLinkToken(id));
    }

    // ---- Tracking: list, transactions, refunds, receipts ----

    [Fact]
    public async Task Links_can_be_filtered_searched_and_exported()
    {
        var f = await CreateAsync();
        var paid = await LinkAsync(f, amount: 7500, reference: "Wedding deposit");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var cancelled = await LinkAsync(f, amount: 2000, reference: null, email: "omar@example.com");
        _host.Clock.Advance(TimeSpan.FromMinutes(1));
        var open = await LinkAsync(f, amount: 5000, reference: "BOOK-42");
        await PayAsync(TokenOf(paid), Pay(PaymentMode.Full, 7500));
        await _host.PaymentLinks.CancelAsync(cancelled.Summary.Id, f.Staff.Id, default);

        var all = await _host.PaymentLinks.ListAsync(new PaymentLinkQuery(f.Venue.Id), default);
        var byReference = await _host.PaymentLinks.ListAsync(new PaymentLinkQuery(f.Venue.Id, Search: "wedding"), default);
        var byAmount = await _host.PaymentLinks.ListAsync(new PaymentLinkQuery(f.Venue.Id, Search: "50"), default);
        var byCode = await _host.PaymentLinks.ListAsync(new PaymentLinkQuery(f.Venue.Id, Search: open.Summary.Code), default);
        var byEmail = await _host.PaymentLinks.ListAsync(new PaymentLinkQuery(f.Venue.Id, Search: "omar@"), default);
        var active = await _host.PaymentLinks.ListAsync(new PaymentLinkQuery(f.Venue.Id, PaymentLinkFilter.Active), default);
        var csv = await _host.PaymentLinks.ExportCsvAsync(new PaymentLinkQuery(f.Venue.Id), default);

        Assert.Equal(new PaymentLinkCountsDto(3, 1, 1, 0, 1), all.Counts);
        Assert.Equal([open.Summary.Id, cancelled.Summary.Id, paid.Summary.Id], all.Items.Select(i => i.Id)); // newest first
        Assert.Equal((7500L, 1), (all.Items[2].PaidMinor, all.Items[2].PaymentCount));
        Assert.Equal(paid.Summary.Id, Assert.Single(byReference.Items).Id);
        Assert.Equal(open.Summary.Id, Assert.Single(byAmount.Items).Id);
        Assert.Equal(open.Summary.Id, Assert.Single(byCode.Items).Id);
        Assert.Equal(cancelled.Summary.Id, Assert.Single(byEmail.Items).Id);
        Assert.Equal(open.Summary.Id, Assert.Single(active.Items).Id);
        Assert.StartsWith("Created,Link,Reference,Status,Currency,Amount,Paid,Tips,Payments,Expires,Paid at,Customer e-mail\r\n", csv);
        Assert.Contains($"{paid.Summary.Code},\"Wedding deposit\",Paid,AED,75.00,75.00,0.00,1,", csv);
    }

    [Fact]
    public async Task Link_payments_are_transactions_that_can_be_refunded_and_count_as_revenue()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f);
        var payment = await PayAsync(TokenOf(link), Pay(PaymentMode.Full, 5000));
        var manager = new StaffUser(f.TenantId, "lina@demo.test", "Lina", StaffRole.Manager);
        _host.Db.StaffUsers.Add(manager);
        await _host.Db.SaveChangesAsync();

        var found = await _host.Transactions.ListAsync(new TransactionQuery(f.Venue.Id, Search: link.Summary.Code), default);
        var detail = await _host.Transactions.GetAsync(payment.Id, default);
        var pending = await _host.Transactions.RefundAsync(payment.Id, new RefundRequest(Guid.NewGuid(), 1000, "Booking shortened"), manager.Id, default);
        await WebhookAsync(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", PaymentStatus.Succeeded, null, null, null,
            $"sbxr_{pending.Refunds.Single().Id:N}"));
        var refunded = await _host.Transactions.GetAsync(payment.Id, default);
        var overview = await _host.Overview.GetAsync(f.Venue.Id, OverviewPeriod.Today, null, default);
        var after = await _host.PaymentLinks.GetAsync(link.Summary.Id, default);

        var row = Assert.Single(found.Items);
        Assert.Equal((link.Summary.Code, "", 5000L), (row.PaymentLinkCode, row.LocationLabel, row.AmountMinor));
        Assert.Equal((link.Summary.Id, "BOOK-42"), (detail.PaymentLinkId!.Value, detail.PaymentLinkReference));
        Assert.Empty(detail.Orders);
        Assert.Equal(("link", $"Payment link {link.Summary.Code} created by Sara for AED 50.00"), (detail.Timeline[0].Kind, detail.Timeline[0].Text));
        Assert.Equal((TransactionState.PartiallyRefunded, 1000L), (refunded.Summary.State, refunded.Summary.RefundedMinor));
        Assert.Equal((5000L, 1000L), (overview.Selected.RevenueMinor, overview.Selected.RefundsMinor));
        Assert.Equal((PaymentLinkStatus.Paid, TransactionState.PartiallyRefunded), (after.Summary.Status, after.Payments.Single().State)); // a refund doesn't reopen the link
    }

    [Fact]
    public async Task The_customer_can_get_a_receipt_once_the_payment_has_gone_through()
    {
        var f = await CreateAsync();
        var link = await LinkAsync(f, amount: 9000, split: true);
        var token = TokenOf(link);
        var started = await StartAsync(token, Pay(PaymentMode.Custom, 3000, amount: 3000));

        var early = await Assert.ThrowsAsync<AppException>(() =>
            AsCustomer(async () => { await _host.LinkCheckout.SendReceiptAsync(token, started.Id, "omar@example.com", null, default); return true; }));
        await WebhookAsync(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{started.Id:N}", PaymentStatus.Succeeded, "Visa", "4242", null));
        await AsCustomer(async () => { await _host.LinkCheckout.SendReceiptAsync(token, started.Id, "omar@example.com", null, default); return true; });

        var receipt = Assert.Single(_host.Emails.Sent);
        Assert.Equal("not_paid", early.Code);
        Assert.Equal("omar@example.com", receipt.To);
        Assert.Contains($"Payment link {link.Summary.Code} · Reference BOOK-42", receipt.TextBody);
        Assert.Contains("AED 30.00", receipt.TextBody);
        Assert.Contains("Visa •••• 4242", receipt.TextBody);
    }

    [Fact]
    public async Task A_payment_cannot_be_read_through_another_link()
    {
        var f = await CreateAsync();
        var one = TokenOf(await LinkAsync(f));
        var two = TokenOf(await LinkAsync(f));
        var payment = await StartAsync(one, Pay(PaymentMode.Full, 5000));

        var ex = await Assert.ThrowsAsync<AppException>(() => AsCustomer(() => _host.LinkCheckout.GetPaymentAsync(two, payment.Id, default)));

        Assert.Equal((404, "payment_not_found"), (ex.StatusCode, ex.Code));
    }
}
