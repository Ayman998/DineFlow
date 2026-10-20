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
}
