using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;
using DineFlow.Application.Transactions;
using DineFlow.Domain.Audit;
using DineFlow.Domain.Common;
using DineFlow.Domain.Pos;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

namespace DineFlow.Application.Pos;

/// <summary>
/// Settings → POS (blueprint E3 "connection config, health", P1-MER-02 "connect the POS, map
/// tables"): choose the connector, store its credentials encrypted, check it's reachable, map
/// tables and menu items to the POS's own, and follow what's been sent, what's waiting and what
/// the POS refused, with a retry once the cause is fixed. Changes are audit-logged.
/// </summary>
public class PosService(IAppDbContext db, ITenantContext tenant, IPosConnectorRegistry connectors, ISecretProtector secrets, TimeProvider clock)
{
    public async Task<PosSettingsDto> GetAsync(Guid venueId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var connection = await db.PosConnections.AsNoTracking().SingleOrDefaultAsync(c => c.VenueId == venue.Id, ct);
        var tables = await db.ServiceLocations.AsNoTracking().Include(l => l.DiningArea)
            .Where(l => l.VenueId == venue.Id && l.IsActive).ToListAsync(ct);
        var products = await db.MenuItems.AsNoTracking().Where(i => i.VenueId == venue.Id)
            .Join(db.MenuCategories.AsNoTracking(), i => i.CategoryId, c => c.Id, (i, c) => new { i.Id, i.Name, Category = c.Name, i.PosProductRef })
            .OrderBy(i => i.Category).ThenBy(i => i.Name).ToListAsync(ct);

        return new PosSettingsDto(
            connection is null ? null : ToDto(connection),
            connectors.All.Select(c => new PosProviderDto(c.Provider, c.DisplayName, PosCapabilityNames.Of(c.Capabilities), c.RequiresAccessToken)).ToList(),
            tables.OrderBy(t => t.DiningArea?.SortOrder ?? int.MaxValue).ThenBy(t => t.Type)
                .ThenBy(t => int.TryParse(t.Label, out var n) ? n : int.MaxValue).ThenBy(t => t.Label)
                .Select(t => new PosTableMappingDto(t.Id, t.DisplayName, t.DiningArea?.Name, t.ExternalRef)).ToList(),
            products.Select(p => new PosProductMappingDto(p.Id, p.Name, p.Category, p.PosProductRef)).ToList(),
            await WaitingAsync(venue.Id, ct));
    }

    public async Task<PosSettingsDto> SaveConnectionAsync(Guid venueId, PosConnectionRequest request, Guid staffUserId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var connector = connectors.Find(request.Provider)
            ?? throw AppException.BadRequest("unknown_pos", "DineFlow doesn't have a connector for that POS yet.");
        var now = clock.GetUtcNow();

        var connection = await db.PosConnections.SingleOrDefaultAsync(c => c.VenueId == venue.Id, ct);
        var keepsToken = connection?.Provider == connector.Provider && connection.ProtectedAccessToken is not null;
        var willHaveToken = request.AccessToken is { } given ? given.Trim().Length > 0 : keepsToken;
        if (request.IsActive && connector.RequiresAccessToken && !willHaveToken)
            throw AppException.BadRequest("token_required", $"Add the access token from {connector.DisplayName} to connect.");

        if (connection is null)
        {
            connection = new PosConnection(venue.TenantId, venue.Id, connector.Provider, now);
            db.PosConnections.Add(connection);
        }
        try { connection.Configure(connector.Provider, request.BranchRef, request.IsActive, request.AutoClose, now); }
        catch (DomainException ex) { throw AppException.BadRequest("invalid_pos", ex.Message); }

        if (request.AccessToken is { } token)
            connection.SetAccessToken(token.Trim().Length == 0 ? null : secrets.Protect(token.Trim()), now);

        Audit(staffUserId, "pos.connection.saved", nameof(PosConnection), connection.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(venueId, ct);
    }

    /// <summary>Asks the POS whether it's reachable with these settings, and records the answer.</summary>
    public async Task<PosSettingsDto> CheckAsync(Guid venueId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        var connection = await db.PosConnections.SingleOrDefaultAsync(c => c.VenueId == venue.Id, ct)
            ?? throw AppException.Conflict("pos_not_connected", "Connect a POS first.");
        var connector = connectors.Find(connection.Provider)
            ?? throw AppException.Conflict("unknown_pos", "DineFlow no longer has a connector for this POS.");

        PosHealth health;
        try
        {
            health = await connector.HealthCheckAsync(new PosContext(connection.VenueId, connection.BranchRef,
                connection.ProtectedAccessToken is { } t ? secrets.Unprotect(t) : null), ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            health = new PosHealth(false, ex.Message);
        }
        connection.RecordHealth(health.Healthy, health.Message, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return await GetAsync(venueId, ct);
    }

    /// <summary>Maps tables and menu items to the POS's own ids; an empty value unmaps.</summary>
    public async Task<PosSettingsDto> MapAsync(Guid venueId, PosMappingRequest request, Guid staffUserId, CancellationToken ct)
    {
        var venue = await VenueAsync(venueId, ct);
        try
        {
            if (request.Tables is { Count: > 0 } tables)
            {
                var ids = tables.Select(t => t.LocationId).ToList();
                var locations = await db.ServiceLocations.Where(l => l.VenueId == venue.Id && ids.Contains(l.Id)).ToDictionaryAsync(l => l.Id, ct);
                foreach (var t in tables)
                    if (locations.GetValueOrDefault(t.LocationId) is { } location) location.SetExternalRef(t.ExternalRef);
            }
            if (request.Products is { Count: > 0 } products)
            {
                var ids = products.Select(p => p.ItemId).ToList();
                var items = await db.MenuItems.Where(i => i.VenueId == venue.Id && ids.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
                foreach (var p in products)
                    if (items.GetValueOrDefault(p.ItemId) is { } item) item.SetPosProductRef(p.PosProductRef);
            }
        }
        catch (DomainException ex)
        {
            throw AppException.BadRequest("invalid_mapping", ex.Message);
        }

        Audit(staffUserId, "pos.mapping.saved", nameof(Venue), venue.Id);
        await db.SaveChangesAsync(ct);
        return await GetAsync(venueId, ct);
    }

    /// <summary>What has been, is being, or couldn't be sent to the POS; newest first.</summary>
    public async Task<PosCommandPageDto> CommandsAsync(PosCommandQuery query, CancellationToken ct)
    {
        var venue = await VenueAsync(query.VenueId, ct);
        var commands = db.PosCommands.AsNoTracking().Where(c => c.VenueId == venue.Id);
        if (query.Status is { } status) commands = commands.Where(c => c.Status == status);

        var total = await commands.CountAsync(ct);
        var page = await commands.OrderByDescending(c => c.CreatedAt).Skip((query.Page - 1) * query.PageSize).Take(query.PageSize).ToListAsync(ct);
        return new PosCommandPageDto(await DescribeAsync(page, venue.Currency, ct), total, query.Page, query.PageSize, await WaitingAsync(venue.Id, ct));
    }

    /// <summary>Sends a refused or waiting command again now, e.g. after mapping the table.</summary>
    public async Task<PosCommandDto> RetryAsync(Guid commandId, Guid staffUserId, CancellationToken ct)
    {
        var command = await db.PosCommands.SingleOrDefaultAsync(c => c.Id == commandId, ct)
            ?? throw AppException.NotFound("pos_command_not_found", "Not found.");
        try { command.Retry(clock.GetUtcNow()); }
        catch (DomainException ex) { throw AppException.Conflict("already_done", ex.Message); }

        Audit(staffUserId, "pos.command.retried", nameof(PosCommand), command.Id);
        await db.SaveChangesAsync(ct);
        var currency = await db.Venues.Where(v => v.Id == command.VenueId).Select(v => v.Currency).SingleAsync(ct);
        return (await DescribeAsync([command], currency, ct)).Single();
    }

    /// <summary>Waiting longer than <see cref="PosCommand.AttentionAfter"/>, or refused: for the overview and the settings badge.</summary>
    public async Task<int> WaitingAsync(Guid venueId, CancellationToken ct)
    {
        var since = clock.GetUtcNow() - PosCommand.AttentionAfter;
        return await db.PosCommands.AsNoTracking().CountAsync(c => c.VenueId == venueId
            && (c.Status == PosCommandStatus.Failed || (c.Status == PosCommandStatus.Pending && c.CreatedAt <= since)), ct);
    }

    private async Task<IReadOnlyList<PosCommandDto>> DescribeAsync(IReadOnlyList<PosCommand> commands, string currency, CancellationToken ct)
    {
        var orderIds = commands.Where(c => c.OrderId != null).Select(c => c.OrderId!.Value).ToList();
        var paymentIds = commands.Where(c => c.PaymentId != null).Select(c => c.PaymentId!.Value).ToList();
        var checkIds = commands.Select(c => c.CheckId).Distinct().ToList();
        var orders = await db.Orders.AsNoTracking().Where(o => orderIds.Contains(o.Id)).ToDictionaryAsync(o => o.Id, o => o.Number, ct);
        var payments = await db.Payments.AsNoTracking().Where(p => paymentIds.Contains(p.Id))
            .ToDictionaryAsync(p => p.Id, p => new { p.Reference, p.BillAmountMinor, p.TipMinor }, ct);
        var places = await db.Checks.AsNoTracking().Where(c => checkIds.Contains(c.Id))
            .Join(db.ServiceLocations.AsNoTracking(), c => c.ServiceLocationId, l => l.Id, (c, l) => new { c.Id, l.Type, l.Label })
            .ToDictionaryAsync(x => x.Id, x => (x.Type, x.Label), ct);
        var now = clock.GetUtcNow();

        return commands.Select(c =>
        {
            var place = places.TryGetValue(c.CheckId, out var p) ? $"{(p.Type == ServiceLocationType.Table ? "Table" : p.Type.ToString())} {p.Label}" : "";
            var description = c.Kind switch
            {
                PosCommandKind.SendOrder => $"Order #{orders.GetValueOrDefault(c.OrderId ?? Guid.Empty)} · {place}",
                PosCommandKind.PostPayment when payments.TryGetValue(c.PaymentId ?? Guid.Empty, out var pay) =>
                    $"Payment {pay.Reference} · {TransactionService.FormatMoney(pay.BillAmountMinor, currency)}{(pay.TipMinor > 0 ? $" + tip {TransactionService.FormatMoney(pay.TipMinor, currency)}" : "")} · {place}",
                PosCommandKind.PostPayment => $"Payment · {place}",
                _ => $"Close the check · {place}"
            };
            return new PosCommandDto(c.Id, c.Kind, description, c.Status, c.NeedsAttention(now), c.Attempts, c.CreatedAt,
                c.Status == PosCommandStatus.Pending ? c.NextAttemptAt : null, c.CompletedAt, c.LastError, c.ResultRef, c.PaymentId);
        }).ToList();
    }

    private static PosConnectionDto ToDto(PosConnection c) =>
        new(c.Provider, c.BranchRef, c.ProtectedAccessToken is not null, c.IsActive, c.AutoClose, c.LastCheckedAt, c.LastCheckHealthy, c.LastCheckMessage);

    private async Task<Venue> VenueAsync(Guid venueId, CancellationToken ct) =>
        await db.Venues.SingleOrDefaultAsync(v => v.Id == venueId, ct) ?? throw AppException.NotFound("venue_not_found", "Venue not found.");

    private void Audit(Guid staffUserId, string action, string objectType, Guid objectId) =>
        db.AuditEntries.Add(new AuditEntry(tenant.RequireTenantId(), staffUserId, action, objectType, objectId, clock.GetUtcNow()));
}
