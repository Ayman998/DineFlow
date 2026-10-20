using DineFlow.Api.Common;
using DineFlow.Application.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

/// <summary>Service locations and their QR codes (blueprint 9.2: POST /tables, /tables/{id}/qr/rotate).</summary>
[ApiController]
[Route("v1/merchant/tables")]
[Authorize(Policy = Policies.MerchantManager)]
public class MerchantTablesController(TableService tables) : ControllerBase
{
    [HttpPost]
    public Task<LocationDto> Create(CreateLocationRequest request, CancellationToken ct) =>
        tables.CreateLocationAsync(request, ct);

    /// <summary>Creates tables numbered From..To, skipping numbers that already exist.</summary>
    [HttpPost("bulk")]
    public Task<IReadOnlyList<LocationDto>> BulkCreate(BulkCreateTablesRequest request, CancellationToken ct) =>
        tables.BulkCreateTablesAsync(request, ct);

    [HttpPost("{locationId:guid}/qr/rotate")]
    public Task<LocationDto> RotateQr(Guid locationId, CancellationToken ct) =>
        tables.RotateQrAsync(locationId, ct);

    [HttpPost("{locationId:guid}/qr/disable")]
    public Task<LocationDto> DisableQr(Guid locationId, CancellationToken ct) =>
        tables.SetQrEnabledAsync(locationId, enabled: false, ct);

    [HttpPost("{locationId:guid}/qr/enable")]
    public Task<LocationDto> EnableQr(Guid locationId, CancellationToken ct) =>
        tables.SetQrEnabledAsync(locationId, enabled: true, ct);
}
