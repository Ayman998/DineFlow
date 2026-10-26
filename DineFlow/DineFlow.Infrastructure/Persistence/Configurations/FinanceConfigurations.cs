using DineFlow.Domain.Finance;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class SettlementConfiguration : IEntityTypeConfiguration<Settlement>
{
    public void Configure(EntityTypeBuilder<Settlement> builder)
    {
        builder.ToTable("Settlements");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Provider).HasMaxLength(30).IsRequired();
        builder.Property(s => s.BatchRef).HasMaxLength(100).IsRequired();
        builder.Property(s => s.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(s => s.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.Source).HasConversion<string>().HasMaxLength(10);
        builder.Property(s => s.FileName).HasMaxLength(200);

        builder.HasMany(s => s.Lines).WithOne().HasForeignKey(l => l.SettlementId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(s => s.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(s => s.VenueId).OnDelete(DeleteBehavior.Restrict);

        // A batch is imported once: the same report can't be counted twice.
        builder.HasIndex(s => new { s.VenueId, s.Provider, s.BatchRef }).IsUnique();
        builder.HasIndex(s => new { s.VenueId, s.PayoutDate });
    }
}

public class SettlementLineConfiguration : IEntityTypeConfiguration<SettlementLine>
{
    public void Configure(EntityTypeBuilder<SettlementLine> builder)
    {
        builder.ToTable("SettlementLines");
        builder.HasKey(l => l.Id);
        builder.Property(l => l.Kind).HasConversion<string>().HasMaxLength(12);
        builder.Property(l => l.ProviderPaymentId).HasMaxLength(100);
        builder.Property(l => l.Reference).HasMaxLength(40);
        builder.Property(l => l.ProviderRefundId).HasMaxLength(100);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(l => l.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(l => l.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(l => l.ProviderPaymentId);
        builder.HasIndex(l => l.Reference);
        builder.HasIndex(l => l.ProviderRefundId);
        builder.HasIndex(l => l.TransactionDate);
    }
}

public class ReconciliationRunConfiguration : IEntityTypeConfiguration<ReconciliationRun>
{
    public void Configure(EntityTypeBuilder<ReconciliationRun> builder)
    {
        builder.ToTable("ReconciliationRuns");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Ignore(r => r.Balanced);
        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(r => r.VenueId).OnDelete(DeleteBehavior.Restrict);
        // One result per venue and business day; running again updates it.
        builder.HasIndex(r => new { r.VenueId, r.BusinessDate }).IsUnique();
    }
}

public class DiscrepancyConfiguration : IEntityTypeConfiguration<Discrepancy>
{
    public void Configure(EntityTypeBuilder<Discrepancy> builder)
    {
        builder.ToTable("Discrepancies");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Key).HasMaxLength(80).IsRequired();
        builder.Property(d => d.Reference).HasMaxLength(100).IsRequired();
        builder.Property(d => d.Details).HasMaxLength(500).IsRequired();
        builder.Property(d => d.Note).HasMaxLength(Discrepancy.MaxNoteLength);
        builder.Ignore(d => d.DifferenceMinor);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(d => d.VenueId).OnDelete(DeleteBehavior.Restrict);
        // The same problem on the same day is one entry, however often the day is reconciled.
        builder.HasIndex(d => new { d.VenueId, d.BusinessDate, d.Key }).IsUnique();
        builder.HasIndex(d => new { d.VenueId, d.Status });
    }
}
