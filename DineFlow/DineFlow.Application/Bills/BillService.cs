using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Orders;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Common;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Bills;

/// <summary>
/// The table's bill and how guests split it (blueprint E5, E7): every non-rejected order on the
/// open check, what the ledger shows as paid, and the split rules in <see cref="CheckState"/>.
/// Money that's been charged is only ever counted from the ledger, which webhook-confirmed
/// payments write.
/// </summary>
public class BillService(
    IAppDbContext db,
    IPaymentProvider provider,
    IPaymentNotifier notifier,
    IOptions<PaymentOptions> paymentOptions,
    TimeProvider clock)
{
    public async Task<BillDto> GetForSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var (session, venue, location) = await LoadTableAsync(sessionId, ct);

        var check = await OpenCheckAsync(location.Id, tracked: false, ct)
            // Just paid in full? Show that bill as settled rather than "nothing ordered".
            ?? await db.Checks.AsNoTracking()
                .Where(c => c.ServiceLocationId == location.Id && c.Status == CheckStatus.Closed && c.ClosedAt >= session.CreatedAt)
                .OrderByDescending(c => c.ClosedAt)
                .FirstOrDefaultAsync(ct);

        return check is null
            ? Empty(venue, location)
            : ToDto(await LoadStateAsync(check, ct), sessionId, venue, location);
    }

    /// <summary>Sets (or removes) how many people the table splits the bill between equally.</summary>
    public async Task<BillDto> SetSplitAsync(Guid sessionId, int? people, CancellationToken ct)
    {
        var table = await LoadTableAsync(sessionId, ct);
        await Concurrency.RetryAsync(db, async () =>
        {
            var check = await OpenCheckAsync(table.Location.Id, tracked: true, ct)
                ?? throw AppException.Conflict("nothing_to_pay", "There's nothing to split yet.");
            var state = await LoadStateAsync(check, ct);
            if (people is not null && !state.CanSplitEquallyFor(sessionId))
                throw AppException.Conflict("equal_unavailable", "Your table is paying by item, so the bill can't be split equally.",
                    ToDto(state, sessionId, table.Venue, table.Location));

            try { check.SetSplitPeople(people, state.SharesTaken); }
            catch (DomainException ex) { throw AppException.BadRequest("invalid_split", ex.Message); }

            await db.SaveChangesAsync(ct);
            return true;
        });

        await notifier.TableBillChangedAsync(table.Venue.TenantId, table.Location.Id, "split", ct);
        return await GetForSessionAsync(sessionId, ct);
    }

    /// <summary>
    /// Holds exactly the given items for this guest while they choose (blueprint
    /// allocations/reserve): new ones are held, ones left out are let go, and ones another guest
    /// already has are reported back as taken. Holds lapse after <see cref="ItemClaim.HoldLifetime"/>.
    /// </summary>
    public async Task<ReserveResultDto> ReserveAsync(Guid sessionId, IReadOnlyList<UnitRef> units, CancellationToken ct)
    {
        var table = await LoadTableAsync(sessionId, ct);
        var wanted = units.Distinct().ToList();

        var taken = await Concurrency.RetryAsync(db, async () =>
        {
            var check = await OpenCheckAsync(table.Location.Id, tracked: true, ct)
                ?? throw AppException.Conflict("nothing_to_pay", "There's nothing left to pay on this bill.");
            var state = await LoadStateAsync(check, ct, trackClaims: true);
            var now = state.Now;

            // New items can't be picked once an amount has been paid; items already held can be kept.
            var mine = state.HeldBy(sessionId);
            if (!state.CanSplitByItem && wanted.Any(w => !mine.Any(c => c.OrderLineId == w.OrderLineId && c.UnitIndex == w.UnitIndex)))
                throw AppException.Conflict("items_unavailable",
                    "Part of this bill has already been paid as an amount, so it can't be split by item any more.",
                    ToDto(state, sessionId, table.Venue, table.Location));

            // Let go of anything this guest held but no longer wants.
            foreach (var claim in state.HeldBy(sessionId).Where(c => !wanted.Any(w => w.OrderLineId == c.OrderLineId && w.UnitIndex == c.UnitIndex)))
                db.ItemClaims.Remove(claim);

            var refused = new List<UnitRef>();
            foreach (var want in wanted)
            {
                var unit = state.Units.FirstOrDefault(u => u.Unit.OrderLineId == want.OrderLineId && u.Unit.UnitIndex == want.UnitIndex)?.Unit;
                if (unit is null) { refused.Add(want); continue; } // no longer on the bill, e.g. the order was rejected

                var claim = state.Claims.FirstOrDefault(c => c.Matches(unit.Value));
                if (claim is null)
                    db.ItemClaims.Add(new ItemClaim(table.Venue.TenantId, check.Id, unit.Value, sessionId, now));
                else if (!claim.IsActive(now))
                    claim.HoldFor(sessionId, unit.Value.AmountMinor, now);
                else if (claim.GuestSessionId == sessionId && claim.Status == ItemClaimStatus.Held)
                    claim.Refresh(now);
                else
                    refused.Add(want);
            }

            check.Touch();
            await db.SaveChangesAsync(ct);
            return refused;
        });

        await notifier.TableBillChangedAsync(table.Venue.TenantId, table.Location.Id, "items", ct);
        return new ReserveResultDto(await GetForSessionAsync(sessionId, ct), taken);
    }

    /// <summary>
    /// Called before staff reject an order on a bill (blueprint 13.1 "waiter voids an item after
    /// guest selects it"): items that are merely selected are let go, but an order can't be
    /// rejected once its items are paid or being paid, or if the table has already paid more
    /// than the bill would be without it. Those need a refund instead.
    /// </summary>
    internal async Task PrepareRejectionAsync(Order order, CancellationToken ct)
    {
        if (order.CheckId is not { } checkId) return;

        var check = await db.Checks.SingleAsync(c => c.Id == checkId, ct);
        var state = await LoadStateAsync(check, ct, trackClaims: true);
        var lineIds = order.Lines.Select(l => l.Id).ToHashSet();
        var claims = state.ActiveClaims.Where(c => lineIds.Contains(c.OrderLineId)).ToList();

        if (claims.Any(c => c.Status != ItemClaimStatus.Held))
            throw AppException.Conflict("order_paid", $"Guests have paid, or are paying, for items on order #{order.Number}. Refund it instead of rejecting it.");
        if (state.PaidMinor + state.InFlightMinor > state.TotalMinor - order.TotalMinor)
            throw AppException.Conflict("order_paid", $"The table has already paid for part of order #{order.Number}. Refund it instead of rejecting it.");

        db.ItemClaims.RemoveRange(claims);
        check.Touch();
    }

    /// <summary>The ledger's view of a check, used when a payment succeeds.</summary>
    internal async Task<BillTotals> TotalsAsync(Guid checkId, CancellationToken ct)
    {
        var total = await db.Orders.IgnoreQueryFilters()
            .Where(o => o.CheckId == checkId && o.Status != OrderStatus.Rejected)
            .SumAsync(o => (long?)o.TotalMinor, ct) ?? 0;
        var paid = await PaidAsync(checkId, ct);
        return new BillTotals(total, paid, Math.Max(0, total - paid));
    }

    internal async Task<CheckState> LoadStateAsync(Check check, CancellationToken ct, bool trackClaims = false)
    {
        // Webhooks have no tenant, so these queries go by check id without the tenant filter.
        var orders = await db.Orders.IgnoreQueryFilters().AsNoTracking()
            .Where(o => o.CheckId == check.Id && o.Status != OrderStatus.Rejected)
            .Include(o => o.Lines)
            .OrderBy(o => o.Number)
            .ToListAsync(ct);
        var payments = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.CheckId == check.Id).ToListAsync(ct);
        var claimsQuery = db.ItemClaims.IgnoreQueryFilters().Where(c => c.CheckId == check.Id);
        var claims = await (trackClaims ? claimsQuery : claimsQuery.AsNoTracking()).ToListAsync(ct);

        return new CheckState(check, orders, await PaidAsync(check.Id, ct), payments, claims, clock.GetUtcNow());
    }

    internal Task<Check?> OpenCheckAsync(Guid serviceLocationId, bool tracked, CancellationToken ct)
    {
        var query = db.Checks.Where(c => c.ServiceLocationId == serviceLocationId && c.Status == CheckStatus.Open);
        return (tracked ? query : query.AsNoTracking()).SingleOrDefaultAsync(ct);
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

    internal BillDto ToDto(CheckState state, Guid sessionId, Venue venue, ServiceLocation location)
    {
        var check = state.Check;
        var status = check.Status == CheckStatus.Closed || (state.Orders.Count > 0 && state.OpenMinor == 0)
            ? BillStatus.Paid
            : state.Orders.Count == 0 ? BillStatus.NoOrders : BillStatus.Open;
        var open = status == BillStatus.Open;

        var units = state.Units.Select(u =>
        {
            var claim = state.ActiveClaimOn(u.Unit);
            var unitStatus = claim?.Status switch
            {
                ItemClaimStatus.Held => BillUnitStatus.Selected,
                ItemClaimStatus.Paying => BillUnitStatus.Paying,
                ItemClaimStatus.Paid => BillUnitStatus.Paid,
                _ => open ? BillUnitStatus.Open : BillUnitStatus.Paid
            };
            return new BillUnitDto(u.Unit.OrderLineId, u.Unit.UnitIndex, u.Order.Number, u.Line.Name, u.Line.NameAr,
                u.Line.Modifiers.Select(m => m.Name).ToList(), u.Unit.AmountMinor, unitStatus, claim?.GuestSessionId == sessionId);
        }).ToList();

        var split = new SplitDto(check.SplitPeople, state.SharesTaken, state.SharesLeft,
            open && state.CanSplitEquallyFor(sessionId), open && state.CanSplitByItem,
            state.HeldBy(sessionId).Sum(c => c.AmountMinor));

        return new BillDto(check.Id, status, check.Currency, location.Type.ToString(), location.Label, location.DiningArea?.Name,
            state.Orders.Select(o => new BillOrderDto(o.Id, o.Number, o.Status, o.ToDto().Lines, o.TotalMinor)).ToList(),
            state.Orders.Sum(o => o.SubtotalMinor), state.Orders.Sum(o => o.ServiceChargeMinor), state.Orders.Sum(o => o.TaxMinor),
            state.Orders.Count == 0 || state.Orders.All(o => o.TaxIncluded), state.TotalMinor, state.PaidMinor, state.OpenMinor,
            open ? state.ReservedFor(sessionId) : 0, open ? state.AvailableFor(sessionId) : 0, state.InFlight.Any(),
            split, units, Tips(venue), provider.SupportedMethods, paymentOptions.Value.TermsVersion, paymentOptions.Value.TermsUrl);
    }

    private BillDto Empty(Venue venue, ServiceLocation location) =>
        new(null, BillStatus.NoOrders, venue.Currency, location.Type.ToString(), location.Label, location.DiningArea?.Name,
            [], 0, 0, 0, venue.PricesIncludeTax, 0, 0, 0, 0, 0, false, new SplitDto(null, 0, null, false, false, 0), [],
            Tips(venue), provider.SupportedMethods, paymentOptions.Value.TermsVersion, paymentOptions.Value.TermsUrl);

    private static TipSettingsDto Tips(Venue venue) =>
        new(venue.TipsEnabled, venue.TipPresetsBasisPoints, venue.MostCommonTipBasisPoints);

    private async Task<long> PaidAsync(Guid checkId, CancellationToken ct) =>
        await db.LedgerEntries.IgnoreQueryFilters()
            .Where(e => e.CheckId == checkId && (e.Type == LedgerEntryType.BillPayment || e.Type == LedgerEntryType.Discount))
            .SumAsync(e => (long?)e.AmountMinor, ct) ?? 0;
}

internal sealed record BillTotals(long TotalMinor, long PaidMinor, long RemainingMinor);
