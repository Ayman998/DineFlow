using DineFlow.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace DineFlow.Api.Common;

/// <summary>
/// Whether a merchant token still matches its user: switched on, with the role the token was issued
/// for. A token outlives changes made in Settings → Team, so a switched-off or demoted person must
/// sign in again. Cached briefly, so a busy dashboard doesn't cost a database lookup per request.
/// </summary>
public class StaffAccess(AppDbContext db, IMemoryCache cache)
{
    public static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(30);

    public async Task<bool> IsCurrentAsync(Guid staffUserId, string? tokenRole, CancellationToken ct)
    {
        var role = await cache.GetOrCreateAsync($"staff-role:{staffUserId}", async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheFor;
            // The tenant isn't known until the token is accepted, so this bypasses the tenant filter.
            var user = await db.StaffUsers.IgnoreQueryFilters().AsNoTracking()
                .Where(u => u.Id == staffUserId && u.IsActive).Select(u => new { u.Role }).SingleOrDefaultAsync(ct);
            return user?.Role.ToString();
        });
        return role is not null && role == tokenRole;
    }
}
