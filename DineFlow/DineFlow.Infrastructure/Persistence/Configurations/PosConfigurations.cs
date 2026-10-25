using DineFlow.Domain.Bills;
using DineFlow.Domain.Pos;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class PosConnectionConfiguration : IEntityTypeConfiguration<PosConnection>
{
    public void Configure(EntityTypeBuilder<PosConnection> builder)
    {
        builder.ToTable("PosConnections");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Provider).HasMaxLength(30).IsRequired();
        builder.Property(c => c.BranchRef).HasMaxLength(100);
        builder.Property(c => c.ProtectedAccessToken).HasMaxLength(8000);
        builder.Property(c => c.LastCheckMessage).HasMaxLength(300);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(c => c.VenueId).OnDelete(DeleteBehavior.Restrict);
        // One POS per venue.
        builder.HasIndex(c => c.VenueId).IsUnique();
    }
}

public class PosCommandConfiguration : IEntityTypeConfiguration<PosCommand>
{
    public void Configure(EntityTypeBuilder<PosCommand> builder)
    {
        builder.ToTable("PosCommands");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.Provider).HasMaxLength(30).IsRequired();
        builder.Property(c => c.LastError).HasMaxLength(PosCommand.MaxErrorLength);
        builder.Property(c => c.ResultRef).HasMaxLength(100);
        // Saved only if no other worker counted an attempt first (see PosCommand remarks).
        builder.Property(c => c.Attempts).IsConcurrencyToken();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(c => c.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Check>().WithMany().HasForeignKey(c => c.CheckId).OnDelete(DeleteBehavior.Restrict);

        // Each order, payment and check close is sent once, however often the event is handled.
        builder.HasIndex(c => new { c.Kind, c.OrderId }).IsUnique().HasFilter("[OrderId] IS NOT NULL");
        builder.HasIndex(c => new { c.Kind, c.PaymentId }).IsUnique().HasFilter("[PaymentId] IS NOT NULL");
        builder.HasIndex(c => new { c.CheckId, c.Kind }).IsUnique().HasFilter("[Kind] = 'CloseCheck'");
        builder.HasIndex(c => new { c.Status, c.NextAttemptAt });
        builder.HasIndex(c => new { c.VenueId, c.CreatedAt });
    }
}
