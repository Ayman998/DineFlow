using DineFlow.Domain.Audit;
using DineFlow.Domain.Reviews;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Menus;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Promotions;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace DineFlow.Application.Abstractions;

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
    DbSet<PromoCode> PromoCodes { get; }
    DbSet<ProcessedWebhook> ProcessedWebhooks { get; }

    /// <summary>Used to discard a failed save before retrying it.</summary>
    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
