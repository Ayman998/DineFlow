using System.Globalization;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Transactions;
using DineFlow.Domain.Finance;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Pos;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Finance;

/// <summary>
/// Reconciles one business day at a venue (blueprint 7.2, 13 "day-end reconciliation"): every
/// payment and refund DineFlow confirmed that day is matched to the acquirer's settlement lines and,
/// where the venue has a POS, to what was posted there. What doesn't add up becomes a
/// <see cref="Discrepancy"/>; what was wrong before and is fine now clears itself. Safe to run again
/// at any time, e.g. after importing a settlement report.
/// </summary>
/// <remarks>
/// Acquirers settle a day or two later, so a payment with no settlement line yet only becomes a
/// discrepancy after <see cref="SettlementGrace"/>; until then it counts as "awaiting settlement".
/// Queries bypass the tenant filter (this also runs in the background) and are limited to the venue.
/// </remarks>
public class Reconciler(IAppDbContext db, TimeProvider clock)
{
    public static readonly TimeSpan SettlementGrace = TimeSpan.FromDays(3);
    /// <summary>How long a payment may wait for the POS before it counts as missing there.</summary>
    public static readonly TimeSpan PosGrace = TimeSpan.FromHours(1);

    public async Task<ReconciliationRun> ReconcileAsync(Guid venueId, DateOnly day, CancellationToken ct)
    {
        var venue = await db.Venues.IgnoreQueryFilters().AsNoTracking().SingleAsync(v => v.Id == venueId, ct);
        var zone = Zone(venue);
        var (from, to) = (Utc(day, zone), Utc(day.AddDays(1), zone));
        var now = clock.GetUtcNow();
        string Money(long minor) => TransactionService.FormatMoney(minor, venue.Currency);

        var payments = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => p.VenueId == venueId && p.Status == PaymentStatus.Succeeded && p.SucceededAt >= from && p.SucceededAt < to)
            .ToListAsync(ct);
        var refunds = await db.Refunds.IgnoreQueryFilters().AsNoTracking()
            .Where(r => r.VenueId == venueId && r.Status == RefundStatus.Succeeded && r.CompletedAt >= from && r.CompletedAt < to)
            .ToListAsync(ct);
        var refundPaymentIds = refunds.Select(r => r.PaymentId).Distinct().ToList();
        var refundPayments = await db.Payments.IgnoreQueryFilters().AsNoTracking()
            .Where(p => refundPaymentIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        // Every settlement line for this venue that could concern these payments or this day.
        var providerIds = payments.Select(p => p.ProviderPaymentId).Concat(refundPayments.Values.Select(p => p.ProviderPaymentId))
            .Where(id => id != null).Select(id => id!).Distinct().ToList();
        var references = payments.Select(p => p.Reference).Concat(refundPayments.Values.Select(p => p.Reference)).Distinct().ToList();
        var lines = await db.SettlementLines.IgnoreQueryFilters()
            .Join(db.Settlements.IgnoreQueryFilters(), l => l.SettlementId, s => s.Id, (l, s) => new { Line = l, s.VenueId, s.BatchRef })
            .Where(x => x.VenueId == venueId && ((x.Line.ProviderPaymentId != null && providerIds.Contains(x.Line.ProviderPaymentId))
                || (x.Line.Reference != null && references.Contains(x.Line.Reference)) || x.Line.TransactionDate == day))
            .Select(x => x.Line).ToListAsync(ct);

        bool Concerns(SettlementLine l, Payment p) =>
            (l.ProviderPaymentId != null && l.ProviderPaymentId == p.ProviderPaymentId) || (l.Reference != null && l.Reference == p.Reference);

        var found = new List<Discrepancy>();
        Discrepancy Found(DiscrepancyKind kind, string key, Guid? paymentId, Guid? lineId, string reference, long expected, long actual, string details)
        {
            var d = new Discrepancy(venue.TenantId, venue.Id, day, kind, key, paymentId, lineId, reference, expected, actual, details, now);
            found.Add(d);
            return d;
        }

        // Payments: each should be in exactly one payment line, for the amount the guest was charged.
        int matched = 0, awaiting = 0, settledCount = 0;
        long settledGross = 0, settledFees = 0;
        foreach (var p in payments)
        {
            var mine = lines.Where(l => l.Kind == SettlementLineKind.Payment && Concerns(l, p)).ToList();
            if (mine.Count == 0)
            {
                if (now - p.SucceededAt!.Value < SettlementGrace) awaiting++;
                else Found(DiscrepancyKind.NotSettled, $"NotSettled:{p.Id}", p.Id, null, p.Reference, p.AmountMinor, 0,
                    $"{p.Reference} ({Money(p.AmountMinor)}) was paid on {day:dd MMM} but isn't in any settlement from the acquirer.");
                continue;
            }
            foreach (var line in mine) line.MatchTo(p.Id);
            settledCount++;
            settledGross += mine.Sum(l => l.AmountMinor);
            settledFees += mine.Sum(l => l.FeeMinor);

            if (mine.Count > 1)
                Found(DiscrepancyKind.SettledTwice, $"Twice:{p.Id}", p.Id, mine[1].Id, p.Reference, p.AmountMinor, mine.Sum(l => l.AmountMinor),
                    $"{p.Reference} appears {mine.Count} times in settlements.");
            else if (mine[0].AmountMinor != p.AmountMinor)
                Found(DiscrepancyKind.AmountMismatch, $"Amount:{p.Id}", p.Id, mine[0].Id, p.Reference, p.AmountMinor, mine[0].AmountMinor,
                    $"{p.Reference}: DineFlow charged {Money(p.AmountMinor)}, the acquirer settled {Money(mine[0].AmountMinor)}.");
            else matched++;
        }

        // Settlement lines dated this day that match no successful payment at all.
        var lineRefs = lines.Where(l => l.Kind == SettlementLineKind.Payment && l.TransactionDate == day && l.PaymentId == null).ToList();
        if (lineRefs.Count > 0)
        {
            var ids = lineRefs.Where(l => l.ProviderPaymentId != null).Select(l => l.ProviderPaymentId!).ToList();
            var refs = lineRefs.Where(l => l.Reference != null).Select(l => l.Reference!).ToList();
            var known = await db.Payments.IgnoreQueryFilters().AsNoTracking()
                .Where(p => p.VenueId == venueId && p.Status == PaymentStatus.Succeeded
                    && ((p.ProviderPaymentId != null && ids.Contains(p.ProviderPaymentId)) || refs.Contains(p.Reference)))
                .ToListAsync(ct);
            foreach (var line in lineRefs)
            {
                var payment = known.FirstOrDefault(p => Concerns(line, p));
                if (payment is not null) { line.MatchTo(payment.Id); continue; } // paid on another day in DineFlow's time zone
                var label = line.Reference ?? line.ProviderPaymentId ?? "?";
                Found(DiscrepancyKind.UnknownToDineFlow, $"Unknown:{line.Id}", null, line.Id, label, 0, line.AmountMinor,
                    $"The acquirer settled {Money(line.AmountMinor)} for {label}, which DineFlow has no successful payment for.");
            }
        }

        // Refunds: each confirmed refund should be in the settlements, for the same amount. Without the
        // acquirer's refund id, a line on the same payment is used: same amount first, then any left over.
        var usedRefundLines = new HashSet<Guid>();
        foreach (var r in refunds.OrderBy(r => r.CompletedAt))
        {
            var payment = refundPayments[r.PaymentId];
            var candidates = lines.Where(l => l.Kind == SettlementLineKind.Refund && !usedRefundLines.Contains(l.Id)).ToList();
            var line = candidates.FirstOrDefault(l => l.ProviderRefundId != null && l.ProviderRefundId == r.ProviderRefundId)
                ?? candidates.FirstOrDefault(l => l.ProviderRefundId == null && Concerns(l, payment) && l.AmountMinor == r.AmountMinor)
                ?? candidates.FirstOrDefault(l => l.ProviderRefundId == null && Concerns(l, payment));
            if (line is not null) usedRefundLines.Add(line.Id);
            if (line is null)
            {
                if (now - r.CompletedAt!.Value >= SettlementGrace)
                    Found(DiscrepancyKind.RefundMismatch, $"RefundMissing:{r.Id}", payment.Id, null, payment.Reference, r.AmountMinor, 0,
                        $"A refund of {Money(r.AmountMinor)} on {payment.Reference} isn't in any settlement.");
                continue;
            }
            line.MatchTo(payment.Id);
            if (line.AmountMinor != r.AmountMinor)
                Found(DiscrepancyKind.RefundMismatch, $"RefundAmount:{r.Id}", payment.Id, line.Id, payment.Reference, r.AmountMinor, line.AmountMinor,
                    $"Refund on {payment.Reference}: DineFlow refunded {Money(r.AmountMinor)}, the acquirer took back {Money(line.AmountMinor)}.");
        }
        foreach (var line in lines.Where(l => l.Kind == SettlementLineKind.Refund && l.TransactionDate == day && l.PaymentId == null))
        {
            var label = line.Reference ?? line.ProviderPaymentId ?? line.ProviderRefundId ?? "?";
            Found(DiscrepancyKind.RefundMismatch, $"RefundUnknown:{line.Id}", null, line.Id, label, 0, line.AmountMinor,
                $"The acquirer took back {Money(line.AmountMinor)} for {label}, but DineFlow has no matching refund.");
        }

        // POS: a payment at a table should be in the venue's POS once it has had time to get there.
        var posted = 0;
        var tablePayments = payments.Where(p => p.CheckId != null).Select(p => p.Id).ToList();
        var postings = await db.PosCommands.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Kind == PosCommandKind.PostPayment && c.PaymentId != null && tablePayments.Contains(c.PaymentId.Value))
            .ToDictionaryAsync(c => c.PaymentId!.Value, ct);
        foreach (var p in payments.Where(p => postings.ContainsKey(p.Id)))
        {
            var posting = postings[p.Id];
            if (posting.Status == PosCommandStatus.Done) { posted++; continue; }
            if (posting.Status == PosCommandStatus.Failed || now - posting.CreatedAt >= PosGrace)
                Found(DiscrepancyKind.NotInPos, $"Pos:{p.Id}", p.Id, null, p.Reference, p.AmountMinor, 0,
                    posting.Status == PosCommandStatus.Failed
                        ? $"{p.Reference} was refused by the POS: {posting.LastError}"
                        : $"{p.Reference} still isn't in the POS ({posting.Attempts} attempts): {posting.LastError ?? "waiting"}");
        }

        // Keep the queue in step: new problems open, gone ones clear, ones people resolved stay resolved.
        var existing = await db.Discrepancies.IgnoreQueryFilters().Where(d => d.VenueId == venueId && d.BusinessDate == day).ToListAsync(ct);
        foreach (var d in found)
        {
            var known = existing.FirstOrDefault(e => e.Key == d.Key);
            if (known is null) db.Discrepancies.Add(d);
            else if (known.Status != DiscrepancyStatus.Resolved) known.Seen(d.ExpectedMinor, d.ActualMinor, d.Details);
        }
        foreach (var gone in existing.Where(e => found.All(d => d.Key != e.Key))) gone.Clear(now);

        var run = await db.ReconciliationRuns.IgnoreQueryFilters().SingleOrDefaultAsync(r => r.VenueId == venueId && r.BusinessDate == day, ct);
        if (run is null)
        {
            run = new ReconciliationRun(venue.TenantId, venue.Id, day, venue.Currency);
            db.ReconciliationRuns.Add(run);
        }
        var open = found.Count(d => existing.FirstOrDefault(e => e.Key == d.Key)?.Status != DiscrepancyStatus.Resolved);
        run.Record(now, payments.Count, payments.Sum(p => p.AmountMinor), payments.Sum(p => p.TipMinor), refunds.Sum(r => r.AmountMinor),
            settledCount, settledGross, settledFees, awaiting, posted, matched, open);

        await db.SaveChangesAsync(ct);
        return run;
    }

    /// <summary>The venue's local date a moment falls on.</summary>
    public static DateOnly LocalDate(DateTimeOffset at, Venue venue) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(at, Zone(venue)).DateTime);

    internal static TimeZoneInfo Zone(Venue venue) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var zone) ? zone : TimeZoneInfo.Utc;

    private static DateTimeOffset Utc(DateOnly day, TimeZoneInfo zone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified), zone), TimeSpan.Zero);

    internal static string Day(DateOnly day) => day.ToString("ddd d MMM yyyy", CultureInfo.InvariantCulture);
}
