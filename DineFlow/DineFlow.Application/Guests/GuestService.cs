using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Guests;

public class GuestService(
    IAppDbContext db,
    IQrTokenService qrTokens,
    ITokenIssuer tokenIssuer,
    IOptions<GuestOptions> options,
    TimeProvider clock)
{
    private static readonly string[] SupportedLocales = ["en", "ar"];

    /// <summary>
    /// Resolves a scanned QR token to its venue and location and starts a guest session
    /// (P1-QR-01, P1-QR-02). Unknown, disabled and retired tokens all give the same
    /// "qr_invalid" answer, so the response never reveals which tokens exist.
    /// </summary>
    public async Task<QrResolutionDto> ResolveQrAsync(string token, string? locale, CancellationToken ct)
    {
        if (!qrTokens.IsWellFormed(token)) throw InvalidQr();

        var hash = qrTokens.Hash(token);

        // The guest has no tenant yet, so this one lookup bypasses the tenant filter.
        var qr = await db.QrTokens
            .IgnoreQueryFilters()
            .Include(q => q.ServiceLocation).ThenInclude(l => l.Venue).ThenInclude(v => v.Tenant)
            .Include(q => q.ServiceLocation).ThenInclude(l => l.DiningArea)
            .SingleOrDefaultAsync(q => q.TokenHash == hash, ct);

        if (qr is null || qr.Status != QrTokenStatus.Active) throw InvalidQr();

        var location = qr.ServiceLocation;
        var venue = location.Venue;
        if (!location.TakesGuests || !venue.IsActive || venue.Tenant.Status == TenantStatus.Suspended)
        {
            throw AppException.Gone("venue_unavailable", "This venue isn't taking orders or payments right now.");
        }

        var session = new GuestSession(
            venue.TenantId, venue.Id, location.Id, qr.Id,
            ChooseLocale(locale, venue.DefaultLocale),
            clock.GetUtcNow(), options.Value.SessionLifetime);

        db.GuestSessions.Add(session);
        await db.SaveChangesAsync(ct);

        var issued = tokenIssuer.IssueGuestToken(session);
        return new QrResolutionDto(issued.Token, ToContext(session, venue, location));
    }

    /// <summary>
    /// Reloads an existing session, e.g. after a page refresh. The tenant filter already limits
    /// this to the tenant in the guest's token.
    /// </summary>
    public async Task<GuestContextDto> GetSessionAsync(Guid sessionId, CancellationToken ct)
    {
        var session = await db.GuestSessions.SingleOrDefaultAsync(s => s.Id == sessionId, ct);
        if (session is null || session.IsExpired(clock.GetUtcNow()))
        {
            throw AppException.Unauthorized("session_expired", "Your session has ended. Scan the QR code again.");
        }

        var location = await db.ServiceLocations
            .Include(l => l.Venue).ThenInclude(v => v.Tenant)
            .Include(l => l.DiningArea)
            .SingleAsync(l => l.Id == session.ServiceLocationId, ct);

        return ToContext(session, location.Venue, location);
    }

    private static GuestContextDto ToContext(GuestSession session, Venue venue, ServiceLocation location) => new(
        session.Id,
        session.ExpiresAt,
        session.Locale,
        new GuestVenueDto(venue.Tenant.DisplayName, venue.Name, venue.LogoUrl, venue.BrandColor, venue.Currency, venue.NameAr),
        new GuestLocationDto(location.Type.ToString(), location.Label, location.DisplayName, location.DiningArea?.Name, location.DiningArea?.NameAr),
        new GuestFeaturesDto(venue.MenuEnabled, venue.PayAtTableEnabled));

    private static string ChooseLocale(string? requested, string venueDefault) =>
        requested is not null && SupportedLocales.Contains(requested) ? requested : venueDefault;

    private static AppException InvalidQr() =>
        AppException.NotFound("qr_invalid", "This QR code isn't active. Please ask a member of staff for help.");
}
