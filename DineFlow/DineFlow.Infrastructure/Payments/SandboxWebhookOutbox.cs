using System.Threading.Channels;
using DineFlow.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DineFlow.Infrastructure.Payments;

/// <summary>Webhooks the sandbox provider will send a little later, like a real provider confirming a refund.</summary>
public class SandboxWebhookOutbox
{
    /// <summary>How long the sandbox "takes" to confirm a refund.</summary>
    public static readonly TimeSpan RefundDelay = TimeSpan.FromSeconds(2);

    private readonly Channel<PendingWebhook> _queue = Channel.CreateUnbounded<PendingWebhook>();

    public void Send(string body, string signature, TimeSpan delay) =>
        _queue.Writer.TryWrite(new PendingWebhook(body, signature, delay));

    internal ChannelReader<PendingWebhook> Reader => _queue.Reader;

    internal sealed record PendingWebhook(string Body, string Signature, TimeSpan Delay);
}

/// <summary>Delivers the sandbox's queued webhooks through the same verification as any provider's.</summary>
public class SandboxWebhookDispatcher(SandboxWebhookOutbox outbox, IServiceScopeFactory scopes, ILogger<SandboxWebhookDispatcher> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await foreach (var webhook in outbox.Reader.ReadAllAsync(stoppingToken))
        {
            _ = DeliverLaterAsync(webhook, stoppingToken);
        }
    }

    private async Task DeliverLaterAsync(SandboxWebhookOutbox.PendingWebhook webhook, CancellationToken ct)
    {
        try
        {
            await Task.Delay(webhook.Delay, ct);
            using var scope = scopes.CreateScope();
            var payments = scope.ServiceProvider.GetRequiredService<PaymentService>();
            await payments.HandleWebhookAsync(SandboxPaymentProvider.ProviderName, webhook.Body,
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [SandboxPaymentProvider.SignatureHeader] = webhook.Signature }, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "The sandbox couldn't deliver a webhook.");
        }
    }
}
