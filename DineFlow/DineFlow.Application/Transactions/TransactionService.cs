using System.Globalization;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Common;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Transactions;

/// <summary>
/// The merchant's Transactions area (blueprint 4.4, 9.2 /v1/merchant/transactions/*): searchable
/// payments with their refunds, each with a complete timeline, and full or partial refunds that
/// only count once the provider confirms them.
/// </summary>
public class TransactionService(IAppDbContext db, ITenantContext tenant, IPaymentProvider provider, TimeProvider clock)
{
    public static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);

    public async Task<TransactionPageDto> ListAsync(TransactionQuery query, CancellationToken ct)
    {
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == query.VenueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var (from, to) = Range(query.From, query.To);

        var payments = db.Payments.AsNoTracking().Where(p => p.VenueId == venue.Id && p.CreatedAt >= from && p.CreatedAt < to);
        payments = await SearchAsync(payments, venue.Id, venue.Currency, query.Search, ct);

        var refunds = db.Refunds.AsNoTracking();
        payments = query.Status switch
        {
            TransactionFilter.Succeeded => payments.Where(p => p.Status == PaymentStatus.Succeeded),
            TransactionFilter.Refunded => payments.Where(p => refunds.Any(r => r.PaymentId == p.Id && r.Status != RefundStatus.Failed)),
            TransactionFilter.Pending => payments.Where(p => p.Status == PaymentStatus.Created || p.Status == PaymentStatus.RequiresAction || p.Status == PaymentStatus.Processing),
            TransactionFilter.Failed => payments.Where(p => p.Status == PaymentStatus.Failed || p.Status == PaymentStatus.Cancelled),
            _ => payments
        };

        var total = await payments.CountAsync(ct);
        var succeeded = payments.Where(p => p.Status == PaymentStatus.Succeeded);
        var gross = await succeeded.SumAsync(p => (long?)p.AmountMinor, ct) ?? 0;
        var tips = await succeeded.SumAsync(p => (long?)p.TipMinor, ct) ?? 0;
        var refunded = await refunds.Where(r => r.Status == RefundStatus.Succeeded && succeeded.Any(p => p.Id == r.PaymentId))
            .SumAsync(r => (long?)r.AmountMinor, ct) ?? 0;
        var totals = new TransactionTotalsDto(await succeeded.CountAsync(ct), gross, tips, refunded, gross - refunded);

        var page = await payments.OrderByDescending(p => p.CreatedAt)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new TransactionPageDto(await RowsAsync(page, ct), total, query.Page, query.PageSize, totals);
    }

    public async Task<TransactionDetailDto> GetAsync(Guid paymentId, CancellationToken ct)
    {
        var payment = await db.Payments.AsNoTracking().SingleOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw AppException.NotFound("transaction_not_found", "Transaction not found.");
        var row = (await RowsAsync([payment], ct)).Single();

        var check = await db.Checks.AsNoTracking().SingleAsync(c => c.Id == payment.CheckId, ct);
        var location = await db.ServiceLocations.AsNoTracking().Include(l => l.DiningArea).SingleAsync(l => l.Id == check.ServiceLocationId, ct);
        var orders = await db.Orders.AsNoTracking().Where(o => o.CheckId == payment.CheckId)
            .Include(o => o.Lines).OrderBy(o => o.Number).ToListAsync(ct);
        var refunds = await db.Refunds.AsNoTracking().Where(r => r.PaymentId == payment.Id).OrderBy(r => r.CreatedAt).ToListAsync(ct);
        var staffIds = refunds.Select(r => r.RequestedByStaffId).Distinct().ToList();
        var staff = await db.StaffUsers.AsNoTracking().Where(s => staffIds.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
        var review = await db.Reviews.AsNoTracking().Where(r => r.PaymentId == payment.Id)
            .Select(r => new { r.Id, r.Rating, r.CreatedAt }).SingleOrDefaultAsync(ct);
        var itemCount = await db.ItemClaims.AsNoTracking().CountAsync(c => c.PaymentId == payment.Id, ct);

        var refundDtos = refunds.Select(r => new RefundDto(r.Id, r.AmountMinor, r.Reason, r.Status, r.FailureReason,
            staff.GetValueOrDefault(r.RequestedByStaffId), r.CreatedAt, r.CompletedAt)).ToList();

        return new TransactionDetailDto(row, location.DiningArea?.Name,
            payment.BillAmountMinor, payment.DiscountMinor, payment.PromoCode, payment.TipMinor, payment.AmountMinor,
            Refundable(payment, refunds), payment.Shares, payment.SplitPeople, itemCount, payment.FailureReason,
            review?.Rating, review?.Id,
            orders.Select(o => new TransactionOrderDto(o.Id, o.Number, o.Status, o.CreatedAt,
                o.Lines.OrderBy(l => l.Position).Select(l => new TransactionOrderLineDto(l.Name, l.Quantity, l.LineTotalMinor)).ToList(),
                o.TotalMinor)).ToList(),
            refundDtos,
            await TimelineAsync(payment, location.DisplayName, orders, refunds, staff, review is null ? null : (review.CreatedAt, review.Rating), ct));
    }

    /// <summary>
    /// Gives money back for a successful payment (full or partial; owners and managers only).
    /// The refund is pending until the provider confirms it. Retrying with the same
    /// clientRequestId returns the same refund, and refunds can never add up to more than was paid.
    /// </summary>
    public async Task<TransactionDetailDto> RefundAsync(Guid paymentId, RefundRequest request, Guid staffUserId, CancellationToken ct)
    {
        var refund = await Concurrency.RetryAsync(db, async () =>
        {
            var existing = await db.Refunds.SingleOrDefaultAsync(r => r.PaymentId == paymentId && r.ClientRequestId == request.ClientRequestId, ct);
            if (existing is not null) return existing;

            var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == paymentId, ct)
                ?? throw AppException.NotFound("transaction_not_found", "Transaction not found.");
            var earlier = await db.Refunds.Where(r => r.PaymentId == paymentId).ToListAsync(ct);

            Refund created;
            try
            {
                created = new Refund(payment, request.ClientRequestId, request.AmountMinor, Refundable(payment, earlier),
                    request.Reason, staffUserId, clock.GetUtcNow());
            }
            catch (DomainException ex)
            {
                throw AppException.Conflict("refund_not_allowed", ex.Message);
            }

            // Two managers refunding the same payment at once: the bill's revision makes one of them think again.
            var check = await db.Checks.SingleAsync(c => c.Id == payment.CheckId, ct);
            check.Touch();
            db.Refunds.Add(created);
            db.AuditEntries.Add(new AuditEntry(payment.TenantId, staffUserId, "payment.refund.requested", nameof(Payment), payment.Id, created.CreatedAt));
            await db.SaveChangesAsync(ct);
            return created;
        });

        if (refund.ProviderRefundId is null && refund.Status == RefundStatus.Pending)
        {
            var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == paymentId, ct);
            try
            {
                var result = await provider.RefundAsync(new ProviderRefundRequest(refund.Id, payment.ProviderPaymentId!, refund.AmountMinor, refund.Currency), ct);
                refund.AttachProvider(result.ProviderRefundId);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                refund.Apply(RefundStatus.Failed, "The payment provider couldn't be reached. Nothing was refunded; try again.", clock.GetUtcNow());
            }
            await db.SaveChangesAsync(ct);
        }

        return await GetAsync(paymentId, ct);
    }

    // ---- Helpers ----

    private static long Refundable(Payment payment, IEnumerable<Refund> refunds) =>
        payment.Status == PaymentStatus.Succeeded
            ? Math.Max(0, payment.AmountMinor - refunds.Where(r => r.Status != RefundStatus.Failed).Sum(r => r.AmountMinor))
            : 0;

    /// <summary>Matches a reference, a table label or an amount in the venue's currency.</summary>
    private async Task<IQueryable<Payment>> SearchAsync(IQueryable<Payment> payments, Guid venueId, string currency, string? search, CancellationToken ct)
    {
        var text = search?.Trim();
        if (string.IsNullOrEmpty(text)) return payments;

        var reference = text.ToUpperInvariant();
        var locationIds = await db.ServiceLocations.AsNoTracking()
            .Where(l => l.VenueId == venueId && l.Label == text).Select(l => l.Id).ToListAsync(ct);
        var checkIds = db.Checks.AsNoTracking().Where(c => locationIds.Contains(c.ServiceLocationId)).Select(c => c.Id);
        long? amount = decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var major)
            ? (long)Math.Round(major * (decimal)Math.Pow(10, MinorDigits(currency)))
            : null;

        // Short searches like "8" are table numbers or amounts, not pieces of a reference.
        var byReference = reference.StartsWith("DF-") || reference.Length >= 4;
        return payments.Where(p => (byReference && p.Reference.Contains(reference)) || checkIds.Contains(p.CheckId)
            || (amount != null && (p.AmountMinor == amount || p.BillAmountMinor == amount)));
    }

    private async Task<IReadOnlyList<TransactionRowDto>> RowsAsync(IReadOnlyList<Payment> payments, CancellationToken ct)
    {
        var ids = payments.Select(p => p.Id).ToList();
        var checkIds = payments.Select(p => p.CheckId).Distinct().ToList();
        var refunds = await db.Refunds.AsNoTracking().Where(r => ids.Contains(r.PaymentId)).ToListAsync(ct);
        var checks = await db.Checks.AsNoTracking().Where(c => checkIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.ServiceLocationId, ct);
        var locationIds = checks.Values.Distinct().ToList();
        var locations = await db.ServiceLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var orders = await db.Orders.AsNoTracking().Where(o => o.CheckId != null && checkIds.Contains(o.CheckId.Value) && o.Status != OrderStatus.Rejected)
            .Select(o => new { o.CheckId, o.Number }).ToListAsync(ct);

        return payments.Select(p =>
        {
            var mine = refunds.Where(r => r.PaymentId == p.Id).ToList();
            var refunded = mine.Where(r => r.Status == RefundStatus.Succeeded).Sum(r => r.AmountMinor);
            var location = locations.GetValueOrDefault(checks.GetValueOrDefault(p.CheckId));
            return new TransactionRowDto(p.Id, p.Reference, p.CreatedAt, p.SucceededAt, StateOf(p, refunded), p.Method, p.CardBrand, p.CardLast4,
                p.Mode, location?.Type.ToString() ?? "", location?.Label ?? "",
                orders.Where(o => o.CheckId == p.CheckId).Select(o => o.Number).Order().ToList(),
                p.Currency, p.AmountMinor, p.TipMinor, refunded, mine.Any(r => r.Status == RefundStatus.Pending));
        }).ToList();
    }

    private static TransactionState StateOf(Payment p, long refundedMinor) => p.Status switch
    {
        PaymentStatus.Succeeded when refundedMinor >= p.AmountMinor => TransactionState.Refunded,
        PaymentStatus.Succeeded when refundedMinor > 0 => TransactionState.PartiallyRefunded,
        PaymentStatus.Succeeded => TransactionState.Succeeded,
        PaymentStatus.Failed => TransactionState.Failed,
        PaymentStatus.Cancelled => TransactionState.Cancelled,
        _ => TransactionState.Pending
    };

    /// <summary>Session → orders → payment → provider → ledger → review → refunds, in time order.</summary>
    private async Task<IReadOnlyList<TimelineEventDto>> TimelineAsync(
        Payment payment, string place, IReadOnlyList<Order> orders, IReadOnlyList<Refund> refunds,
        Dictionary<Guid, string> staff, (DateTimeOffset At, int Rating)? review, CancellationToken ct)
    {
        string Money(long minor) => FormatMoney(minor, payment.Currency);
        var events = new List<TimelineEventDto>();

        var sessionStart = await db.GuestSessions.AsNoTracking().Where(s => s.Id == payment.GuestSessionId).Select(s => (DateTimeOffset?)s.CreatedAt).SingleOrDefaultAsync(ct);
        if (sessionStart is { } started) events.Add(new(started, "session", $"Guest scanned the QR code at {place}"));

        foreach (var o in orders)
            events.Add(new(o.CreatedAt, "order", $"Order #{o.Number} placed · {Money(o.TotalMinor)}{(o.Status == OrderStatus.Rejected ? " (later rejected)" : "")}"));

        var split = payment.Mode switch
        {
            PaymentMode.Equal => $" · {payment.Shares} of {payment.SplitPeople} shares",
            PaymentMode.Items => " · by item",
            PaymentMode.Custom => " · custom amount",
            _ => ""
        };
        events.Add(new(payment.CreatedAt, "payment", $"Payment started · {Money(payment.AmountMinor)} by {payment.Method}{split}"));

        if (payment.ProviderPaymentId is { } providerId)
        {
            var webhooks = await db.ProcessedWebhooks.AsNoTracking().Where(w => w.ProviderPaymentId == providerId).OrderBy(w => w.CreatedAt).ToListAsync(ct);
            foreach (var w in webhooks)
                events.Add(new(w.CreatedAt, "provider", w.Status switch
                {
                    nameof(PaymentStatus.Succeeded) => $"{payment.Provider} confirmed the payment",
                    nameof(PaymentStatus.Failed) => $"{payment.Provider} declined the payment",
                    "RefundSucceeded" => $"{payment.Provider} confirmed a refund",
                    "RefundFailed" => $"{payment.Provider} declined a refund",
                    _ => $"{payment.Provider} reported {w.Status}"
                }));
        }
        if (payment.Status == PaymentStatus.Cancelled) events.Add(new(payment.UpdatedAt, "payment", "The guest left the payment page without paying"));

        var ledger = await db.LedgerEntries.AsNoTracking().Where(e => e.PaymentId == payment.Id).OrderBy(e => e.CreatedAt).ToListAsync(ct);
        foreach (var group in ledger.Where(e => e.Type != LedgerEntryType.Refund).GroupBy(e => e.CreatedAt))
            events.Add(new(group.Key, "ledger", "Ledger: " + string.Join(", ", group.Select(e => $"{LedgerName(e.Type)} {Money(e.AmountMinor)}"))));
        foreach (var e in ledger.Where(e => e.Type == LedgerEntryType.Refund))
            events.Add(new(e.CreatedAt, "ledger", $"Ledger: refund {Money(e.AmountMinor)}"));

        if (review is { } r) events.Add(new(r.At, "review", $"Guest rated the visit {r.Rating} / 5"));

        foreach (var refund in refunds)
        {
            events.Add(new(refund.CreatedAt, "refund",
                $"Refund of {Money(refund.AmountMinor)} requested{(staff.GetValueOrDefault(refund.RequestedByStaffId) is { } name ? $" by {name}" : "")}: {refund.Reason}"));
            if (refund.Status == RefundStatus.Failed && refund.CompletedAt is { } failedAt)
                events.Add(new(failedAt, "refund", $"Refund of {Money(refund.AmountMinor)} failed: {refund.FailureReason}"));
        }

        // Things that happen in the same instant still read in cause-then-effect order.
        return events.OrderBy(e => e.At).ThenBy(e => Array.IndexOf(TimelineOrder, e.Kind)).ToList();
    }

    private static readonly string[] TimelineOrder = ["session", "order", "payment", "refund", "provider", "ledger", "review"];

    private static string LedgerName(LedgerEntryType type) => type switch
    {
        LedgerEntryType.BillPayment => "bill",
        LedgerEntryType.Discount => "promotion",
        LedgerEntryType.Tip => "tip",
        _ => "refund"
    };

    private (DateTimeOffset From, DateTimeOffset To) Range(DateTimeOffset? from, DateTimeOffset? to)
    {
        tenant.RequireTenantId();
        var end = to ?? clock.GetUtcNow().AddSeconds(1);
        var start = from ?? end - DefaultRange;
        if (start >= end) throw AppException.BadRequest("invalid_range", "The start of the range must be before its end.");
        if (end - start > TimeSpan.FromDays(366)) throw AppException.BadRequest("invalid_range", "Choose a range of a year or less.");
        return (start, end);
    }

    internal static int MinorDigits(string currency) => currency is "KWD" or "BHD" or "OMR" or "JOD" ? 3 : 2;

    private static string FormatMoney(long minor, string currency)
    {
        var digits = MinorDigits(currency);
        return $"{currency} {(minor / (decimal)Math.Pow(10, digits)).ToString($"N{digits}", CultureInfo.InvariantCulture)}";
    }
}
