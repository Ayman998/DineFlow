using DineFlow.Application.Abstractions;
using DineFlow.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.PaymentLinks;

/// <summary>
/// What's been paid on a link, what other customers are paying right now, and what's left. Only
/// webhook-confirmed payments count as paid; a payment becomes Succeeded in the same save that
/// writes its ledger entries.
/// </summary>
internal sealed record LinkTotals(long AmountMinor, long PaidMinor, long TipsMinor, int PaymentCount, long InFlightMinor)
{
    public long RemainingMinor => Math.Max(0, AmountMinor - PaidMinor);

    /// <summary>What a new payment can cover: what's left minus payments in progress.</summary>
    public long AvailableMinor => Math.Max(0, RemainingMinor - InFlightMinor);

    public static async Task<LinkTotals> LoadAsync(IAppDbContext db, PaymentLink link, DateTimeOffset now, CancellationToken ct)
    {
        var payments = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.PaymentLinkId == link.Id && p.Status != PaymentStatus.Failed && p.Status != PaymentStatus.Cancelled)
            .Select(p => new { p.Status, p.BillAmountMinor, p.TipMinor, p.CreatedAt })
            .ToListAsync(ct);
        var paid = payments.Where(p => p.Status == PaymentStatus.Succeeded).ToList();
        // Unfinished payments hold their amount only while they can still complete (Payment.InFlightLifetime).
        var inFlight = payments.Where(p => p.Status != PaymentStatus.Succeeded && now - p.CreatedAt < Payment.InFlightLifetime);

        return new LinkTotals(link.AmountMinor, paid.Sum(p => p.BillAmountMinor), paid.Sum(p => p.TipMinor), paid.Count,
            inFlight.Sum(p => p.BillAmountMinor));
    }

    /// <summary>
    /// Paying <paramref name="shares"/> of the link's amount split between <paramref name="people"/>.
    /// Each share rounds up, so the shares always cover the amount; the caller caps it at what's left,
    /// which makes the last share exactly what remains.
    /// </summary>
    public static long EqualShare(long amountMinor, int people, int shares) => (amountMinor + people - 1) / people * shares;
}
