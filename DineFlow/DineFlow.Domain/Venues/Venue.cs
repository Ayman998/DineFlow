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
