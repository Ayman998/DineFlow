using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace DineFlow.Application.Tables;

/// <summary>
/// Dining areas, service locations and their QR codes for the signed-in merchant
/// (P1-MER-02: create/manage areas, tables and QR tokens; disable/rotate tokens).
/// </summary>
public class TableService(
    IAppDbContext db,
    ITenantContext tenant,
    IQrTokenService qrTokens,
    IOptions<GuestOptions> guestOptions,
    TimeProvider clock)
{
    public async Task<FloorPlanDto> GetFloorPlanAsync(Guid venueId, CancellationToken ct)
    {
        var venue = await FindVenueAsync(venueId, ct);

        var areas = await db.DiningAreas
            .Where(a => a.VenueId == venueId)
            .OrderBy(a => a.SortOrder).ThenBy(a => a.Name)
            .ToListAsync(ct);

        var locations = await db.ServiceLocations
            .Where(l => l.VenueId == venueId)
            .ToListAsync(ct);

        var currentQr = await CurrentQrByLocationAsync(locations.Select(l => l.Id).ToList(), ct);

        var dtos = locations
            .OrderBy(l => l.Type).ThenBy(l => l.Label.Length).ThenBy(l => l.Label)
            .Select(l => ToDto(l, currentQr.GetValueOrDefault(l.Id)))
            .ToList();

        return new FloorPlanDto(
            venue.Id,
            venue.Name,
            areas.Select(a => new AreaDto(a.Id, a.Name, a.SortOrder,
                dtos.Where(l => l.DiningAreaId == a.Id).ToList())).ToList(),
            dtos.Where(l => l.DiningAreaId is null).ToList());
    }

    public async Task<AreaDto> CreateAreaAsync(Guid venueId, CreateAreaRequest request, CancellationToken ct)
    {
        await FindVenueAsync(venueId, ct);

        var nextOrder = await db.DiningAreas.Where(a => a.VenueId == venueId)
            .Select(a => (int?)a.SortOrder).MaxAsync(ct) ?? -1;

        var area = new DiningArea(tenant.RequireTenantId(), venueId, request.Name, nextOrder + 1);
        db.DiningAreas.Add(area);
        await db.SaveChangesAsync(ct);

        return new AreaDto(area.Id, area.Name, area.SortOrder, []);
    }

    /// <summary>Creates a location and issues its first QR code.</summary>
    public async Task<LocationDto> CreateLocationAsync(CreateLocationRequest request, CancellationToken ct)
    {
        var created = await CreateLocationsAsync(
            request.VenueId, request.DiningAreaId, request.Type, [request.Label], ct);
        return created.Single();
    }

    /// <summary>Creates tables numbered From..To in one go, skipping numbers that already exist.</summary>
    public async Task<IReadOnlyList<LocationDto>> BulkCreateTablesAsync(BulkCreateTablesRequest request, CancellationToken ct)
    {
        if (request.To < request.From)
            throw AppException.BadRequest("invalid_range", "'To' must be greater than or equal to 'From'.");
        if (request.To - request.From >= 200)
            throw AppException.BadRequest("invalid_range", "Create at most 200 tables at a time.");

        var existing = await db.ServiceLocations
            .Where(l => l.VenueId == request.VenueId && l.Type == ServiceLocationType.Table)
            .Select(l => l.Label)
            .ToListAsync(ct);

        var labels = Enumerable.Range(request.From, request.To - request.From + 1)
            .Select(n => n.ToString())
            .Except(existing)
            .ToList();

        return labels.Count == 0
            ? []
            : await CreateLocationsAsync(request.VenueId, request.DiningAreaId, ServiceLocationType.Table, labels, ct);
    }

    /// <summary>
    /// Retires the current QR code and issues a new version (P1-MER-02 rotate). Printed copies
    /// of the old code stop working straight away.
    /// </summary>
    public async Task<LocationDto> RotateQrAsync(Guid locationId, CancellationToken ct)
    {
        var location = await FindLocationAsync(locationId, ct);
        var current = await CurrentQrAsync(locationId, ct);

        var nextVersion = 1;
        if (current is not null)
        {
            current.Retire(clock.GetUtcNow());
            nextVersion = current.Version + 1;
        }

        var issued = IssueQr(location, nextVersion);
        await db.SaveChangesAsync(ct);

        return ToDto(location, issued);
    }

    /// <summary>Turns the current QR code off or back on without changing it.</summary>
    public async Task<LocationDto> SetQrEnabledAsync(Guid locationId, bool enabled, CancellationToken ct)
    {
        var location = await FindLocationAsync(locationId, ct);
        var current = await CurrentQrAsync(locationId, ct)
            ?? throw AppException.NotFound("qr_not_found", "This location has no QR code. Rotate it to issue one.");

        if (enabled) current.Enable(); else current.Disable();
        await db.SaveChangesAsync(ct);

        return ToDto(location, current);
    }

    private async Task<IReadOnlyList<LocationDto>> CreateLocationsAsync(
        Guid venueId, Guid? areaId, ServiceLocationType type, IReadOnlyList<string> labels, CancellationToken ct)
    {
        await FindVenueAsync(venueId, ct);

        if (areaId is not null &&
            !await db.DiningAreas.AnyAsync(a => a.Id == areaId && a.VenueId == venueId, ct))
        {
            throw AppException.BadRequest("area_not_found", "That area doesn't belong to this venue.");
        }

        var trimmed = labels.Select(l => l.Trim()).ToList();
        var clash = await db.ServiceLocations
            .Where(l => l.VenueId == venueId && l.Type == type && trimmed.Contains(l.Label))
            .Select(l => l.Label)
            .FirstOrDefaultAsync(ct);
        if (clash is not null)
            throw AppException.Conflict("location_exists", $"{type} {clash} already exists at this venue.");

        var tenantId = tenant.RequireTenantId();
        var result = new List<LocationDto>();
        foreach (var label in trimmed)
        {
            var location = new ServiceLocation(tenantId, venueId, areaId, type, label);
            db.ServiceLocations.Add(location);
            result.Add(ToDto(location, IssueQr(location, version: 1)));
        }

        await db.SaveChangesAsync(ct);
        return result;
    }

    private QrToken IssueQr(ServiceLocation location, int version)
    {
        var id = Guid.NewGuid();
        var qr = new QrToken(id, location.TenantId, location.Id, version, qrTokens.Hash(qrTokens.CreateToken(id)));
        db.QrTokens.Add(qr);
        return qr;
    }

    private Task<QrToken?> CurrentQrAsync(Guid locationId, CancellationToken ct) =>
        db.QrTokens
            .Where(q => q.ServiceLocationId == locationId && q.Status != QrTokenStatus.Retired)
            .OrderByDescending(q => q.Version)
            .FirstOrDefaultAsync(ct);

    private async Task<Dictionary<Guid, QrToken>> CurrentQrByLocationAsync(List<Guid> locationIds, CancellationToken ct)
    {
        var tokens = await db.QrTokens
            .Where(q => locationIds.Contains(q.ServiceLocationId) && q.Status != QrTokenStatus.Retired)
            .ToListAsync(ct);

        return tokens
            .GroupBy(q => q.ServiceLocationId)
            .ToDictionary(g => g.Key, g => g.MaxBy(q => q.Version)!);
    }

    private async Task<Venue> FindVenueAsync(Guid venueId, CancellationToken ct) =>
        await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId, ct)
        ?? throw AppException.NotFound("venue_not_found", "Venue not found.");

    private async Task<ServiceLocation> FindLocationAsync(Guid locationId, CancellationToken ct) =>
        await db.ServiceLocations.SingleOrDefaultAsync(l => l.Id == locationId, ct)
        ?? throw AppException.NotFound("location_not_found", "Location not found.");

    private LocationDto ToDto(ServiceLocation l, QrToken? qr) => new(
        l.Id, l.DiningAreaId, l.Type.ToString(), l.Label, l.DisplayName, l.IsActive,
        qr is null ? null : new QrDto(qr.Id, qr.Version, qr.Status.ToString(), QrUrl(qr.Id)));

    private string QrUrl(Guid qrTokenId) =>
        $"{guestOptions.Value.AppBaseUrl.TrimEnd('/')}/q/{qrTokens.CreateToken(qrTokenId)}";
}
