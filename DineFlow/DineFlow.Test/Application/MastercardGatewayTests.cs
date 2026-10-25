using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Payments;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Test.Application;

/// <summary>Mastercard Gateway (MPGS) Hosted Checkout, against a fake gateway, for a venue in Amman paying in JOD (3 decimals).</summary>
public class MastercardGatewayTests : IDisposable
{
    private readonly TestHost _host = new();
    private readonly FakeGateway _gateway = new();

    public void Dispose() => _host.Dispose();

    private const string Secret = "whsec-0123456789abcdef";
    private const string SessionId = "SESSION0002561575526H6901242G59";
    private static readonly DateTimeOffset Noon = new(2027, 3, 10, 9, 0, 0, TimeSpan.Zero);

    private static readonly MastercardGatewayOptions Options = new()
    {
        GatewayUrl = "https://test-gateway.mastercard.test",
        ApiVersion = 100,
        MerchantId = "TESTDINEFLOWJO",
        ApiPassword = "api-password",
        WebhookSecret = Secret,
        NotificationUrl = "https://api.dineflow.test/v1/webhooks/payments/mastercard"
    };

    private MastercardGatewayProvider Provider() =>
        new(new HttpClient(_gateway), Microsoft.Extensions.Options.Options.Create(Options),
            Microsoft.Extensions.Options.Options.Create(new GuestOptions { AppBaseUrl = "https://guest.test" }));

    private PaymentService Payments(MastercardGatewayProvider provider) =>
        new(_host.Db, _host.Bills, _host.Promotions, provider, _host.PaymentNotifier, _host.Emails, _host.Alerts, _host.PosSync,
            Microsoft.Extensions.Options.Options.Create(new PaymentOptions()),
            Microsoft.Extensions.Options.Options.Create(new GuestOptions { AppBaseUrl = "https://guest.test" }), _host.Clock);

    /// <summary>A venue in Amman (JOD), a guest at table 4 with a JOD 12.500 order.</summary>
    private async Task<Guid> GuestWithOrderAsync()
    {
        _host.Clock.SetUtcNow(Noon);
        await _host.CreateTenantAsync("Hashem Kitchen");
        var venue = await _host.Venues.CreateAsync(new VenueSettingsRequest("Abdoun", "JOD", "Asia/Amman", "ar", null, "#0B3B38", true, true), default);
        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(menu.Id, null, "Mains", null), default);
        var mansaf = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Mansaf", "منسف", null, null, 12_500, null, true, [], [], []), default);
        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "4"), default);

        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null;
        var session = (await _host.Guests.ResolveQrAsync(TestHost.TokenFromUrl(table.Qr!.Url), null, default)).Context.SessionId;
        _host.CurrentTenant = tenant;
        var lines = new List<CartLineRequest> { new(mansaf.Id, 1, [], null) };
        var quote = await _host.CartQuotes.QuoteAsync(session, new CartRequest(lines), default);
        await _host.GuestOrders.PlaceAsync(session, new PlaceOrderRequest(Guid.NewGuid(), lines, null, quote.TotalMinor), default);
        return session;
    }

    private async Task<PaymentDto> StartAsync(PaymentService payments, Guid session)
    {
        var bill = await _host.Bills.GetForSessionAsync(session, default);
        return await payments.CreateAsync(new CreatePaymentRequest(session, Guid.NewGuid(), PaymentMethod.Card, PaymentMode.Full,
            null, null, 0, null, bill.AvailableMinor, true), default);
    }

    private static string Transaction(string orderId, string transactionId, string type, string result, string gatewayCode) => """
        {"result":"RESULT","response":{"gatewayCode":"CODE"},
         "order":{"id":"ORDER","status":"STATUS"},
         "transaction":{"id":"TXN","type":"TYPE"},
         "sourceOfFunds":{"provided":{"card":{"brand":"MASTERCARD","number":"512345xxxxxx0008"}}}}
        """.Replace("RESULT", result).Replace("CODE", gatewayCode).Replace("ORDER", orderId)
        .Replace("STATUS", result == "SUCCESS" ? "CAPTURED" : "FAILED").Replace("TXN", transactionId).Replace("TYPE", type);

    private static Dictionary<string, string> SecretHeader(string secret = Secret) =>
        new(StringComparer.OrdinalIgnoreCase) { [MastercardGatewayProvider.SecretHeader] = secret };

    // ---- Checkout ----

    [Fact]
    public async Task A_checkout_session_is_opened_for_the_payment_in_JOD_and_the_guest_goes_to_the_launcher()
    {
        var session = await GuestWithOrderAsync();
        var payment = await StartAsync(Payments(Provider()), session);

        var (method, url, body, auth) = Assert.Single(_gateway.Requests);
        var stored = await _host.Db.Payments.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((HttpMethod.Post, "https://test-gateway.mastercard.test/api/rest/version/100/merchant/TESTDINEFLOWJO/session"), (method, url));
        Assert.Equal("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("merchant.TESTDINEFLOWJO:api-password")), auth);
        Assert.Equal(("INITIATE_CHECKOUT", "PURCHASE"), ((string)body!["apiOperation"]!, (string)body["interaction"]!["operation"]!));
        Assert.Equal(("12.500", "JOD", $"df-{payment.Id:N}"), ((string)body["order"]!["amount"]!, (string)body["order"]!["currency"]!, (string)body["order"]!["id"]!));
        Assert.Equal(Options.NotificationUrl, (string)body["order"]!["notificationUrl"]!);
        Assert.Equal($"https://guest.test/pay/{payment.Id}", (string)body["interaction"]!["returnUrl"]!);
        Assert.Equal($"https://guest.test/v1/public/checkout/mastercard?session={SessionId}", payment.CheckoutUrl);
        Assert.Equal(($"df-{payment.Id:N}", "mastercard", PaymentStatus.RequiresAction), (stored.ProviderPaymentId, stored.Provider, stored.Status));
    }

    // ---- Webhooks ----

    [Fact]
    public async Task A_verified_webhook_marks_the_payment_paid_with_the_card_shown_as_brand_and_last_four()
    {
        var session = await GuestWithOrderAsync();
        var provider = Provider();
        var payments = Payments(provider);
        var payment = await StartAsync(payments, session);

        var tenant = _host.CurrentTenant;
        _host.CurrentTenant = null; // webhooks arrive without a tenant
        await payments.HandleWebhookAsync("mastercard", Transaction($"df-{payment.Id:N}", "1", "PAYMENT", "SUCCESS", "APPROVED"), SecretHeader(), default);
        _host.CurrentTenant = tenant;
        var after = await payments.GetAsync(session, payment.Id, default);

        Assert.Equal((PaymentStatus.Succeeded, "Mastercard", "0008", true), (after.Status, after.CardBrand, after.CardLast4, after.FullyPaid));
        Assert.Equal(12_500L, (await _host.Db.LedgerEntries.IgnoreQueryFilters().SingleAsync()).AmountMinor);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("wrong-secret-0000")]
    public void A_webhook_without_the_right_secret_is_refused(string? secret)
    {
        var headers = secret is null ? new Dictionary<string, string>() : SecretHeader(secret);

        var ex = Assert.Throws<InvalidWebhookException>(() =>
            Provider().ParseWebhook(Transaction("df-1", "1", "PAYMENT", "SUCCESS", "APPROVED"), headers));

        Assert.Equal(secret is null ? "The notification secret doesn't match." : "The notification secret doesn't match.", ex.Message);
    }

    [Fact]
    public void A_declined_payment_and_a_refund_are_read_from_their_notifications()
    {
        var provider = Provider();

        var declined = provider.ParseWebhook(Transaction("df-1", "1", "PAYMENT", "FAILURE", "INSUFFICIENT_FUNDS"), SecretHeader());
        var refund = provider.ParseWebhook(Transaction("df-1", "refund-abc", "REFUND", "SUCCESS", "APPROVED"), SecretHeader());

        Assert.Equal((PaymentStatus.Failed, "Insufficient funds.", (string?)null), (declined.Status, declined.FailureReason, declined.ProviderRefundId));
        Assert.Equal(("df-1", PaymentStatus.Succeeded, "refund-abc"), (refund.ProviderPaymentId, refund.Status, refund.ProviderRefundId));
        Assert.Equal("mpgs:df-1:refund-abc:Succeeded", refund.EventId);
    }

    // ---- Asking the gateway (webhooks aren't guaranteed) ----

    [Fact]
    public async Task Without_a_webhook_the_payment_is_settled_by_asking_the_gateway_and_counted_once()
    {
        var session = await GuestWithOrderAsync();
        var provider = Provider();
        var payments = Payments(provider);
        var payment = await StartAsync(payments, session);
        var orderId = $"df-{payment.Id:N}";
        var sync = new PaymentStatusSync(_host.Db, provider, payments, _host.Clock);

        _host.CurrentTenant = null;
        var tooSoon = await sync.RefreshAsync(default);                  // gives the webhook a moment first
        _host.Clock.Advance(TimeSpan.FromSeconds(15));
        var notYet = await sync.RefreshAsync(default);                   // the guest hasn't paid yet: no order at the gateway
        _gateway.Orders[orderId] = """
            {"result":"SUCCESS","id":"ORDER","status":"CAPTURED","amount":12.5,"currency":"JOD",
             "sourceOfFunds":{"provided":{"card":{"brand":"VISA","number":"411111xxxxxx1111"}}},
             "transaction":[{"result":"SUCCESS","response":{"gatewayCode":"APPROVED"},"transaction":{"id":"1","type":"PAYMENT"}}]}
            """.Replace("ORDER", orderId);
        var settled = await sync.RefreshAsync(default);
        // The webhook for the same transaction arrives late: it's recognised and not counted again.
        await payments.HandleWebhookAsync("mastercard", Transaction(orderId, "1", "PAYMENT", "SUCCESS", "APPROVED"), SecretHeader(), default);

        Assert.Equal((0, 0, 1), (tooSoon, notYet, settled));
        var stored = await _host.Db.Payments.IgnoreQueryFilters().SingleAsync();
        Assert.Equal((PaymentStatus.Succeeded, "Visa", "1111"), (stored.Status, stored.CardBrand, stored.CardLast4));
        Assert.Single(await _host.Db.LedgerEntries.IgnoreQueryFilters().ToListAsync());
        Assert.Single(_host.PaymentNotifier.Notices);
    }

    [Fact]
    public async Task A_failed_order_at_the_gateway_fails_the_payment_with_the_reason()
    {
        var provider = Provider();
        _gateway.Orders["df-9"] = """
            {"result":"SUCCESS","id":"df-9","status":"FAILED",
             "transaction":[{"result":"FAILURE","response":{"gatewayCode":"DECLINED"},"transaction":{"id":"1","type":"PAYMENT"}}]}
            """;
        _gateway.Orders["df-10"] = """{"result":"SUCCESS","id":"df-10","status":"AUTHENTICATION_INITIATED","transaction":[]}""";

        var failed = await provider.GetPaymentStatusAsync("df-9", default);
        var waiting = await provider.GetPaymentStatusAsync("df-10", default);

        Assert.Equal((PaymentStatus.Failed, "The card was declined."), (failed!.Status, failed.FailureReason));
        Assert.Null(waiting);
    }

    // ---- Refunds ----

    [Fact]
    public async Task A_refund_is_a_REFUND_transaction_on_the_gateway_order_in_JOD()
    {
        var refundId = Guid.NewGuid();

        var refund = await Provider().RefundAsync(new ProviderRefundRequest(refundId, "df-1", 2_250, "JOD"), default);

        var (method, url, body, _) = Assert.Single(_gateway.Requests);
        Assert.Equal($"refund-{refundId:N}", refund.ProviderRefundId);
        Assert.Equal((HttpMethod.Put, $"https://test-gateway.mastercard.test/api/rest/version/100/merchant/TESTDINEFLOWJO/order/df-1/transaction/refund-{refundId:N}"), (method, url));
        Assert.Equal(("REFUND", "2.250", "JOD"), ((string)body!["apiOperation"]!, (string)body["transaction"]!["amount"]!, (string)body["transaction"]!["currency"]!));
    }

    [Fact]
    public async Task Gateway_errors_carry_the_gateways_explanation()
    {
        _gateway.Fail = (HttpStatusCode.BadRequest, """{"result":"ERROR","error":{"cause":"INVALID_REQUEST","explanation":"Value 'XYZ' is invalid for field 'order.currency'"}}""");

        var ex = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Provider().CreateCheckoutAsync(new ProviderCheckoutRequest(Guid.NewGuid(), "DF-1", 100, "XYZ", PaymentMethod.Card, "Venue", "https://r", "https://c"), default));

        Assert.Equal("Mastercard Gateway refused the request (400): Value 'XYZ' is invalid for field 'order.currency'", ex.Message);
    }

    [Theory]
    [InlineData("SESSION0002561575526H6901242G59", true)]
    [InlineData("<script>", false)]
    [InlineData("abc", false)]
    public void Only_gateway_session_ids_are_accepted_by_the_launcher(string value, bool valid) =>
        Assert.Equal(valid, MastercardGatewayProvider.IsSessionId(value));

    /// <summary>A pretend Mastercard Gateway: opens sessions, answers Retrieve Order from <see cref="Orders"/>, accepts refunds.</summary>
    private sealed class FakeGateway : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, JsonNode? Body, string? Auth)> Requests { get; } = [];
        public Dictionary<string, string> Orders { get; } = [];
        public (HttpStatusCode Status, string Body)? Fail { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var url = request.RequestUri!.ToString();
            var body = request.Content is null ? null : JsonNode.Parse(await request.Content.ReadAsStringAsync(ct));
            Requests.Add((request.Method, url, body, request.Headers.Authorization?.ToString()));

            var (status, text) = Fail ?? (request.Method == HttpMethod.Post && url.EndsWith("/session")
                ? (HttpStatusCode.Created, $$"""{"result":"SUCCESS","session":{"id":"{{SessionId}}"},"successIndicator":"ab12cd34ef56"}""")
                : request.Method == HttpMethod.Put
                    ? (HttpStatusCode.OK, """{"result":"SUCCESS","response":{"gatewayCode":"APPROVED"}}""")
                    : Orders.TryGetValue(url[(url.LastIndexOf('/') + 1)..], out var order)
                        ? (HttpStatusCode.OK, order)
                        : (HttpStatusCode.NotFound, """{"result":"ERROR","error":{"cause":"INVALID_REQUEST","explanation":"Unable to find order"}}"""));
            return new HttpResponseMessage(status) { Content = new StringContent(text, Encoding.UTF8, "application/json") };
        }
    }
}
