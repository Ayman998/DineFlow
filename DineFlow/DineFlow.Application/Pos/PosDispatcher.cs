using DineFlow.Application.Abstractions;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Pos;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Pos;

/// <summary>
/// Carries out queued POS commands (blueprint E9, 7.1): orders first, then payments, then closing
/// the check, one bill at a time and in the order they happened. A payment is posted exactly once:
/// after any attempt that may have reached the POS, the next one asks the POS whether the tender
/// is already there before posting again (blueprint 13.1 "POS applies tender but response times
/// out"). When the POS is down, commands wait (POSTING_PENDING) and are retried until it's back;
/// the guest has already paid and is never charged again.
/// </summary>
public class PosDispatcher(IAppDbContext db, IPosConnectorRegistry connectors, ISecretProtector secrets, TimeProvider clock)
{
    public const int BatchSize = 20;

    /// <summary>What happened at the POS; recorded after the call, and again if saving it ran into a guest changing the bill.</summary>
    private sealed record Outcome(PosCommandStatus Status, string? Ref = null, string? CheckRef = null, bool CloseCheck = false,
        string? Error = null, bool MayHaveApplied = false);

    /// <summary>Runs up to <see cref="BatchSize"/> due commands; returns how many reached the POS.</summary>
    public async Task<int> RunDueAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        // Background work without a tenant: the tenant filter is bypassed and every lookup is by id.
        var due = await db.PosCommands.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.Status == PosCommandStatus.Pending && c.NextAttemptAt <= now)
            .OrderBy(c => c.CreatedAt).Take(BatchSize).Select(c => new { c.Id, c.CreatedAt, c.Kind })
            .ToListAsync(ct);

        var done = 0;
        foreach (var id in due.OrderBy(c => c.CreatedAt).ThenBy(c => c.Kind).Select(c => c.Id))
            if (await RunAsync(id, ct)) done++;
        return done;
    }

    private async Task<bool> RunAsync(Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();
        var command = await db.PosCommands.IgnoreQueryFilters().SingleAsync(c => c.Id == id, ct);
        var now = clock.GetUtcNow();
        if (!command.IsDue(now)) return false;

        // Earlier work on the same bill goes first: a payment can't be posted before its order is there.
        // At the same instant, orders come before payments and payments before closing (compared here,
        // not in SQL, because the kind is stored as text).
        var unfinished = await db.PosCommands.IgnoreQueryFilters().AsNoTracking()
            .Where(c => c.CheckId == command.CheckId && c.Id != command.Id && c.Status != PosCommandStatus.Done && c.CreatedAt <= command.CreatedAt)
            .Select(c => new { c.CreatedAt, c.Kind }).ToListAsync(ct);
        if (unfinished.Any(c => c.CreatedAt < command.CreatedAt || c.Kind < command.Kind)) return false;

        command.StartAttempt(now);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { return false; } // another worker claimed it

        var outcome = await ExecuteAsync(command, ct);
        return await RecordAsync(id, outcome, ct);
    }

    private async Task<Outcome> ExecuteAsync(PosCommand command, CancellationToken ct)
    {
        var connection = await db.PosConnections.IgnoreQueryFilters().AsNoTracking().SingleOrDefaultAsync(c => c.VenueId == command.VenueId, ct);
        var connector = connection is null ? null : connectors.Find(connection.Provider);
        if (connection is not { IsActive: true } || connector is null)
            return new(PosCommandStatus.Pending, Error: "The POS connection is paused or removed. It will be sent once it's active again.");

        var context = new PosContext(connection.VenueId, connection.BranchRef,
            connection.ProtectedAccessToken is { } token ? secrets.Unprotect(token) : null);
        var check = await db.Checks.IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == command.CheckId, ct);
        try
        {
            return command.Kind switch
            {
                PosCommandKind.SendOrder => await SendOrderAsync(command, check, connector, context, ct),
                PosCommandKind.PostPayment => await PostPaymentAsync(command, check, connection.AutoClose, connector, context, ct),
                _ => await CloseCheckAsync(check, connector, context, ct)
            };
        }
        catch (PosRejectedException ex)
        {
            return new(PosCommandStatus.Failed, Error: ex.Message);
        }
        catch (PosUnavailableException ex)
        {
            return new(PosCommandStatus.Pending, Error: ex.Message, MayHaveApplied: ex.MayHaveApplied);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Unknown failure: it may or may not have reached the POS, so check before trying again.
            return new(PosCommandStatus.Pending, Error: ex.Message, MayHaveApplied: true);
        }
    }

    private async Task<Outcome> SendOrderAsync(PosCommand command, Check check, IPosConnector connector, PosContext context, CancellationToken ct)
    {
        var order = await db.Orders.IgnoreQueryFilters().AsNoTracking().Include(o => o.Lines).SingleAsync(o => o.Id == command.OrderId, ct);
        var table = await TableRefAsync(order.ServiceLocationId, ct);

        var itemIds = order.Lines.Select(l => l.MenuItemId).Distinct().ToList();
        var products = await db.MenuItems.IgnoreQueryFilters().AsNoTracking().Where(i => itemIds.Contains(i.Id))
            .ToDictionaryAsync(i => i.Id, i => i.PosProductRef, ct);
        var lines = order.Lines.OrderBy(l => l.Position).Select(l => new PosOrderLine(l.Name, products.GetValueOrDefault(l.MenuItemId),
            l.Quantity, l.UnitPriceMinor, l.LineTotalMinor, l.Modifiers.Select(m => m.Name).ToList())).ToList();

        // Connectors use the order id to recognise a retried send, so it can't be added twice.
        var result = await connector.CreateOrderAsync(context, new PosOrder(check.PosCheckRef, table, order.Id.ToString(), order.Number,
            order.Currency, lines, order.TotalMinor, order.Note), ct);
        return new(PosCommandStatus.Done, result.OrderRef, result.CheckRef);
    }

    private async Task<Outcome> PostPaymentAsync(PosCommand command, Check check, bool autoClose, IPosConnector connector, PosContext context, CancellationToken ct)
    {
        var payment = await db.Payments.IgnoreQueryFilters().AsNoTracking().SingleAsync(p => p.Id == command.PaymentId, ct);
        var checkRef = check.PosCheckRef ?? await FindOpenCheckAsync(check, connector, context, ct);
        var close = check.Status == CheckStatus.Closed && autoClose && connector.Capabilities.HasFlag(PosCapabilities.AutoClose);

        // A previous attempt may have reached the POS: if the tender is already there, don't post it twice.
        if (command.Uncertain)
        {
            var existing = (await connector.GetCheckDetailsAsync(context, checkRef, ct)).Payments
                .FirstOrDefault(p => p.ExternalPaymentId == payment.Id.ToString());
            if (existing is not null) return new(PosCommandStatus.Done, existing.PostingRef, checkRef, close);
        }

        var tip = connector.Capabilities.HasFlag(PosCapabilities.TipPosting) ? payment.TipMinor : 0;
        var reference = payment.PromoCode is { } promo ? $"{payment.Reference} (promotion {promo})" : payment.Reference;
        var posted = await connector.PostPaymentAsync(context, new PosPayment(checkRef, payment.Id.ToString(), payment.BillAmountMinor, tip,
            payment.Currency, payment.CardBrand ?? payment.Method.ToString(), reference), ct);
        return new(PosCommandStatus.Done, posted.PostingRef, checkRef, close);
    }

    private static async Task<Outcome> CloseCheckAsync(Check check, IPosConnector connector, PosContext context, CancellationToken ct)
    {
        if (check.PosCheckRef is not { } checkRef) throw new PosRejectedException("This bill was never linked to a POS check.");
        await connector.CloseCheckAsync(context, checkRef, ct);
        return new(PosCommandStatus.Done, checkRef);
    }

    /// <summary>
    /// Saves what happened. The bill is checked for concurrent changes on save, so a guest paying at
    /// that moment can make it fail; then it's read again and applied again, because the POS already
    /// has it and that must not be lost.
    /// </summary>
    private async Task<bool> RecordAsync(Guid id, Outcome outcome, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            db.ChangeTracker.Clear();
            var command = await db.PosCommands.IgnoreQueryFilters().SingleAsync(c => c.Id == id, ct);
            var now = clock.GetUtcNow();
            switch (outcome.Status)
            {
                case PosCommandStatus.Done:
                    command.Succeeded(outcome.Ref, now);
                    if (outcome.CheckRef is not null)
                    {
                        var check = await db.Checks.IgnoreQueryFilters().SingleAsync(c => c.Id == command.CheckId, ct);
                        if (check.PosCheckRef is null) check.LinkPosCheck(outcome.CheckRef);
                    }
                    if (outcome.CloseCheck && !await db.PosCommands.IgnoreQueryFilters()
                            .AnyAsync(c => c.CheckId == command.CheckId && c.Kind == PosCommandKind.CloseCheck, ct))
                        // Created after this payment, so it runs once everything before it on the bill is done.
                        db.PosCommands.Add(PosCommand.CloseCheck(command.TenantId, command.VenueId, command.CheckId, command.Provider, now));
                    break;
                case PosCommandStatus.Failed:
                    command.Rejected(outcome.Error!, now);
                    break;
                default:
                    command.Unavailable(outcome.Error!, outcome.MayHaveApplied, now);
                    break;
            }

            try
            {
                await db.SaveChangesAsync(ct);
                return outcome.Status == PosCommandStatus.Done;
            }
            catch (DbUpdateException) when (attempt < 3)
            {
                // A guest changed the bill at the same moment; read it again and record the result on top.
            }
        }
    }

    /// <summary>No order was sent from DineFlow (e.g. the POS doesn't take orders): use the table's one open check in the POS.</summary>
    private async Task<string> FindOpenCheckAsync(Check check, IPosConnector connector, PosContext context, CancellationToken ct)
    {
        var table = await TableRefAsync(check.ServiceLocationId, ct);
        var open = (await connector.GetOpenChecksAsync(context, table, ct)).Where(c => c.IsOpen).ToList();
        return open.Count switch
        {
            1 => open[0].CheckRef,
            0 => throw new PosRejectedException("The POS has no open check for this table to post the payment to."),
            _ => throw new PosRejectedException("The POS has more than one open check for this table, so DineFlow can't tell which one this payment is for.")
        };
    }

    private async Task<string> TableRefAsync(Guid locationId, CancellationToken ct)
    {
        var location = await db.ServiceLocations.IgnoreQueryFilters().AsNoTracking().SingleAsync(l => l.Id == locationId, ct);
        return location.ExternalRef ?? throw new PosRejectedException(
            $"{location.DisplayName} isn't mapped to a POS table. Map it under Settings → POS, then retry.");
    }
}
