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

    private ProcessedWebhook() { }

    public ProcessedWebhook(string provider, string eventId, string providerPaymentId, string status, DateTimeOffset now)
    {
        Provider = provider;
        EventId = eventId;
        ProviderPaymentId = providerPaymentId;
        Status = status;
        CreatedAt = now;
    }
}
