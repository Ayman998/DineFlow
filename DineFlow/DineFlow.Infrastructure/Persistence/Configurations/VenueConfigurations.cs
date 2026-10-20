using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class VenueConfiguration : IEntityTypeConfiguration<Venue>
{
    public void Configure(EntityTypeBuilder<Venue> builder)
    {
        builder.ToTable("Venues");
        builder.HasKey(v => v.Id);
        builder.Property(v => v.Name).HasMaxLength(120).IsRequired();
        builder.Property(v => v.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(v => v.TimeZone).HasMaxLength(64).IsRequired();
        builder.Property(v => v.DefaultLocale).HasMaxLength(5).IsRequired();
        builder.Property(v => v.LogoUrl).HasMaxLength(500);
        builder.Property(v => v.BrandColor).HasMaxLength(7).IsRequired();

        builder.HasOne(v => v.Tenant).WithMany().HasForeignKey(v => v.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(v => v.TenantId);
    }
}

public class DiningAreaConfiguration : IEntityTypeConfiguration<DiningArea>
{
    public void Configure(EntityTypeBuilder<DiningArea> builder)
    {
        builder.ToTable("DiningAreas");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(80).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(a => a.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.VenueId, a.Name }).IsUnique();
    }
}

public class ServiceLocationConfiguration : IEntityTypeConfiguration<ServiceLocation>
{
    public void Configure(EntityTypeBuilder<ServiceLocation> builder)
    {
        builder.ToTable("ServiceLocations");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(l => l.Label).HasMaxLength(20).IsRequired();
        builder.Property(l => l.ExternalRef).HasMaxLength(100);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.Venue).WithMany().HasForeignKey(l => l.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(l => l.DiningArea).WithMany().HasForeignKey(l => l.DiningAreaId).OnDelete(DeleteBehavior.ClientSetNull);
        builder.HasIndex(l => new { l.VenueId, l.Type, l.Label }).IsUnique();
    }
}

public class QrTokenConfiguration : IEntityTypeConfiguration<QrToken>
{
    public void Configure(EntityTypeBuilder<QrToken> builder)
    {
        builder.ToTable("QrTokens");
        builder.HasKey(q => q.Id);
        builder.Property(q => q.TokenHash).HasMaxLength(64).IsFixedLength().IsUnicode(false).IsRequired();
        builder.Property(q => q.Status).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(q => q.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne(q => q.ServiceLocation).WithMany().HasForeignKey(q => q.ServiceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(q => q.TokenHash).IsUnique();
        builder.HasIndex(q => new { q.ServiceLocationId, q.Version }).IsUnique();
    }
}
