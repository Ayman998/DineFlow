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

    /// <summary>
    /// Asks the provider to give money back. <see cref="ProviderRefundRequest.RefundId"/> is the
    /// idempotency key. The result arrives later as a webhook with <see cref="ProviderWebhookEvent.ProviderRefundId"/> set.
    /// </summary>
    Task<ProviderRefund> RefundAsync(ProviderRefundRequest request, CancellationToken ct);
}

/// <summary>
/// A provider DineFlow can ask for the current state of a payment or refund, e.g. because its
/// webhooks aren't guaranteed to arrive (Mastercard Gateway says so). Results are applied through
/// the same path as webhooks, so nothing is counted twice.
/// </summary>
public interface IPaymentStatusSource
{
    /// <summary>The payment's current state at the provider, or null when the provider has nothing final or useful yet.</summary>
    Task<ProviderWebhookEvent?> GetPaymentStatusAsync(string providerPaymentId, CancellationToken ct);

    /// <summary>The refund's current state at the provider, or null when it isn't final yet.</summary>
    Task<ProviderWebhookEvent?> GetRefundStatusAsync(string providerPaymentId, string providerRefundId, CancellationToken ct);
}

public record ProviderRefundRequest(Guid RefundId, string ProviderPaymentId, long AmountMinor, string Currency);

public record ProviderRefund(string ProviderRefundId);

public record ProviderCheckoutRequest(
    Guid PaymentId, string Reference, long AmountMinor, string Currency, PaymentMethod Method,
    string MerchantName, string ReturnUrl, string CancelUrl);

/// <param name="RedirectUrl">The provider's page the guest is sent to.</param>
public record ProviderCheckout(string ProviderPaymentId, string RedirectUrl);

/// <param name="ProviderRefundId">Set when the event is about a refund; <paramref name="Status"/> is then the refund's outcome (Succeeded or Failed).</param>
public record ProviderWebhookEvent(
    string EventId, string ProviderPaymentId, PaymentStatus Status,
    string? CardBrand, string? CardLast4, string? FailureReason, string? ProviderRefundId = null);

public class InvalidWebhookException(string message) : Exception(message);

/// <summary>Pushes payment and bill updates to staff screens and the guests at the table.</summary>
public interface IPaymentNotifier
{
    Task PaymentSucceededAsync(PaymentNotice notice, CancellationToken ct);

    /// <summary>Tells the guests at a table that their bill changed, e.g. someone selected items or set a split.</summary>
    Task TableBillChangedAsync(Guid tenantId, Guid serviceLocationId, string reason, CancellationToken ct);

    /// <summary>Tells staff screens a refund was confirmed or declined by the provider.</summary>
    Task RefundUpdatedAsync(RefundNotice notice, CancellationToken ct);

    /// <summary>Tells staff screens a payment came in through a payment link (blueprint 9.3 PaymentLinkPaid).</summary>
    Task PaymentLinkPaidAsync(PaymentLinkNotice notice, CancellationToken ct);
}

/// <param name="AmountMinor">What this payment charged, tip included.</param>
/// <param name="FullyPaid">The link is now paid in full and has closed.</param>
public record PaymentLinkNotice(
    Guid TenantId, Guid VenueId, Guid PaymentLinkId, string Code, string? LinkReference,
    Guid PaymentId, string Reference, string Currency, long AmountMinor, long TipMinor, long RemainingMinor, bool FullyPaid);

public record RefundNotice(Guid TenantId, Guid VenueId, Guid PaymentId, Guid RefundId, string Reference, string Currency, long AmountMinor, RefundStatus Status);

/// <param name="TenantId">Carried explicitly: webhooks arrive without a signed-in tenant.</param>
public record PaymentNotice(
    Guid TenantId,
    Guid VenueId, Guid ServiceLocationId, string LocationType, string LocationLabel, string? AreaName,
    Guid PaymentId, string Reference, string Currency, long AmountMinor, long TipMinor,
    long RemainingMinor, bool FullyPaid,
    PaymentMode Mode, int? Shares, int? SplitPeople, int ItemCount);

/// <summary>Sends e-mail such as receipts. Development writes .eml files instead of sending.</summary>
public interface IEmailSender
{
    Task SendAsync(EmailMessage message, CancellationToken ct);
}

public record EmailMessage(string To, string Subject, string TextBody, string HtmlBody);
