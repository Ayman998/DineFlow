using DineFlow.Application.Abstractions;
using DineFlow.Application.Pos;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace DineFlow.Infrastructure.Pos;

/// <summary>The connectors this build has, by provider key.</summary>
public class PosConnectorRegistry(IEnumerable<IPosConnector> connectors) : IPosConnectorRegistry
{
    public IReadOnlyList<IPosConnector> All { get; } = connectors.ToList();

    public IPosConnector? Find(string provider) => All.FirstOrDefault(c => c.Provider == provider);
}

/// <summary>Encrypts POS tokens with ASP.NET Core Data Protection before they're stored.</summary>
public class DataProtectionSecretProtector(IDataProtectionProvider provider) : ISecretProtector
{
    private readonly IDataProtector _protector = provider.CreateProtector("DineFlow.PosAccessToken.v1");

    public string Protect(string secret) => _protector.Protect(secret);

    public string Unprotect(string protectedSecret) => _protector.Unprotect(protectedSecret);
}

/// <summary>Every few seconds, sends what's due to the POS (see <see cref="PosDispatcher"/>). Errors are logged and the next pass carries on.</summary>
public class PosWorker(IServiceScopeFactory scopes, ILogger<PosWorker> logger) : BackgroundService
{
    public static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(Interval);
        do
        {
            try
            {
                int done;
                do
                {
                    using var scope = scopes.CreateScope();
                    done = await scope.ServiceProvider.GetRequiredService<PosDispatcher>().RunDueAsync(stoppingToken);
                } while (done == PosDispatcher.BatchSize && !stoppingToken.IsCancellationRequested);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "The POS worker's pass failed; it will try again.");
            }
        } while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
