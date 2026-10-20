using DineFlow.Domain.Payments;

namespace DineFlow.Application.Abstractions;

/// <summary>
/// A payment service provider (PSP) behind DineFlow's hosted checkout (blueprint "Payment
/// Orchestrator"). Card details are only ever entered on the provider's page, never in DineFlow
/// (P1-SEC-01). Real providers (Checkout.com, Tap, HyperPay, Stripe…) implement this alongside
/// the built-in sandbox.
/// </summary>
public interface IPaymentProvider
{
    string Name { get; }
    IReadOnlyList<PaymentMethod> SupportedMethods { get; }

    /// <summary>
    /// Starts a hosted checkout. <see cref="ProviderCheckoutRequest.PaymentId"/> is sent as the
    /// provider's idempotency key, so a retry can't create a second charge.
    /// </summary>
    Task<ProviderCheckout> CreateCheckoutAsync(ProviderCheckoutRequest request, CancellationToken ct);

    /// <summary>Verifies the webhook's signature and reads it; throws <see cref="InvalidWebhookException"/> when it can't be trusted.</summary>
    ProviderWebhookEvent ParseWebhook(string body, IReadOnlyDictionary<string, string> headers);
}

public record ProviderCheckoutRequest(
    Guid PaymentId, string Reference, long AmountMinor, string Currency, PaymentMethod Method,
    string MerchantName, string ReturnUrl, string CancelUrl);

/// <param name="RedirectUrl">The provider's page the guest is sent to.</param>
public record ProviderCheckout(string ProviderPaymentId, string RedirectUrl);

public record ProviderWebhookEvent(
    string EventId, string ProviderPaymentId, PaymentStatus Status,
    string? CardBrand, string? CardLast4, string? FailureReason);

public class InvalidWebhookException(string message) : Exception(message);

/// <summary>Pushes payment and bill updates to staff screens and the guests at the table.</summary>
public interface IPaymentNotifier
{
    Task PaymentSucceededAsync(PaymentNotice notice, CancellationToken ct);
}

/// <param name="TenantId">Carried explicitly: webhooks arrive without a signed-in tenant.</param>
public record PaymentNotice(
    Guid TenantId,
    Guid VenueId, Guid ServiceLocationId, string LocationType, string LocationLabel, string? AreaName,
    Guid PaymentId, string Reference, string Currency, long AmountMinor, long TipMinor,
    long RemainingMinor, bool FullyPaid);

/// <summary>Sends e-mail such as receipts. Development writes .eml files instead of sending.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);
