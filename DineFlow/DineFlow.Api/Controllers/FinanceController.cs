using System.Text;
using DineFlow.Api.Common;
using DineFlow.Application.Common;
using DineFlow.Application.Finance;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

/// <summary>
/// Settlements, reconciliation and the ledger export (blueprint 9.2 /v1/merchant/settlements,
/// /v1/merchant/reconciliation/discrepancies). Money detail is for owners and managers.
/// </summary>
[ApiController]
[Authorize(Policy = Policies.MerchantManager)]
[Route("v1/merchant")]
public class MerchantSettlementsController(FinanceService finance, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet("settlements")]
    public Task<SettlementPageDto> Settlements([FromQuery] Guid venueId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        finance.SettlementsAsync(venueId, from, to, ct);

    [HttpGet("settlements/{settlementId:guid}")]
    public Task<SettlementDetailDto> Settlement(Guid settlementId, CancellationToken ct) => finance.SettlementAsync(settlementId, ct);

    /// <summary>Imports the acquirer's settlement report (CSV) and reconciles the days it covers.</summary>
    [HttpPost("settlements/import")]
    [RequestSizeLimit(FinanceService.MaxImportBytes + 64 * 1024)]
    public async Task<SettlementImportResultDto> Import([FromQuery] Guid venueId, IFormFile file, CancellationToken ct)
    {
        if (file.Length is 0 or > FinanceService.MaxImportBytes)
            throw AppException.BadRequest("invalid_settlement_file", "Choose a CSV settlement report up to 5 MB.");
        using var reader = new StreamReader(file.OpenReadStream(), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return await finance.ImportAsync(venueId, Path.GetFileName(file.FileName), await reader.ReadToEndAsync(ct), User.UserId(), ct);
    }

    /// <summary>Development: the sandbox acquirer pays out what it hasn't settled yet.</summary>
    [HttpPost("settlements/sandbox-payout")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public Task<SettlementImportResultDto> SandboxPayout([FromQuery] Guid venueId, CancellationToken ct) =>
        environment.IsDevelopment()
            ? finance.SandboxPayoutAsync(venueId, User.UserId(), ct)
            : throw AppException.NotFound("not_found", "Not found.");

    [HttpGet("reconciliation")]
    public Task<IReadOnlyList<ReconciliationRunDto>> Runs([FromQuery] Guid venueId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        finance.RunsAsync(venueId, from, to, ct);

    /// <summary>Reconciles a day now (yesterday when no day is given).</summary>
    [HttpPost("reconciliation/run")]
    public Task<ReconciliationRunDto> Run(ReconcileRequest request, CancellationToken ct) => finance.ReconcileAsync(request, ct);

    [HttpGet("reconciliation/discrepancies")]
    public Task<DiscrepancyPageDto> Discrepancies([FromQuery] DiscrepancyQuery query, CancellationToken ct) => finance.DiscrepanciesAsync(query, ct);

    [HttpPost("reconciliation/discrepancies/{discrepancyId:guid}/resolve")]
    public Task<DiscrepancyDto> Resolve(Guid discrepancyId, ResolveDiscrepancyRequest request, CancellationToken ct) =>
        finance.ResolveAsync(discrepancyId, request.Note, User.UserId(), ct);

    /// <summary>The ledger for a period as CSV, with settlement batch and fee per payment.</summary>
    [HttpGet("ledger/export")]
    public async Task<IActionResult> Ledger([FromQuery] Guid venueId, [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
    {
        var csv = await finance.LedgerCsvAsync(venueId, from, to, ct);
        return File(Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(csv)).ToArray(), "text/csv; charset=utf-8",
            $"ledger-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }
}
