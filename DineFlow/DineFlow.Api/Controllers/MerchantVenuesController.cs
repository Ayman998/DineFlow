using DineFlow.Api.Common;
using DineFlow.Application.Tables;
using DineFlow.Application.Venues;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

[ApiController]
[Route("v1/merchant/venues")]
[Authorize(Policy = Policies.Merchant)]
public class MerchantVenuesController(VenueService venues, TableService tables) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<VenueDto>> List(CancellationToken ct) => venues.ListAsync(ct);

    [HttpGet("{venueId:guid}")]
    public Task<VenueDto> Get(Guid venueId, CancellationToken ct) => venues.GetAsync(venueId, ct);

    [HttpPost]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<VenueDto> Create(VenueSettingsRequest request, CancellationToken ct) =>
        venues.CreateAsync(request, ct);

    [HttpPut("{venueId:guid}/settings")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<VenueDto> UpdateSettings(Guid venueId, VenueSettingsRequest request, CancellationToken ct) =>
        venues.UpdateSettingsAsync(venueId, request, ct);

    /// <summary>The venue's areas and locations with their current QR codes.</summary>
    [HttpGet("{venueId:guid}/tables")]
    public Task<FloorPlanDto> FloorPlan(Guid venueId, CancellationToken ct) =>
        tables.GetFloorPlanAsync(venueId, ct);

    [HttpPost("{venueId:guid}/areas")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<AreaDto> CreateArea(Guid venueId, CreateAreaRequest request, CancellationToken ct) =>
        tables.CreateAreaAsync(venueId, request, ct);

    /// <summary>Renames an area, in English and (optionally) Arabic.</summary>
    [HttpPut("{venueId:guid}/areas/{areaId:guid}")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<AreaDto> UpdateArea(Guid venueId, Guid areaId, UpdateAreaRequest request, CancellationToken ct) =>
        tables.UpdateAreaAsync(venueId, areaId, request, ct);

    /// <summary>Closes a whole area: its QR codes stop opening and its tables take no new orders.</summary>
    [HttpPost("{venueId:guid}/areas/{areaId:guid}/disable")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<AreaDto> DisableArea(Guid venueId, Guid areaId, CancellationToken ct) =>
        tables.SetAreaActiveAsync(venueId, areaId, active: false, ct);

    [HttpPost("{venueId:guid}/areas/{areaId:guid}/enable")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<AreaDto> EnableArea(Guid venueId, Guid areaId, CancellationToken ct) =>
        tables.SetAreaActiveAsync(venueId, areaId, active: true, ct);
}
