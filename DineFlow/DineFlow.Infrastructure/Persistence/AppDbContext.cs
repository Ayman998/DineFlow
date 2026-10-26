using DineFlow.Domain.Audit;
using DineFlow.Domain.Reviews;
using System.Linq.Expressions;
using DineFlow.Application.Abstractions;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Finance;
using DineFlow.Domain.Common;
using DineFlow.Domain.Menus;
using DineFlow.Domain.Notifications;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Pos;
using DineFlow.Domain.Promotions;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace DineFlow.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options, ITenantContext tenantContext)
    : DbContext(options), IAppDbContext
{
    // Read by the query filters on every query, so the filter follows the current request's tenant.
    private Guid? CurrentTenantId => tenantContext.TenantId;

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Venue> Venues => Set<Venue>();
    public DbSet<DiningArea> DiningAreas => Set<DiningArea>();
    public DbSet<ServiceLocation> ServiceLocations => Set<ServiceLocation>();
    public DbSet<QrToken> QrTokens => Set<QrToken>();
    public DbSet<GuestSession> GuestSessions => Set<GuestSession>();
    public DbSet<StaffUser> StaffUsers => Set<StaffUser>();
    public DbSet<Menu> Menus => Set<Menu>();
    public DbSet<MenuCategory> MenuCategories => Set<MenuCategory>();
    public DbSet<MenuItem> MenuItems => Set<MenuItem>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Check> Checks => Set<Check>();
    public DbSet<ItemClaim> ItemClaims => Set<ItemClaim>();
    public DbSet<Review> Reviews => Set<Review>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<LedgerEntry> LedgerEntries => Set<LedgerEntry>();
    public DbSet<Refund> Refunds => Set<Refund>();
    public DbSet<PaymentLink> PaymentLinks => Set<PaymentLink>();
    public DbSet<AlertContact> AlertContacts => Set<AlertContact>();
    public DbSet<NotificationDelivery> NotificationDeliveries => Set<NotificationDelivery>();
    public DbSet<ReportSchedule> ReportSchedules => Set<ReportSchedule>();
    public DbSet<PosConnection> PosConnections => Set<PosConnection>();
    public DbSet<PosCommand> PosCommands => Set<PosCommand>();
    public DbSet<Settlement> Settlements => Set<Settlement>();
    public DbSet<SettlementLine> SettlementLines => Set<SettlementLine>();
    public DbSet<ReconciliationRun> ReconciliationRuns => Set<ReconciliationRun>();
    public DbSet<Discrepancy> Discrepancies => Set<Discrepancy>();
    public DbSet<PromoCode> PromoCodes => Set<PromoCode>();
    public DbSet<ProcessedWebhook> ProcessedWebhooks => Set<ProcessedWebhook>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (typeof(Entity).IsAssignableFrom(entityType.ClrType))
            {
                // Ids are created in code (Entity.Id), never by the database. Saying so lets EF
                // treat a new child added to a tracked parent's collection as an insert.
                var id = entityType.FindProperty(nameof(Entity.Id));
                if (id is not null) id.ValueGenerated = ValueGenerated.Never;
            }

            if (typeof(ITenantOwned).IsAssignableFrom(entityType.ClrType))
            {
                entityType.SetQueryFilter(BuildTenantFilter(entityType.ClrType));
            }
        }
    }

    private LambdaExpression BuildTenantFilter(Type entityClrType)
    {
        // e => e.TenantId == this.CurrentTenantId
        var entity = Expression.Parameter(entityClrType, "e");
        var tenantId = Expression.Convert(
            Expression.Property(entity, nameof(ITenantOwned.TenantId)), typeof(Guid?));
        // Typed as AppDbContext so the private property resolves on derived contexts too.
        var current = Expression.Property(Expression.Constant(this, typeof(AppDbContext)), nameof(CurrentTenantId));
        return Expression.Lambda(Expression.Equal(tenantId, current), entity);
    }
}
