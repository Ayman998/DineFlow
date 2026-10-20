using DineFlow.Domain.Common;

namespace DineFlow.Domain.Menus;

/// <summary>
/// A choice on an item, e.g. "Size" (required, pick 1) or "Extras" (optional, up to 3).
/// Required means <see cref="MinSelect"/> is at least 1.
/// </summary>
public class ModifierGroup : Entity, ITenantOwned
{
    public const int MaxOptions = 20;

    private readonly List<ModifierOption> _options = [];

    public Guid TenantId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public int MinSelect { get; private set; }
    public int MaxSelect { get; private set; } = 1;
    public int SortOrder { get; private set; }

    public IReadOnlyList<ModifierOption> Options => _options;
    public bool IsRequired => MinSelect > 0;

    private ModifierGroup() { }

    internal ModifierGroup(Guid tenantId, Guid menuItemId)
    {
        TenantId = tenantId;
        MenuItemId = menuItemId;
    }

    internal void Update(string name, string? nameAr, int minSelect, int maxSelect, int sortOrder,
        IReadOnlyList<ModifierOptionSpec> options)
    {
        Name = Text.Required(name, 80, "Modifier group name");
        NameAr = Text.Optional(nameAr, 80);

        if (options.Count == 0) throw new DomainException($"\"{Name}\" needs at least one option.");
        if (options.Count > MaxOptions) throw new DomainException($"\"{Name}\" can have at most {MaxOptions} options.");
        if (minSelect < 0 || maxSelect < 1 || minSelect > maxSelect)
            throw new DomainException($"\"{Name}\": pick limits must satisfy 0 ≤ minimum ≤ maximum, with maximum at least 1.");
        if (maxSelect > options.Count)
            throw new DomainException($"\"{Name}\": maximum picks can't be more than the number of options.");
        if (options.Count(o => o.IsDefault) > maxSelect)
            throw new DomainException($"\"{Name}\": more options are pre-selected than guests are allowed to pick.");

        MinSelect = minSelect;
        MaxSelect = maxSelect;
        SortOrder = sortOrder;
        SetOptions(options);
    }

    private void SetOptions(IReadOnlyList<ModifierOptionSpec> specs)
    {
        var result = new List<ModifierOption>(specs.Count);
        for (var i = 0; i < specs.Count; i++)
        {
            var spec = specs[i];
            var option = spec.Id is { } id
                ? _options.FirstOrDefault(o => o.Id == id)
                    ?? throw new DomainException("A modifier option no longer exists. Reload the item and try again.")
                : new ModifierOption(TenantId, Id);

            option.Update(spec.Name, spec.NameAr, spec.PriceDeltaMinor, spec.IsDefault, spec.IsAvailable, i);
            result.Add(option);
        }

        _options.Clear();
        _options.AddRange(result);
    }
}

/// <summary>One choice within a modifier group, with the amount it adds to the item price.</summary>
public class ModifierOption : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid ModifierGroupId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string? NameAr { get; private set; }
    public long PriceDeltaMinor { get; private set; }
    public bool IsDefault { get; private set; }
    public bool IsAvailable { get; private set; } = true;
    public int SortOrder { get; private set; }

    private ModifierOption() { }

    internal ModifierOption(Guid tenantId, Guid modifierGroupId)
    {
        TenantId = tenantId;
        ModifierGroupId = modifierGroupId;
    }

    internal void Update(string name, string? nameAr, long priceDeltaMinor, bool isDefault, bool isAvailable, int sortOrder)
    {
        if (priceDeltaMinor < 0) throw new DomainException("Option prices can't be negative.");

        Name = Text.Required(name, 80, "Option name");
        NameAr = Text.Optional(nameAr, 80);
        PriceDeltaMinor = priceDeltaMinor;
        IsDefault = isDefault;
        IsAvailable = isAvailable;
        SortOrder = sortOrder;
    }
}

public record ModifierGroupSpec(
    Guid? Id, string Name, string? NameAr, int MinSelect, int MaxSelect, IReadOnlyList<ModifierOptionSpec> Options);

public record ModifierOptionSpec(
    Guid? Id, string Name, string? NameAr, long PriceDeltaMinor, bool IsDefault, bool IsAvailable);
