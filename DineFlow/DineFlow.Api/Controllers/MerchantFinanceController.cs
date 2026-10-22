using DineFlow.Api.Common;
using DineFlow.Application.Dashboard;
using DineFlow.Application.Transactions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

/// <summary>Transactions and refunds (blueprint 9.2 /v1/merchant/transactions/*).</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[Route("v1/merchant/transactions")]
public class MerchantTransactionsController(TransactionService transactions) : ControllerBase
{
    /// <summary>Payments for a venue and period, newest first, with totals for the successful ones.</summary>
    [HttpGet]
    public Task<TransactionPageDto> List([FromQuery] TransactionQuery query, CancellationToken ct) => transactions.ListAsync(query, ct);

    /// <summary>A payment with its items, refunds and complete timeline.</summary>
    [HttpGet("{paymentId:guid}")]
    public Task<TransactionDetailDto> Get(Guid paymentId, CancellationToken ct) => transactions.GetAsync(paymentId, ct);

    /// <summary>Full or partial refund; owners and managers only. It completes when the provider confirms it.</summary>
    [HttpPost("{paymentId:guid}/refunds")]
    [Authorize(Policy = Policies.MerchantManager)]
    public Task<TransactionDetailDto> Refund(Guid paymentId, RefundRequest request, CancellationToken ct) =>
        transactions.RefundAsync(paymentId, request, User.UserId(), ct);
}

/// <summary>The merchant overview and the live table view (blueprint 4.4).</summary>
[ApiController]
[Authorize(Policy = Policies.Merchant)]
[Route("v1/merchant/venues/{venueId:guid}")]
public class MerchantDashboardController(OverviewService overview, TableStatusService tables) : ControllerBase
{
    [HttpGet("overview")]
    public Task<OverviewDto> Overview(Guid venueId, [FromQuery] OverviewPeriod period, [FromQuery] TrendGranularity? granularity, CancellationToken ct) =>
        overview.GetAsync(venueId, period, granularity, ct);

    [HttpGet("table-status")]
    public Task<IReadOnlyList<TableStatusDto>> TableStatus(Guid venueId, CancellationToken ct) => tables.ListAsync(venueId, ct);
}
