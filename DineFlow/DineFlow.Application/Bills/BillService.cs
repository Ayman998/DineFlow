using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Orders;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Bills;

public enum BillStatus
{
    /// <summary>Nothing has been ordered at this table yet.</summary>
    NoOrders,
    Open,
    /// <summary>The table's last bill has been settled.</summary>
    Paid
}

/// <summary>The table's bill for the Pay Bill screen (blueprint 9.1 GET …/bill, 3.2).</summary>
public record BillDto(
    Guid? CheckId,
    BillStatus Status,
    string Currency,
    string LocationType,
    string LocationLabel,
    string? AreaName,
    IReadOnlyList<BillOrderDto> Orders,
    long SubtotalMinor,
    long ServiceChargeMinor,
    long TaxMinor,
    bool TaxIncluded,
    long TotalMinor,
    long PaidMinor,
    long RemainingMinor,
    /// <summary>Someone at the table has started paying and hasn't finished yet.</summary>
    bool PaymentInProgress,
    TipSettingsDto Tips,
    IReadOnlyList<PaymentMethod> Methods,
    string TermsVersion,
    string TermsUrl);

public record BillOrderDto(Guid Id, int Number, OrderStatus Status, IReadOnlyList<OrderLineDto> Lines, long TotalMinor);

public record TipSettingsDto(bool Enabled, IReadOnlyList<int> PresetsBasisPoints, int? MostCommonBasisPoints);

/// <summary>
/// Works out what a table owes: every non-rejected order on its open check, minus what the ledger
/// shows has been paid or discounted. Money that's been charged is only ever counted from the
/// ledger, which successful (webhook-confirmed) payments write.
/// </summary>
public class BillService(
    IAppDbContext db,
    IPaymentProvider provider,
    IOptions<PaymentOptions> paymentOptions,
    TimeProvider clock)
{
    public async Task<BillDto> GetForSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var (session, venue, location) = await LoadTableAsync(sessionId, ct);

        var check = await db.Checks.SingleOrDefaultAsync(c => c.ServiceLocationId == location.Id && c.Status == CheckStatus.Open, ct)
            // Just paid in full? Show that bill as settled rather than "nothing ordered".
            ?? await db.Checks
                .Where(c => c.ServiceLocationId == location.Id && c.Status == CheckStatus.Closed && c.ClosedAt >= session.CreatedAt)
                .OrderByDescending(c => c.ClosedAt)
                .FirstOrDefaultAsync(ct);

        return await BuildAsync(check, venue, location, ct);
    }

    /// <summary>The current totals for a check; used when paying and after a payment succeeds.</summary>
    internal async Task<BillTotals> TotalsAsync(Guid checkId, CancellationToken ct)
    {
        var orders = await db.Orders.IgnoreQueryFilters()
            .Where(o => o.CheckId == checkId && o.Status != OrderStatus.Rejected)
            .Select(o => new { o.SubtotalMinor, o.ServiceChargeMinor, o.TaxMinor, o.TaxIncluded, o.TotalMinor })
            .ToListAsync(ct);

        var paid = await db.LedgerEntries.IgnoreQueryFilters()
            .Where(e => e.CheckId == checkId && (e.Type == LedgerEntryType.BillPayment || e.Type == LedgerEntryType.Discount))
            .SumAsync(e => (long?)e.AmountMinor, ct) ?? 0;

        var total = orders.Sum(o => o.TotalMinor);
        return new BillTotals(
            orders.Sum(o => o.SubtotalMinor), orders.Sum(o => o.ServiceChargeMinor), orders.Sum(o => o.TaxMinor),
            orders.Count == 0 || orders.All(o => o.TaxIncluded), total, paid, Math.Max(0, total - paid), orders.Count);
    }

    internal async Task<TableContext> LoadTableAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.GuestSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.IsExpired(clock.GetUtcNow()))
            throw AppException.Unauthorized("session_expired", "Your session has ended. Scan the QR code again.");

        var venue = await db.Venues.Include(v => v.Tenant).SingleAsync(v => v.Id == session.VenueId, ct);
        if (!venue.PayAtTableEnabled)
            throw AppException.NotFound("pay_unavailable", "This venue doesn't take payments from the table.");

        var location = await db.ServiceLocations.Include(l => l.DiningArea).SingleAsync(l => l.Id == session.ServiceLocationId, ct);
        return new TableContext(session, venue, location);
    }

    private async Task<BillDto> BuildAsync(Check? check, Venue venue, ServiceLocation location, CancellationToken ct)
    {
        var tips = new TipSettingsDto(venue.TipsEnabled, venue.TipPresetsBasisPoints, venue.MostCommonTipBasisPoints);
        var terms = paymentOptions.Value;

        if (check is null)
        {
            return new BillDto(null, BillStatus.NoOrders, venue.Currency, location.Type.ToString(), location.Label,
                location.DiningArea?.Name, [], 0, 0, 0, venue.PricesIncludeTax, 0, 0, 0, false, tips,
                provider.SupportedMethods, terms.TermsVersion, terms.TermsUrl);
        }

        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CheckId == check.Id && o.Status != OrderStatus.Rejected)
            .Include(o => o.Lines)
            .OrderBy(o => o.Number)
            .ToListAsync(ct);
        var totals = await TotalsAsync(check.Id, ct);

        var now = clock.GetUtcNow();
        var inFlightSince = now - Payment.InFlightLifetime;
        var paymentInProgress = await db.Payments.AnyAsync(p => p.CheckId == check.Id
            && (p.Status == PaymentStatus.Created || p.Status == PaymentStatus.RequiresAction || p.Status == PaymentStatus.Processing)
            && p.CreatedAt > inFlightSince, ct);

        var status = check.Status == CheckStatus.Closed || (totals.OrderCount > 0 && totals.RemainingMinor == 0)
            ? BillStatus.Paid
            : totals.OrderCount == 0 ? BillStatus.NoOrders : BillStatus.Open;

        return new BillDto(check.Id, status, check.Currency, location.Type.ToString(), location.Label, location.DiningArea?.Name,
            orders.Select(o => new BillOrderDto(o.Id, o.Number, o.Status, o.ToDto().Lines, o.TotalMinor)).ToList(),
            totals.SubtotalMinor, totals.ServiceChargeMinor, totals.TaxMinor, totals.TaxIncluded, totals.TotalMinor,
            totals.PaidMinor, totals.RemainingMinor, paymentInProgress, tips, provider.SupportedMethods,
            terms.TermsVersion, terms.TermsUrl);
    }
}

internal sealed record BillTotals(
    long SubtotalMinor, long ServiceChargeMinor, long TaxMinor, bool TaxIncluded,
    long TotalMinor, long PaidMinor, long RemainingMinor, int OrderCount);
