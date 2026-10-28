using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Menus;
using DineFlow.Application.Orders;
using DineFlow.Application.Payments;
using DineFlow.Application.Reviews;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Reviews;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Venues;
using DineFlow.Infrastructure.Payments;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Test.Application;

/// <summary>Rating the visit after paying, and the merchant's Reviews area (blueprint E27).</summary>
public class ReviewTests : IDisposable
{
    private readonly TestHost _host = new();

    public void Dispose() => _host.Dispose();

    private const string GoogleUrl = "https://example.com/demo/review";

    private sealed record Fixture(Guid TenantId, VenueDto Venue, string QrToken, ItemDto Burger, StaffUser Sara, StaffUser Omar);

    private async Task<Fixture> CreateAsync(bool publicLink = true, bool enabled = true, string? feedbackTitle = null)
    {
        var tenant = await _host.CreateTenantAsync();
        var venue = await _host.CreateVenueAsync("Marina");
        await _host.Venues.UpdateSettingsAsync(venue.Id, new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "en", null, "#0B3B38", true, true,
            ReviewsEnabled: enabled, ReviewPlatform: publicLink ? ReviewPlatform.Google : null, ReviewUrl: publicLink ? GoogleUrl : null,
            FeedbackTitle: feedbackTitle), default);

        var menu = await _host.Menus.CreateMenuAsync(venue.Id, new MenuRequest("All day", null, true, Enum.GetValues<DayOfWeek>(), null, null), default);
        var mains = await _host.Menus.CreateCategoryAsync(new CreateCategoryRequest(venue.Id, null, "Mains", null), default);
        var burger = await _host.Menus.CreateItemAsync(new ItemRequest(mains.Id, "Burger", null, null, null, 5000, null, true, [], [], []), default);
        await _host.Menus.SetMenuItemsAsync(menu.Id, new MenuItemsRequest([burger.Id]), default);
        var table = await _host.Tables.CreateLocationAsync(new CreateLocationRequest(venue.Id, null, ServiceLocationType.Table, "7"), default);

        var sara = new StaffUser(tenant.Id, "sara@demo.test", "Sara", StaffRole.Staff);
        var omar = new StaffUser(tenant.Id, "omar@demo.test", "Omar", StaffRole.Staff);
        _host.Db.StaffUsers.AddRange(sara, omar);
        await _host.Db.SaveChangesAsync();

        return new Fixture(tenant.Id, venue, TestHost.TokenFromUrl(table.Qr!.Url), burger, sara, omar);
    }

    /// <summary>A guest orders a burger, staff serve it, and the guest pays. Returns the guest and the paid payment.</summary>
    private async Task<(Guid SessionId, PaymentDto Payment)> PaidVisitAsync(Fixture f, StaffUser? servedBy = null)
    {
        _host.CurrentTenant = null;
        var sessionId = (await _host.Guests.ResolveQrAsync(f.QrToken, null, default)).Context.SessionId;
        _host.CurrentTenant = f.TenantId;

        var line = new CartLineRequest(f.Burger.Id, 1, [], null);
        var order = await _host.GuestOrders.PlaceAsync(sessionId, new PlaceOrderRequest(Guid.NewGuid(), [line], null, 5000), default);
        if (servedBy is not null)
        {
            await _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Accepted, null), default, servedBy.Id);
            await _host.MerchantOrders.ChangeStatusAsync(order.Id, new ChangeOrderStatusRequest(OrderStatus.Completed, null), default, servedBy.Id);
        }

        var bill = await _host.Bills.GetForSessionAsync(sessionId, default);
        var payment = await _host.Payments.CreateAsync(new CreatePaymentRequest(sessionId, Guid.NewGuid(), PaymentMethod.Card,
            PaymentMode.Full, null, null, 0, null, bill.AvailableMinor, true), default);

        var (body, signature) = _host.Sandbox.CreateWebhook(new ProviderWebhookEvent(
            $"evt_{Guid.NewGuid():N}", $"sbx_{payment.Id:N}", PaymentStatus.Succeeded, "Visa", "4242", null));
        _host.CurrentTenant = null;
        await _host.Payments.HandleWebhookAsync("sandbox", body, new Dictionary<string, string> { [SandboxPaymentProvider.SignatureHeader] = signature }, default);
        _host.CurrentTenant = f.TenantId;

        return (sessionId, await _host.Payments.GetAsync(sessionId, payment.Id, default));
    }

    private Task<ReviewResultDto> RateAsync(Guid sessionId, PaymentDto payment, int rating, string? comment = null) =>
        _host.GuestReviews.SubmitAsync(sessionId, new SubmitReviewRequest(payment.Id, rating, comment), default);

    // ---- Guests ----

    [Fact]
    public async Task A_happy_guest_is_offered_the_public_review_page_and_staff_get_the_credit()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f, servedBy: f.Sara);
        Assert.True(payment.CanReview);

        var result = await RateAsync(guest, payment, 5, "  Perfect as usual  ");
        await _host.GuestReviews.OpenedPublicReviewAsync(guest, result.Id, default);

        Assert.True(result.Positive);
        Assert.Equal((ReviewPlatform.Google, GoogleUrl), (result.PublicReview!.Platform, result.PublicReview.Url));
        Assert.Null(result.FeedbackTitle);
        var review = await _host.Db.Reviews.SingleAsync();
        Assert.Equal(("Perfect as usual", f.Sara.Id), (review.Comment, review.StaffUserId));
        Assert.NotNull(review.PublicLinkOpenedAt);
        var after = await _host.Payments.GetAsync(guest, payment.Id, default);
        Assert.Equal((false, 5), (after.CanReview, after.ReviewRating));
        Assert.Equal(5, Assert.Single(_host.ReviewNotifier.Notices).Rating);
    }

    [Fact]
    public async Task Without_a_review_page_a_happy_guest_just_finishes()
    {
        var f = await CreateAsync(publicLink: false);
        var (guest, payment) = await PaidVisitAsync(f);

        var result = await RateAsync(guest, payment, 4);

        Assert.Null(result.PublicReview);
        await Assert.ThrowsAsync<AppException>(() => _host.GuestReviews.OpenedPublicReviewAsync(guest, result.Id, default));
    }

    [Fact]
    public async Task An_unhappy_guest_gets_the_private_form_and_contact_details_need_consent()
    {
        var f = await CreateAsync(feedbackTitle: "We're sorry");
        var (guest, payment) = await PaidVisitAsync(f);

        var result = await RateAsync(guest, payment, 2);
        var noConsent = await Assert.ThrowsAsync<AppException>(() => _host.GuestReviews.SendFeedbackAsync(guest, result.Id,
            new FeedbackRequest("Cold food", "Lina", "lina@example.com", null, false), default));
        var noWayToReach = await Assert.ThrowsAsync<AppException>(() => _host.GuestReviews.SendFeedbackAsync(guest, result.Id,
            new FeedbackRequest("Cold food", "Lina", null, null, true), default));
        var sent = await _host.GuestReviews.SendFeedbackAsync(guest, result.Id,
            new FeedbackRequest("Cold food", "Lina", "Lina@Example.com", "+971 50 123 4567", true), default);

        Assert.Equal((false, null, "We're sorry"), (result.Positive, result.PublicReview, result.FeedbackTitle));
        Assert.Equal(("invalid_feedback", "invalid_feedback"), (noConsent.Code, noWayToReach.Code));
        Assert.True(sent.FeedbackSent);
        var review = await _host.Db.Reviews.SingleAsync();
        Assert.Equal(("Cold food", "Lina", "lina@example.com", "+971 50 123 4567"), (review.Comment, review.ContactName, review.ContactEmail, review.ContactPhone));
        Assert.NotNull(review.ContactConsentAt);
        Assert.Null(review.PublicPlatform);
    }

    [Fact]
    public async Task Feedback_without_consent_keeps_no_contact_details()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f);
        var result = await RateAsync(guest, payment, 1);

        await _host.GuestReviews.SendFeedbackAsync(guest, result.Id, new FeedbackRequest("Too slow", null, null, null, false), default);

        var review = await _host.Db.Reviews.SingleAsync();
        Assert.Equal("Too slow", review.Comment);
        Assert.False(review.HasContact);
        Assert.Null(review.ContactEmail);
    }

    [Fact]
    public async Task Each_paid_visit_is_rated_once_and_only_by_its_guest()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f);
        var (other, _) = await PaidVisitAsync(f);

        var first = await RateAsync(guest, payment, 5);
        var again = await RateAsync(guest, payment, 1);
        var stranger = await Assert.ThrowsAsync<AppException>(() => RateAsync(other, payment, 3));

        Assert.Equal(first.Id, again.Id);
        Assert.Equal(5, again.Rating);
        Assert.Equal(404, stranger.StatusCode);
        Assert.Single(await _host.Db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task Venues_can_turn_ratings_off()
    {
        var f = await CreateAsync(enabled: false);
        var (guest, payment) = await PaidVisitAsync(f);

        var ex = await Assert.ThrowsAsync<AppException>(() => RateAsync(guest, payment, 5));

        Assert.False(payment.CanReview);
        Assert.Equal("reviews_disabled", ex.Code);
    }

    [Fact]
    public async Task A_review_link_must_be_a_full_https_address()
    {
        var f = await CreateAsync();
        var ex = await Assert.ThrowsAsync<AppException>(() => _host.Venues.UpdateSettingsAsync(f.Venue.Id,
            new VenueSettingsRequest("Marina", "AED", "Asia/Dubai", "en", null, "#0B3B38", true, true,
                ReviewPlatform: ReviewPlatform.TripAdvisor, ReviewUrl: "http://example.com/review"), default));

        Assert.Equal("invalid_settings", ex.Code);
    }

    // ---- Merchants ----

    private async Task<Fixture> RatedVenueAsync()
    {
        var f = await CreateAsync();
        foreach (var (rating, comment, staff, opened) in new (int, string?, StaffUser?, bool)[]
        {
            (5, "Perfect", null, true), (5, null, null, false), (4, "Good service", null, true),
            (2, "Cake was burned", null, false), (3, null, null, false)
        })
        {
            var (guest, payment) = await PaidVisitAsync(f, staff);
            var result = await RateAsync(guest, payment, rating, comment);
            if (opened) await _host.GuestReviews.OpenedPublicReviewAsync(guest, result.Id, default);
            _host.Clock.Advance(TimeSpan.FromMinutes(5));
        }
        await PaidVisitAsync(f); // a sixth visit nobody rated
        return f;
    }

    [Fact]
    public async Task The_dashboard_counts_ratings_per_star_and_how_many_visits_were_rated()
    {
        var f = await RatedVenueAsync();

        var page = await _host.MerchantReviews.ListAsync(new ReviewQuery(f.Venue.Id), default);

        var current = page.Summary.Current;
        Assert.Equal(5, current.Count);
        Assert.Equal(3.8, current.Average);
        Assert.Equal([0, 1, 1, 1, 2], page.Summary.PerStar);
        Assert.Equal(Math.Round(5 / 6.0, 4), current.RatedShare);
        Assert.Equal(0.4, current.PublicShare);
        Assert.Equal(0, page.Summary.Previous.Count);
        Assert.Equal(5, page.Total);
        Assert.Equal("Cake was burned", page.Items[1].Comment); // most recent first
    }

    [Fact]
    public async Task Merchants_filter_sort_and_page_the_list()
    {
        var f = await RatedVenueAsync();
        Task<ReviewPageDto> List(ReviewQuery q) => _host.MerchantReviews.ListAsync(q, default);

        var constructive = await List(new ReviewQuery(f.Venue.Id, Kind: ReviewKind.Constructive));
        var fives = await List(new ReviewQuery(f.Venue.Id, Rating: 5));
        var withComment = await List(new ReviewQuery(f.Venue.Id, WithComment: true, Sort: ReviewSort.Lowest));
        var secondPage = await List(new ReviewQuery(f.Venue.Id, Sort: ReviewSort.Oldest, Page: 2, PageSize: 2));

        Assert.Equal([3, 2], constructive.Items.Select(r => r.Rating));
        Assert.Equal(2, fives.Total);
        Assert.Equal([2, 4, 5], withComment.Items.Select(r => r.Rating));
        Assert.Equal((5, 2), (secondPage.Total, secondPage.Items.Count));
        Assert.Equal([4, 2], secondPage.Items.Select(r => r.Rating));
        Assert.Equal(5, constructive.Summary.Current.Count); // the headline numbers ignore the filters
    }

    [Fact]
    public async Task Last_periods_numbers_are_shown_for_comparison()
    {
        var f = await RatedVenueAsync();
        _host.Clock.Advance(TimeSpan.FromDays(31));
        var (guest, payment) = await PaidVisitAsync(f);
        await RateAsync(guest, payment, 1);

        var page = await _host.MerchantReviews.ListAsync(new ReviewQuery(f.Venue.Id), default);

        Assert.Equal((1, 1.0), (page.Summary.Current.Count, page.Summary.Current.Average));
        Assert.Equal((5, 3.8), (page.Summary.Previous.Count, page.Summary.Previous.Average));
    }

    [Fact]
    public async Task A_review_opens_the_visit_it_is_about()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f, servedBy: f.Omar);
        var result = await RateAsync(guest, payment, 2, "Slow");

        var detail = await _host.MerchantReviews.GetAsync(result.Id, default);

        Assert.Equal((2, "Slow", false, "Omar"), (detail.Rating, detail.Comment, detail.Positive, detail.StaffName));
        Assert.Equal(("Table", "7"), (detail.LocationType, detail.LocationLabel));
        Assert.Equal((payment.Reference, "4242", 5000L), (detail.Payment.Reference, detail.Payment.CardLast4, detail.Payment.AmountMinor));
        var order = Assert.Single(detail.Orders);
        Assert.Equal(("Burger", 1, 5000L), (order.Lines.Single().Name, order.Lines.Single().Quantity, order.Lines.Single().LineTotalMinor));
    }

    [Fact]
    public async Task Viewing_a_guests_contact_details_is_audit_logged()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f);
        var result = await RateAsync(guest, payment, 1);
        await _host.GuestReviews.SendFeedbackAsync(guest, result.Id, new FeedbackRequest(null, "Lina", "lina@example.com", null, true), default);
        var (quiet, quietPayment) = await PaidVisitAsync(f);
        var noContact = await RateAsync(quiet, quietPayment, 2);
        var manager = f.Sara.Id;

        var contact = await _host.MerchantReviews.GetContactAsync(result.Id, manager, default);
        var none = await Assert.ThrowsAsync<AppException>(() => _host.MerchantReviews.GetContactAsync(noContact.Id, manager, default));

        Assert.Equal(("Lina", "lina@example.com"), (contact.Name, contact.Email));
        Assert.Equal("no_contact", none.Code);
        var audit = Assert.Single(await _host.Db.AuditEntries.ToListAsync());
        Assert.Equal(("review.contact.viewed", result.Id, manager), (audit.Action, audit.ObjectId, audit.StaffUserId));
        var row = (await _host.MerchantReviews.ListAsync(new ReviewQuery(f.Venue.Id), default)).Items.Single(r => r.Id == result.Id);
        Assert.True(row.HasContact);
    }

    [Fact]
    public async Task Staff_are_ranked_by_their_average_rating()
    {
        var f = await CreateAsync();
        foreach (var (staff, rating) in new[] { (f.Sara, 5), (f.Sara, 4), (f.Omar, 5), (f.Omar, 5), (f.Omar, 3) })
        {
            var (guest, payment) = await PaidVisitAsync(f, staff);
            await RateAsync(guest, payment, rating);
        }
        var (unattributed, payment2) = await PaidVisitAsync(f);
        await RateAsync(unattributed, payment2, 1);

        var summary = await _host.MerchantReviews.StaffSummaryAsync(f.Venue.Id, null, null, default);

        Assert.Equal([("Sara", 2, 4.5), ("Omar", 3, 4.33)], summary.Select(s => (s.Name, s.Count, s.Average)));
    }

    [Fact]
    public async Task Following_up_is_recorded_once()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f);
        var result = await RateAsync(guest, payment, 2);

        var first = await _host.MerchantReviews.MarkFollowedUpAsync(result.Id, f.Sara.Id, default);
        _host.Clock.Advance(TimeSpan.FromHours(1));
        var second = await _host.MerchantReviews.MarkFollowedUpAsync(result.Id, f.Omar.Id, default);

        Assert.Equal((first.FollowedUpAt, "Sara"), (second.FollowedUpAt, second.FollowedUpBy));
    }

    [Fact]
    public async Task The_export_is_csv_without_contact_details_and_safe_to_open_in_a_spreadsheet()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f, f.Sara);
        var result = await RateAsync(guest, payment, 1, "=HYPERLINK(\"http://evil\") \"bad\"");
        await _host.GuestReviews.SendFeedbackAsync(guest, result.Id, new FeedbackRequest(null, "Lina", "lina@example.com", null, true), default);

        var csv = await _host.MerchantReviews.ExportCsvAsync(new ReviewQuery(f.Venue.Id), default);

        var lines = csv.TrimEnd().Split("\r\n");
        Assert.Equal("Date,Time,Rating,Comment,Location,Staff,Opened public review,Followed up", lines[0]);
        Assert.Contains(",1,\"'=HYPERLINK(\"\"http://evil\"\") \"\"bad\"\"\",\"Table 7\",\"Sara\",No,No", lines[1]);
        Assert.DoesNotContain("lina", csv, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Another_merchant_cannot_see_the_reviews()
    {
        var f = await CreateAsync();
        var (guest, payment) = await PaidVisitAsync(f);
        var result = await RateAsync(guest, payment, 5);
        await _host.CreateTenantAsync("Rival Cafe");

        var list = await Assert.ThrowsAsync<AppException>(() => _host.MerchantReviews.ListAsync(new ReviewQuery(f.Venue.Id), default));
        var detail = await Assert.ThrowsAsync<AppException>(() => _host.MerchantReviews.GetAsync(result.Id, default));

        Assert.Equal((404, 404), (list.StatusCode, detail.StatusCode));
    }
}
