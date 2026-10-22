using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Payments;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure.Payments;

public class SandboxOptions
{
    public const string Section = "Payments:Sandbox";

    /// <summary>Base64 HMAC key (at least 32 bytes) that signs checkout links and webhooks.</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Where guests' browsers reach the sandbox payment page. Defaults to the guest app, whose dev
    /// server proxies /sandbox to the API.
    /// </summary>
    public string? PublicBaseUrl { get; set; }

    /// <summary>Lets the sandbox run outside Development, e.g. on a demo server. Never in production.</summary>
    public bool AllowOutsideDevelopment { get; set; }
}

/// <summary>A sandbox checkout, carried in the signed link to the hosted page (no server state).</summary>
public record SandboxCheckout(
    string ProviderPaymentId, string Reference, long AmountMinor, string Currency, PaymentMethod Method,
    string MerchantName, string ReturnUrl, string CancelUrl, DateTimeOffset ExpiresAt);

/// <summary>
/// DineFlow's built-in test PSP. It behaves like a real hosted checkout: the guest is sent to its
/// payment page, and the result arrives as a signed webhook (header
/// <c>DineFlow-Sandbox-Signature: t=unix-time,v1=hex(HMAC-SHA256(key, "t.body"))</c>) that is
/// checked for tampering and replay. Use it until a real provider's sandbox keys are configured.
/// </summary>
/// <remarks>Refunds are accepted straight away and confirmed by a webhook shortly after, through <see cref="SandboxWebhookOutbox"/> when one is registered.</remarks>
public class SandboxPaymentProvider(
    IOptions<SandboxOptions> options, IOptions<GuestOptions> guest, TimeProvider clock, SandboxWebhookOutbox? outbox = null) : IPaymentProvider
{
    public const string ProviderName = "sandbox";
    public const string SignatureHeader = "DineFlow-Sandbox-Signature";
    private static readonly TimeSpan SignatureTolerance = TimeSpan.FromMinutes(5);
    // No longer than a payment stays in flight, so a stale page can't pay a bill someone else is now paying.
    private static readonly TimeSpan CheckoutLifetime = Payment.InFlightLifetime;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };

    private readonly byte[] _key = DecodeKey(options.Value.SecretKey);

    public string Name => ProviderName;

    public IReadOnlyList<PaymentMethod> SupportedMethods { get; } =
        [PaymentMethod.Card, PaymentMethod.ApplePay, PaymentMethod.GooglePay, PaymentMethod.StcPay];

    public Task<ProviderCheckout> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken ct)
    {
        // The internal payment id doubles as the idempotency key: the same payment always gets the same id here.
        var providerPaymentId = $"sbx_{request.PaymentId:N}";
        var checkout = new SandboxCheckout(providerPaymentId, request.Reference, request.AmountMinor, request.Currency,
            request.Method, request.MerchantName, request.ReturnUrl, request.CancelUrl, clock.GetUtcNow() + CheckoutLifetime);

        var baseUrl = (options.Value.PublicBaseUrl ?? guest.Value.AppBaseUrl).TrimEnd('/');
        var url = $"{baseUrl}/sandbox/pay/{providerPaymentId}?t={Uri.EscapeDataString(Protect(checkout))}";
        return Task.FromResult(new ProviderCheckout(providerPaymentId, url));
    }

    public Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct)
    {
        // The internal refund id doubles as the idempotency key: the same refund always gets the same id here.
        var providerRefundId = $"sbxr_{request.RefundId:N}";
        if (outbox is not null)
        {
            var (body, signature) = CreateWebhook(new ProviderWebhookEvent(
                $"evt_{Guid.NewGuid():N}", request.ProviderPaymentId, PaymentStatus.Succeeded, null, null, null, providerRefundId));
            outbox.Send(body, signature, SandboxWebhookOutbox.RefundDelay);
        }
        return Task.FromResult(new ProviderRefund(providerRefundId));
    }

    /// <summary>Reads a checkout link; null when it was tampered with, is for another payment or has expired.</summary>
    public SandboxCheckout? ReadCheckout(string providerPaymentId, string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 2) return null;

        var payload = FromBase64Url(parts[0]);
        if (payload is null || !CryptographicOperations.FixedTimeEquals(Sign("checkout", payload), FromBase64Url(parts[1]) ?? []))
            return null;

        var checkout = JsonSerializer.Deserialize<SandboxCheckout>(payload, Json);
        return checkout is not null && checkout.ProviderPaymentId == providerPaymentId && checkout.ExpiresAt > clock.GetUtcNow()
            ? checkout
            : null;
    }

    /// <summary>The webhook body and signature header the sandbox sends for a result.</summary>
    public (string Body, string Signature) CreateWebhook(ProviderWebhookEvent webhook)
    {
        var body = JsonSerializer.Serialize(webhook, Json);
        var timestamp = clock.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var signature = Convert.ToHexString(Sign("webhook", Encoding.UTF8.GetBytes($"{timestamp}.{body}"))).ToLowerInvariant();
        return (body, $"t={timestamp},v1={signature}");
    }

    public ProviderWebhookEvent ParseWebhook(string body, IReadOnlyDictionary<string, string> headers)
    {
        if (!headers.TryGetValue(SignatureHeader, out var header))
            throw new InvalidWebhookException("Missing signature.");

        var fields = header.Split(',').Select(p => p.Split('=', 2)).Where(p => p.Length == 2).ToDictionary(p => p[0].Trim(), p => p[1].Trim());
        if (!fields.TryGetValue("t", out var t) || !fields.TryGetValue("v1", out var v1)
            || !long.TryParse(t, NumberStyles.None, CultureInfo.InvariantCulture, out var unix))
            throw new InvalidWebhookException("Malformed signature.");

        // An old (or future) timestamp means a replayed or forged request.
        var sentAt = DateTimeOffset.FromUnixTimeSeconds(unix);
        if ((clock.GetUtcNow() - sentAt).Duration() > SignatureTolerance)
            throw new InvalidWebhookException("Signature timestamp is outside the allowed window.");

        var expected = Sign("webhook", Encoding.UTF8.GetBytes($"{t}.{body}"));
        byte[] actual;
        try { actual = Convert.FromHexString(v1); }
        catch (FormatException) { throw new InvalidWebhookException("Malformed signature."); }
        if (!CryptographicOperations.FixedTimeEquals(expected, actual))
            throw new InvalidWebhookException("Signature doesn't match.");

        return JsonSerializer.Deserialize<ProviderWebhookEvent>(body, Json)
            ?? throw new InvalidWebhookException("Empty event.");
    }

    private string Protect(SandboxCheckout checkout)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(checkout, Json);
        return $"{ToBase64Url(payload)}.{ToBase64Url(Sign("checkout", payload))}";
    }

    /// <summary>HMAC with a purpose prefix, so a checkout signature can never pass as a webhook signature.</summary>
    private byte[] Sign(string purpose, byte[] data)
    {
        byte[] message = [.. Encoding.ASCII.GetBytes(purpose + ":"), .. data];
        return HMACSHA256.HashData(_key, message);
    }

    private static string ToBase64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[]? FromBase64Url(string text)
    {
        var b64 = text.Replace('-', '+').Replace('_', '/');
        b64 = b64.PadRight(b64.Length + (4 - b64.Length % 4) % 4, '=');
        try { return Convert.FromBase64String(b64); }
        catch (FormatException) { return null; }
    }

    private static byte[] DecodeKey(string key)
    {
        try
        {
            var bytes = Convert.FromBase64String(key);
            if (bytes.Length >= 32) return bytes;
        }
        catch (FormatException) { }
        throw new InvalidOperationException("Payments:Sandbox:SecretKey must be base64 of at least 32 bytes.");
    }
}

/// <summary>Test cards for the sandbox page, like those real PSPs publish.</summary>
public static class SandboxCards
{
    public record Outcome(PaymentStatus Status, string Brand, string Last4, string? FailureReason);

    public static readonly (string Number, string Description)[] Examples =
    [
        ("4242 4242 4242 4242", "Visa · succeeds"),
        ("5555 5555 5555 4444", "Mastercard · succeeds"),
        ("3782 822463 10005", "Amex · succeeds"),
        ("4000 0000 0000 0002", "Visa · declined"),
        ("4000 0000 0000 9995", "Visa · insufficient funds")
    ];

    /// <summary>The result for a card number, or null when the number isn't valid (fails the Luhn check).</summary>
    public static Outcome? For(string cardNumber)
    {
        var digits = new string(cardNumber.Where(char.IsAsciiDigit).ToArray());
        if (digits.Length is < 13 or > 19 || !PassesLuhn(digits)) return null;

        var brand = digits switch
        {
            _ when digits.StartsWith("34") || digits.StartsWith("37") => "Amex",
            _ when digits[0] == '4' => "Visa",
            _ when digits[0] == '5' || digits[0] == '2' => "Mastercard",
            _ => "Card"
        };
        var last4 = digits[^4..];

        return digits switch
        {
            "4000000000000002" => new Outcome(PaymentStatus.Failed, brand, last4, "Your card was declined."),
            "4000000000009995" => new Outcome(PaymentStatus.Failed, brand, last4, "Insufficient funds."),
            _ => new Outcome(PaymentStatus.Succeeded, brand, last4, null)
        };
    }

    private static bool PassesLuhn(string digits)
    {
        var sum = 0;
        var doubleIt = false;
        for (var i = digits.Length - 1; i >= 0; i--)
        {
            var d = digits[i] - '0';
            if (doubleIt && (d *= 2) > 9) d -= 9;
            sum += d;
            doubleIt = !doubleIt;
        }
        return sum % 10 == 0;
    }
}
