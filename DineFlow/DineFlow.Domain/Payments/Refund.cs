using DineFlow.Domain.Common;

namespace DineFlow.Domain.Payments;

/// <summary>
/// Money given back to a guest for a successful payment (blueprint P1-LNK-03, states
/// REFUND_PENDING → REFUNDED). It is requested at the provider and only counts once the
/// provider confirms it; the original payment is never changed, the refund is its own ledger
/// entry (blueprint 7.2). Full or partial, and several refunds can be made against one payment
/// as long as together they never exceed what the guest paid.
/// </summary>
public class Refund : Entity, ITenantOwned
{
    public const int MaxReasonLength = 200;

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid PaymentId { get; private set; }
    /// <summary>The table's bill the payment was for; null for a payment-link payment.</summary>
    public Guid? CheckId { get; private set; }
    /// <summary>The merchant app's id for this request; retries return the same refund (blueprint 7.3).</summary>
    public Guid ClientRequestId { get; private set; }
    public long AmountMinor { get; private set; }
    public string Currency { get; private set; } = string.Empty;
    public string Reason { get; private set; } = string.Empty;
    public Guid RequestedByStaffId { get; private set; }

    public string Provider { get; private set; } = string.Empty;
    public string? ProviderRefundId { get; private set; }
    public RefundStatus Status { get; private set; } = RefundStatus.Pending;
    public string? FailureReason { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }

    private Refund() { }

    /// <param name="refundableMinor">What is left to refund on the payment: paid, minus earlier refunds that succeeded or are pending.</param>
    public Refund(Payment payment, Guid clientRequestId, long amountMinor, long refundableMinor, string reason, Guid staffUserId, DateTimeOffset now)
    {
        if (payment.Status != PaymentStatus.Succeeded) throw new DomainException("Only a successful payment can be refunded.");
        if (amountMinor < 1) throw new DomainException("Enter an amount to refund.");
        if (amountMinor > refundableMinor)
            throw new DomainException(refundableMinor == 0 ? "This payment has already been fully refunded." : "That's more than is left to refund on this payment.");

        TenantId = payment.TenantId;
        VenueId = payment.VenueId;
        PaymentId = payment.Id;
        CheckId = payment.CheckId;
        ClientRequestId = clientRequestId;
        AmountMinor = amountMinor;
        Currency = payment.Currency;
        Reason = Text.Required(reason, MaxReasonLength, "A reason");
        RequestedByStaffId = staffUserId;
        Provider = payment.Provider;
        CreatedAt = now;
    }

    public bool IsFinal => Status is RefundStatus.Succeeded or RefundStatus.Failed;

    public void AttachProvider(string providerRefundId) => ProviderRefundId = providerRefundId;

    /// <summary>The provider's verdict. Final states stick; returns true only when this call made it succeed.</summary>
    public bool Apply(RefundStatus status, string? failureReason, DateTimeOffset now)
    {
        if (IsFinal || status == RefundStatus.Pending) return false;
        Status = status;
        CompletedAt = now;
        if (status == RefundStatus.Failed) FailureReason = failureReason ?? "The provider declined the refund.";
        return status == RefundStatus.Succeeded;
    }
}

public enum RefundStatus
{
    /// <summary>Requested at the provider, waiting for its confirmation.</summary>
    Pending = 0,
    Succeeded = 1,
    Failed = 2
}
