using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Payments;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure.Payments;

public class MastercardGatewayOptions
{
    public const string Section = "Payments:Mastercard";

    /// <summary>The gateway host your acquirer gave you, e.g. https://test-gateway.mastercard.com (test) or the bank's own host.</summary>
    public string GatewayUrl { get; set; } = "https://test-gateway.mastercard.com";

    /// <summary>REST API version your merchant profile is set up for.</summary>
    public int ApiVersion { get; set; } = 100;

    /// <summary>The gateway merchant id from your acquirer (MEPS, Network International, your bank…).</summary>
    public string MerchantId { get; set; } = "";

    /// <summary>API password from Merchant Administration → Admin → Integration Settings. A secret.</summary>
    public string ApiPassword { get; set; } = "";

    /// <summary>The webhook secret from Merchant Administration → Admin → Webhook Notifications. A secret.</summary>
    public string WebhookSecret { get; set; } = "";

    /// <summary>Public HTTPS address of DineFlow's webhook, e.g. https://api.example.jo/v1/webhooks/payments/mastercard.</summary>
    public string NotificationUrl { get; set; } = "";

    /// <summary>Where guests' browsers reach DineFlow's checkout launcher page; defaults to the guest app (which proxies /v1 to the API).</summary>
    public string? LauncherBaseUrl { get; set; }

    /// <summary>
    /// The methods the guest can pick before going to the gateway's page (Apple Pay must be enabled by your
    /// acquirer). Empty means Card and Apple Pay; it starts empty because configuration binding appends to lists.
    /// </summary>
    public List<PaymentMethod> Methods { get; set; } = [];

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(20);
}

/// <summary>
/// Mastercard Gateway (MPGS) Hosted Checkout, the gateway most Jordanian acquirers (MEPS, Network
/// International Jordan, local banks) give merchants. The guest pays on the gateway's own page, so card
/// details never touch DineFlow (P1-PAY-12). Flow:
/// <list type="number">
/// <item>INITIATE_CHECKOUT (POST /session) with the order, return URL and webhook URL → a session id.</item>
/// <item>The guest goes to DineFlow's launcher page, which loads the gateway's checkout.js and shows its payment page.</item>
/// <item>The gateway sends a webhook (with the merchant's secret in X-Notification-Secret); DineFlow also
/// asks Retrieve Order while a payment is waiting, because the gateway doesn't guarantee webhook delivery.</item>
/// <item>Refunds are REFUND transactions on the order (PUT /order/{id}/transaction/{id}).</item>
/// </list>
/// </summary>
/// <remarks>
/// Built from Mastercard's public API documentation and tested against a fake gateway; confirm the field
/// set against your acquirer's merchant profile (API version, Apple Pay, 3-D Secure) with test credentials.
/// Amounts are sent in major units with the currency's decimals, e.g. JOD 12.500.
/// </remarks>
public partial class MastercardGatewayProvider(HttpClient http, IOptions<MastercardGatewayOptions> options, IOptions<GuestOptions> guest)
    : IPaymentProvider, IPaymentStatusSource
{
    public const string ProviderName = "mastercard";
    public const string SecretHeader = "X-Notification-Secret";
    public const string LauncherPath = "/v1/public/checkout/mastercard";

    private MastercardGatewayOptions Options => options.Value;

    public string Name => ProviderName;

    public IReadOnlyList<PaymentMethod> SupportedMethods =>
        Options.Methods.Count == 0 ? [PaymentMethod.Card, PaymentMethod.ApplePay] : Options.Methods.Distinct().ToList();

    public async Task<ProviderCheckout> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken ct)
    {
        // DineFlow's payment id is the gateway order id, so a retried request can't open a second order.
        var orderId = OrderIdFor(request.PaymentId);
        var order = new JsonObject
        {
            ["id"] = orderId,
            ["amount"] = Amount(request.AmountMinor, request.Currency),
            ["currency"] = request.Currency,
            ["description"] = Truncate($"{request.MerchantName} {request.Reference}", 127),
            ["reference"] = request.Reference
        };
        if (!string.IsNullOrEmpty(Options.NotificationUrl)) order["notificationUrl"] = Options.NotificationUrl;

        var session = await SendAsync(HttpMethod.Post, "session", new JsonObject
        {
            ["apiOperation"] = "INITIATE_CHECKOUT",
            ["interaction"] = new JsonObject
            {
                ["operation"] = "PURCHASE",
                ["returnUrl"] = request.ReturnUrl,
                ["cancelUrl"] = request.CancelUrl,
                ["merchant"] = new JsonObject { ["name"] = Truncate(request.MerchantName, 40) },
                ["displayControl"] = new JsonObject { ["billingAddress"] = "HIDE", ["customerEmail"] = "HIDE", ["shipping"] = "HIDE" }
            },
            ["order"] = order,
            ["transaction"] = new JsonObject { ["reference"] = request.Reference }
        }, ct);

        var sessionId = session?["session"]?["id"]?.GetValue<string>()
            ?? throw new InvalidOperationException("The gateway didn't return a checkout session.");
        var launcher = (Options.LauncherBaseUrl ?? guest.Value.AppBaseUrl).TrimEnd('/');
        return new ProviderCheckout(orderId, $"{launcher}{LauncherPath}?session={Uri.EscapeDataString(sessionId)}");
    }

    public async Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct)
    {
        // DineFlow's refund id is the gateway transaction id: retrying the same refund can't refund twice.
        var transactionId = $"refund-{request.RefundId:N}";
        await SendAsync(HttpMethod.Put, $"order/{Uri.EscapeDataString(request.ProviderPaymentId)}/transaction/{transactionId}", new JsonObject
        {
            ["apiOperation"] = "REFUND",
            ["transaction"] = new JsonObject
            {
                ["amount"] = Amount(request.AmountMinor, request.Currency),
                ["currency"] = request.Currency
            }
        }, ct);
        // The result arrives as a webhook, or is read back by GetRefundStatusAsync.
        return new ProviderRefund(transactionId);
    }

    /// <summary>
    /// Reads a webhook: the merchant's secret must be in X-Notification-Secret (compared in constant
    /// time), and the body is the transaction as the gateway reports it.
    /// </summary>
    public ProviderWebhookEvent ParseWebhook(string body, IReadOnlyDictionary<string, string> headers)
    {
        if (string.IsNullOrEmpty(Options.WebhookSecret)) throw new InvalidWebhookException("No webhook secret is configured.");
        if (!headers.TryGetValue(SecretHeader, out var secret)
            || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(Options.WebhookSecret)))
            throw new InvalidWebhookException("The notification secret doesn't match.");

        JsonObject notification;
        try { notification = JsonNode.Parse(body) as JsonObject ?? throw new InvalidWebhookException("The notification isn't a JSON object."); }
        catch (JsonException) { throw new InvalidWebhookException("The notification isn't valid JSON."); }

        return ReadTransaction(notification) ?? throw new InvalidWebhookException("The notification has no order or transaction.");
    }

    public async Task<ProviderWebhookEvent?> GetPaymentStatusAsync(string providerPaymentId, CancellationToken ct)
    {
        var order = await SendAsync(HttpMethod.Get, $"order/{Uri.EscapeDataString(providerPaymentId)}", null, ct, notFoundIsNull: true);
        if (order is null) return null; // the guest hasn't submitted anything yet

        // The order's own status decides; its latest payment transaction carries the card and reason.
        var transactions = (order["transaction"] as JsonArray ?? []).OfType<JsonObject>()
            .Where(t => (t["transaction"]?["type"]?.GetValue<string>() ?? "") is "PAYMENT" or "AUTHORIZATION" or "CAPTURE").ToList();
        var latest = transactions.LastOrDefault();
        var status = order["status"]?.GetValue<string>() switch
        {
            "CAPTURED" or "PARTIALLY_REFUNDED" or "REFUNDED" or "PARTIALLY_CAPTURED" => PaymentStatus.Succeeded,
            "FAILED" or "CANCELLED" or "DISPUTED" => PaymentStatus.Failed,
            _ => (PaymentStatus?)null
        };
        if (status is null) return null;

        var card = order["sourceOfFunds"]?["provided"]?["card"] ?? latest?["sourceOfFunds"]?["provided"]?["card"];
        var transactionId = latest?["transaction"]?["id"]?.GetValue<string>() ?? "order";
        return new ProviderWebhookEvent(EventId(providerPaymentId, transactionId, status.Value.ToString()), providerPaymentId, status.Value,
            Brand(card), Last4(card), status == PaymentStatus.Failed ? Reason(latest?["response"]?["gatewayCode"]?.GetValue<string>()) : null);
    }

    public async Task<ProviderWebhookEvent?> GetRefundStatusAsync(string providerPaymentId, string providerRefundId, CancellationToken ct)
    {
        var transaction = await SendAsync(HttpMethod.Get,
            $"order/{Uri.EscapeDataString(providerPaymentId)}/transaction/{Uri.EscapeDataString(providerRefundId)}", null, ct, notFoundIsNull: true);
        return transaction is null ? null : ReadTransaction(transaction);
    }

    /// <summary>The order id DineFlow uses at the gateway for a payment.</summary>
    public static string OrderIdFor(Guid paymentId) => $"df-{paymentId:N}";

    /// <summary>A gateway session id as it may appear in the launcher URL (31–35 ASCII characters, per the API docs).</summary>
    public static bool IsSessionId(string? value) => value is not null && SessionPattern().IsMatch(value);

    /// <summary>A gateway transaction (webhook body or Retrieve Transaction) as a provider event; null when it's not one DineFlow acts on.</summary>
    private static ProviderWebhookEvent? ReadTransaction(JsonObject t)
    {
        var orderId = t["order"]?["id"]?.GetValue<string>();
        var transactionId = t["transaction"]?["id"]?.GetValue<string>();
        var type = t["transaction"]?["type"]?.GetValue<string>();
        var result = t["result"]?.GetValue<string>();
        var gatewayCode = t["response"]?["gatewayCode"]?.GetValue<string>();
        if (orderId is null || transactionId is null || type is null) return null;

        var status = result switch
        {
            "SUCCESS" when gatewayCode is null or "APPROVED" or "APPROVED_AUTO" or "APPROVED_PENDING_SETTLEMENT" => PaymentStatus.Succeeded,
            "FAILURE" or "ERROR" => PaymentStatus.Failed,
            "SUCCESS" => PaymentStatus.Failed, // a "successful" request with a non-approval code
            _ => PaymentStatus.Processing      // PENDING / UNKNOWN: wait for a final answer
        };
        var card = t["sourceOfFunds"]?["provided"]?["card"];
        var eventId = EventId(orderId, transactionId, status.ToString());

        return type == "REFUND"
            ? status == PaymentStatus.Processing ? null
                : new ProviderWebhookEvent(eventId, orderId, status, null, null, status == PaymentStatus.Failed ? Reason(gatewayCode) : null, transactionId)
            : new ProviderWebhookEvent(eventId, orderId, status, Brand(card), Last4(card), status == PaymentStatus.Failed ? Reason(gatewayCode) : null);
    }

    /// <summary>The same transaction outcome always has the same id, whether it came by webhook or by asking, so it's applied once.</summary>
    private static string EventId(string orderId, string transactionId, string status) => $"mpgs:{orderId}:{transactionId}:{status}";

    private static string? Brand(JsonNode? card) => card?["brand"]?.GetValue<string>() switch
    {
        null => null,
        "MASTERCARD" => "Mastercard",
        "VISA" => "Visa",
        "AMEX" => "Amex",
        var other => CultureInfo.InvariantCulture.TextInfo.ToTitleCase(other.ToLowerInvariant())
    };

    /// <summary>Only the last four digits are kept (the gateway sends a masked number).</summary>
    private static string? Last4(JsonNode? card) =>
        card?["number"]?.GetValue<string>() is { Length: >= 4 } number && number[^4..].All(char.IsAsciiDigit) ? number[^4..] : null;

    private static string Reason(string? gatewayCode) => gatewayCode switch
    {
        "DECLINED" or "DECLINED_DO_NOT_CONTACT" => "The card was declined.",
        "INSUFFICIENT_FUNDS" => "Insufficient funds.",
        "EXPIRED_CARD" => "The card has expired.",
        "DECLINED_AVS" or "DECLINED_CSC" or "DECLINED_AVS_CSC" => "The card details didn't match.",
        "AUTHENTICATION_FAILED" or "DECLINED_PAYMENT_PLAN" => "The payment wasn't authenticated by the card's bank.",
        "TIMED_OUT" => "The bank didn't answer in time.",
        "ABORTED" or "CANCELLED" => "The payment was cancelled.",
        null => "The payment didn't go through.",
        var code => $"The payment didn't go through ({code})."
    };

    /// <summary>"12.500" for JOD 12.500; the gateway takes major units with the currency's decimals.</summary>
    private static string Amount(long minor, string currency)
    {
        var digits = currency is "KWD" or "BHD" or "OMR" or "JOD" or "TND" or "LYD" or "IQD" ? 3 : 2;
        return (minor / (decimal)Math.Pow(10, digits)).ToString($"F{digits}", CultureInfo.InvariantCulture);
    }

    private static string Truncate(string text, int max) => text.Length <= max ? text : text[..max];

    private async Task<JsonObject?> SendAsync(HttpMethod method, string path, JsonObject? body, CancellationToken ct, bool notFoundIsNull = false)
    {
        if (string.IsNullOrEmpty(Options.MerchantId) || string.IsNullOrEmpty(Options.ApiPassword))
            throw new InvalidOperationException("Payments:Mastercard:MerchantId and ApiPassword must be set.");

        using var request = new HttpRequestMessage(method,
            $"{Options.GatewayUrl.TrimEnd('/')}/api/rest/version/{Options.ApiVersion}/merchant/{Uri.EscapeDataString(Options.MerchantId)}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes($"merchant.{Options.MerchantId}:{Options.ApiPassword}")));
        if (body is not null) request.Content = JsonContent.Create(body);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(Options.Timeout);
        using var response = await http.SendAsync(request, timeout.Token);
        var text = await response.Content.ReadAsStringAsync(ct);
        if (notFoundIsNull && response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        var json = text.Length == 0 ? null : JsonNode.Parse(text) as JsonObject;
        if (response.IsSuccessStatusCode && json?["result"]?.GetValue<string>() != "ERROR") return json;

        var explanation = json?["error"]?["explanation"]?.GetValue<string>() ?? response.ReasonPhrase ?? "error";
        if (notFoundIsNull && json?["error"]?["cause"]?.GetValue<string>() == "INVALID_REQUEST"
            && explanation.Contains("order", StringComparison.OrdinalIgnoreCase) && explanation.Contains("not", StringComparison.OrdinalIgnoreCase))
            return null;
        throw new HttpRequestException($"Mastercard Gateway refused the request ({(int)response.StatusCode}): {explanation}");
    }

    [GeneratedRegex("^[A-Za-z0-9]{20,40}$")]
    private static partial Regex SessionPattern();
}
