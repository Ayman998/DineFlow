using System.Globalization;
using System.Text;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Transactions;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Common;
using DineFlow.Domain.Finance;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Finance;

/// <summary>
/// Settlements and reconciliation for the merchant (blueprint 9.2 /v1/merchant/settlements,
/// /v1/merchant/reconciliation/discrepancies; 4.4 "Reports: settlements, reconciliation, refunds,
/// fees, tips … and exports"): import the acquirer's settlement reports, see payouts with gross,
/// refunds, fees and net, reconcile days against DineFlow's ledger and the POS, work through the
/// discrepancy queue, and export the ledger. Imports and resolutions are audit-logged.
/// </summary>
public class FinanceService(IAppDbContext db, ITenantContext tenant, Reconciler reconciler, IPaymentProvider provider, TimeProvider clock)
{
    public const int MaxImportBytes = 5 * 1024 * 1024;
    /// <summary>The sandbox acquirer's pretend fee: 2.25% plus a fixed 0.1 in the currency per payment.</summary>
    public const int SandboxFeeBasisPoints = 225;

    // ---- Settlements ----

    public async Task<SettlementPageDto> SettlementsAsync(Guid venueId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var query = db.Settlements.AsNoTracking().Where(s => s.VenueId == venue.Id);
        if (from is { } f) query = query.Where(s => s.PayoutDate >= f);
        if (to is { } t) query = query.Where(s => s.PayoutDate <= t);
        var settlements = await query.OrderByDescending(s => s.PayoutDate).ThenByDescending(s => s.CreatedAt).Take(200).ToListAsync(ct);
        var counts = await CountsAsync(settlements.Select(s => s.Id).ToList(), ct);

        return new SettlementPageDto(settlements.Select(s => ToDto(s, counts.GetValueOrDefault(s.Id))).ToList(),
            new SettlementTotalsDto(settlements.Sum(s => s.GrossMinor), settlements.Sum(s => s.RefundsMinor),
                settlements.Sum(s => s.FeesMinor), settlements.Sum(s => s.NetMinor)));
    }

    public async Task<SettlementDetailDto> SettlementAsync(Guid settlementId, CancellationToken ct)
    {
        var settlement = await db.Settlements.AsNoTracking().SingleOrDefaultAsync(s => s.Id == settlementId, ct)
            ?? throw AppException.NotFound("settlement_not_found", "Settlement not found.");
        var lines = await db.SettlementLines.AsNoTracking().Where(l => l.SettlementId == settlement.Id)
            .OrderBy(l => l.TransactionDate).ThenBy(l => l.Kind).ToListAsync(ct);
        var paymentIds = lines.Where(l => l.PaymentId != null).Select(l => l.PaymentId!.Value).Distinct().ToList();
        var references = await db.Payments.AsNoTracking().Where(p => paymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, p => p.Reference, ct);

        return new SettlementDetailDto(ToDto(settlement, lines.Count), lines.Select(l => new SettlementLineDto(l.Id, l.Kind, l.TransactionDate,
            l.ProviderPaymentId, l.Reference, l.AmountMinor, l.FeeMinor, l.PaymentId,
            l.PaymentId is { } id ? references.GetValueOrDefault(id) : null)).ToList());
    }

    /// <summary>
    /// Imports an acquirer settlement report (CSV, see <see cref="SettlementCsv"/>). Each batch in the
    /// file becomes a settlement; a batch that was imported before is refused, so a file can't be
    /// counted twice. The days it covers are reconciled straight away.
    /// </summary>
    public async Task<SettlementImportResultDto> ImportAsync(Guid venueId, string fileName, string content, Guid staffUserId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        IReadOnlyList<SettlementRow> rows;
        try { rows = SettlementCsv.Parse(content, venue.Currency); }
        catch (FormatException ex) { throw AppException.BadRequest("invalid_settlement_file", ex.Message); }

        var batches = rows.GroupBy(r => r.BatchRef).ToList();
        var batchRefs = batches.Select(b => b.Key).ToList();
        var existing = await db.Settlements.AsNoTracking()
            .Where(s => s.VenueId == venue.Id && s.Provider == provider.Name && batchRefs.Contains(s.BatchRef)).Select(s => s.BatchRef).ToListAsync(ct);
        if (existing.Count > 0)
            throw AppException.Conflict("settlement_exists", $"Already imported: {string.Join(", ", existing)}. Nothing was changed.");

        var now = clock.GetUtcNow();
        var created = new List<Settlement>();
        try
        {
            foreach (var batch in batches)
            {
                if (batch.Select(r => r.PayoutDate).Distinct().Count() > 1)
                    throw new DomainException($"Batch {batch.Key} has more than one payout date.");
                created.Add(new Settlement(venue.TenantId, venue.Id, provider.Name, batch.Key, venue.Currency, batch.First().PayoutDate,
                    SettlementStatus.Expected, SettlementSource.File, fileName, staffUserId,
                    batch.Select(r => new SettlementLine(venue.TenantId, r.Kind, r.TransactionDate, r.OrderId, r.Reference, r.RefundId, r.AmountMinor, r.FeeMinor)),
                    now));
            }
        }
        catch (DomainException ex)
        {
            throw AppException.BadRequest("invalid_settlement_file", ex.Message);
        }

        db.Settlements.AddRange(created);
        foreach (var s in created) db.AuditEntries.Add(new AuditEntry(venue.TenantId, staffUserId, "settlement.imported", nameof(Settlement), s.Id, now));
        await db.SaveChangesAsync(ct);

        var days = await ReconcileAffectedAsync(venue, rows.Select(r => r.TransactionDate), ct);
        return new SettlementImportResultDto(created.Select(s => ToDto(s, s.Lines.Count)).ToList(), rows.Count, days, await OpenAsync(venue.Id, ct));
    }

    /// <summary>
    /// Development only: makes the sandbox acquirer pay out everything it hasn't settled yet, with
    /// its pretend fee, as a real acquirer's report would. Then reconciles the days it covers.
    /// </summary>
    public async Task<SettlementImportResultDto> SandboxPayoutAsync(Guid venueId, Guid staffUserId, CancellationToken ct)
    {
        if (provider.Name != "sandbox") throw AppException.Conflict("not_sandbox", "Only the sandbox provider can produce a payout.");
        var venue = await VenueAsync(venueId, ct);
        var settled = db.SettlementLines.AsNoTracking().Where(l => l.ProviderPaymentId != null).Select(l => l.ProviderPaymentId);
        var settledRefunds = db.SettlementLines.AsNoTracking().Where(l => l.ProviderRefundId != null).Select(l => l.ProviderRefundId);

        var payments = await db.Payments.AsNoTracking().Where(p => p.VenueId == venue.Id && p.Provider == provider.Name
            && p.Status == PaymentStatus.Succeeded && !settled.Contains(p.ProviderPaymentId)).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.VenueId == venue.Id && r.Provider == provider.Name
            && r.Status == RefundStatus.Succeeded && !settledRefunds.Contains(r.ProviderRefundId)).ToListAsync(ct);
        if (payments.Count == 0 && refunds.Count == 0)
            throw AppException.Conflict("nothing_to_settle", "Everything the sandbox took has already been paid out.");

        var refundPaymentIds = refunds.Select(r => r.PaymentId).ToList();
        var refundPayments = await db.Payments.AsNoTracking().Where(p => refundPaymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var fixedFee = (long)Math.Pow(10, TransactionService.MinorDigits(venue.Currency) - 1);
        var now = clock.GetUtcNow();
        var lines = payments.Select(p => new SettlementLine(venue.TenantId, SettlementLineKind.Payment, Reconciler.LocalDate(p.SucceededAt!.Value, venue),
                p.ProviderPaymentId, p.Reference, null, p.AmountMinor, (long)Math.Round(p.AmountMinor * SandboxFeeBasisPoints / 10_000m) + fixedFee))
            .Concat(refunds.Select(r => new SettlementLine(venue.TenantId, SettlementLineKind.Refund, Reconciler.LocalDate(r.CompletedAt!.Value, venue),
                refundPayments[r.PaymentId].ProviderPaymentId, refundPayments[r.PaymentId].Reference, r.ProviderRefundId, r.AmountMinor, 0)))
            .ToList();

        var today = Reconciler.LocalDate(now, venue);
        var settlement = new Settlement(venue.TenantId, venue.Id, provider.Name, $"SBX-{now:yyyyMMddHHmmss}", venue.Currency, today,
            SettlementStatus.Paid, SettlementSource.Provider, null, staffUserId, lines, now);
        db.Settlements.Add(settlement);
        db.AuditEntries.Add(new AuditEntry(venue.TenantId, staffUserId, "settlement.imported", nameof(Settlement), settlement.Id, now));
        await db.SaveChangesAsync(ct);

        var days = await ReconcileAffectedAsync(venue, lines.Select(l => l.TransactionDate), ct);
        return new SettlementImportResultDto([ToDto(settlement, lines.Count)], lines.Count, days, await OpenAsync(venue.Id, ct));
    }

    // ---- Reconciliation ----

    public async Task<IReadOnlyList<ReconciliationRunDto>> RunsAsync(Guid venueId, DateOnly? from, DateOnly? to, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var today = Reconciler.LocalDate(clock.GetUtcNow(), venue);
        var (start, end) = (from ?? today.AddDays(-30), to ?? today);
        var runs = await db.ReconciliationRuns.AsNoTracking()
            .Where(r => r.VenueId == venue.Id && r.BusinessDate >= start && r.BusinessDate <= end)
            .OrderByDescending(r => r.BusinessDate).ToListAsync(ct);
        return runs.Select(r => new ReconciliationRunDto(r.BusinessDate, r.RanAt, r.Currency, r.Payments, r.LedgerGrossMinor, r.LedgerTipsMinor,
            r.LedgerRefundsMinor, r.Settled, r.SettledGrossMinor, r.SettledFeesMinor, r.AwaitingSettlement, r.PostedToPos, r.Matched,
            r.OpenDiscrepancies)).ToList();
    }

    /// <summary>Reconciles one day now (yesterday, venue time, when no day is given).</summary>
    public async Task<ReconciliationRunDto> ReconcileAsync(ReconcileRequest request, CancellationToken ct)
    {
        var venue = await VenueAsync(request.VenueId, ct);
        var day = request.Day ?? Reconciler.LocalDate(clock.GetUtcNow(), venue).AddDays(-1);
        if (day > Reconciler.LocalDate(clock.GetUtcNow(), venue)) throw AppException.BadRequest("future_day", "That day hasn't happened yet.");
        await reconciler.ReconcileAsync(venue.Id, day, ct);
        return (await RunsAsync(venue.Id, day, day, ct)).Single();
    }

    public async Task<DiscrepancyPageDto> DiscrepanciesAsync(DiscrepancyQuery query, CancellationToken ct)
    {
        var venue = await VenueAsync(query.VenueId, ct);
        var all = db.Discrepancies.AsNoTracking().Where(d => d.VenueId == venue.Id);
        var filtered = query.Status is { } status ? all.Where(d => d.Status == status) : all;
        var total = await filtered.CountAsync(ct);
        var page = await filtered.OrderByDescending(d => d.BusinessDate).ThenBy(d => d.Kind)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        var staffIds = page.Where(d => d.ResolvedByStaffId != null).Select(d => d.ResolvedByStaffId!.Value).Distinct().ToList();
        var staff = await db.StaffUsers.AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);

        return new DiscrepancyPageDto(page.Select(d => ToDto(d, d.ResolvedByStaffId is { } by ? staff.GetValueOrDefault(by) : null)).ToList(),
            total, query.Page, query.PageSize, await all.CountAsync(d => d.Status == DiscrepancyStatus.Open, ct));
    }

    /// <summary>Accepts a discrepancy with a reason (e.g. a fee dispute raised with the acquirer); audit-logged.</summary>
    public async Task<DiscrepancyDto> ResolveAsync(Guid discrepancyId, string note, Guid staffUserId, CancellationToken ct)
    {
        var discrepancy = await db.Discrepancies.SingleOrDefaultAsync(d => d.Id == discrepancyId, ct)
            ?? throw AppException.NotFound("discrepancy_not_found", "Not found.");
        var now = clock.GetUtcNow();
        try { discrepancy.Resolve(staffUserId, note, now); }
        catch (DomainException ex) { throw AppException.Conflict("already_resolved", ex.Message); }

        db.AuditEntries.Add(new AuditEntry(discrepancy.TenantId, staffUserId, "reconciliation.resolved", nameof(Discrepancy), discrepancy.Id, now));
        var run = await db.ReconciliationRuns.SingleOrDefaultAsync(r => r.VenueId == discrepancy.VenueId && r.BusinessDate == discrepancy.BusinessDate, ct);
        await db.SaveChangesAsync(ct);
        if (run is not null) await reconciler.ReconcileAsync(discrepancy.VenueId, discrepancy.BusinessDate, ct); // refresh the day's count

        var by = await db.StaffUsers.AsNoTracking().Where(s => s.Id == staffUserId).Select(s => s.DisplayName).SingleOrDefaultAsync(ct);
        return ToDto(discrepancy, by);
    }

    /// <summary>Open discrepancies, for the overview's "Needs attention".</summary>
    public Task<int> OpenAsync(Guid venueId, CancellationToken ct) =>
        db.Discrepancies.AsNoTracking().CountAsync(d => d.VenueId == venueId && d.Status == DiscrepancyStatus.Open, ct);

    // ---- Export ----

    /// <summary>
    /// The ledger for a period as CSV (blueprint E14 "ledger exports"): every money movement with its
    /// payment, where it was paid, and the settlement batch and fee once the acquirer has settled it.
    /// </summary>
    public async Task<string> LedgerCsvAsync(Guid venueId, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        if (to < from || to.DayNumber - from.DayNumber > 366) throw AppException.BadRequest("invalid_range", "Choose up to a year, start before end.");
        var zone = Reconciler.Zone(venue);
        var (start, end) = (Utc(from, zone), Utc(to.AddDays(1), zone));

        var entries = await db.LedgerEntries.AsNoTracking().Where(e => e.VenueId == venue.Id && e.CreatedAt >= start && e.CreatedAt < end)
            .OrderBy(e => e.CreatedAt).ToListAsync(ct);
        var paymentIds = entries.Select(e => e.PaymentId).Distinct().ToList();
        var payments = await db.Payments.AsNoTracking().Where(p => paymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var checkIds = payments.Values.Where(p => p.CheckId != null).Select(p => p.CheckId!.Value).Distinct().ToList();
        var linkIds = payments.Values.Where(p => p.PaymentLinkId != null).Select(p => p.PaymentLinkId!.Value).Distinct().ToList();
        var places = await db.Checks.AsNoTracking().Where(c => checkIds.Contains(c.Id))
            .Join(db.ServiceLocations.AsNoTracking(), c => c.ServiceLocationId, l => l.Id, (c, l) => new { c.Id, l.Type, l.Label })
            .ToDictionaryAsync(x => x.Id, x => $"{(x.Type == ServiceLocationType.Table ? "Table" : x.Type.ToString())} {x.Label}", ct);
        var links = await db.PaymentLinks.AsNoTracking().Where(l => linkIds.Contains(l.Id))
            .ToDictionaryAsync(l => l.Id, l => l.Code, ct);
        var settled = await db.SettlementLines.AsNoTracking().Where(l => l.PaymentId != null && paymentIds.Contains(l.PaymentId.Value) && l.Kind == SettlementLineKind.Payment)
            .Join(db.Settlements.AsNoTracking(), l => l.SettlementId, s => s.Id, (l, s) => new { PaymentId = l.PaymentId!.Value, s.BatchRef, s.PayoutDate, l.FeeMinor })
            .ToListAsync(ct);

        var digits = TransactionService.MinorDigits(venue.Currency);
        string Major(long minor) => (minor / (decimal)Math.Pow(10, digits)).ToString($"F{digits}", CultureInfo.InvariantCulture);
        var csv = new StringBuilder("Date,Time,Type,Payment,Where,Amount,Currency,Settlement batch,Payout date,Fee\r\n");
        foreach (var e in entries)
        {
            var local = TimeZoneInfo.ConvertTime(e.CreatedAt, zone);
            var p = payments.GetValueOrDefault(e.PaymentId);
            var where = p is null ? "" : p.CheckId is { } c ? places.GetValueOrDefault(c, "") : p.PaymentLinkId is { } l ? $"Link {links.GetValueOrDefault(l)}" : "";
            var s = e.Type == LedgerEntryType.BillPayment ? settled.FirstOrDefault(x => x.PaymentId == e.PaymentId) : null;
            var signed = e.Type == LedgerEntryType.Refund ? -e.AmountMinor : e.AmountMinor;
            csv.AppendJoin(',', local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.ToString("HH:mm", CultureInfo.InvariantCulture),
                e.Type.ToString(), p?.Reference ?? "", where, Major(signed), e.Currency, s?.BatchRef ?? "",
                s?.PayoutDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "", s is null ? "" : Major(s.FeeMinor));
            csv.Append("\r\n");
        }
        return csv.ToString();
    }

    // ---- Helpers ----

    /// <summary>Reconciles the days a settlement covers, plus the day before (acquirers date in their own time zone).</summary>
    private async Task<IReadOnlyList<DateOnly>> ReconcileAffectedAsync(Venue venue, IEnumerable<DateOnly> dates, CancellationToken ct)
    {
        var today = Reconciler.LocalDate(clock.GetUtcNow(), venue);
        var days = dates.SelectMany(d => new[] { d.AddDays(-1), d }).Where(d => d <= today).Distinct().Order().ToList();
        foreach (var day in days) await reconciler.ReconcileAsync(venue.Id, day, ct);
        return days;
    }

    private async Task<Dictionary<Guid, int>> CountsAsync(List<Guid> ids, CancellationToken ct) =>
        await db.SettlementLines.AsNoTracking().Where(l => ids.Contains(l.SettlementId))
            .GroupBy(l => l.SettlementId).Select(g => new { g.Key, Count = g.Count() }).ToDictionaryAsync(g => g.Key, g => g.Count, ct);

    private static SettlementDto ToDto(Settlement s, int lines) => new(s.Id, s.BatchRef, s.Provider, s.PayoutDate, s.Status, s.Source, s.FileName,
        s.Currency, lines, s.GrossMinor, s.RefundsMinor, s.FeesMinor, s.NetMinor, s.CreatedAt);

    private static DiscrepancyDto ToDto(Discrepancy d, string? resolvedBy) => new(d.Id, d.BusinessDate, d.Kind, d.Reference, d.ExpectedMinor,
        d.ActualMinor, d.DifferenceMinor, d.Details, d.Status, d.PaymentId, d.CreatedAt, d.ResolvedAt, resolvedBy, d.Note);

    private async Task<Venue> VenueAsync(Guid venueId, CancellationToken ct)
    {
        tenant.RequireTenantId();
        return await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId, ct) ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
    }

    private static DateTimeOffset Utc(DateOnly day, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);
}
