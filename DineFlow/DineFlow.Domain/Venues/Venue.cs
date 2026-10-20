using DineFlow.Domain.Common;
using DineFlow.Domain.Tenants;

namespace DineFlow.Domain.Venues;

/// <summary>A branch of a tenant's brand, with its own currency, timezone, branding and features.</summary>
public class Venue : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Tenant Tenant { get; private set; } = null!;

    public string Name { get; private set; } = string.Empty;
    /// <summary>ISO 4217, e.g. AED.</summary>
    public string Currency { get; private set; } = string.Empty;
    /// <summary>IANA timezone, e.g. Asia/Dubai.</summary>
    public string TimeZone { get; private set; } = string.Empty;
    /// <summary>Default guest language: "en" or "ar".</summary>
    public string DefaultLocale { get; private set; } = "en";
    public string? LogoUrl { get; private set; }
    /// <summary>Hex colour used for the guest landing background.</summary>
    public string BrandColor { get; private set; } = "#0B3B38";
    public bool MenuEnabled { get; private set; } = true;
    public bool PayAtTableEnabled { get; private set; } = true;
    public bool IsActive { get; private set; } = true;

    /// <summary>Service charge added to the subtotal, in basis points (1000 = 10%).</summary>
    public int ServiceChargeBasisPoints { get; private set; }
    /// <summary>VAT or sales tax rate in basis points (500 = 5%).</summary>
    public int TaxBasisPoints { get; private set; }
    /// <summary>True when menu prices already include tax (usual in the UAE), so tax is shown, not added.</summary>
    public bool PricesIncludeTax { get; private set; } = true;

    public const int MaxRateBasisPoints = 3000;

    /// <summary>Whether guests are offered a tip when paying (P1-PAY-10).</summary>
    public bool TipsEnabled { get; private set; } = true;
    /// <summary>Tip suggestions in basis points of the amount being paid, e.g. 5%, 10%, 15%.</summary>
    public List<int> TipPresetsBasisPoints { get; private set; } = [500, 1000, 1500];
    /// <summary>The preset highlighted as "Most common" and pre-selected (P1-PAY-11); null for none.</summary>
    public int? MostCommonTipBasisPoints { get; private set; } = 1000;

    public void SetTips(bool enabled, IReadOnlyList<int> presetsBasisPoints, int? mostCommonBasisPoints)
    {
        var presets = presetsBasisPoints.Distinct().Order().ToList();
        if (presets.Count is 0 or > 4) throw new DomainException("Offer between one and four tip suggestions.");
        if (presets.Any(p => p is < 1 or > MaxRateBasisPoints)) throw new DomainException("Tip suggestions must be between 0.01% and 30%.");
        if (mostCommonBasisPoints is { } common && !presets.Contains(common))
            throw new DomainException("The most common tip must be one of the suggestions.");

        TipsEnabled = enabled;
        TipPresetsBasisPoints = presets;
        MostCommonTipBasisPoints = mostCommonBasisPoints;
    }

    public void SetCharges(int serviceChargeBasisPoints, int taxBasisPoints, bool pricesIncludeTax)
    {
        if (serviceChargeBasisPoints is < 0 or > MaxRateBasisPoints)
            throw new DomainException("Service charge must be between 0% and 30%.");
        if (taxBasisPoints is < 0 or > MaxRateBasisPoints)
            throw new DomainException("Tax must be between 0% and 30%.");

        ServiceChargeBasisPoints = serviceChargeBasisPoints;
        TaxBasisPoints = taxBasisPoints;
        PricesIncludeTax = pricesIncludeTax;
    }

    private Venue() { }

    public Venue(Guid tenantId, string name, string currency, string timeZone, string defaultLocale)
    {
        TenantId = tenantId;
        UpdateSettings(name, currency, timeZone, defaultLocale, LogoUrl, BrandColor, MenuEnabled, PayAtTableEnabled);
    }

    public void UpdateSettings(
        string name, string currency, string timeZone, string defaultLocale,
        string? logoUrl, string brandColor, bool menuEnabled, bool payAtTableEnabled)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("Venue name is required.");
        if (currency is not { Length: 3 }) throw new DomainException("Currency must be a 3-letter ISO 4217 code.");
        if (!menuEnabled && !payAtTableEnabled)
            throw new DomainException("Enable the menu, Pay at Table, or both, so guests have something to do.");

        Name = name.Trim();
        Currency = currency.ToUpperInvariant();
        TimeZone = timeZone;
        DefaultLocale = defaultLocale;
        LogoUrl = string.IsNullOrWhiteSpace(logoUrl) ? null : logoUrl.Trim();
        BrandColor = brandColor;
        MenuEnabled = menuEnabled;
        PayAtTableEnabled = payAtTableEnabled;
    }
}
