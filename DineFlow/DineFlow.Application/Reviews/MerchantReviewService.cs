using System.Globalization;
using System.Text;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Reviews;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Reviews;

/// <summary>
/// The merchant's Reviews area (blueprint 4.4, P1-REV-07…09, P1-STAFF-03, P1-MER-11): headline
/// numbers with the previous period for comparison, the filtered list, each review with its
/// visit, guest contact details (managers only, audit-logged), staff ratings and CSV export.
/// </summary>
public class MerchantReviewService(IAppDbContext db, ITenantContext tenant, TimeProvider clock)
{
    public static readonly TimeSpan DefaultRange = TimeSpan.FromDays(30);

    public async Task<ReviewPageDto> ListAsync(ReviewQuery query, CancellationToken ct)
    {
        await EnsureVenueAsync(query.VenueId, ct);
        var (from, to) = Range(query);
        var summary = await SummaryAsync(query.VenueId, from, to, ct);

        var filtered = Filter(query, from, to);
        var total = await filtered.CountAsync(ct);
        var page = await Sort(filtered, query.Sort)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .ToListAsync(ct);

        return new ReviewPageDto(summary, await RowsAsync(page, ct), total, query.Page, query.PageSize);
    }

    public async Task<ReviewDetailDto> GetAsync(Guid reviewId, CancellationToken ct)
    {
        var review = await FindAsync(reviewId, ct);
        var payment = await db.Payments.AsNoTracking().SingleAsync(p => p.Id == review.PaymentId, ct);
        var location = await db.ServiceLocations.AsNoTracking().Include(l => l.DiningArea).SingleAsync(l => l.Id == review.ServiceLocationId, ct);
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CheckId == review.CheckId && o.Status != OrderStatus.Rejected)
            .Include(o => o.Lines).OrderBy(o => o.Number).ToListAsync(ct);
        var names = await StaffNamesAsync([review.StaffUserId, review.FollowedUpByStaffId], ct);

        return new ReviewDetailDto(review.Id, review.CreatedAt, review.Rating, review.Comment, review.IsPositive, review.HasContact,
            review.PublicPlatform, review.PublicLinkOpenedAt is not null,
            Name(names, review.StaffUserId), review.FollowedUpAt, Name(names, review.FollowedUpByStaffId),
            location.Type.ToString(), location.Label, location.DiningArea?.Name,
            new ReviewPaymentDto(payment.Reference, payment.SucceededAt, payment.Method, payment.CardBrand, payment.CardLast4, payment.Status,
                payment.Mode, payment.Shares, payment.SplitPeople, payment.Currency,
                payment.BillAmountMinor, payment.DiscountMinor, payment.TipMinor, payment.AmountMinor),
            orders.Select(o => new ReviewOrderDto(o.Number, o.Status.ToString(),
                o.Lines.OrderBy(l => l.Position).Select(l => new ReviewOrderLineDto(l.Name, l.Quantity, l.LineTotalMinor)).ToList(),
                o.TotalMinor)).ToList());
    }

    /// <summary>The guest's contact details. Only owners and managers may see them, and every view is recorded (blueprint 11).</summary>
    public async Task<ReviewContactDto> GetContactAsync(Guid reviewId, Guid staffUserId, CancellationToken ct)
    {
        var review = await FindAsync(reviewId, ct);
        if (review.ContactConsentAt is not { } consentAt)
            throw AppException.NotFound("no_contact", "The guest didn't leave contact details.");

        db.AuditEntries.Add(new AuditEntry(review.TenantId, staffUserId, "review.contact.viewed", nameof(Review), review.Id, clock.GetUtcNow()));
        await db.SaveChangesAsync(ct);
        return new ReviewContactDto(review.ContactName, review.ContactEmail, review.ContactPhone, consentAt);
    }

    /// <summary>Someone from the venue has been in touch with the guest (customer recovery).</summary>
    public async Task<ReviewDetailDto> MarkFollowedUpAsync(Guid reviewId, Guid staffUserId, CancellationToken ct)
    {
        var review = await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId, ct)
            ?? throw AppException.NotFound("review_not_found", "Review not found.");
        review.MarkFollowedUp(staffUserId, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetAsync(reviewId, ct);
    }

    /// <summary>Ratings per staff member who served the table, best average first (P1-STAFF-03).</summary>
    public async Task<IReadOnlyList<StaffRatingDto>> StaffSummaryAsync(Guid venueId, DateTimeOffset? from, DateTimeOffset? to, CancellationToken ct)
    {
        await EnsureVenueAsync(venueId, ct);
        var (start, end) = Range(new ReviewQuery(venueId, from, to));
        var ratings = await db.Reviews.AsNoTracking()
            .Where(r => r.VenueId == venueId && r.StaffUserId != null && r.CreatedAt >= start && r.CreatedAt < end)
            .GroupBy(r => r.StaffUserId!.Value)
            .Select(g => new { StaffUserId = g.Key, Count = g.Count(), Sum = g.Sum(r => r.Rating) })
            .ToListAsync(ct);
        var names = await StaffNamesAsync(ratings.Select(r => (Guid?)r.StaffUserId), ct);

        return ratings
            .Select(r => new StaffRatingDto(r.StaffUserId, Name(names, r.StaffUserId) ?? "Former staff member", r.Count, Math.Round((double)r.Sum / r.Count, 2)))
            .OrderByDescending(r => r.Average).ThenByDescending(r => r.Count).ThenBy(r => r.Name)
            .ToList();
    }

    /// <summary>
    /// The filtered list as CSV, in the venue's time zone. Guest contact details are left out:
    /// they're only shown one review at a time, to managers, with an audit record.
    /// </summary>
    public async Task<string> ExportCsvAsync(ReviewQuery query, CancellationToken ct)
    {
        var venue = await db.Venues.AsNoTracking().SingleOrDefaultAsync(v => v.Id == query.VenueId, ct)
            ?? throw AppException.NotFound("venue_not_found", "Venue not found.");
        var (from, to) = Range(query);
        var reviews = await Sort(Filter(query, from, to), query.Sort).Take(50_000).ToListAsync(ct);
        var rows = await RowsAsync(reviews, ct);
        var zone = TimeZoneInfo.TryFindSystemTimeZoneById(venue.TimeZone, out var tz) ? tz : TimeZoneInfo.Utc;

        var csv = new StringBuilder("Date,Time,Rating,Comment,Location,Staff,Opened public review,Followed up\r\n");
        foreach (var row in rows)
        {
            var local = TimeZoneInfo.ConvertTime(row.CreatedAt, zone);
            csv.AppendJoin(',',
                local.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), local.ToString("HH:mm", CultureInfo.InvariantCulture),
                row.Rating.ToString(CultureInfo.InvariantCulture), Cell(row.Comment), Cell($"{row.LocationType} {row.LocationLabel}"),
                Cell(row.StaffName), row.PublicLinkOpened ? "Yes" : "No", row.FollowedUp ? "Yes" : "No");
            csv.Append("\r\n");
        }
        return csv.ToString();
    }

    // ---- Helpers ----

    /// <summary>Counts, average, per-star counts and the two shares, for this period and the one before it.</summary>
    private async Task<ReviewSummaryDto> SummaryAsync(Guid venueId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var perStar = await db.Reviews.AsNoTracking()
            .Where(r => r.VenueId == venueId && r.CreatedAt >= from && r.CreatedAt < to)
            .GroupBy(r => r.Rating).Select(g => new { Rating = g.Key, Count = g.Count() })
            .ToListAsync(ct);
        var length = to - from;

        return new ReviewSummaryDto(from, to, await StatsAsync(venueId, from, to, ct),
            Enumerable.Range(1, 5).Select(star => perStar.FirstOrDefault(p => p.Rating == star)?.Count ?? 0).ToList(),
            await StatsAsync(venueId, from - length, from, ct));
    }

    private async Task<ReviewStatsDto> StatsAsync(Guid venueId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct)
    {
        var reviews = db.Reviews.AsNoTracking().Where(r => r.VenueId == venueId && r.CreatedAt >= from && r.CreatedAt < to);
        var count = await reviews.CountAsync(ct);
        var sum = await reviews.SumAsync(r => (int?)r.Rating, ct) ?? 0;
        var opened = await reviews.CountAsync(r => r.PublicLinkOpenedAt != null, ct);
        var payments = await db.Payments.AsNoTracking().CountAsync(p => p.VenueId == venueId && p.Status == PaymentStatus.Succeeded
            && p.SucceededAt >= from && p.SucceededAt < to, ct);

        return new ReviewStatsDto(count,
            count == 0 ? null : Math.Round((double)sum / count, 2),
            payments == 0 ? null : Math.Round(Math.Min(1, (double)count / payments), 4),
            count == 0 ? null : Math.Round((double)opened / count, 4));
    }

    private IQueryable<Review> Filter(ReviewQuery q, DateTimeOffset from, DateTimeOffset to)
    {
        var reviews = db.Reviews.AsNoTracking().Where(r => r.VenueId == q.VenueId && r.CreatedAt >= from && r.CreatedAt < to);
        if (q.Kind == ReviewKind.Positive) reviews = reviews.Where(r => r.Rating >= 4);
        if (q.Kind == ReviewKind.Constructive) reviews = reviews.Where(r => r.Rating <= 3);
        if (q.Rating is { } rating) reviews = reviews.Where(r => r.Rating == rating);
        if (q.WithComment) reviews = reviews.Where(r => r.Comment != null);
        return reviews;
    }

    private static IQueryable<Review> Sort(IQueryable<Review> reviews, ReviewSort sort) => sort switch
    {
        ReviewSort.Oldest => reviews.OrderBy(r => r.CreatedAt),
        ReviewSort.Highest => reviews.OrderByDescending(r => r.Rating).ThenByDescending(r => r.CreatedAt),
        ReviewSort.Lowest => reviews.OrderBy(r => r.Rating).ThenByDescending(r => r.CreatedAt),
        _ => reviews.OrderByDescending(r => r.CreatedAt)
    };

    private async Task<IReadOnlyList<ReviewRowDto>> RowsAsync(IReadOnlyList<Review> reviews, CancellationToken ct)
    {
        var locationIds = reviews.Select(r => r.ServiceLocationId).Distinct().ToList();
        var locations = await db.ServiceLocations.AsNoTracking().Where(l => locationIds.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
        var names = await StaffNamesAsync(reviews.Select(r => r.StaffUserId), ct);

        return reviews.Select(r =>
        {
            var location = locations.GetValueOrDefault(r.ServiceLocationId);
            return new ReviewRowDto(r.Id, r.CreatedAt, r.Rating, r.Comment, r.HasContact,
                location?.Type.ToString() ?? "", location?.Label ?? "", Name(names, r.StaffUserId),
                r.PublicLinkOpenedAt is not null, r.FollowedUpAt is not null);
        }).ToList();
    }

    private async Task<Dictionary<Guid, string>> StaffNamesAsync(IEnumerable<Guid?> ids, CancellationToken ct)
    {
        var wanted = ids.Where(id => id is not null).Select(id => id!.Value).Distinct().ToList();
        if (wanted.Count == 0) return [];
        return await db.StaffUsers.AsNoTracking().Where(s => wanted.Contains(s.Id)).ToDictionaryAsync(s => s.Id, s => s.DisplayName, ct);
    }

    private static string? Name(Dictionary<Guid, string> names, Guid? id) => id is { } key ? names.GetValueOrDefault(key) : null;

    private (DateTimeOffset From, DateTimeOffset To) Range(ReviewQuery q)
    {
        // The range's end is exclusive; by default it's "now", including anything from this very moment.
        var to = q.To ?? clock.GetUtcNow().AddSeconds(1);
        var from = q.From ?? to - DefaultRange;
        if (from >= to) throw AppException.BadRequest("invalid_range", "The start of the range must be before its end.");
        if (to - from > TimeSpan.FromDays(366)) throw AppException.BadRequest("invalid_range", "Choose a range of a year or less.");
        return (from, to);
    }

    private async Task<Review> FindAsync(Guid reviewId, CancellationToken ct) =>
        await db.Reviews.AsNoTracking().SingleOrDefaultAsync(r => r.Id == reviewId, ct)
        ?? throw AppException.NotFound("review_not_found", "Review not found.");

    private async Task EnsureVenueAsync(Guid venueId, CancellationToken ct)
    {
        tenant.RequireTenantId();
        if (!await db.Venues.AnyAsync(v => v.Id == venueId, ct))
            throw AppException.NotFound("venue_not_found", "Venue not found.");
    }

    /// <summary>A CSV cell: quoted, and never starting with a formula character (CSV injection).</summary>
    private static string Cell(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        if ("=+-@\t\r".Contains(value[0])) value = "'" + value;
        return $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
