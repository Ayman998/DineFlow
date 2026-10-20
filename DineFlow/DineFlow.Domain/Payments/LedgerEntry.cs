using DineFlow.Domain.Common;

namespace DineFlow.Domain.Payments;

/// <summary>
/// Append-only money movement (blueprint 7.2). A successful payment writes its bill share,
/// any promotion and any tip as separate entries; refunds will be new entries, never edits.
/// The bill's paid amount is the sum of its BillPayment and Discount entries.
/// </summary>
public class LedgerEntry : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid CheckId { get; private set; }
    public Guid PaymentId { get; private set; }
    public LedgerEntryType Type { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = string.Empty;

    private LedgerEntry() { }

    private LedgerEntry(Payment payment, LedgerEntryType type, long amountMinor, DateTimeOffset now)
    {
        TenantId = payment.TenantId;
        VenueId = payment.VenueId;
        CheckId = payment.CheckId;
        PaymentId = payment.Id;
        Type = type;
        AmountMinor = amountMinor;
        Currency = payment.Currency;
        CreatedAt = now;
    }

    /// <summary>The entries for a payment that has just succeeded.</summary>
    public static IReadOnlyList<LedgerEntry> ForSucceededPayment(Payment payment, DateTimeOffset now)
    {
        if (payment.Status != PaymentStatus.Succeeded) throw new DomainException("Only successful payments go in the ledger.");

        var entries = new List<LedgerEntry>
        {
            new(payment, LedgerEntryType.BillPayment, payment.BillAmountMinor - payment.DiscountMinor, now)
        };
        if (payment.DiscountMinor > 0) entries.Add(new(payment, LedgerEntryType.Discount, payment.DiscountMinor, now));
        if (payment.TipMinor > 0) entries.Add(new(payment, LedgerEntryType.Tip, payment.TipMinor, now));
        return entries;
    }
}

public enum LedgerEntryType
{
    /// <summary>Money the guest paid towards the bill (excluding tip).</summary>
    BillPayment = 0,
    /// <summary>Part of the bill covered by a promotion.</summary>
    Discount = 1,
    Tip = 2,
    Refund = 3
}
