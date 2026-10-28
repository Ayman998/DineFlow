using DineFlow.Domain.Common;

namespace DineFlow.Domain.Staff;

/// <summary>A merchant user who signs in to the dashboard.</summary>
public class StaffUser : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    /// <summary>Stored lower-case; unique across the platform.</summary>
    public string Email { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public StaffRole Role { get; private set; }
    public bool IsActive { get; private set; } = true;

    private StaffUser() { }

    public StaffUser(Guid tenantId, string email, string displayName, StaffRole role)
    {
        TenantId = tenantId;
        Email = email.Trim().ToLowerInvariant();
        DisplayName = displayName.Trim();
        Role = role;
    }

    public void SetPasswordHash(string passwordHash) => PasswordHash = passwordHash;

    public void Rename(string displayName) => DisplayName = Text.Required(displayName, MaxNameLength, "Name");

    public void SetRole(StaffRole role) => Role = role;

    /// <summary>Signs the person out everywhere (see the API's token check) and blocks sign-in; history keeps their name.</summary>
    public void Deactivate() => IsActive = false;

    public void Activate() => IsActive = true;

    public const int MaxNameLength = 80;

    /// <summary>The shortest password DineFlow accepts (NIST 800-63B asks for at least 8; staff share tablets, so 10).</summary>
    public const int MinPasswordLength = 10;
}

public enum StaffRole
{
    Owner = 0,
    Manager = 1,
    Staff = 2
}
