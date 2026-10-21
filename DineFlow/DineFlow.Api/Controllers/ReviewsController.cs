using System.Text;
using DineFlow.Api.Common;
using DineFlow.Application.Common;
using DineFlow.Application.Reviews;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace DineFlow.Api.Controllers;

/// <summary>Guests rating their visit after paying (blueprint 9.1 POST …/reviews).</summary>
[ApiController]
[Authorize(Policy = Policies.Guest)]
[Route("v1/public/sessions/{sessionId:guid}/reviews")]
public class GuestReviewsController(GuestReviewService reviews) : ControllerBase
{
    /// <summary>Rates a paid visit, 1–5 stars. Returns the venue's public review page for 4–5 stars.</summary>
    [HttpPost]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<ReviewResultDto> Submit(Guid sessionId, SubmitReviewRequest request, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return reviews.SubmitAsync(sessionId, request, ct);
    }

    /// <summary>Private feedback for 1–3 stars, with contact details only if the guest agrees.</summary>
    [HttpPut("{reviewId:guid}/feedback")]
    [EnableRateLimiting(Policies.PublicRateLimit)]
    public Task<ReviewResultDto> Feedback(Guid sessionId, Guid reviewId, FeedbackRequest request, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        return reviews.SendFeedbackAsync(sessionId, reviewId, request, ct);
    }

    /// <summary>The guest opened the public review page.</summary>
    [HttpPost("{reviewId:guid}/public-opened")]
    public async Task<IActionResult> PublicOpened(Guid sessionId, Guid reviewId, CancellationToken ct)
    {
        EnsureOwnSession(sessionId);
        await reviews.OpenedPublicReviewAsync(sessionId, reviewId, ct);
        return NoContent();
    }

    private void EnsureOwnSession(Guid sessionId)
    {
        if (User.GuestSessionId() != sessionId)
            throw AppException.NotFound("session_not_found", "Session not found.");
    }
}

/// <summary>The Reviews area of the merchant portal (blueprint 9.2 /v1/merchant/reviews/*).</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[Route("v1/merchant/reviews")]
public class MerchantReviewsController(MerchantReviewService reviews) : ControllerBase
{
    [HttpGet]
    public Task<ReviewPageDto> List([FromQuery] ReviewQuery query, CancellationToken ct) => reviews.ListAsync(query, ct);

    [HttpGet("{reviewId:guid}")]
    public Task<ReviewDetailDto> Get(Guid reviewId, CancellationToken ct) => reviews.GetAsync(reviewId, ct);

    /// <summary>The guest's contact details: owners and managers only, and every view is audit-logged.</summary>
    [HttpGet("{reviewId:guid}/contact")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<ReviewContactDto> Contact(Guid reviewId, CancellationToken ct) => reviews.GetContactAsync(reviewId, User.UserId(), ct);

    [HttpPost("{reviewId:guid}/follow-up")]
    public Task<ReviewDetailDto> FollowUp(Guid reviewId, CancellationToken ct) => reviews.MarkFollowedUpAsync(reviewId, User.UserId(), ct);

    [HttpGet("staff-summary")]
    public Task<IReadOnlyList<StaffRatingDto>> StaffSummary([FromQuery] Guid venueId, [FromQuery] DateTimeOffset? from, [FromQuery] DateTimeOffset? to, CancellationToken ct) =>
        reviews.StaffSummaryAsync(venueId, from, to, ct);

    /// <summary>The filtered reviews as CSV (without guest contact details).</summary>
    [HttpGet("export")]
    public async Task<IActionResult> Export([FromQuery] ReviewQuery query, CancellationToken ct)
    {
        var csv = await reviews.ExportCsvAsync(query, ct);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8",
            $"reviews-{DateTime.UtcNow:yyyyMMdd}.csv");
    }
}
