using DineFlow.Application.Finance;
using DineFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DineFlow.Infrastructure.Payments;

/// <summary>
/// Every hour, reconciles the last week for each venue that has taken payments (blueprint "daily
/// reconciliation"). Days are re-run because settlements arrive later and POS posts catch up, so
/// discrepancies open and clear on their own. Errors are logged and the next pass carries on.
/// </summary>
public class ReconciliationWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<ReconciliationWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromHours(1);
    public const int DaysBack = 7;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // A short pause after start-up, then hourly.
        await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken).ContinueWith(_ => { }, CancellationToken.None);
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                await PassAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The reconciliation pass failed; it will try again.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task PassAsync(CancellationToken ct)
    {
        List<Guid> venues;
        using (var scope = scopes.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var since = clock.GetUtcNow().AddDays(-(DaysBack + 1));
            venues = await db.Payments.IgnoreQueryFilters().Where(p => p.SucceededAt >= since).Select(p => p.VenueId).Distinct().ToListAsync(ct);
        }

        foreach (var venueId in venues)
        {
            using var scope = scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var reconciler = scope.ServiceProvider.GetRequiredService<Reconciler>();
            var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == venueId, ct);
            var today = Reconciler.LocalDate(clock.GetUtcNow(), venue);
            for (var day = today.AddDays(-DaysBack); day <= today; day = day.AddDays(1))
            {
                db.ChangeTracker.Clear();
                await reconciler.ReconcileAsync(venueId, day, ct);
            }
        }
    }
}
