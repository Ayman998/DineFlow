using DineFlow.Domain.Common;

namespace DineFlow.Domain.Payments;

/// <summary>
/// A provider webhook event that has been handled. Unique on (provider, event id) so a
/// redelivered event is recognised and skipped (blueprint 7.3, 8.1).
/// </summary>
public class ProcessedWebhook : Entity
{
    public string Provider { get; private set; } = string.Empty;
    public string EventId { get; private set; } = string.Empty;
    public string ProviderPaymentId { get; private set; } = string.Empty;
    public string Status { get; private set; } = string.Empty;

    /// <summary>
    /// Whether the provider's signed webhook delivered this event, as opposed to DineFlow asking the
    /// provider (the status check). Set even when the webhook arrives after the check already applied
    /// the event, so it proves the gateway's notification settings work (the go-live checklist).
    /// </summary>
    public bool ViaWebhook { get; private set; }

    private ProcessedWebhook() { }

    public ProcessedWebhook(string provider, string eventId, string providerPaymentId, string status, bool viaWebhook, DateTimeOffset now)
    {
        Provider = provider;
        EventId = eventId;
        ProviderPaymentId = providerPaymentId;
        Status = status;
        ViaWebhook = viaWebhook;
        CreatedAt = now;
    }

    public void MarkWebhookReceived() => ViaWebhook = true;
}
