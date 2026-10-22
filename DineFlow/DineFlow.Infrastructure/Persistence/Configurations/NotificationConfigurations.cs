using DineFlow.Domain.Notifications;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class AlertContactConfiguration : IEntityTypeConfiguration<AlertContact>
{
    public void Configure(EntityTypeBuilder<AlertContact> builder)
    {
        builder.ToTable("AlertContacts");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(60).IsRequired();
        builder.Property(c => c.PhoneNumber).HasMaxLength(16).IsRequired();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(c => c.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => new { c.VenueId, c.PhoneNumber }).IsUnique();
    }
}

public class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("NotificationDeliveries");
        builder.HasKey(d => d.Id);
        builder.Property(d => d.Channel).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Kind).HasConversion<string>().HasMaxLength(20);
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(10);
        builder.Property(d => d.Recipient).HasMaxLength(254).IsRequired();
        builder.Property(d => d.DedupeKey).HasMaxLength(120).IsRequired();
        builder.Property(d => d.Subject).HasMaxLength(200);
        builder.Property(d => d.TextBody).IsRequired();
        builder.Property(d => d.TemplateParameters).HasMaxLength(2000);
        builder.Property(d => d.LastError).HasMaxLength(NotificationDelivery.MaxErrorLength);
        builder.Property(d => d.ProviderMessageId).HasMaxLength(200);
        // Saved only if no other worker counted an attempt first (see NotificationDelivery remarks).
        builder.Property(d => d.Attempts).IsConcurrencyToken();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(d => d.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(d => d.VenueId).OnDelete(DeleteBehavior.Restrict);

        // One message per event per recipient, however often the event is processed.
        builder.HasIndex(d => new { d.Channel, d.Recipient, d.DedupeKey }).IsUnique();
        // What the worker looks for: pending messages that are due.
        builder.HasIndex(d => new { d.Status, d.NextAttemptAt });
        builder.HasIndex(d => new { d.VenueId, d.CreatedAt });
    }
}

public class ReportScheduleConfiguration : IEntityTypeConfiguration<ReportSchedule>
{
    public void Configure(EntityTypeBuilder<ReportSchedule> builder)
    {
        builder.ToTable("ReportSchedules");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Frequency).HasConversion<string>().HasMaxLength(10);
        builder.PrimitiveCollection(s => s.Recipients).HasMaxLength(2600);
        // Saved only if no other worker ran this schedule first.
        builder.Property(s => s.Revision).IsConcurrencyToken();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(s => s.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(s => s.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(s => new { s.IsActive, s.NextRunAt });
        builder.HasIndex(s => s.VenueId);
    }
}
