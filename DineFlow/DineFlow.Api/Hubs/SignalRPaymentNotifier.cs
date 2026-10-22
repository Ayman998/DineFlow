using DineFlow.Application.Abstractions;
using Microsoft.AspNetCore.SignalR;

namespace DineFlow.Api.Hubs;

/// <summary>
/// Sends "paymentReceived" (a <see cref="PaymentNotice"/>) to the venue's staff screens and
/// "billChanged" to the guests at the table. Failures are logged, never thrown: the payment is
/// already recorded, and screens catch up on reload.
/// </summary>
public class SignalRPaymentNotifier(
    IHubContext<MerchantHub> merchants,
    IHubContext<GuestHub> guests,
    ILogger<SignalRPaymentNotifier> logger) : IPaymentNotifier
{
    public async Task PaymentSucceededAsync(PaymentNotice notice, CancellationToken ct)
    {
        try
        {
            await merchants.Clients.Group(MerchantHub.Group(notice.TenantId)).SendAsync("paymentReceived", notice, ct);
            await guests.Clients.Group(GuestHub.LocationGroup(notice.ServiceLocationId)).SendAsync("billChanged",
                new BillChangedEvent(notice.ServiceLocationId, "payment", notice.RemainingMinor, notice.FullyPaid), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't push payment {Reference}; screens will catch up on reload.", notice.Reference);
        }
    }

    public async Task RefundUpdatedAsync(RefundNotice notice, CancellationToken ct)
    {
        try
        {
            await merchants.Clients.Group(MerchantHub.Group(notice.TenantId)).SendAsync("refundUpdated", notice, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't push refund {RefundId}; screens will catch up on reload.", notice.RefundId);
        }
    }

    public async Task TableBillChangedAsync(Guid tenantId, Guid serviceLocationId, string reason, CancellationToken ct)
    {
        try
        {
            await guests.Clients.Group(GuestHub.LocationGroup(serviceLocationId))
                .SendAsync("billChanged", new BillChangedEvent(serviceLocationId, reason), ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't tell table {Location} its bill changed ({Reason}).", serviceLocationId, reason);
        }
    }
}
