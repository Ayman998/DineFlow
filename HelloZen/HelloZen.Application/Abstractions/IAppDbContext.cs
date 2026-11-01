using HelloZen.Domain.Audit;
using HelloZen.Domain.Reviews;
using HelloZen.Domain.Bills;
using HelloZen.Domain.Finance;
using HelloZen.Domain.Menus;
using HelloZen.Domain.Notifications;
using HelloZen.Domain.Orders;
using HelloZen.Domain.Payments;
using HelloZen.Domain.Pos;
using HelloZen.Domain.Promotions;
using HelloZen.Domain.Sessions;
using HelloZen.Domain.Staff;
using HelloZen.Domain.Tenants;
using HelloZen.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace HelloZen.Application.Abstractions;

/// <summary>
/// The database as the use cases see it. Every tenant-owned set is already filtered to the
/// current tenant; use IgnoreQueryFilters() only for public lookups such as QR resolution.
/// </summary>
public interface IAppDbContext
{
    DbSet<Tenant> Tenants { get; }
    DbSet<Venue> Venues { get; }
    DbSet<DiningArea> DiningAreas { get; }
    DbSet<ServiceLocation> ServiceLocations { get; }
    DbSet<QrToken> QrTokens { get; }
    DbSet<GuestSession> GuestSessions { get; }
    DbSet<StaffUser> StaffUsers { get; }
    DbSet<Menu> Menus { get; }
    DbSet<MenuCategory> MenuCategories { get; }
    DbSet<MenuItem> MenuItems { get; }
    DbSet<MenuItemLink> MenuItemLinks { get; }
    DbSet<Order> Orders { get; }
    DbSet<Check> Checks { get; }
    DbSet<ItemClaim> ItemClaims { get; }
    DbSet<Review> Reviews { get; }
    DbSet<AuditEntry> AuditEntries { get; }
    DbSet<Payment> Payments { get; }
    DbSet<LedgerEntry> LedgerEntries { get; }
    DbSet<Refund> Refunds { get; }
    DbSet<PaymentLink> PaymentLinks { get; }
    DbSet<AlertContact> AlertContacts { get; }
    DbSet<NotificationDelivery> NotificationDeliveries { get; }
    DbSet<ReportSchedule> ReportSchedules { get; }
    DbSet<PosConnection> PosConnections { get; }
    DbSet<PosCommand> PosCommands { get; }
    DbSet<Settlement> Settlements { get; }
    DbSet<SettlementLine> SettlementLines { get; }
    DbSet<ReconciliationRun> ReconciliationRuns { get; }
    DbSet<Discrepancy> Discrepancies { get; }
    DbSet<PromoCode> PromoCodes { get; }
    DbSet<ProcessedWebhook> ProcessedWebhooks { get; }

    /// <summary>Used to discard a failed save before retrying it.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
