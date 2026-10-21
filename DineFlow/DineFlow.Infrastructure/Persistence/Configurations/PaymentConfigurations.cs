using DineFlow.Domain.Audit;
using DineFlow.Domain.Bills;
using DineFlow.Domain.Reviews;
using DineFlow.Domain.Orders;
using DineFlow.Domain.Payments;
using DineFlow.Domain.Promotions;
using DineFlow.Domain.Sessions;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class CheckConfiguration : IEntityTypeConfiguration<Check>
{
    public void Configure(EntityTypeBuilder<Check> builder)
    {
        builder.ToTable("Checks");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        // Saved only if nobody else changed the check since it was read (see Check.Revision).
        builder.Property(c => c.Revision).IsConcurrencyToken();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(c => c.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ServiceLocation>().WithMany().HasForeignKey(c => c.ServiceLocationId).OnDelete(DeleteBehavior.Restrict);

        // At most one open bill per table, even if two guests order at the same moment.
        builder.HasIndex(c => c.ServiceLocationId).IsUnique().HasFilter("[Status] = 'Open'");
    }
}

public class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("Payments");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Reference).HasMaxLength(16).IsRequired();
        builder.Property(p => p.Provider).HasMaxLength(30).IsRequired();
        builder.Property(p => p.ProviderPaymentId).HasMaxLength(100);
        builder.Property(p => p.CheckoutUrl).HasMaxLength(2000);
        builder.Property(p => p.Method).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Mode).HasConversion<string>().HasMaxLength(10);
        builder.Property(p => p.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
        builder.Property(p => p.PromoCode).HasMaxLength(20);
        builder.Property(p => p.TermsVersion).HasMaxLength(20).IsRequired();
        builder.Property(p => p.CardBrand).HasMaxLength(20);
        builder.Property(p => p.CardLast4).HasMaxLength(4).IsFixedLength();
        builder.Property(p => p.FailureReason).HasMaxLength(200);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(p => p.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Check>().WithMany().HasForeignKey(p => p.CheckId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GuestSession>().WithMany().HasForeignKey(p => p.GuestSessionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<PromoCode>().WithMany().HasForeignKey(p => p.PromoCodeId).OnDelete(DeleteBehavior.Restrict);

        // The reference is for people (receipts, staff look-ups), not a key: 8 hex digits can repeat across the platform.
        builder.HasIndex(p => p.Reference);
        builder.HasIndex(p => new { p.GuestSessionId, p.ClientRequestId }).IsUnique();
        builder.HasIndex(p => new { p.Provider, p.ProviderPaymentId }).IsUnique().HasFilter("[ProviderPaymentId] IS NOT NULL");
        builder.HasIndex(p => new { p.CheckId, p.Status });
    }
}

public class ItemClaimConfiguration : IEntityTypeConfiguration<ItemClaim>
{
    public void Configure(EntityTypeBuilder<ItemClaim> builder)
    {
        builder.ToTable("ItemClaims");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(10);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Check>().WithMany().HasForeignKey(c => c.CheckId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<OrderLine>().WithMany().HasForeignKey(c => c.OrderLineId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(c => c.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<GuestSession>().WithMany().HasForeignKey(c => c.GuestSessionId).OnDelete(DeleteBehavior.Restrict);

        // One claim per item unit: two guests can never hold or pay for the same one.
        builder.HasIndex(c => new { c.OrderLineId, c.UnitIndex }).IsUnique();
        builder.HasIndex(c => c.CheckId);
        builder.HasIndex(c => c.PaymentId);
    }
}

public class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("Reviews");
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Comment).HasMaxLength(Review.MaxCommentLength);
        builder.Property(r => r.ContactName).HasMaxLength(100);
        builder.Property(r => r.ContactEmail).HasMaxLength(254);
        builder.Property(r => r.ContactPhone).HasMaxLength(30);
        builder.Property(r => r.PublicPlatform).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(r => r.IsPositive);
        builder.Ignore(r => r.HasContact);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(r => r.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(r => r.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(r => r.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Check>().WithMany().HasForeignKey(r => r.CheckId).OnDelete(DeleteBehavior.Restrict);

        // One rating per paid visit.
        builder.HasIndex(r => r.PaymentId).IsUnique();
        builder.HasIndex(r => new { r.VenueId, r.CreatedAt });
        builder.HasIndex(r => new { r.VenueId, r.StaffUserId });
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(60).IsRequired();
        builder.Property(a => a.ObjectType).HasMaxLength(60).IsRequired();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(a => a.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(a => new { a.TenantId, a.CreatedAt });
        builder.HasIndex(a => new { a.ObjectType, a.ObjectId });
    }
}

public class LedgerEntryConfiguration : IEntityTypeConfiguration<LedgerEntry>
{
    public void Configure(EntityTypeBuilder<LedgerEntry> builder)
    {
        builder.ToTable("LedgerEntries");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Type).HasConversion<string>().HasMaxLength(20);
        builder.Property(e => e.Currency).HasMaxLength(3).IsFixedLength().IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(e => e.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Check>().WithMany().HasForeignKey(e => e.CheckId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Payment>().WithMany().HasForeignKey(e => e.PaymentId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(e => e.CheckId);
        builder.HasIndex(e => new { e.VenueId, e.CreatedAt });
    }
}

public class PromoCodeConfiguration : IEntityTypeConfiguration<PromoCode>
{
    public void Configure(EntityTypeBuilder<PromoCode> builder)
    {
        builder.ToTable("PromoCodes");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Code).HasMaxLength(20).IsRequired();
        builder.Property(p => p.Headline).HasMaxLength(120).IsRequired();
        builder.Property(p => p.Type).HasConversion<string>().HasMaxLength(10);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(p => p.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(p => new { p.TenantId, p.Code }).IsUnique();
    }
}

public class ProcessedWebhookConfiguration : IEntityTypeConfiguration<ProcessedWebhook>
{
    public void Configure(EntityTypeBuilder<ProcessedWebhook> builder)
    {
        builder.ToTable("ProcessedWebhooks");
        builder.HasKey(w => w.Id);
        builder.Property(w => w.Provider).HasMaxLength(30).IsRequired();
        builder.Property(w => w.EventId).HasMaxLength(100).IsRequired();
        builder.Property(w => w.ProviderPaymentId).HasMaxLength(100).IsRequired();
        builder.Property(w => w.Status).HasMaxLength(20).IsRequired();
        builder.HasIndex(w => new { w.Provider, w.EventId }).IsUnique();
    }
}
