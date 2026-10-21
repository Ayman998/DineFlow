using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Common;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Venues;

/// <summary>Venue (branch) profile and settings for the signed-in merchant (P1-MER-01).</summary>
public class VenueService(IAppDbContext db, ITenantContext tenant)
{
    public async Task<IReadOnlyList<VenueDto>> ListAsync(CancellationToken ct) =>
        await db.Venues.OrderBy(v => v.Name).Select(v => ToDto(v)).ToListAsync(ct);

    public async Task<VenueDto> GetAsync(Guid venueId, CancellationToken ct) =>
        ToDto(await FindAsync(venueId, ct));

    public async Task<VenueDto> CreateAsync(VenueSettingsRequest request, CancellationToken ct)
    {
        EnsureValidTimeZone(request.TimeZone);

        var venue = Apply(() =>
        {
            var v = new Venue(tenant.RequireTenantId(), request.Name, request.Currency, request.TimeZone, request.DefaultLocale);
            v.UpdateSettings(request.Name, request.Currency, request.TimeZone, request.DefaultLocale,
                request.LogoUrl, request.BrandColor, request.MenuEnabled, request.PayAtTableEnabled);
            v.SetCharges(request.ServiceChargeBasisPoints, request.TaxBasisPoints, request.PricesIncludeTax);
            v.SetTips(request.TipsEnabled, request.TipPresetsBasisPoints ?? [500, 1000, 1500], request.MostCommonTipBasisPoints);
            v.SetReviews(request.ReviewsEnabled, request.ReviewPlatform, request.ReviewUrl, request.FeedbackTitle);
            return v;
        });

        db.Venues.Add(venue);
        await db.SaveChangesAsync(ct);
        return ToDto(venue);
    }

    public async Task<VenueDto> UpdateSettingsAsync(Guid venueId, VenueSettingsRequest request, CancellationToken ct)
    {
        EnsureValidTimeZone(request.TimeZone);

        var venue = await FindAsync(venueId, ct);
        Apply(() =>
        {
            venue.UpdateSettings(request.Name, request.Currency, request.TimeZone, request.DefaultLocale,
                request.LogoUrl, request.BrandColor, request.MenuEnabled, request.PayAtTableEnabled);
            venue.SetCharges(request.ServiceChargeBasisPoints, request.TaxBasisPoints, request.PricesIncludeTax);
            venue.SetTips(request.TipsEnabled, request.TipPresetsBasisPoints ?? [500, 1000, 1500], request.MostCommonTipBasisPoints);
            venue.SetReviews(request.ReviewsEnabled, request.ReviewPlatform, request.ReviewUrl, request.FeedbackTitle);
            return venue;
        });

        await db.SaveChangesAsync(ct);
        return ToDto(venue);
    }

    private async Task<Venue> FindAsync(Guid venueId, CancellationToken ct) =>
        await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId, ct)
        ?? throw AppException.NotFound("venue_not_found", "Venue not found.");

    private static void EnsureValidTimeZone(string timeZone)
    {
        if (!TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out _))
            throw AppException.BadRequest("invalid_timezone", $"'{timeZone}' is not a known timezone. Use an IANA name such as Asia/Dubai.");
    }

    private static T Apply<T>(Func<T> change)
    {
        try { return change(); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_settings", ex.Message); }
    }

    private static VenueDto ToDto(Venue v) => new(
        v.Id, v.Name, v.Currency, v.TimeZone, v.DefaultLocale, v.LogoUrl, v.BrandColor,
        v.MenuEnabled, v.PayAtTableEnabled, v.IsActive,
        v.ServiceChargeBasisPoints, v.TaxBasisPoints, v.PricesIncludeTax,
        v.TipsEnabled, v.TipPresetsBasisPoints, v.MostCommonTipBasisPoints,
        v.ReviewsEnabled, v.ReviewPlatform, v.ReviewUrl, v.FeedbackTitle);
}
