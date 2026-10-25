using DineFlow.Domain.Common;

namespace DineFlow.Domain.Menus;

/// <summary>
/// A dish or drink (blueprint 3.3): name, description, price, image, allergens, tags,
/// availability and its modifier groups. Prices are integer minor units in the venue currency.
/// </summary>
public class MenuItem : Entity, ITenantOwned
{
    public const int MaxTags = 8;
    public const int MaxTagLength = 24;
    public const int MaxModifierGroups = 10;

    private readonly List<ModifierGroup> _modifierGroups = [];

    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    public Guid CategoryId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public string? Description { get; private set; }
    public string? DescriptionAr { get; private set; }
    public long PriceMinor { get; private set; }
    public string? ImageUrl { get; private set; }
    /// <summary>False means sold out: still shown, but it can't be ordered (P1-MENU-03).</summary>
    public bool IsAvailable { get; private set; } = true;
    public Allergen Allergens { get; private set; }
    /// <summary>Merchant-defined labels such as "Vegan" or "Spicy" (P1-MENU-07).</summary>
    public List<string> Tags { get; private set; } = [];
    public int SortOrder { get; private set; }
    /// <summary>The matching product in the POS, so orders sent there use the POS's own item (blueprint E3 mapping).</summary>
    public string? PosProductRef { get; private set; }

    public void SetPosProductRef(string? productRef) => PosProductRef = Text.Optional(productRef, 100, "POS product");

    public IReadOnlyList<ModifierGroup> ModifierGroups => _modifierGroups;

    private MenuItem() { }

    public MenuItem(Guid tenantId, Guid venueId, Guid categoryId, int sortOrder)
    {
        TenantId = tenantId;
        VenueId = venueId;
        CategoryId = categoryId;
        SortOrder = sortOrder;
    }

    public void Update(
        Guid categoryId, string name, string? nameAr, string? description, string? descriptionAr,
        long priceMinor, string? imageUrl, Allergen allergens, IEnumerable<string> tags)
    {
        if (priceMinor < 0) throw new DomainException("Price can't be negative.");

        CategoryId = categoryId;
        Name = Text.Required(name, 120, "Item name");
        NameAr = Text.Optional(nameAr, 120, "Arabic name");
        Description = Text.Optional(description, 500, "Description");
        DescriptionAr = Text.Optional(descriptionAr, 500, "Arabic description");
        PriceMinor = priceMinor;
        ImageUrl = Text.Optional(imageUrl, 500, "Image URL");
        Allergens = allergens;
        Tags = NormaliseTags(tags);
    }

    public void SetAvailable(bool available) => IsAvailable = available;

    public void SetSortOrder(int sortOrder) => SortOrder = sortOrder;

    /// <summary>
    /// Makes the item's modifier groups match <paramref name="specs"/>, in that order. Groups and
    /// options with an id are updated in place, ones without are added, and missing ones are removed.
    /// </summary>
    public void SetModifierGroups(IReadOnlyList<ModifierGroupSpec> specs)
    {
        if (specs.Count > MaxModifierGroups)
            throw new DomainException($"An item can have at most {MaxModifierGroups} modifier groups.");

        var result = new List<ModifierGroup>(specs.Count);
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var group = spec.Id is { } id
                ? _modifierGroups.FirstOrDefault(g => g.Id == id)
                    ?? throw new DomainException("A modifier group no longer exists. Reload the item and try again.")
                : new ModifierGroup(TenantId, Id);

            group.Update(spec.Name, spec.NameAr, spec.MinSelect, spec.MaxSelect, i, spec.Options);
            result.Add(group);
        }

        _modifierGroups.Clear();
        _modifierGroups.AddRange(result);
    }

    private static List<string> NormaliseTags(IEnumerable<string> tags)
    {
        var result = tags
            .Select(t => t.Trim())
            .Where(t => t.Length > 0)
            .DistinctBy(t => t, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (result.Count > MaxTags) throw new DomainException($"Use at most {MaxTags} tags.");
        if (result.Any(t => t.Length > MaxTagLength))
            throw new DomainException($"Tags must be {MaxTagLength} characters or fewer.");
        return result;
    }
}

/// <summary>The 14 allergens food businesses commonly have to declare.</summary>
[Flags]
public enum Allergen
{
    None = 0,
    Gluten = 1 << 0,
    Crustaceans = 1 << 1,
    Eggs = 1 << 2,
    Fish = 1 << 3,
    Peanuts = 1 << 4,
    Soy = 1 << 5,
    Milk = 1 << 6,
    TreeNuts = 1 << 7,
    Celery = 1 << 8,
    Mustard = 1 << 9,
    Sesame = 1 << 10,
    Sulphites = 1 << 11,
    Lupin = 1 << 12,
    Molluscs = 1 << 13
}
