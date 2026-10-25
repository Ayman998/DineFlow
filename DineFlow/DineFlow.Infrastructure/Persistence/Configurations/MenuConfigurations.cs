using DineFlow.Domain.Menus;
using DineFlow.Domain.Tenants;
using DineFlow.Domain.Venues;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DineFlow.Infrastructure.Persistence.Configurations;

public class MenuConfiguration : IEntityTypeConfiguration<Menu>
{
    public void Configure(EntityTypeBuilder<Menu> builder)
    {
        builder.ToTable("Menus");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Name).HasMaxLength(80).IsRequired();
        builder.Property(m => m.NameAr).HasMaxLength(80);
        builder.Property(m => m.Days).HasConversion<int>();

        builder.HasOne<Tenant>().WithMany().HasForeignKey(m => m.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Venue>().WithMany().HasForeignKey(m => m.VenueId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(m => new { m.VenueId, m.SortOrder });
    }
}

public class MenuCategoryConfiguration : IEntityTypeConfiguration<MenuCategory>
{
    public void Configure(EntityTypeBuilder<MenuCategory> builder)
    {
        builder.ToTable("MenuCategories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.Name).HasMaxLength(80).IsRequired();
        builder.Property(c => c.NameAr).HasMaxLength(80);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(c => c.TenantId).OnDelete(DeleteBehavior.Restrict);
        // Deleting a menu deletes its categories; subcategories are removed by the service first.
        builder.HasOne<Menu>().WithMany().HasForeignKey(c => c.MenuId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<MenuCategory>().WithMany().HasForeignKey(c => c.ParentId).OnDelete(DeleteBehavior.NoAction);
        builder.HasIndex(c => new { c.MenuId, c.SortOrder });
    }
}

public class MenuItemConfiguration : IEntityTypeConfiguration<MenuItem>
{
    public void Configure(EntityTypeBuilder<MenuItem> builder)
    {
        builder.ToTable("MenuItems");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Name).HasMaxLength(120).IsRequired();
        builder.Property(i => i.PosProductRef).HasMaxLength(100);
        builder.Property(i => i.NameAr).HasMaxLength(120);
        builder.Property(i => i.Description).HasMaxLength(500);
        builder.Property(i => i.DescriptionAr).HasMaxLength(500);
        builder.Property(i => i.ImageUrl).HasMaxLength(500);
        builder.Property(i => i.Allergens).HasConversion<int>();
        builder.PrimitiveCollection(i => i.Tags).HasMaxLength(400);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(i => i.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<MenuCategory>().WithMany().HasForeignKey(i => i.CategoryId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(i => i.ModifierGroups).WithOne().HasForeignKey(g => g.MenuItemId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(i => i.ModifierGroups).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.HasIndex(i => new { i.CategoryId, i.SortOrder });
        builder.HasIndex(i => i.VenueId);
    }
}

public class ModifierGroupConfiguration : IEntityTypeConfiguration<ModifierGroup>
{
    public void Configure(EntityTypeBuilder<ModifierGroup> builder)
    {
        builder.ToTable("ModifierGroups");
        builder.HasKey(g => g.Id);
        builder.Property(g => g.Name).HasMaxLength(80).IsRequired();
        builder.Property(g => g.NameAr).HasMaxLength(80);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(g => g.TenantId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(g => g.Options).WithOne().HasForeignKey(o => o.ModifierGroupId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(g => g.Options).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

public class ModifierOptionConfiguration : IEntityTypeConfiguration<ModifierOption>
{
    public void Configure(EntityTypeBuilder<ModifierOption> builder)
    {
        builder.ToTable("ModifierOptions");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).HasMaxLength(80).IsRequired();
        builder.Property(o => o.NameAr).HasMaxLength(80);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(o => o.TenantId).OnDelete(DeleteBehavior.Restrict);
    }
}
