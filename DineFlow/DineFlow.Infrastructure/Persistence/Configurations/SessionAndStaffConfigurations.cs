using DineFlow.Domain.Sessions;
using DineFlow.Domain.Staff;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class GuestSessionConfiguration : IEntityTypeConfiguration<GuestSession>
{
    public void Configure(EntityTypeBuilder<GuestSession> builder)
    {
        builder.ToTable("GuestSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Locale).HasMaxLength(5).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(s => s.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServiceLocation>().WithMany().HasForeignKey(s => s.ServiceLocationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<QrToken>().WithMany().HasForeignKey(s => s.QrTokenId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => s.ServiceLocationId);
    }
}

public class StaffUserConfiguration : IEntityTypeConfiguration<StaffUser>
{
    public void Configure(EntityTypeBuilder<StaffUser> builder)
    {
        builder.ToTable("StaffUsers");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Email).HasMaxLength(256).IsRequired();
        builder.Property(u => u.DisplayName).HasMaxLength(120).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500).IsRequired();
        builder.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(u => u.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(u => u.Email).IsUnique();
    }
}
