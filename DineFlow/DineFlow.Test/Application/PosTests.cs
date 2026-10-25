using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Dashboard;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.PaymentLinks;
using DineFlow.Application.Payments;
using DineFlow.Application.Pos;
using DineFlow.Application.Tables;
using DineFlow.Application.Transactions;
using DineFlow.Application.Venues;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Pos;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using DineFlow.Infrastructure.Pos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Test.Application;

/// <summary>The POS connector framework and payment posting (blueprint E3, E9, 13.1), with the simulated POS and a fake Foodics.</summary>
public class PosTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private static readonly DateTimeOffset Noon = new(2027, 3, 10, 8, 0, 0, TimeSpan.Zero);

    private sealed record Fixture(Guid TenantId, VenueDto Venue, ItemDto Burger, LocationDto Table7, StaffUser Manager);

    private async Task<Fixture> CreateAsync(bool connect = true, bool mapTable = true)
    {
        _host.Clock.SetUtcNow(Noon);
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, null, "Mains", null), default);
        var burger = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Burger", null, null, null, 5000, null, true, [], [], []), default);
        var table7 = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7"), default);
        var manager = new StaffUser(tenant.Id, "lina@demo.test", "Lina", StaffRole.Manager);
        _host.Db.StaffUsers.Add(manager);
        await _host.Db.SaveChangesAsync();

        if (connect) await _host.Pos.SaveConnectionAsync(venue.Id, new PosConnectionRequest("simulator", null, null, true, true), manager.Id, default);
        if (mapTable) await MapTableAsync(venue, table7, manager, "T7");
        return new Fixture(tenant.Id, venue, burger, table7, manager);
    }

    private Task<PosSettingsDto> MapTableAsync(VenueDto venue, LocationDto table, StaffUser manager, string? posRef) =>
        _host.Pos.MapAsync(venue.Id, new PosMappingRequest([new PosTableMappingRequest(table.Id, posRef)], null), manager.Id, default);

    /// <summary>A guest at table 7 orders; staff accept it (unless told not to).</summary>
    private async Task<(Guid Session, OrderDto Order)> OrderAsync(Fixture f, int burgers = 1, bool accept = true)
    {
        _host.CurrentTenant = null;
        var session = (await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(f.Table7.Qr!.Url), null, default)).Context.SessionId;
        _host.CurrentTenant = f.TenantId;
        var lines = new List<CartLineRequest> { new(f.Burger.Id, burgers, [], null) };
        var quote = await _host.CartQuotes.QuoteAsync(session, new CartRequest(lines), default);
        var order = await _host.GuestOrders.PlaceAsync(session, new PlaceOrderRequest(Guid.NewGuid(), lines, null, quote.TotalMinor), default);
        if (accept) await _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted, null), default, f.Manager.Id);
        return (session, order);
    }

    private async Task<PaymentDto> PayAsync(Guid session, long tip = 0)
    {
        var bill = await _host.Bills.GetForSessionAsync(session, default);
        var payment = await _host.Payments.CreateAsync(new CreatePaymentRequest(session, Guid.NewGuid(), PaymentMethod.Card, PaymentMode.Full,
            null, null, tip, null, bill.AvailableMinor, true), default);
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}",
            PaymentStatus.Succeeded, "Visa", "4242", null));
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        try { await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default); }
        finally { _host.CurrentTenant = tenant; }
        return payment;
    }

    private async Task<List<PosCommand>> CommandsAsync() =>
        await _host.Db.PosCommands.IgnoreQueryFilters().AsNoTracking().OrderBy(c => c.CreatedAt).ToListAsync();

    private async Task<int> RunPosAsync()
    {
        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null; // the worker runs without a tenant
        try { return await _host.PosDispatcher.RunDueAsync(default); }
        finally { _host.CurrentTenant = tenant; }
    }

    // ---- Posting ----

    [Fact]
    public async Task An_accepted_order_and_its_payment_reach_the_POS_and_the_check_is_closed()
    {
        var f = await CreateAsync();
        var (session, _) = await OrderAsync(f, burgers: 2);
        var payment = await PayAsync(session, tip: 1000);

        var first = await RunPosAsync();   // order, then payment (queues the close)
        var second = await RunPosAsync();  // close
        var commands = await CommandsAsync();
        var check = Assert.Single(_host.Simulator.Checks(f.Venue.Id));
        var row = (await _host.Transactions.ListAsync(new TransactionQuery(f.Venue.Id), default)).Items.Single();
        var detail = await _host.Transactions.GetAsync(payment.Id, default);

        Assert.Equal((2, 1), (first, second));
        Assert.Equal([PosCommandKind.SendOrder, PosCommandKind.PostPayment, PosCommandKind.CloseCheck], commands.Select(c => c.Kind));
        Assert.All(commands, c => Assert.Equal(PosCommandStatus.Done, c.Status));
        Assert.Equal(("SIM-0001", "T7", 10_000L, false), (check.CheckRef, check.TableRef, check.TotalMinor, check.IsOpen));
        var tender = Assert.Single(check.Payments);
        Assert.Equal((payment.Id.ToString(), 10_000L, 1000L), (tender.ExternalPaymentId, tender.AmountMinor, tender.TipMinor));
        Assert.Equal("SIM-0001", (await _host.Db.Checks.SingleAsync()).PosCheckRef);
        Assert.Equal("Posted", row.PosStatus);
        Assert.Contains(detail.Timeline, e => e.Kind == "pos" && e.Text == "Posted to the POS (simulator, SIM-0001-T1)");
    }

    [Fact]
    public async Task While_the_POS_is_offline_the_payment_waits_and_is_posted_once_it_is_back()
    {
        var f = await CreateAsync();
        _host.Simulator.Mode = SimulatorMode.Offline;
        var (session, _) = await OrderAsync(f);
        var payment = await PayAsync(session);

        await RunPosAsync();
        var waiting = await CommandsAsync();
        var row = (await _host.Transactions.ListAsync(new TransactionQuery(f.Venue.Id), default)).Items.Single();
        _host.Clock.Advance(TimeSpan.FromMinutes(20));
        await RunPosAsync();
        var overview = await _host.Overview.GetAsync(f.Venue.Id, OverviewPeriod.Today, null, default);

        _host.Simulator.Mode = SimulatorMode.Online;
        _host.Clock.Advance(TimeSpan.FromMinutes(15));
        await RunPosAsync();
        await RunPosAsync();

        Assert.Equal((PosCommandStatus.Pending, 1, "The simulated POS is offline."), (waiting[0].Status, waiting[0].Attempts, waiting[0].LastError));
        Assert.Equal(0, waiting[1].Attempts); // the payment waits behind its order
        Assert.Equal("Pending", row.PosStatus);
        var attention = Assert.Single(overview.Attention, a => a.Kind == "pos");
        Assert.Equal(("2 items not in the POS yet", "Waiting for the POS to come back; nobody is charged again"), (attention.Title, attention.Detail));
        Assert.All(await CommandsAsync(), c => Assert.Equal(PosCommandStatus.Done, c.Status));
        Assert.Equal(payment.Id.ToString(), Assert.Single(Assert.Single(_host.Simulator.Checks(f.Venue.Id)).Payments).ExternalPaymentId);
        Assert.Single(await _host.Db.Payments.IgnoreQueryFilters().ToListAsync()); // the guest was charged once
    }

    [Fact]
    public async Task A_tender_the_POS_took_before_timing_out_is_not_posted_again()
    {
        var f = await CreateAsync();
        var (session, _) = await OrderAsync(f);
        await RunPosAsync(); // the order is in the POS
        await PayAsync(session);
        _host.Simulator.Mode = SimulatorMode.TimeoutAfterApplying;

        await RunPosAsync();
        var uncertain = (await CommandsAsync()).Single(c => c.Kind == PosCommandKind.PostPayment);
        _host.Clock.Advance(TimeSpan.FromSeconds(30));
        await RunPosAsync();

        Assert.Equal((PosCommandStatus.Pending, true), (uncertain.Status, uncertain.Uncertain));
        var posted = (await CommandsAsync()).Single(c => c.Kind == PosCommandKind.PostPayment);
        Assert.Equal((PosCommandStatus.Done, "SIM-0001-T1"), (posted.Status, posted.ResultRef));
        Assert.Single(Assert.Single(_host.Simulator.Checks(f.Venue.Id)).Payments);
    }

    [Fact]
    public async Task An_unmapped_table_is_refused_and_holds_back_the_payment_until_it_is_fixed()
    {
        var f = await CreateAsync(mapTable: false);
        var (session, _) = await OrderAsync(f);
        await PayAsync(session);

        await RunPosAsync();
        var refused = await CommandsAsync();
        var settings = await _host.Pos.GetAsync(f.Venue.Id, default);
        var queue = await _host.Pos.CommandsAsync(new PosCommandQuery(f.Venue.Id), default);

        await MapTableAsync(f.Venue, f.Table7, f.Manager, "T7");
        await _host.Pos.RetryAsync(refused[0].Id, f.Manager.Id, default);
        await RunPosAsync();
        await RunPosAsync();

        Assert.Equal((PosCommandStatus.Failed, "Table 7 isn't mapped to a POS table. Map it under Settings → POS, then retry."),
            (refused[0].Status, refused[0].LastError));
        Assert.Equal((PosCommandStatus.Pending, 0), (refused[1].Status, refused[1].Attempts));
        Assert.Equal(1, settings.Waiting);
        Assert.Contains(queue.Items, c => c.Description == "Order #1001 · Table 7" && c.NeedsAttention);
        Assert.Contains(queue.Items, c => c.Description.StartsWith("Payment DF-") && c.Description.EndsWith("AED 50.00 · Table 7"));
        Assert.All(await CommandsAsync(), c => Assert.Equal(PosCommandStatus.Done, c.Status));
        Assert.Contains(await _host.Db.AuditEntries.ToListAsync(), a => a.Action == "pos.command.retried");
    }

    [Fact]
    public async Task Without_a_DineFlow_order_in_the_POS_a_payment_goes_to_the_tables_open_check_or_is_refused()
    {
        var f = await CreateAsync();
        var (session, _) = await OrderAsync(f, accept: false); // never sent to the POS

        await PayAsync(session);
        await RunPosAsync();

        var refused = Assert.Single(await CommandsAsync());
        Assert.Equal((PosCommandKind.PostPayment, PosCommandStatus.Failed, "The POS has no open check for this table to post the payment to."),
            (refused.Kind, refused.Status, refused.LastError));
    }

    [Fact]
    public async Task Nothing_is_sent_without_an_active_POS_and_payment_links_are_not_posted()
    {
        var f = await CreateAsync(connect: false);
        var (session, _) = await OrderAsync(f);
        await PayAsync(session);
        await _host.Pos.SaveConnectionAsync(f.Venue.Id, new PosConnectionRequest("simulator", null, null, true, true), f.Manager.Id, default);
        var link = await _host.PaymentLinks.CreateAsync(new CreatePaymentLinkRequest(f.Venue.Id, Guid.NewGuid(), 5000, null, false, false, 24,
            ValidityUnit.Hours, null), f.Manager.Id, default);
        _host.CurrentTenant = null;
        var linkPayment = await _host.LinkCheckout.CreatePaymentAsync(TestHost.TokenFromUrl(link.Summary.Url),
            new CreateLinkPaymentRequest(Guid.NewGuid(), PaymentMethod.Card, PaymentMode.Full, null, null, null, 0, 5000, true), default);
        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent($"evt_{Guid.NewGuid():N}", $"sbx_{linkPayment.Id:N}",
            PaymentStatus.Succeeded, "Visa", "4242", null));
        await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default);
        _host.CurrentTenant = f.TenantId;

        Assert.Empty(await CommandsAsync());
    }

    [Fact]
    public async Task A_paused_connection_keeps_everything_waiting()
    {
        var f = await CreateAsync();
        var (session, _) = await OrderAsync(f);
        await PayAsync(session);
        await _host.Pos.SaveConnectionAsync(f.Venue.Id, new PosConnectionRequest("simulator", null, null, false, true), f.Manager.Id, default);

        await RunPosAsync();

        var order = (await CommandsAsync())[0];
        Assert.Equal((PosCommandStatus.Pending, "The POS connection is paused or removed. It will be sent once it's active again."), (order.Status, order.LastError));
        Assert.Empty(_host.Simulator.Checks(f.Venue.Id));
    }

    [Fact]
    public async Task The_same_payment_is_queued_for_the_POS_once()
    {
        var f = await CreateAsync();
        var (session, _) = await OrderAsync(f);
        await PayAsync(session);
        var payment = await _host.Db.Payments.SingleAsync();

        await _host.PosSync.PaymentSucceededAsync(payment, default); // the same event handled again

        Assert.Single(await CommandsAsync(), c => c.Kind == PosCommandKind.PostPayment);
    }

    // ---- Connection settings ----

    [Fact]
    public async Task The_access_token_is_stored_encrypted_and_never_returned()
    {
        var f = await CreateAsync(connect: false);

        var registry = new PosConnectorRegistry([_host.Simulator, Foodics(new FakeFoodics())]);
        var pos = new PosService(_host.Db, new FixedTenant(f.TenantId), registry, _host.Secrets, _host.Clock);
        var missing = await Assert.ThrowsAsync<AppException>(() =>
            pos.SaveConnectionAsync(f.Venue.Id, new PosConnectionRequest("foodics", "branch-1", null, true, true), f.Manager.Id, default));
        var saved = await pos.SaveConnectionAsync(f.Venue.Id, new PosConnectionRequest("foodics", "branch-1", "secret-token-123", true, true), f.Manager.Id, default);
        var stored = await _host.Db.PosConnections.SingleAsync();

        Assert.Equal("token_required", missing.Code);
        Assert.True(saved.Connection!.HasAccessToken);
        Assert.DoesNotContain("secret-token-123", stored.ProtectedAccessToken);
        Assert.Equal("secret-token-123", _host.Secrets.Unprotect(stored.ProtectedAccessToken!));
        Assert.Equal(["PaymentPosting", "AutoClose", "TipPosting", "OrderCreate"], saved.Providers.Single(p => p.Provider == "foodics").Capabilities);
    }

    [Fact]
    public async Task A_health_check_records_whether_the_POS_is_reachable()
    {
        var f = await CreateAsync();

        var healthy = await _host.Pos.CheckAsync(f.Venue.Id, default);
        _host.Simulator.Mode = SimulatorMode.Offline;
        var offline = await _host.Pos.CheckAsync(f.Venue.Id, default);

        Assert.Equal((true, "Simulated POS is online · 0 open checks"), (healthy.Connection!.LastCheckHealthy!.Value, healthy.Connection.LastCheckMessage));
        Assert.Equal((false, "The simulated POS is offline."), (offline.Connection!.LastCheckHealthy!.Value, offline.Connection.LastCheckMessage));
    }

    [Fact]
    public async Task Products_can_be_mapped_to_POS_items()
    {
        var f = await CreateAsync();

        var settings = await _host.Pos.MapAsync(f.Venue.Id, new PosMappingRequest(null, [new PosProductMappingRequest(f.Burger.Id, "prod-123")]), f.Manager.Id, default);

        Assert.Equal(("Burger", "Mains", "prod-123"), (settings.Products[0].Name, settings.Products[0].Category, settings.Products[0].PosProductRef));
        Assert.Equal("T7", Assert.Single(settings.Tables).ExternalRef);
    }

    // ---- Foodics ----

    private static FoodicsConnector Foodics(FakeFoodics fake) =>
        new(new HttpClient(fake), Options.Create(new FoodicsOptions { BaseUrl = "https://foodics.test/v5", PaymentMethodId = "pm-dineflow" }));

    private static readonly PosContext FoodicsContext = new(Guid.NewGuid(), "branch-1", "token-abc");

    [Fact]
    public async Task Foodics_orders_use_mapped_products_on_the_mapped_table()
    {
        var fake = new FakeFoodics((_, _) => (HttpStatusCode.OK, """{"data":{"id":"order-9"}}"""));
        var foodics = Foodics(fake);

        var result = await foodics.CreateOrderAsync(FoodicsContext, new PosOrder(null, "table-7", "df-order-1", 1001, "AED",
            [new PosOrderLine("Burger", "prod-1", 2, 5000, 10_000, [])], 10_000, "No onions"), default);

        var (method, url, body) = Assert.Single(fake.Requests);
        Assert.Equal(("order-9", "order-9"), (result.CheckRef, result.OrderRef));
        Assert.Equal((HttpMethod.Post, "https://foodics.test/v5/orders"), (method, url));
        Assert.Equal(("branch-1", "table-7", 1, "df-order-1"), ((string)body!["branch_id"]!, (string)body["table_id"]!, (int)body["type"]!, (string)body["meta"]!["dineflow_order_id"]!));
        Assert.Equal(("prod-1", 2, 50.00m, 100.00m), ((string)body["products"]![0]!["product_id"]!, (int)body["products"]![0]!["quantity"]!,
            (decimal)body["products"]![0]!["unit_price"]!, (decimal)body["products"]![0]!["total_price"]!));
        Assert.Equal("Bearer token-abc", fake.Authorization);
    }

    [Fact]
    public async Task Foodics_payments_go_on_the_order_under_the_DineFlow_payment_method()
    {
        var fake = new FakeFoodics((_, _) => (HttpStatusCode.OK, """{"data":{"id":"pay-5"}}"""));

        var posted = await Foodics(fake).PostPaymentAsync(FoodicsContext, new PosPayment("order-9", "df-pay-1", 6250, 500, "AED", "Visa", "DF-1A2B3C4D"), default);

        var (method, url, body) = Assert.Single(fake.Requests);
        Assert.Equal("pay-5", posted.PostingRef);
        Assert.Equal((HttpMethod.Post, "https://foodics.test/v5/orders/order-9/payments"), (method, url));
        Assert.Equal(("pm-dineflow", 62.50m, 5.00m, "df-pay-1"), ((string)body!["payment_method_id"]!, (decimal)body["amount"]!,
            (decimal)body["tips"]!, (string)body["meta"]!["dineflow_payment_id"]!));
    }

    [Fact]
    public async Task Foodics_errors_are_retried_or_refused_depending_on_the_cause()
    {
        var unmapped = await Assert.ThrowsAsync<PosRejectedException>(() => Foodics(new FakeFoodics((_, _) => (HttpStatusCode.OK, "{}")))
            .CreateOrderAsync(FoodicsContext, new PosOrder(null, "t", "o", 1, "AED", [new PosOrderLine("Burger", null, 1, 5000, 5000, [])], 5000, null), default));
        var down = await Assert.ThrowsAsync<PosUnavailableException>(() => Foodics(new FakeFoodics((_, _) => (HttpStatusCode.BadGateway, """{"message":"Upstream"}""")))
            .PostPaymentAsync(FoodicsContext, new PosPayment("order-9", "p", 100, 0, "AED", "Visa", "r"), default));
        var token = await Assert.ThrowsAsync<PosRejectedException>(() => Foodics(new FakeFoodics((_, _) => (HttpStatusCode.Unauthorized, "{}")))
            .HealthCheckAsync(FoodicsContext, default));
        var invalid = await Assert.ThrowsAsync<PosRejectedException>(() => Foodics(new FakeFoodics((_, _) => (HttpStatusCode.UnprocessableEntity, """{"message":"The table is closed."}""")))
            .CloseCheckAsync(FoodicsContext, "order-9", default));

        Assert.Equal("\"Burger\" isn't mapped to a Foodics product. Map it under Settings → POS, then retry.", unmapped.Message);
        Assert.Equal(("Foodics is having trouble (502): Upstream", true), (down.Message, down.MayHaveApplied));
        Assert.Equal("Foodics didn't accept the access token. Reconnect with a new one.", token.Message);
        Assert.Equal("Foodics refused it (422): The table is closed.", invalid.Message);
    }

    [Fact]
    public async Task Foodics_check_details_report_which_DineFlow_payments_are_on_the_order()
    {
        var fake = new FakeFoodics((_, _) => (HttpStatusCode.OK, """
            {"data":{"id":"order-9","status":2,"total_price":100.0,"table":{"id":"table-7"},
              "products":[{"product":{"name":"Burger"},"quantity":2,"total_price":100.0}],
              "payments":[{"id":"pay-5","amount":62.5,"tips":5,"meta":{"dineflow_payment_id":"df-pay-1"}}]}}
            """));

        var check = await Foodics(fake).GetCheckDetailsAsync(FoodicsContext, "order-9", default);

        Assert.Equal(("order-9", "table-7", 10_000L, 6250L, true), (check.CheckRef, check.TableRef, check.TotalMinor, check.PaidMinor, check.IsOpen));
        Assert.Equal(new PosCheckPayment("pay-5", "df-pay-1", 6250, 500), Assert.Single(check.Payments));
    }

    /// <summary>Answers Foodics requests from a function and records them.</summary>
    private sealed class FakeFoodics(Func<HttpMethod, string, (HttpStatusCode Status, string Body)>? respond = null) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, JsonNode? Body)> Requests { get; } = [];
        public string? Authorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct));
            Requests.Add((request.Method, request.RequestUri!.ToString(), body));
            Authorization = request.Headers.Authorization?.ToString();
            var (status, text) = (respond ?? ((_, _) => (HttpStatusCode.OK, "{}")))(request.Method, request.RequestUri.ToString());
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class FixedTenant(Guid tenantId) : ITenantContext
    {
        public Guid? TenantId => tenantId;
    }
}
