using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Common;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Reviews;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Reviews;

/// <summary>Pushes new reviews to the venue's staff screens (blueprint ReviewSubmitted).</summary>
public interface IReviewNotifier
{
    Task ReviewSubmittedAsync(ReviewNotice notice, CancellationToken ct);
}

public record ReviewNotice(Guid TenantId, Guid VenueId, Guid ReviewId, int Rating, string LocationType, string LocationLabel, bool HasComment);

/// <summary>
/// The guest's side of reviews (blueprint 4.2 step 10, P1-REV-04…06): rate the visit after a
/// successful payment, then either open the venue's public review page (4–5 stars) or send
/// private feedback with optional contact details (1–3 stars). Skipping never affects the payment.
/// </summary>
public class GuestReviewService(IAppDbContext db, IReviewNotifier notifier, TimeProvider clock)
{
    /// <summary>The default heading of the private feedback form when the venue hasn't set one.</summary>
    public const string DefaultFeedbackTitle = "Sorry we missed the mark";

    /// <summary>Rates the visit a payment belongs to. Rating the same payment again returns the first rating.</summary>
    public async Task<ReviewResultDto> SubmitAsync(Guid sessionId, SubmitReviewRequest request, CancellationToken ct)
    {
        var payment = await db.Payments.SingleOrDefaultAsync(p => p.Id == request.PaymentId && p.GuestSessionId == sessionId, ct);
        if (payment?.CheckId is not { } checkId) throw AppException.NotFound("payment_not_found", "Payment not found.");
        if (payment.Status != PaymentStatus.Succeeded)
            throw AppException.Conflict("not_paid", "You can rate your visit once the payment has gone through.");

        var venue = await db.Venues.SingleAsync(v => v.Id == payment.VenueId, ct);
        if (!venue.ReviewsEnabled)
            throw AppException.Conflict("reviews_disabled", "This venue isn't collecting ratings right now.");

        var existing = await db.Reviews.SingleOrDefaultAsync(r => r.PaymentId == payment.Id, ct);
        if (existing is not null) return ToResult(existing, venue);

        var locationId = await db.Checks.Where(c => c.Id == checkId).Select(c => c.ServiceLocationId).SingleAsync(ct);
        var staff = await ServedByAsync(checkId, ct);
        Review review;
        try
        {
            review = new Review(payment.TenantId, payment.VenueId, locationId, checkId, payment.Id, sessionId,
                request.Rating, request.Comment, venue.ReviewUrl is null ? null : venue.ReviewPlatform, staff, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw AppException.BadRequest("invalid_review", ex.Message);
        }

        db.Reviews.Add(review);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException)
        {
            // Two taps at once: the first one counts.
            db.ChangeTracker.Clear();
            var first = await db.Reviews.SingleOrDefaultAsync(r => r.PaymentId == payment.Id, ct);
            if (first is null) throw;
            return ToResult(first, venue);
        }

        var location = await db.ServiceLocations.SingleAsync(l => l.Id == locationId, ct);
        await notifier.ReviewSubmittedAsync(new ReviewNotice(review.TenantId, review.VenueId, review.Id, review.Rating,
            location.Type.ToString(), location.Label, review.Comment is not null), ct);
        return ToResult(review, venue);
    }

    /// <summary>The private feedback form for 1–3 stars: what went wrong and, with consent, how to reach the guest.</summary>
    public async Task<ReviewResultDto> SendFeedbackAsync(Guid sessionId, Guid reviewId, FeedbackRequest request, CancellationToken ct)
    {
        var review = await FindAsync(sessionId, reviewId, ct);
        try
        {
            review.AddFeedback(request.Comment, request.Name, request.Email, request.Phone, request.Consent, clock.GetUtcNow());
        }
        catch (DomainException ex)
        {
            throw AppException.BadRequest("invalid_feedback", ex.Message);
        }
        await db.SaveChangesAsync(ct);
        return ToResult(review, await db.Venues.SingleAsync(v => v.Id == review.VenueId, ct), feedbackSent: true);
    }

    /// <summary>Records that a happy guest went on to the public review page (the routing outcome).</summary>
    public async Task OpenedPublicReviewAsync(Guid sessionId, Guid reviewId, CancellationToken ct)
    {
        var review = await FindAsync(sessionId, reviewId, ct);
        try { review.OpenedPublicLink(clock.GetUtcNow()); }
        catch (DomainException ex) { throw AppException.BadRequest("no_public_review", ex.Message); }
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Who to credit the rating to: whoever marked the table's latest order served, or failing
    /// that whoever accepted it (P1-STAFF-03, "where a rating can be attributed").
    /// </summary>
    private async Task<Guid?> ServedByAsync(Guid checkId, CancellationToken ct)
    {
        var orders = await db.Orders.AsNoTracking()
            .Where(o => o.CheckId == checkId && o.Status != OrderStatus.Rejected)
            .OrderByDescending(o => o.Number)
            .Select(o => new { o.ServedByStaffId, o.AcceptedByStaffId })
            .ToListAsync(ct);
        return orders.Select(o => o.ServedByStaffId).FirstOrDefault(id => id is not null)
            ?? orders.Select(o => o.AcceptedByStaffId).FirstOrDefault(id => id is not null);
    }

    private async Task<Review> FindAsync(Guid sessionId, Guid reviewId, CancellationToken ct) =>
        await db.Reviews.SingleOrDefaultAsync(r => r.Id == reviewId && r.GuestSessionId == sessionId, ct)
        ?? throw AppException.NotFound("review_not_found", "Review not found.");

    private static ReviewResultDto ToResult(Review review, Venue venue, bool feedbackSent = false) => new(
        review.Id, review.Rating, review.IsPositive,
        review.IsPositive && review.PublicPlatform is { } platform && venue.ReviewUrl is { } url ? new PublicReviewDto(platform, url) : null,
        review.IsPositive ? null : venue.FeedbackTitle ?? DefaultFeedbackTitle,
        feedbackSent || review.HasContact);
}
