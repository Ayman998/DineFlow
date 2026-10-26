using System.ComponentModel.DataAnnotations;
using DineFlow.Domain.Finance;

namespace DineFlow.Application.Finance;

public record SettlementDto(
    Guid Id, string BatchRef, string Provider, DateOnly PayoutDate, SettlementStatus Status, SettlementSource Source, string? FileName,
    string Currency, int Transactions, long GrossMinor, long RefundsMinor, long FeesMinor, long NetMinor, DateTimeOffset ImportedAt);

/// <param name="PaymentReference">DineFlow's reference for the payment this line matched, once reconciled.</param>
public record SettlementLineDto(
    Guid Id, SettlementLineKind Kind, DateOnly TransactionDate, string? OrderId, string? Reference, long AmountMinor, long FeeMinor,
    Guid? PaymentId, string? PaymentReference);

public record SettlementDetailDto(SettlementDto Settlement, IReadOnlyList<SettlementLineDto> Lines);

/// <summary>Totals over the listed settlements.</summary>
public record SettlementTotalsDto(long GrossMinor, long RefundsMinor, long FeesMinor, long NetMinor);

public record SettlementPageDto(IReadOnlyList<SettlementDto> Items, SettlementTotalsDto Totals);

/// <param name="Days">Business days reconciled again because of this import.</param>
public record SettlementImportResultDto(IReadOnlyList<SettlementDto> Settlements, int Lines, IReadOnlyList<DateOnly> Days, int OpenDiscrepancies);

public record ReconciliationRunDto(
    DateOnly BusinessDate, DateTimeOffset RanAt, string Currency,
    int Payments, long LedgerGrossMinor, long LedgerTipsMinor, long LedgerRefundsMinor,
    int Settled, long SettledGrossMinor, long SettledFeesMinor, int AwaitingSettlement,
    int PostedToPos, int Matched, int OpenDiscrepancies);

public record DiscrepancyDto(
    Guid Id, DateOnly BusinessDate, DiscrepancyKind Kind, string Reference, long ExpectedMinor, long ActualMinor, long DifferenceMinor,
    string Details, DiscrepancyStatus Status, Guid? PaymentId, DateTimeOffset CreatedAt, DateTimeOffset? ResolvedAt, string? ResolvedBy, string? Note);

public record DiscrepancyPageDto(IReadOnlyList<DiscrepancyDto> Items, int Total, int Page, int PageSize, int Open);

public record DiscrepancyQuery(
    [Required] Guid VenueId,
    /// <summary>Null for every status.</summary>
    DiscrepancyStatus? Status = null,
    [Range(1, 10_000)] int Page = 1,
    [Range(1, 100)] int PageSize = 50);

public record ResolveDiscrepancyRequest([Required, StringLength(Discrepancy.MaxNoteLength, MinimumLength = 3)] string Note);

public record ReconcileRequest([Required] Guid VenueId, DateOnly? Day);
