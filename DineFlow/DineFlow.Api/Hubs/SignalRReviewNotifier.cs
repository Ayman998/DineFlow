using DineFlow.Application.Reviews;
using Microsoft.AspNetCore.SignalR;

namespace DineFlow.Api.Hubs;

/// <summary>Sends "reviewSubmitted" (a <see cref="ReviewNotice"/>) to the venue's staff screens.</summary>
public class SignalRReviewNotifier(IHubContext<MerchantHub> merchants, ILogger<SignalRReviewNotifier> logger) : IReviewNotifier
{
    public async Task ReviewSubmittedAsync(ReviewNotice notice, CancellationToken ct)
    {
        try
        {
            await merchants.Clients.Group(MerchantHub.Group(notice.TenantId)).SendAsync("reviewSubmitted", notice, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Couldn't push review {ReviewId}; the Reviews page shows it on reload.", notice.ReviewId);
        }
    }
}
