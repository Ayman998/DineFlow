using DineFlow.Domain.Common;

namespace DineFlow.Domain.Reviews;

/// <summary>
/// A guest's rating of their visit after paying (blueprint E27, P1-REV-04…09): 1–5 stars and an
/// optional comment, one per payment. 4–5 stars are positive and the guest is offered the
/// venue's public review page; 1–3 stars are constructive and stay private, and the guest may
/// leave contact details, with consent, so the venue can make it right.
/// </summary>
public class Review : Entity, ITenantOwned
{
    public const int MaxCommentLength = 1000;

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid ServiceLocationId { get; private set; }
    public Guid CheckId { get; private set; }
    public Guid PaymentId { get; private set; }
    public Guid GuestSessionId { get; private set; }

    public int Rating { get; private set; }
    public string? Comment { get; private set; }

    /// <summary>Contact details are only ever kept when the guest agreed to be contacted.</summary>
    public string? ContactName { get; private set; }
    public string? ContactEmail { get; private set; }
    public string? ContactPhone { get; private set; }
    public DateTimeOffset? ContactConsentAt { get; private set; }

    /// <summary>Where a positive guest was offered to post publicly, and whether they opened it (routing outcome).</summary>
    public ReviewPlatform? PublicPlatform { get; private set; }
    public DateTimeOffset? PublicLinkOpenedAt { get; private set; }

    /// <summary>The staff member who served the table, when one can be identified (P1-STAFF-03).</summary>
    public Guid? StaffUserId { get; private set; }

    public DateTimeOffset? FollowedUpAt { get; private set; }
    public Guid? FollowedUpByStaffId { get; private set; }

    private Review() { }

    public Review(
        Guid tenantId, Guid venueId, Guid serviceLocationId, Guid checkId, Guid paymentId, Guid guestSessionId,
        int rating, string? comment, ReviewPlatform? publicPlatform, Guid? staffUserId, DateTimeOffset now)
    {
        if (rating is < 1 or > 5) throw new DomainException("Choose between 1 and 5 stars.");

        TenantId = tenantId;
        VenueId = venueId;
        ServiceLocationId = serviceLocationId;
        CheckId = checkId;
        PaymentId = paymentId;
        GuestSessionId = guestSessionId;
        Rating = rating;
        Comment = Text.Optional(comment, MaxCommentLength, "Comment");
        PublicPlatform = IsPositive ? publicPlatform : null;
        StaffUserId = staffUserId;
        CreatedAt = now;
    }

    public bool IsPositive => Rating >= 4;
    public bool HasContact => ContactConsentAt is not null;

    /// <summary>
    /// The private feedback form for 1–3 stars: what went wrong and, only with consent, how to
    /// reach the guest. Without consent no contact detail is kept.
    /// </summary>
    public void AddFeedback(string? comment, string? name, string? email, string? phone, bool consent, DateTimeOffset now)
    {
        if (IsPositive) throw new DomainException("Private feedback is for ratings of 1 to 3 stars.");

        Comment = Text.Optional(comment, MaxCommentLength, "Comment") ?? Comment;
        var anyContact = !string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(email) || !string.IsNullOrWhiteSpace(phone);
        if (anyContact && !consent) throw new DomainException("Please agree to be contacted, or leave the contact details empty.");
        if (consent && string.IsNullOrWhiteSpace(email) && string.IsNullOrWhiteSpace(phone))
            throw new DomainException("Leave an email address or a phone number so the team can reach you.");

        if (consent)
        {
            ContactName = Text.Optional(name, 100, "Name");
            ContactEmail = Text.Optional(email, 254, "Email")?.ToLowerInvariant();
            ContactPhone = Text.Optional(phone, 30, "Phone");
            ContactConsentAt = now;
        }
        else
        {
            ContactName = ContactEmail = ContactPhone = null;
            ContactConsentAt = null;
        }
    }

    /// <summary>The guest tapped through to the public review page.</summary>
    public void OpenedPublicLink(DateTimeOffset now)
    {
        if (PublicPlatform is null) throw new DomainException("This review wasn't offered a public review link.");
        PublicLinkOpenedAt ??= now;
    }

    public void MarkFollowedUp(Guid staffUserId, DateTimeOffset now)
    {
        FollowedUpAt ??= now;
        FollowedUpByStaffId ??= staffUserId;
    }
}

/// <summary>Where positive guests are invited to post a public review (configured per branch).</summary>
public enum ReviewPlatform
{
    Google = 0,
    TripAdvisor = 1,
    Foursquare = 2
}
