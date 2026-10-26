using DineFlow.Domain.Common;

namespace DineFlow.Domain.Finance;

/// <summary>
/// One payout from the acquirer to the venue's bank account (blueprint "Settlement / Payout":
/// provider ref, gross, fees, net, expected date, status). It lists the transactions it covers,
/// each with the acquirer's fee, so fees and payout references are kept apart from the payments
/// themselves (blueprint 7.2). In Jordan it comes from the acquirer's settlement report (a file);
/// the sandbox can also produce one for testing.
/// </summary>
public class Settlement : Entity, ITenantOwned
{
    private readonly List<SettlementLine> _lines = [];

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public string Provider { get; private set; } = string.Empty;
    /// <summary>The acquirer's batch or payout reference; unique per provider and venue.</summary>
    public string BatchRef { get; private set; } = string.Empty;
    public string Currency { get; private set; } = string.Empty;
    public DateOnly PayoutDate { get; private set; }
    public SettlementStatus Status { get; private set; }
    public SettlementSource Source { get; private set; }
    public string? FileName { get; private set; }
    public Guid? ImportedByStaffId { get; private set; }

    /// <summary>Payments in the batch (positive).</summary>
    public long GrossMinor { get; private set; }
    /// <summary>Refunds taken back in the batch (positive number, subtracted).</summary>
    public long RefundsMinor { get; private set; }
    /// <summary>Acquirer fees and adjustments (positive number, subtracted).</summary>
    public long FeesMinor { get; private set; }
    public long NetMinor { get; private set; }

    public IReadOnlyList<SettlementLine> Lines => _lines;

    private Settlement() { }

    public Settlement(Guid tenantId, Guid venueId, string provider, string batchRef, string currency, DateOnly payoutDate,
        SettlementStatus status, SettlementSource source, string? fileName, Guid? staffUserId, IEnumerable<SettlementLine> lines, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        Provider = provider;
        BatchRef = Text.Required(batchRef, 100, "Batch reference");
        Currency = currency;
        PayoutDate = payoutDate;
        Status = status;
        Source = source;
        FileName = Text.Optional(fileName, 200, "File name");
        ImportedByStaffId = staffUserId;
        CreatedAt = now;

        foreach (var line in lines)
        {
            line.Attach(this);
            _lines.Add(line);
        }
        if (_lines.Count == 0) throw new DomainException($"Batch {batchRef} has no transactions.");

        GrossMinor = _lines.Where(l => l.Kind == SettlementLineKind.Payment).Sum(l => l.AmountMinor);
        RefundsMinor = _lines.Where(l => l.Kind == SettlementLineKind.Refund).Sum(l => l.AmountMinor);
        FeesMinor = _lines.Sum(l => l.FeeMinor) + _lines.Where(l => l.Kind == SettlementLineKind.Adjustment).Sum(l => l.AmountMinor);
        NetMinor = GrossMinor - RefundsMinor - FeesMinor;
    }

    public void MarkPaid() => Status = SettlementStatus.Paid;
}

/// <summary>One transaction in a settlement batch, as the acquirer reports it.</summary>
public class SettlementLine : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid SettlementId { get; private set; }
    public SettlementLineKind Kind { get; private set; }
    public DateOnly TransactionDate { get; private set; }
    /// <summary>The acquirer's order id (DineFlow sends its own, e.g. df-… or sbx_…).</summary>
    public string? ProviderPaymentId { get; private set; }
    /// <summary>DineFlow's reference (DF-…) when the report carries it.</summary>
    public string? Reference { get; private set; }
    public string? ProviderRefundId { get; private set; }
    /// <summary>Positive: the payment's gross, the refund's amount, or the adjustment charged.</summary>
    public long AmountMinor { get; private set; }
    public long FeeMinor { get; private set; }
    /// <summary>The DineFlow payment this line was matched to, once reconciliation finds it.</summary>
    public Guid? PaymentId { get; private set; }

    private SettlementLine() { }

    public SettlementLine(Guid tenantId, SettlementLineKind kind, DateOnly transactionDate, string? providerPaymentId, string? reference,
        string? providerRefundId, long amountMinor, long feeMinor)
    {
        if (amountMinor < 0 || feeMinor < 0) throw new DomainException("Settlement amounts and fees must be positive; the line type says which way they go.");
        TenantId = tenantId;
        Kind = kind;
        TransactionDate = transactionDate;
        ProviderPaymentId = Text.Optional(providerPaymentId, 100, "Order id");
        Reference = Text.Optional(reference, 40, "Reference");
        ProviderRefundId = Text.Optional(providerRefundId, 100, "Refund id");
        AmountMinor = amountMinor;
        FeeMinor = feeMinor;
    }

    internal void Attach(Settlement settlement)
    {
        SettlementId = settlement.Id;
        CreatedAt = settlement.CreatedAt;
    }

    public void MatchTo(Guid paymentId) => PaymentId = paymentId;
}

public enum SettlementStatus
{
    /// <summary>Reported by the acquirer, not yet seen in the bank account.</summary>
    Expected = 0,
    /// <summary>The money arrived.</summary>
    Paid = 1
}

public enum SettlementSource
{
    /// <summary>Imported from the acquirer's settlement report.</summary>
    File = 0,
    /// <summary>Produced by the provider itself (the sandbox, for testing).</summary>
    Provider = 1
}

public enum SettlementLineKind
{
    Payment = 0,
    Refund = 1,
    /// <summary>A fee or charge that isn't tied to one payment (e.g. a monthly terminal fee).</summary>
    Adjustment = 2
}
