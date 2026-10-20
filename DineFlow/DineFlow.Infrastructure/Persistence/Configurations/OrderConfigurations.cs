using DineFlow.Domain.Orders;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(o => o.LocationType).HasMaxLength(20).IsRequired();
        builder.Property(o => o.LocationLabel).HasMaxLength(20).IsRequired();
        builder.Property(o => o.AreaName).HasMaxLength(80);
        builder.Property(o => o.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(o => o.Note).HasMaxLength(200);
        builder.Property(o => o.RejectReason).HasMaxLength(200);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(o => o.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServiceLocation>().WithMany().HasForeignKey(o => o.ServiceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GuestSession>().WithMany().HasForeignKey(o => o.GuestSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(o => o.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Order numbers are unique per venue; a retried submission maps back to the same order.
        builder.HasIndex(o => new { o.VenueId, o.Number }).IsUnique();
        builder.HasIndex(o => new { o.GuestSessionId, o.ClientRequestId }).IsUnique();
        builder.HasIndex(o => new { o.VenueId, o.Status, o.CreatedAt });
    }
}

public class OrderLineConfiguration : IEntityTypeConfiguration<OrderLine>
{
    public void Configure(EntityTypeBuilder<OrderLine> builder)
    {
        builder.ToTable("OrderLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Name).HasMaxLength(120).IsRequired();
        builder.Property(l => l.NameAr).HasMaxLength(120);
        builder.Property(l => l.Note).HasMaxLength(200);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);

        // Chosen options are a snapshot that is only ever read with its line, so they live in a JSON column.
        builder.OwnsMany(l => l.Modifiers, m =>
        {
            m.ToJson();
            m.Property(x => x.GroupName).HasMaxLength(80);
            m.Property(x => x.Name).HasMaxLength(80);
        });
    }
}
