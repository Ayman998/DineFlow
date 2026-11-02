using HelloZen.Api.Common;
using HelloZen.Application.Bills;
using HelloZen.Application.Tables;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelloZen.Api.Controllers;

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

/// <summary>A table's open bill item by item, and moving it, whole or in part, to another table. Any staff role.</summary>
[ApiController]
[Route("v1/merchant/tables/{locationId:guid}")]
[Authorize(Policy = Policies.Merchant)]
public class MerchantTableBillsController(TableTransferService transfers) : ControllerBase
{
    [HttpGet("bill")]
    public Task<TableBillDto> Bill(Guid locationId, CancellationToken ct) => transfers.GetBillAsync(locationId, ct);

    /// <summary>Moves the listed items (or the whole bill, when none are listed) to another table.</summary>
    [HttpPost("transfer")]
    public Task<TransferResultDto> Transfer(Guid locationId, TransferRequest request, CancellationToken ct) =>
        transfers.TransferAsync(locationId, request, User.UserId(), ct);
}
