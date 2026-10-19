using DineFlow.Domain.Common;

namespace DineFlow.Domain.Tenants;

/// <summary>A merchant brand: the data-isolation boundary for everything it owns.</summary>
public class Tenant : Entity
{
    public string LegalName { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    /// <summary>ISO 3166-1 alpha-2, e.g. AE or SA.</summary>
    public string Country { get; private set; } = string.Empty;
    public TenantStatus Status { get; private set; } = TenantStatus.Onboarding;

    private Tenant() { }

    public Tenant(string legalName, string displayName, string country)
    {
        LegalName = legalName;
        DisplayName = displayName;
        Country = country.ToUpperInvariant();
    }
}

public enum TenantStatus
{
    Onboarding = 0,
    Active = 1,
    Suspended = 2
}
