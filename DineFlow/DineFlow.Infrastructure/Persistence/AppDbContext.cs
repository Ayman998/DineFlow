using System.Linq.Expressions;
using DineFlow.Application.Abstractions;
using DineFlow.Domain.Common;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;

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

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
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
        var current = Expression.Property(Expression.Constant(this), nameof(CurrentTenantId));
        return Expression.Lambda(Expression.Equal(tenantId, current), entity);
    }
}
