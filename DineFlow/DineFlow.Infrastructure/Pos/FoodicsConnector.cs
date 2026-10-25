using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using DineFlow.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure.Pos;

public class FoodicsOptions
{
    public const string Section = "Pos:Foodics";

    /// <summary>Foodics API v5. Sandbox: https://api-sandbox.foodics.com/v5; production: https://api.foodics.com/v5.</summary>
    public string BaseUrl { get; set; } = "https://api-sandbox.foodics.com/v5";

    /// <summary>The Foodics payment method DineFlow tenders are recorded under (create one named "DineFlow" in Foodics).</summary>
    public string PaymentMethodId { get; set; } = "";

    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>
/// Foodics (API v5) behind the canonical POS contract. A DineFlow bill is one Foodics dine-in order
/// on the mapped table: the first accepted order creates it, later ones add products to it, payments
/// are added to it as tenders under the configured payment method, and it's closed when paid.
/// Products must be mapped to Foodics product ids (Settings → POS).
/// </summary>
/// <remarks>
/// Built from Foodics' public API documentation and tested against a fake Foodics; the request
/// shapes for adding products and payments to an existing order still need to be confirmed with a
/// Foodics sandbox account. DineFlow's own ids are sent in each order's and payment's "meta", so a
/// retried request can be recognised.
/// </remarks>
public class FoodicsConnector(HttpClient http, IOptions<FoodicsOptions> options) : IPosConnector
{
    public const string ProviderName = "foodics";

    private const int DineIn = 1;
    private const int ActiveStatus = 2;
    private const int ClosedStatus = 4;

    public string Provider => ProviderName;
    public string DisplayName => "Foodics";
    public PosCapabilities Capabilities => PosCapabilities.PaymentPosting | PosCapabilities.AutoClose | PosCapabilities.TipPosting | PosCapabilities.OrderCreate;
    public bool RequiresAccessToken => true;

    public async Task<PosHealth> HealthCheckAsync(PosContext context, CancellationToken ct)
    {
        var me = await SendAsync(context, HttpMethod.Get, "whoami", null, ct);
        var business = me?["data"]?["business"]?["name"]?.GetValue<string>();
        if (string.IsNullOrEmpty(context.BranchRef)) return new PosHealth(false, "Connected, but no branch is set. Add the Foodics branch id.");
        await SendAsync(context, HttpMethod.Get, $"branches/{Uri.EscapeDataString(context.BranchRef)}", null, ct);
        return new PosHealth(true, $"Connected to Foodics{(business is null ? "" : $" ({business})")}, branch {context.BranchRef}.");
    }

    public async Task<IReadOnlyList<PosCheckSummary>> GetOpenChecksAsync(PosContext context, string tableRef, CancellationToken ct)
    {
        var path = $"orders?filter[branch_id]={Uri.EscapeDataString(Branch(context))}&filter[table_id]={Uri.EscapeDataString(tableRef)}&filter[status]={ActiveStatus}";
        var body = await SendAsync(context, HttpMethod.Get, path, null, ct);
        return (body?["data"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(o => new PosCheckSummary(Str(o, "id"), tableRef, Minor(o, "total_price"), Paid(o), true)).ToList();
    }

    public async Task<PosCheck> GetCheckDetailsAsync(PosContext context, string checkRef, CancellationToken ct)
    {
        var body = await SendAsync(context, HttpMethod.Get, $"orders/{Uri.EscapeDataString(checkRef)}?include=products,payments", null, ct);
        var order = body?["data"] as JsonObject ?? throw new PosRejectedException($"Foodics has no order {checkRef}.");
        var lines = (order["products"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(p => new PosCheckLine(p["product"]?["name"]?.GetValue<string>() ?? "Item", (int)Num(p, "quantity"), Minor(p, "total_price"))).ToList();
        var payments = (order["payments"] as JsonArray ?? []).OfType<JsonObject>()
            .Select(p => new PosCheckPayment(Str(p, "id"), p["meta"]?["dineflow_payment_id"]?.GetValue<string>(), Minor(p, "amount"), Minor(p, "tips"))).ToList();
        return new PosCheck(Str(order, "id"), order["table"]?["id"]?.GetValue<string>() ?? "", Minor(order, "total_price"),
            payments.Sum(p => p.AmountMinor), (int)Num(order, "status") == ActiveStatus, lines, payments);
    }

    public async Task<PosOrderResult> CreateOrderAsync(PosContext context, PosOrder order, CancellationToken ct)
    {
        var products = new JsonArray(order.Lines.Select(l => (JsonNode)new JsonObject
        {
            ["product_id"] = l.ProductRef ?? throw new PosRejectedException($"\"{l.Name}\" isn't mapped to a Foodics product. Map it under Settings → POS, then retry."),
            ["quantity"] = l.Quantity,
            ["unit_price"] = Major(l.UnitPriceMinor, order.Currency),
            ["total_price"] = Major(l.LineTotalMinor, order.Currency)
        }).ToArray());

        if (order.CheckRef is { } existing)
        {
            await SendAsync(context, HttpMethod.Post, $"orders/{Uri.EscapeDataString(existing)}/products",
                new JsonObject { ["products"] = products, ["meta"] = new JsonObject { ["dineflow_order_id"] = order.ExternalOrderId } }, ct);
            return new PosOrderResult(existing, existing);
        }

        var created = await SendAsync(context, HttpMethod.Post, "orders", new JsonObject
        {
            ["branch_id"] = Branch(context),
            ["table_id"] = order.TableRef,
            ["type"] = DineIn,
            ["status"] = ActiveStatus,
            ["guests"] = 1,
            ["reference"] = $"DineFlow #{order.Number}",
            ["kitchen_notes"] = order.Note,
            ["products"] = products,
            ["meta"] = new JsonObject { ["dineflow_order_id"] = order.ExternalOrderId }
        }, ct);
        var id = created?["data"]?["id"]?.GetValue<string>() ?? throw new PosUnavailableException("Foodics didn't return the order id.", mayHaveApplied: true);
        return new PosOrderResult(id, id);
    }

    public async Task<PosPostingResult> PostPaymentAsync(PosContext context, PosPayment payment, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(options.Value.PaymentMethodId))
            throw new PosRejectedException("Set Pos:Foodics:PaymentMethodId to the Foodics payment method DineFlow payments are recorded under.");

        var amount = Major(payment.AmountMinor, payment.Currency);
        var posted = await SendAsync(context, HttpMethod.Post, $"orders/{Uri.EscapeDataString(payment.CheckRef)}/payments", new JsonObject
        {
            ["payment_method_id"] = options.Value.PaymentMethodId,
            ["amount"] = amount,
            ["tendered"] = amount,
            ["tips"] = Major(payment.TipMinor, payment.Currency),
            ["meta"] = new JsonObject { ["dineflow_payment_id"] = payment.ExternalPaymentId, ["reference"] = payment.Reference }
        }, ct);
        var id = posted?["data"]?["id"]?.GetValue<string>() ?? throw new PosUnavailableException("Foodics didn't return the payment id.", mayHaveApplied: true);
        return new PosPostingResult(id, null);
    }

    public Task CloseCheckAsync(PosContext context, string checkRef, CancellationToken ct) =>
        SendAsync(context, HttpMethod.Put, $"orders/{Uri.EscapeDataString(checkRef)}", new JsonObject { ["status"] = ClosedStatus }, ct);

    /// <summary>
    /// One call to Foodics. Network failures, timeouts and 5xx/429 are "unavailable" (retried; a write
    /// that timed out may have been applied); other 4xx are refusals with Foodics' message.
    /// </summary>
    private async Task<JsonNode?> SendAsync(PosContext context, HttpMethod method, string path, JsonObject? body, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(context.AccessToken)) throw new PosRejectedException("No Foodics access token is set.");
        using var request = new HttpRequestMessage(method, $"{options.Value.BaseUrl.TrimEnd('/')}/{path}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", context.AccessToken);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        if (body is not null) request.Content = JsonContent.Create(body);
        var writes = method != HttpMethod.Get;

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(options.Value.Timeout);
        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, timeout.Token);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new PosUnavailableException("Foodics didn't answer in time.", mayHaveApplied: writes);
        }
        catch (HttpRequestException ex)
        {
            // The request may have reached Foodics before the connection dropped.
            throw new PosUnavailableException($"Couldn't reach Foodics: {ex.Message}", mayHaveApplied: writes, ex);
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode) return text.Length == 0 ? null : JsonNode.Parse(text);

            var message = Message(text) ?? response.ReasonPhrase ?? "error";
            if ((int)response.StatusCode >= 500 || response.StatusCode == HttpStatusCode.TooManyRequests)
                throw new PosUnavailableException($"Foodics is having trouble ({(int)response.StatusCode}): {message}", mayHaveApplied: writes && (int)response.StatusCode >= 500);
            throw new PosRejectedException(response.StatusCode == HttpStatusCode.Unauthorized
                ? "Foodics didn't accept the access token. Reconnect with a new one."
                : $"Foodics refused it ({(int)response.StatusCode}): {message}");
        }
    }

    private static string? Message(string text)
    {
        try { return JsonNode.Parse(text)?["message"]?.GetValue<string>(); }
        catch (JsonException) { return null; }
    }

    private static string Branch(PosContext context) =>
        context.BranchRef ?? throw new PosRejectedException("No Foodics branch is set for this venue.");

    private static string Str(JsonObject o, string key) => o[key]?.ToString() ?? "";

    private static decimal Num(JsonObject o, string key) =>
        o[key] is JsonValue v && v.TryGetValue<decimal>(out var d) ? d
        : decimal.TryParse(o[key]?.ToString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) ? parsed : 0;

    /// <summary>Foodics sends amounts in major units; this assumes two decimals unless the value says otherwise.</summary>
    private static long Minor(JsonObject o, string key) => (long)Math.Round(Num(o, key) * 100m);

    private static long Paid(JsonObject o) => (o["payments"] as JsonArray ?? []).OfType<JsonObject>().Sum(p => Minor(p, "amount"));

    private static decimal Major(long minor, string currency) =>
        minor / (decimal)Math.Pow(10, currency is "KWD" or "BHD" or "OMR" or "JOD" ? 3 : 2);
}
