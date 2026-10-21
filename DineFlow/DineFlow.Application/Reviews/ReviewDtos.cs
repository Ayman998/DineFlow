using System.ComponentModel.DataAnnotations;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Reviews;

namespace DineFlow.Application.Reviews;

// ---- Guests ----

public record SubmitReviewRequest(
    [Required] Guid PaymentId,
    [Range(1, 5)] int Rating,
    [StringLength(Review.MaxCommentLength)] string? Comment);

/// <param name="Consent">The guest agreed the venue may contact them about this feedback.</param>
public record FeedbackRequest(
    [StringLength(Review.MaxCommentLength)] string? Comment,
    [StringLength(100)] string? Name,
    [EmailAddress, StringLength(254)] string? Email,
    [RegularExpression(@"^\+?[0-9 ()-]{6,30}$", ErrorMessage = "Enter a phone number with digits only.")] string? Phone,
    bool Consent);

/// <summary>Where to send a happy guest to post publicly.</summary>
public record PublicReviewDto(ReviewPlatform Platform, string Url);

/// <param name="PublicReview">For 4–5 stars when the venue has a review page; null otherwise (the flow just ends).</param>
/// <param name="FeedbackTitle">For 1–3 stars: the heading of the private feedback form.</param>
public record ReviewResultDto(Guid Id, int Rating, bool Positive, PublicReviewDto? PublicReview, string? FeedbackTitle, bool FeedbackSent);

// ---- Merchants ----

public enum ReviewKind { All, Positive, Constructive }

public enum ReviewSort { Recent, Oldest, Highest, Lowest }

/// <param name="From">Start of the range; defaults to 30 days ago.</param>
/// <param name="To">End of the range; defaults to now.</param>
public record ReviewQuery(
    [Required] Guid VenueId,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    [Range(1, 5)] int? Rating = null,
    ReviewKind Kind = ReviewKind.All,
    bool WithComment = false,
    ReviewSort Sort = ReviewSort.Recent,
    [Range(1, 10_000)] int Page = 1,
    [Range(1, 100)] int PageSize = 10);

/// <summary>Headline numbers for a period (P1-REV-07, P1-MER-11).</summary>
/// <param name="RatedShare">Share of successful payments in the period that left a rating (0–1).</param>
/// <param name="PublicShare">Share of ratings whose guest opened the public review page (0–1).</param>
public record ReviewStatsDto(int Count, double? Average, double? RatedShare, double? PublicShare);

/// <param name="PerStar">Ratings at 1, 2, 3, 4 and 5 stars.</param>
/// <param name="Previous">The same numbers for the period just before, for comparison.</param>
public record ReviewSummaryDto(DateTimeOffset From, DateTimeOffset To, ReviewStatsDto Current, IReadOnlyList<int> PerStar, ReviewStatsDto Previous);

public record ReviewRowDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    int Rating,
    string? Comment,
    /// <summary>The guest left contact details (viewing them is restricted and audited).</summary>
    bool HasContact,
    string LocationType,
    string LocationLabel,
    string? StaffName,
    bool PublicLinkOpened,
    bool FollowedUp);

public record ReviewPageDto(ReviewSummaryDto Summary, IReadOnlyList<ReviewRowDto> Items, int Total, int Page, int PageSize);

public record ReviewPaymentDto(
    string Reference, DateTimeOffset? PaidAt, PaymentMethod Method, string? CardBrand, string? CardLast4, PaymentStatus Status,
    PaymentMode Mode, int? Shares, int? SplitPeople, string Currency,
    long BillAmountMinor, long DiscountMinor, long TipMinor, long AmountMinor);

public record ReviewOrderLineDto(string Name, int Quantity, long LineTotalMinor);

public record ReviewOrderDto(int Number, string Status, IReadOnlyList<ReviewOrderLineDto> Lines, long TotalMinor);

/// <summary>A review with the visit it's about (P1-REV-09): the payment and what the table ordered.</summary>
public record ReviewDetailDto(
    Guid Id,
    DateTimeOffset CreatedAt,
    int Rating,
    string? Comment,
    bool Positive,
    bool HasContact,
    ReviewPlatform? PublicPlatform,
    bool PublicLinkOpened,
    string? StaffName,
    DateTimeOffset? FollowedUpAt,
    string? FollowedUpBy,
    string LocationType,
    string LocationLabel,
    string? AreaName,
    ReviewPaymentDto Payment,
    IReadOnlyList<ReviewOrderDto> Orders);

/// <summary>Contact details a guest left with constructive feedback (P1-REV-08). Every view is audit-logged.</summary>
public record ReviewContactDto(string? Name, string? Email, string? Phone, DateTimeOffset ConsentAt);

/// <summary>Ratings attributed to one staff member (P1-STAFF-03).</summary>
public record StaffRatingDto(Guid StaffUserId, string Name, int Count, double Average);
