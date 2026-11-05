namespace HelloZen.Api.Common;

/// <summary>
/// The business that operates helloZen, shown on the guest app's terms, privacy, refunds and contact
/// pages. Not secret: set it in appsettings (appsettings.MonsterAsp.json for the live site). Empty
/// values are left off the pages, and outside Development a startup warning lists them.
/// </summary>
public class LegalOptions
{
    public const string Section = "Legal";

    /// <summary>The registered company name, e.g. "Example Technology LLC".</summary>
    public string LegalName { get; set; } = "";

    /// <summary>Registered address, e.g. "Mecca Street, Amman, Jordan".</summary>
    public string Address { get; set; } = "";

    /// <summary>Where guests write about refunds and their data.</summary>
    public string Email { get; set; } = "";

    public string Phone { get; set; } = "";

    /// <summary>Commercial or national registration number, if you want it shown.</summary>
    public string RegistrationNumber { get; set; } = "";

    public LegalInfoDto ToDto() => new(Blank(LegalName), Blank(Address), Blank(Email), Blank(Phone), Blank(RegistrationNumber));

    private static string? Blank(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

public record LegalInfoDto(string? LegalName, string? Address, string? Email, string? Phone, string? RegistrationNumber);
