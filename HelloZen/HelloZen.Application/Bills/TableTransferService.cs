using HelloZen.Application.Abstractions;
using HelloZen.Application.Common;
using HelloZen.Application.Orders;
using HelloZen.Application.Pos;
using HelloZen.Domain.Audit;
using HelloZen.Domain.Bills;
using HelloZen.Domain.Common;
using HelloZen.Domain.Orders;
using HelloZen.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace HelloZen.Application.Bills;

/// <summary>
/// Staff moving what a table ordered to another table, in full or in part: the guests change
/// tables, two groups join up, or one guest takes their drinks to the bar.
/// <list type="bullet">
/// <item>The whole bill to a table with no open bill: the bill itself moves, with its payments and split.</item>
/// <item>Anything else joins the other table's bill (opening one if it has none). Whole orders move as
/// they are; part of an order is split off into a new order with the same status, and both are repriced.</item>
/// </list>
/// Money stays where it was paid: what a table has paid must still be covered by what's left on
/// its bill, items paid for by item stay with their payment, and nothing moves while a guest is
/// paying. With a POS connected the POS keeps the bills, so tables are moved there instead.
/// </summary>
public class TableTransferService(
    IAppDbContext db,
    ITenantContext tenant,
    BillService bills,
    PosSync pos,
    IOrderNotifier orderNotifier,
    IPaymentNotifier billNotifier,
    TimeProvider clock)
{
    /// <summary>The table's open bill line by line, with how much of each line can move.</summary>
    public async Task<TableBillDto> GetBillAsync(Guid locationId, CancellationToken ct)
    {
        var location = await LocationAsync(locationId, ct);
        var check = await bills.OpenCheckAsync(location.Id, tracked: false, ct);
        if (check is null) return new TableBillDto(location.Id, null, location.Venue.Currency, 0, 0, false, []);

        var state = await bills.LoadStateAsync(check, ct);
        var paidByItem = PaidByItem(state);
        var lines = state.Orders.SelectMany(o => o.Lines.OrderBy(l => l.Position).Select(l => new TableBillLineDto(
            l.Id, o.Id, o.Number, o.Status, l.Name, l.NameAr, l.Modifiers.Select(m => m.ToDto()).ToList(), l.Note,
            l.Quantity, l.UnitPriceMinor, l.LineTotalMinor, paidByItem.Contains(l.Id) ? 0 : l.Quantity))).ToList();
        return new TableBillDto(location.Id, check.Id, check.Currency, state.TotalMinor, state.PaidMinor, state.InFlight.Any(), lines);
    }

    /// <param name="staffUserId">Who moved it, for the audit log.</param>
    public async Task<TransferResultDto> TransferAsync(Guid fromLocationId, TransferRequest request, Guid staffUserId, CancellationToken ct)
    {
        if (request.ToLocationId == fromLocationId)
            throw AppException.BadRequest("same_table", "Pick a different table to move to.");
        var from = await LocationAsync(fromLocationId, ct);
        var to = await LocationAsync(request.ToLocationId, ct);
        if (to.VenueId != from.VenueId) throw AppException.BadRequest("other_branch", "Both tables must be in the same branch.");
        if (!to.TakesGuests) throw AppException.Conflict("table_closed", "That table is closed. Pick one that's open.");
        if (await pos.IsConnectedAsync(from.VenueId, ct))
            throw AppException.Conflict("pos_bills", "This branch's bills are kept on the POS, so move tables there.");

        var wanted = (request.Lines ?? []).GroupBy(l => l.OrderLineId).ToDictionary(g => g.Key, g => g.Sum(l => l.Quantity));

        var (result, changed) = await Concurrency.RetryAsync(db, async () =>
        {
            try { return await MoveOnceAsync(from, to, wanted, staffUserId, ct); }
            catch (AppException)
            {
                db.ChangeTracker.Clear(); // refused after orders were split in memory: drop the half-done move
                throw;
            }
        });

        foreach (var order in changed) await orderNotifier.OrderChangedAsync(order.ToDto(), ct);
        await billNotifier.TableBillChangedAsync(from.TenantId, from.Id, "transfer", ct);
        await billNotifier.TableBillChangedAsync(from.TenantId, to.Id, "transfer", ct);
        return result;
    }

    /// <summary>One attempt at the move, from fresh data, under the rules in the class summary.</summary>
    private async Task<(TransferResultDto Result, List<Order> Changed)> MoveOnceAsync(
        ServiceLocation from, ServiceLocation to, Dictionary<Guid, int> wanted, Guid staffUserId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var source = await bills.OpenCheckAsync(from.Id, tracked: true, ct)
            ?? throw AppException.Conflict("nothing_to_move", "This table has nothing on its bill to move.");
        var state = await bills.LoadStateAsync(source, ct, trackClaims: true);
        if (state.Orders.Count == 0) throw AppException.Conflict("nothing_to_move", "This table has nothing on its bill to move.");
        if (state.InFlight.Any())
            throw AppException.Conflict("payment_in_progress", "A guest at this table is paying right now. Try again once the payment has finished.");

        var orders = await db.Orders.Include(o => o.Lines).Where(o => o.CheckId == source.Id).OrderBy(o => o.Number).ToListAsync(ct);
        var live = orders.Where(o => o.Status != OrderStatus.Rejected).ToList();
        var lines = live.SelectMany(o => o.Lines).ToDictionary(l => l.Id);
        var moving = wanted.Count == 0 ? lines.Values.ToDictionary(l => l.Id, l => l.Quantity) : wanted;
        if (moving.Any(m => !lines.TryGetValue(m.Key, out var line) || m.Value < 1 || m.Value > line.Quantity))
            throw AppException.Conflict("items_changed", "Some of these items are no longer on this table's bill. Check the bill and try again.");
        var wholeBill = lines.Values.All(l => moving.GetValueOrDefault(l.Id) == l.Quantity);

        var destination = await bills.OpenCheckAsync(to.Id, tracked: true, ct);
        var destinationInUse = destination is not null
            && await db.Orders.AnyAsync(o => o.CheckId == destination.Id && o.Status != OrderStatus.Rejected, ct);

        List<Order> changed;
        if (wholeBill && !destinationInUse)
        {
            // The guests changed tables: their bill goes with them, payments, split and all.
            if (destination is not null)
            {
                // An empty bill there (e.g. its only order was rejected) makes way. It's saved on its
                // own first, because a table can only have one open bill at a time.
                destination.Close(now);
                await db.SaveChangesAsync(ct);
            }
            source.MoveTo(to.Id);
            foreach (var order in orders) MoveOrder(order, to, source.Id, now);
            destination = source;
            changed = live;
        }
        else
        {
            var paidByItem = PaidByItem(state);
            if (moving.Keys.Any(paidByItem.Contains))
                throw AppException.Conflict("items_paid", "Guests have paid, or are paying, for some of these items by item, so they stay on this table's bill.");

            if (destination is null)
            {
                destination = new Check(from.TenantId, from.VenueId, to.Id, source.Currency, now);
                db.Checks.Add(destination);
            }

            // Holds on orders that change no longer match their amounts: let them go, and guests pick again.
            var affected = live.Where(o => o.Lines.Any(l => moving.ContainsKey(l.Id))).ToList();
            var affectedLines = affected.SelectMany(o => o.Lines).Select(l => l.Id).ToHashSet();
            db.ItemClaims.RemoveRange(state.Claims.Where(c => c.Status == ItemClaimStatus.Held && affectedLines.Contains(c.OrderLineId)));

            var number = await NextNumberAsync(from.VenueId, ct);
            changed = [];
            foreach (var order in affected)
            {
                var take = order.Lines.Where(l => moving.ContainsKey(l.Id)).ToDictionary(l => l.Id, l => moving[l.Id]);
                if (order.Lines.All(l => take.GetValueOrDefault(l.Id) == l.Quantity))
                {
                    MoveOrder(order, to, destination.Id, now);
                    changed.Add(order);
                    continue;
                }

                Order split;
                try { split = order.SplitOff(take, number++, now); }
                catch (DomainException ex) { throw AppException.BadRequest("invalid_transfer", ex.Message); }
                MoveOrder(split, to, destination.Id, now);
                db.Orders.Add(split);
                changed.Add(order);
                changed.Add(split);
            }

            // What the table has paid has to stay covered by what's left on its bill.
            var left = live.Where(o => o.CheckId == source.Id).Sum(o => o.TotalMinor);
            if (state.PaidMinor > left)
                throw AppException.Conflict("paid_more_than_left",
                    "This table has already paid more than would be left on its bill. Leave more items on it, or move the whole bill to a free table.");
            if (left == 0 && source.SplitPeople is not null) source.SetSplitPeople(null, state.SharesTaken);
            if (left > 0 && state.PaidMinor == left) source.Close(now); // everything that's left is paid for

            source.Touch();
            destination.Touch();
        }

        db.AuditEntries.Add(new AuditEntry(from.TenantId, staffUserId, wholeBill ? "table.bill_moved" : "table.items_moved", nameof(Check), source.Id, now));
        await db.SaveChangesAsync(ct);

        var numbers = changed.Where(o => o.CheckId == destination.Id).Select(o => o.Number).Order().ToList();
        return (new TransferResultDto(from.Id, to.Id, wholeBill, moving.Values.Sum(), numbers), changed);
    }

    private static void MoveOrder(Order order, ServiceLocation to, Guid checkId, DateTimeOffset now) =>
        order.MoveTo(to.Id, to.Type.ToString(), to.Label, to.DiningArea?.Name, checkId, now);

    /// <summary>Lines with units guests have paid, or are paying, for by item: those payments belong to this bill.</summary>
    private static HashSet<Guid> PaidByItem(CheckState state) =>
        state.Claims.Where(c => c.Status != ItemClaimStatus.Held).Select(c => c.OrderLineId).ToHashSet();

    private async Task<int> NextNumberAsync(Guid venueId, CancellationToken ct) =>
        (await db.Orders.Where(o => o.VenueId == venueId).MaxAsync(o => (int?)o.Number, ct) ?? Order.FirstNumber - 1) + 1;

    private async Task<ServiceLocation> LocationAsync(Guid locationId, CancellationToken ct)
    {
        tenant.RequireTenantId();
        return await db.ServiceLocations.Include(l => l.Venue).Include(l => l.DiningArea).SingleOrDefaultAsync(l => l.Id == locationId, ct)
            ?? throw AppException.NotFound("location_not_found", "Table not found.");
    }
}
