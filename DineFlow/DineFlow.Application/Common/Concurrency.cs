using DineFlow.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Common;

/// <summary>
/// Runs a read-decide-save step again when another request changed the same rows first: the
/// check's revision moved on, or a unique index (one claim per item) refused a duplicate.
/// Each attempt starts from a clean change tracker and fresh data, so it decides again with
/// the other guest's change in view.
/// </summary>
internal static class Concurrency
{
    public const int Attempts = 3;

    public static async Task<T> RetryAsync<T>(IAppDbContext db, Func<Task<T>> attempt)
    {
        for (var i = 1; ; i++)
        {
            try
            {
                return await attempt();
            }
            catch (DbUpdateConcurrencyException) when (i >= Attempts)
            {
                db.ChangeTracker.Clear();
                throw AppException.Conflict("bill_busy", "Someone at your table just changed the bill. Please try again.");
            }
            catch (DbUpdateException) when (i < Attempts)
            {
                db.ChangeTracker.Clear();
            }
        }
    }
}
