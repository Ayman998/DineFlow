using DineFlow.Application.Payments;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DineFlow.Infrastructure.Payments;

/// <summary>Every 15 seconds, asks the provider about payments and refunds still waiting (see <see cref="PaymentStatusSync"/>).</summary>
public class PaymentStatusWorker(IServiceScopeFactory scopes, ILogger<PaymentStatusWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PaymentStatusSync>().RefreshAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Checking waiting payments with the provider failed; it will try again.");
            }
        }
    }
}
