using DineFlow.Api.Common;
using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Pos;
using DineFlow.Infrastructure.Pos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

/// <summary>
/// The venue's POS (blueprint E3, E9): connection and credentials, health, table and product
/// mapping, and what's been sent, is waiting, or was refused. Changing anything is for owners and managers.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[Route("v1/merchant")]
public class MerchantPosController(PosService pos) : ControllerBase
{
    [HttpGet("venues/{venueId:guid}/pos")]
    public Task<PosSettingsDto> Get(Guid venueId, CancellationToken ct) => pos.GetAsync(venueId, ct);

    /// <summary>Connects or reconfigures the POS. The access token is write-only: null keeps it, "" removes it.</summary>
    [HttpPut("venues/{venueId:guid}/pos")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<PosSettingsDto> Save(Guid venueId, PosConnectionRequest request, CancellationToken ct) =>
        pos.SaveConnectionAsync(venueId, request, User.UserId(), ct);

    [HttpPost("venues/{venueId:guid}/pos/check")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<PosSettingsDto> Check(Guid venueId, CancellationToken ct) => pos.CheckAsync(venueId, ct);

    [HttpPut("venues/{venueId:guid}/pos/mappings")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<PosSettingsDto> Map(Guid venueId, PosMappingRequest request, CancellationToken ct) =>
        pos.MapAsync(venueId, request, User.UserId(), ct);

    [HttpGet("pos/commands")]
    public Task<PosCommandPageDto> Commands([FromQuery] PosCommandQuery query, CancellationToken ct) => pos.CommandsAsync(query, ct);

    /// <summary>Sends a waiting or refused item again now, e.g. after mapping the table.</summary>
    [HttpPost("pos/commands/{commandId:guid}/retry")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<PosCommandDto> Retry(Guid commandId, CancellationToken ct) => pos.RetryAsync(commandId, User.UserId(), ct);
}

/// <summary>
/// The simulated POS's checks and failure mode, so the POS flows (and the failure cases in blueprint
/// 13.1) can be tried without a real POS. Development only.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.MerchantManager)]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("v1/merchant/venues/{venueId:guid}/pos/simulator")]
public class PosSimulatorController(SimulatedPosConnector simulator, IWebHostEnvironment environment) : ControllerBase
{
    public record SimulatorDto(SimulatorMode Mode, IReadOnlyList<PosCheck> Checks);

    public record SimulatorModeRequest(SimulatorMode Mode);

    [HttpGet]
    public SimulatorDto Get(Guid venueId)
    {
        EnsureDevelopment();
        return new(simulator.Mode, simulator.Checks(venueId));
    }

    [HttpPut]
    public SimulatorDto SetMode(Guid venueId, SimulatorModeRequest request)
    {
        EnsureDevelopment();
        simulator.Mode = request.Mode;
        return new(simulator.Mode, simulator.Checks(venueId));
    }

    private void EnsureDevelopment()
    {
        if (!environment.IsDevelopment()) throw AppException.NotFound("not_found", "Not found.");
    }
}
