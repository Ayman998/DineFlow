using DineFlow.Application.Notifications;
using DineFlow.Application.Reports;
using DineFlow.Domain.Notifications;
using DineFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DineFlow.Infrastructure.Messaging;

/// <summary>When the worker last finished a pass, for the health check.</summary>
public class NotificationWorkerStatus
{
    public DateTimeOffset? LastPassAt { get; set; }
}

/// <summary>
/// Every few seconds: queues the scheduled reports that are due, then sends due messages from the
/// outbox (WhatsApp alerts, report e-mails) until none are left. Errors are logged and the next pass
/// carries on; messages keep their own retry state.
/// </summary>
public class NotificationWorker(IServiceScopeFactory scopes, NotificationWorkerStatus status, TimeProvider clock, ILogger<NotificationWorker> logger)
    : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                using (var scope = scopes.CreateScope())
                    await scope.ServiceProvider.GetRequiredService<ReportService>().RunDueAsync(stoppingToken);

                int sent;
                do
                {
                    using var scope = scopes.CreateScope();
                    sent = await scope.ServiceProvider.GetRequiredService<NotificationDispatcher>().DispatchDueAsync(stoppingToken);
                } while (sent == NotificationDispatcher.BatchSize && !stoppingToken.IsCancellationRequested);

                status.LastPassAt = clock.GetUtcNow();
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The notification worker's pass failed; it will try again.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}

/// <summary>
/// /health/ready reports Degraded when the worker has stopped making passes or messages are stuck:
/// overdue by more than ten minutes. Failed messages are reported as data, not as unhealthy.
/// </summary>
public class NotificationHealthCheck(AppDbContext db, NotificationWorkerStatus status, TimeProvider clock) : IHealthCheck
{
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var overdue = await db.NotificationDeliveries.IgnoreQueryFilters()
            .CountAsync(d => d.Status == DeliveryStatus.Pending && d.NextAttemptAt < now - TimeSpan.FromMinutes(10), ct);
        var failed = await db.NotificationDeliveries.IgnoreQueryFilters()
            .CountAsync(d => d.Status == DeliveryStatus.Failed && d.CreatedAt >= now - TimeSpan.FromDays(1), ct);
        var data = new Dictionary<string, object> { ["overdue"] = overdue, ["failedLast24h"] = failed, ["lastPassAt"] = status.LastPassAt?.ToString("O") ?? "never" };

        if (status.LastPassAt is not { } last || now - last > TimeSpan.FromMinutes(2))
            return HealthCheckResult.Degraded("The notification worker hasn't run in the last two minutes.", data: data);
        return overdue > 0
            ? HealthCheckResult.Degraded($"{overdue} message(s) are more than ten minutes overdue.", data: data)
            : HealthCheckResult.Healthy("Messages are going out.", data);
    }
}
