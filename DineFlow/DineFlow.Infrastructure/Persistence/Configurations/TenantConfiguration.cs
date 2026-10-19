using DineFlow.Domain.Tenants;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.LegalName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(t => t.Country).HasMaxLength(2).IsFixedLength().IsRequired();
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20);
    }
}
