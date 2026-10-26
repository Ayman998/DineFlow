using DineFlow.Domain.Common;

namespace DineFlow.Domain.Finance;

/// <summary>
/// The result of reconciling one business day at a venue (blueprint 7.2 "daily reconciliation
/// compares internal ledger, PSP reports/webhooks and POS payment records"). Running it again
/// replaces the totals; discrepancies are kept separately so their history and notes survive.
/// </summary>
public class ReconciliationRun : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public DateTimeOffset RanAt { get; private set; }

    // DineFlow's ledger for the day
    public int Payments { get; private set; }
    public long LedgerGrossMinor { get; private set; }
    public long LedgerTipsMinor { get; private set; }
    public long LedgerRefundsMinor { get; private set; }

    // The acquirer's settlement reports, for the day's transactions
    public int Settled { get; private set; }
    public long SettledGrossMinor { get; private set; }
    public long SettledFeesMinor { get; private set; }
    /// <summary>Payments not in any settlement yet, but still within the grace period.</summary>
    public int AwaitingSettlement { get; private set; }

    // The POS, when the venue has one
    public int PostedToPos { get; private set; }

    public int Matched { get; private set; }
    public int OpenDiscrepancies { get; private set; }

    private ReconciliationRun() { }

    public ReconciliationRun(Guid tenantId, Guid venueId, DateOnly businessDate, string currency)
    {
        TenantId = tenantId;
        VenueId = venueId;
        BusinessDate = businessDate;
        Currency = currency;
    }

    public void Record(DateTimeOffset now, int payments, long ledgerGross, long ledgerTips, long ledgerRefunds,
        int settled, long settledGross, long settledFees, int awaitingSettlement, int postedToPos, int matched, int openDiscrepancies)
    {
        RanAt = now;
        CreatedAt = CreatedAt == default ? now : CreatedAt;
        (Payments, LedgerGrossMinor, LedgerTipsMinor, LedgerRefundsMinor) = (payments, ledgerGross, ledgerTips, ledgerRefunds);
        (Settled, SettledGrossMinor, SettledFeesMinor, AwaitingSettlement) = (settled, settledGross, settledFees, awaitingSettlement);
        (PostedToPos, Matched, OpenDiscrepancies) = (postedToPos, matched, openDiscrepancies);
    }

    public bool Balanced => OpenDiscrepancies == 0;
}

/// <summary>
/// Something that doesn't add up between DineFlow, the acquirer and the POS (blueprint "differences
/// enter a discrepancy queue"). It stays open until it's fixed, when the next run closes it by itself,
/// or until someone with authority resolves it with a note, which is audit-logged.
/// </summary>
public class Discrepancy : Entity, ITenantOwned
{
    public const int MaxNoteLength = 300;

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public DateOnly BusinessDate { get; private set; }
    public DiscrepancyKind Kind { get; private set; }
    /// <summary>Identifies the problem so a re-run finds the same one, e.g. "NotSettled:{paymentId}".</summary>
    public string Key { get; private set; } = string.Empty;
    public Guid? PaymentId { get; private set; }
    public Guid? SettlementLineId { get; private set; }
    /// <summary>A reference people recognise: DF-… or the acquirer's order id.</summary>
    public string Reference { get; private set; } = string.Empty;
    public long ExpectedMinor { get; private set; }
    public long ActualMinor { get; private set; }
    public string Details { get; private set; } = string.Empty;

    public DiscrepancyStatus Status { get; private set; } = DiscrepancyStatus.Open;
    public DateTimeOffset? ResolvedAt { get; private set; }
    public Guid? ResolvedByStaffId { get; private set; }
    public string? Note { get; private set; }

    private Discrepancy() { }

    public Discrepancy(Guid tenantId, Guid venueId, DateOnly businessDate, DiscrepancyKind kind, string key, Guid? paymentId,
        Guid? settlementLineId, string reference, long expectedMinor, long actualMinor, string details, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        BusinessDate = businessDate;
        Kind = kind;
        Key = key;
        PaymentId = paymentId;
        SettlementLineId = settlementLineId;
        Reference = reference;
        ExpectedMinor = expectedMinor;
        ActualMinor = actualMinor;
        Details = details;
        CreatedAt = now;
    }

    public long DifferenceMinor => ActualMinor - ExpectedMinor;

    /// <summary>Still there on a re-run: refresh the numbers (they may have changed).</summary>
    public void Seen(long expectedMinor, long actualMinor, string details)
    {
        (ExpectedMinor, ActualMinor, Details) = (expectedMinor, actualMinor, details);
        if (Status == DiscrepancyStatus.Cleared)
        {
            Status = DiscrepancyStatus.Open;
            ResolvedAt = null;
        }
    }

    /// <summary>Gone on a re-run, e.g. the settlement arrived or the POS got the payment.</summary>
    public void Clear(DateTimeOffset now)
    {
        if (Status != DiscrepancyStatus.Open) return;
        Status = DiscrepancyStatus.Cleared;
        ResolvedAt = now;
    }

    /// <summary>Accepted by a person, with the reason (e.g. "acquirer fee dispute raised, ticket 1234").</summary>
    public void Resolve(Guid staffUserId, string note, DateTimeOffset now)
    {
        if (Status == DiscrepancyStatus.Resolved) throw new DomainException("This has already been resolved.");
        Note = Text.Required(note, MaxNoteLength, "A note");
        Status = DiscrepancyStatus.Resolved;
        ResolvedAt = now;
        ResolvedByStaffId = staffUserId;
    }
}

public enum DiscrepancyKind
{
    /// <summary>DineFlow has a successful payment the acquirer hasn't settled after the grace period.</summary>
    NotSettled = 0,
    /// <summary>The acquirer settled a payment DineFlow doesn't have as successful.</summary>
    UnknownToDineFlow = 1,
    /// <summary>Both have it, with different amounts.</summary>
    AmountMismatch = 2,
    /// <summary>A refund confirmed in DineFlow isn't in the settlements, or the other way round, or differs.</summary>
    RefundMismatch = 3,
    /// <summary>The payment should be in the POS but isn't (waiting too long or refused).</summary>
    NotInPos = 4,
    /// <summary>The same payment appears in more than one settlement line.</summary>
    SettledTwice = 5
}

public enum DiscrepancyStatus
{
    Open = 0,
    /// <summary>Fixed itself: a later run no longer found it.</summary>
    Cleared = 1,
    /// <summary>Accepted by a person with a note.</summary>
    Resolved = 2
}
