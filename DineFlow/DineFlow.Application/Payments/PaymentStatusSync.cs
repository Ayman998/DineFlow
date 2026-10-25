using DineFlow.Application.Abstractions;
using DineFlow.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Payments;

/// <summary>
/// Asks the provider about payments and refunds that are still waiting, for providers whose webhooks
/// aren't guaranteed to arrive (Mastercard Gateway recommends confirming with Retrieve Order). What it
/// learns goes through <see cref="PaymentService.ApplyEventAsync"/>, exactly like a webhook, so an
/// outcome that arrives both ways is applied once. Providers that can't be asked are skipped.
/// </summary>
public class PaymentStatusSync(IAppDbContext db, IPaymentProvider provider, PaymentService payments, TimeProvider clock)
{
    /// <summary>Give the webhook a moment before asking.</summary>
    public static readonly TimeSpan FirstCheckAfter = TimeSpan.FromSeconds(10);
    /// <summary>Keep asking a little past the payment window, in case the guest finished at the last moment.</summary>
    public static readonly TimeSpan StopCheckingAfter = Payment.InFlightLifetime + TimeSpan.FromMinutes(10);
    public const int BatchSize = 25;

    /// <summary>Returns how many payments and refunds were settled by asking.</summary>
    public async Task<int> RefreshAsync(CancellationToken ct)
    {
        if (provider is not IPaymentStatusSource source) return 0;
        var now = clock.GetUtcNow();
        var (from, to) = (now - StopCheckingAfter, now - FirstCheckAfter);

        // Background work without a tenant: lookups bypass the tenant filter and are by provider id.
        var waiting = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.Provider == provider.Name && p.ProviderPaymentId != null
                && (p.Status == PaymentStatus.RequiresAction || p.Status == PaymentStatus.Processing)
                && p.CreatedAt >= from && p.CreatedAt <= to)
            .OrderBy(p => p.CreatedAt).Take(BatchSize).Select(p => p.ProviderPaymentId!).ToListAsync(ct);
        var refunds = await db.Refunds.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.Provider == provider.Name && r.Status == RefundStatus.Pending && r.ProviderRefundId != null && r.CreatedAt <= to)
            .Join(db.Payments.IgnoreQueryFilters(), r => r.PaymentId, p => p.Id, (r, p) => new { p.ProviderPaymentId, r.ProviderRefundId })
            .Take(BatchSize).ToListAsync(ct);

        var settled = 0;
        foreach (var providerPaymentId in waiting)
            if (await ApplyAsync(await source.GetPaymentStatusAsync(providerPaymentId, ct), ct)) settled++;
        foreach (var refund in refunds)
            if (await ApplyAsync(await source.GetRefundStatusAsync(refund.ProviderPaymentId!, refund.ProviderRefundId!, ct), ct)) settled++;
        return settled;
    }

    private async Task<bool> ApplyAsync(ProviderWebhookEvent? status, CancellationToken ct)
    {
        if (status is null || status.Status is not (PaymentStatus.Succeeded or PaymentStatus.Failed)) return false;
        db.ChangeTracker.Clear();
        await payments.ApplyEventAsync(status, ct);
        return true;
    }
}
