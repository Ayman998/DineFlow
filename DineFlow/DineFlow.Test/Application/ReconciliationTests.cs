using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Dashboard;
using DineFlow.Application.Finance;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Payments;
using DineFlow.Application.Pos;
using DineFlow.Application.Tables;
using DineFlow.Application.Transactions;
using DineFlow.Application.Venues;
using DineFlow.Domain.Finance;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

/// <summary>Settlement imports, daily reconciliation, the discrepancy queue and the ledger export (blueprint E12, E14), in Amman (JOD).</summary>
public class ReconciliationTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    /// <summary>Wednesday 10 March 2027, 12:00 in Amman (UTC+3).</summary>
    private static readonly DateTimeOffset Noon = new(2027, 3, 10, 9, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Wednesday = new(2027, 3, 10);

    private sealed record Fixture(Guid TenantId, VenueDto Venue, ItemDto Mansaf, LocationDto Table4, StaffUser Manager);

    private async Task<Fixture> CreateAsync()
    {
        _host.Clock.SetUtcNow(Noon);
        var tenant = await _host.CreateTenantAsync("Hashem Kitchen");
        var venue = await _host.Venues.CreateAsync(new VenueSettingsRequest("Abdoun", "JOD", "Asia/Amman", "ar", null, "#0B3B38", true, true), default);
        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, null, "Mains", null), default);
        var mansaf = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Mansaf", null, null, null, 12_500, null, true, [], [], []), default);
        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "4"), default);
        var manager = new StaffUser(tenant.Id, "rami@demo.test", "Rami", StaffRole.Manager);
        _host.Db.StaffUsers.Add(manager);
        await _host.Db.SaveChangesAsync();
        return new Fixture(tenant.Id, venue, mansaf, table, manager);
    }

    /// <summary>A guest at table 4 orders mansaf and pays it all (plus tip), confirmed by the sandbox.</summary>
    private async Task<Payment> PayAsync(Fixture f, int portions = 1, long tip = 0)
    {
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        var session = (await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(f.Table4.Qr!.Url), null, default)).Context.SessionId;
        _host.CurrentTenant = tenant;
        var lines = new List<CartLineRequest> { new(f.Mansaf.Id, portions, [], null) };
        var quote = await _host.CartQuotes.QuoteAsync(session, new CartRequest(lines), default);
        await _host.GuestOrders.PlaceAsync(session, new PlaceOrderRequest(Guid.NewGuid(), lines, null, quote.TotalMinor), default);
        var bill = await _host.Bills.GetForSessionAsync(session, default);
        var payment = await _host.Payments.CreateAsync(new CreatePaymentRequest(session, Guid.NewGuid(), PaymentMethod.Card, PaymentMode.Full,
            null, null, tip, null, bill.AvailableMinor, true), default);
        await WebhookAsync(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", PaymentStatus.Succeeded, "Visa", "4242", null));
        return await _host.Db.Payments.SingleAsync(p => p.Id == payment.Id);
    }

    private async Task WebhookAsync(ProviderWebhookEvent webhook)
    {
        var (body, signature) = _host.Sandbox.CreateWebhook(webhook);
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        try { await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default); }
        finally { _host.CurrentTenant = tenant; }
    }

    private Task<SettlementImportResultDto> ImportAsync(Fixture f, string csv) =>
        _host.Finance.ImportAsync(f.Venue.Id, "meps-settlement.csv", csv, f.Manager.Id, default);

    private const string Header = "Batch ID,Settlement Date,Transaction Date,Transaction Type,Merchant Order ID,Reference,Gross Amount,Commission,Currency\n";

    private static string Row(string batch, Payment p, string amount, string fee = "0.300", string type = "SALE", string date = "2027-03-10") =>
        $"{batch},2027-03-11,{date},{type},{p.ProviderPaymentId},{p.Reference},{amount},{fee},JOD\n";

    private Task<DiscrepancyPageDto> QueueAsync(Fixture f, DiscrepancyStatus? status = DiscrepancyStatus.Open) =>
        _host.Finance.DiscrepanciesAsync(new DiscrepancyQuery(f.Venue.Id, status), default);

    // ---- Reading settlement reports ----

    [Fact]
    public void Settlement_reports_are_read_with_the_acquirers_own_column_names_and_JOD_decimals()
    {
        var rows = SettlementCsv.Parse("""
            "Payout ID";"Value Date";"Txn Date";"Type";"Order ID";"Gross";"MDR"
            "B-77";"11/03/2027";"10/03/2027";"Purchase";"df-1";"1,250.500";"28.137"
            "B-77";"11/03/2027";"10/03/2027";"Refund";"df-1";"-5.000";""
            """, "JOD");

        Assert.Equal(2, rows.Count);
        Assert.Equal(("B-77", new DateOnly(2027, 3, 11), new DateOnly(2027, 3, 10), SettlementLineKind.Payment, "df-1", 1_250_500L, 28_137L),
            (rows[0].BatchRef, rows[0].PayoutDate, rows[0].TransactionDate, rows[0].Kind, rows[0].OrderId, rows[0].AmountMinor, rows[0].FeeMinor));
        Assert.Equal((SettlementLineKind.Refund, 5_000L, 0L), (rows[1].Kind, rows[1].AmountMinor, rows[1].FeeMinor));
    }

    [Theory]
    [InlineData("Batch,Payout date,Amount\nB1,2027-03-11,10.000\n", "The file needs an \"order id\" or \"reference\" column.")]
    [InlineData("Batch,Payout date,Order id,Amount\nB1,2027-03-11,df-1,10.0005\n", "Line 2: \"10.0005\" has more decimals than the currency allows.")]
    [InlineData("Batch,Payout date,Order id,Type,Amount\nB1,2027-03-11,df-1,Chargeback,10\n", "Line 2: \"CHARGEBACK\" isn't a type DineFlow knows (use payment, refund or fee).")]
    [InlineData("Batch,Payout date,Order id,Amount,Currency\nB1,2027-03-11,df-1,10,AED\n", "Line 2: the currency is AED, but this venue uses JOD.")]
    [InlineData("Batch,Payout date,Order id,Amount\nB1,tomorrow,df-1,10\n", "Line 2: \"tomorrow\" isn't a date (use yyyy-MM-dd).")]
    public void A_report_that_cant_be_read_says_where(string csv, string message)
    {
        var ex = Assert.Throws<FormatException>(() => SettlementCsv.Parse(csv, "JOD"));

        Assert.Equal(message, ex.Message);
    }

    // ---- Importing and matching ----

    [Fact]
    public async Task An_imported_settlement_matches_the_days_payments_and_the_day_balances()
    {
        var f = await CreateAsync();
        var first = await PayAsync(f, tip: 1_500);      // 12.500 + 1.500 tip = 14.000
        var second = await PayAsync(f, portions: 2);    // 25.000

        var result = await ImportAsync(f, Header + Row("MEPS-0311", first, "14.000", "0.315") + Row("MEPS-0311", second, "25.000", "0.563"));
        var run = (await _host.Finance.RunsAsync(f.Venue.Id, Wednesday, Wednesday, default)).Single();
        var settlement = Assert.Single(result.Settlements);
        var detail = await _host.Finance.SettlementAsync(settlement.Id, default);

        Assert.Equal(("MEPS-0311", 39_000L, 878L, 38_122L, 2), (settlement.BatchRef, settlement.GrossMinor, settlement.FeesMinor, settlement.NetMinor, settlement.Transactions));
        Assert.Equal((2, 39_000L, 1_500L, 2, 39_000L, 878L, 2, 0), (run.Payments, run.LedgerGrossMinor, run.LedgerTipsMinor, run.Settled,
            run.SettledGrossMinor, run.SettledFeesMinor, run.Matched, run.OpenDiscrepancies));
        Assert.All(detail.Lines, l => Assert.NotNull(l.PaymentReference));
        Assert.Contains(result.Days, d => d == Wednesday);
        Assert.Contains(await _host.Db.AuditEntries.ToListAsync(), a => a.Action == "settlement.imported");
    }

    [Fact]
    public async Task The_same_batch_cant_be_imported_twice()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f);
        var csv = Header + Row("MEPS-0311", p, "12.500");
        await ImportAsync(f, csv);

        var ex = await Assert.ThrowsAsync<AppException>(() => ImportAsync(f, csv));

        Assert.Equal(("settlement_exists", "Already imported: MEPS-0311. Nothing was changed."), (ex.Code, ex.Message));
        Assert.Single(await _host.Db.Settlements.ToListAsync());
    }

    [Fact]
    public async Task An_unsettled_payment_waits_three_days_then_becomes_a_difference_that_clears_when_the_settlement_arrives()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f);

        await _host.Reconciler.ReconcileAsync(f.Venue.Id, Wednesday, default);
        var waiting = (await _host.Finance.RunsAsync(f.Venue.Id, Wednesday, Wednesday, default)).Single();
        _host.Clock.Advance(TimeSpan.FromDays(3));
        await _host.Reconciler.ReconcileAsync(f.Venue.Id, Wednesday, default);
        var missing = Assert.Single((await QueueAsync(f)).Items);
        await ImportAsync(f, Header + Row("MEPS-0313", p, "12.500"));
        var cleared = Assert.Single((await QueueAsync(f, null)).Items);

        Assert.Equal((1, 0), (waiting.AwaitingSettlement, waiting.OpenDiscrepancies));
        Assert.Equal((DiscrepancyKind.NotSettled, p.Reference, 12_500L, 0L), (missing.Kind, missing.Reference, missing.ExpectedMinor, missing.ActualMinor));
        Assert.Equal($"{p.Reference} (JOD 12.500) was paid on 10 Mar but isn't in any settlement from the acquirer.", missing.Details);
        Assert.Equal(DiscrepancyStatus.Cleared, cleared.Status);
        Assert.Equal(0, (await QueueAsync(f)).Open);
    }

    [Fact]
    public async Task A_settled_amount_that_differs_is_flagged_and_stays_resolved_once_someone_accepts_it()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f);
        await ImportAsync(f, Header + Row("MEPS-0311", p, "12.000"));

        var mismatch = Assert.Single((await QueueAsync(f)).Items);
        var resolved = await _host.Finance.ResolveAsync(mismatch.Id, "Short-paid by MEPS, claim #4471 raised", f.Manager.Id, default);
        await _host.Reconciler.ReconcileAsync(f.Venue.Id, Wednesday, default);
        var after = Assert.Single((await QueueAsync(f, null)).Items);

        Assert.Equal((DiscrepancyKind.AmountMismatch, 12_500L, 12_000L, -500L), (mismatch.Kind, mismatch.ExpectedMinor, mismatch.ActualMinor, mismatch.DifferenceMinor));
        Assert.Equal((DiscrepancyStatus.Resolved, "Rami", "Short-paid by MEPS, claim #4471 raised"), (resolved.Status, resolved.ResolvedBy, resolved.Note));
        Assert.Equal(DiscrepancyStatus.Resolved, after.Status); // the next run doesn't reopen it
        Assert.Equal(0, (await _host.Finance.RunsAsync(f.Venue.Id, Wednesday, Wednesday, default)).Single().OpenDiscrepancies);
        Assert.Contains(await _host.Db.AuditEntries.ToListAsync(), a => a.Action == "reconciliation.resolved");
    }

    [Fact]
    public async Task Money_the_acquirer_settled_for_an_unknown_order_or_twice_is_flagged()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f);

        await ImportAsync(f, Header + Row("MEPS-0311", p, "12.500") + Row("MEPS-0311", p, "12.500")
            + "MEPS-0311,2027-03-11,2027-03-10,SALE,df-unknown,,9.000,0.200,JOD\n");
        var kinds = (await QueueAsync(f)).Items.Select(d => (d.Kind, d.Reference)).OrderBy(k => k.Kind).ToList();

        Assert.Equal([(DiscrepancyKind.UnknownToDineFlow, "df-unknown"), (DiscrepancyKind.SettledTwice, p.Reference)], kinds);
    }

    [Fact]
    public async Task Refunds_must_appear_in_the_settlements_for_the_same_amount()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f);
        var refund = await _host.Transactions.RefundAsync(p.Id, new RefundRequest(Guid.NewGuid(), 2_500, "Dish returned"), f.Manager.Id, default);
        await WebhookAsync(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", p.ProviderPaymentId!, PaymentStatus.Succeeded, null, null, null,
            $"sbxr_{refund.Refunds.Single().Id:N}"));

        await ImportAsync(f, Header + Row("MEPS-0311", p, "12.500") + Row("MEPS-0311", p, "2.000", "0", "REFUND"));
        var mismatch = Assert.Single((await QueueAsync(f)).Items);

        Assert.Equal((DiscrepancyKind.RefundMismatch, 2_500L, 2_000L), (mismatch.Kind, mismatch.ExpectedMinor, mismatch.ActualMinor));
        var settlement = await _host.Db.Settlements.SingleAsync();
        Assert.Equal((12_500L, 2_000L, 300L, 10_200L), (settlement.GrossMinor, settlement.RefundsMinor, settlement.FeesMinor, settlement.NetMinor));
    }

    [Fact]
    public async Task A_payment_that_never_reached_the_POS_is_a_difference()
    {
        var f = await CreateAsync();
        await _host.Pos.SaveConnectionAsync(f.Venue.Id, new PosConnectionRequest("simulator", null, null, true, true), f.Manager.Id, default);
        var p = await PayAsync(f); // table 4 isn't mapped, so the POS refuses it
        _host.CurrentTenant = null;
        await _host.PosDispatcher.RunDueAsync(default);
        _host.CurrentTenant = f.TenantId;

        await ImportAsync(f, Header + Row("MEPS-0311", p, "12.500"));
        var notInPos = Assert.Single((await QueueAsync(f)).Items);

        Assert.Equal(DiscrepancyKind.NotInPos, notInPos.Kind);
        Assert.StartsWith($"{p.Reference} was refused by the POS: Table 4 isn't mapped", notInPos.Details);
    }

    // ---- Sandbox payouts, overview and export ----

    [Fact]
    public async Task The_sandbox_pays_out_with_its_fee_and_everything_matches()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f);

        var payout = await _host.Finance.SandboxPayoutAsync(f.Venue.Id, f.Manager.Id, default);
        var again = await Assert.ThrowsAsync<AppException>(() => _host.Finance.SandboxPayoutAsync(f.Venue.Id, f.Manager.Id, default));
        var run = (await _host.Finance.RunsAsync(f.Venue.Id, Wednesday, Wednesday, default)).Single();

        var settlement = Assert.Single(payout.Settlements);
        // 2.25% of JOD 12.500 = 0.281 (rounded) + 0.100 fixed = 0.381.
        Assert.Equal((12_500L, 381L, 12_119L, SettlementStatus.Paid), (settlement.GrossMinor, settlement.FeesMinor, settlement.NetMinor, settlement.Status));
        Assert.Equal("nothing_to_settle", again.Code);
        Assert.Equal((1, 0), (run.Matched, run.OpenDiscrepancies));
        Assert.Equal(p.Id, (await _host.Db.SettlementLines.SingleAsync()).PaymentId);
    }

    [Fact]
    public async Task Open_differences_show_on_the_overview()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f);
        await ImportAsync(f, Header + Row("MEPS-0311", p, "11.000"));

        var overview = await _host.Overview.GetAsync(f.Venue.Id, OverviewPeriod.Today, null, default);

        var item = Assert.Single(overview.Attention, a => a.Kind == "reconciliation");
        Assert.Equal("1 reconciliation difference", item.Title);
    }

    [Fact]
    public async Task The_ledger_export_carries_the_settlement_batch_and_fee()
    {
        var f = await CreateAsync();
        var p = await PayAsync(f, tip: 1_000);
        await ImportAsync(f, Header + Row("MEPS-0311", p, "13.500", "0.304"));

        var csv = await _host.Finance.LedgerCsvAsync(f.Venue.Id, Wednesday, Wednesday, default);

        Assert.StartsWith("Date,Time,Type,Payment,Where,Amount,Currency,Settlement batch,Payout date,Fee\r\n", csv);
        Assert.Contains($"2027-03-10,12:00,BillPayment,{p.Reference},Table 4,12.500,JOD,MEPS-0311,2027-03-11,0.304", csv);
        Assert.Contains($"2027-03-10,12:00,Tip,{p.Reference},Table 4,1.000,JOD,,,", csv);
    }
}
