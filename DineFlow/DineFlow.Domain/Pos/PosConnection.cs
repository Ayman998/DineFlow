using DineFlow.Domain.Common;

namespace DineFlow.Domain.Pos;

/// <summary>
/// A venue's link to its POS (blueprint E3 "connection config, health"). While it's active,
/// accepted orders are sent to the POS and every successful payment at a table is posted to it.
/// The access token is stored encrypted and never returned by the API.
/// </summary>
public class PosConnection : Entity, ITenantOwned
{
    public Guid TenantId { get; private set; }
    public Guid VenueId { get; private set; }
    /// <summary>Which connector: "simulator" (built in, for testing) or "foodics".</summary>
    public string Provider { get; private set; } = string.Empty;
    /// <summary>The venue's branch in the POS, e.g. a Foodics branch id.</summary>
    public string? BranchRef { get; private set; }
    /// <summary>Encrypted access token (see ISecretProtector); null when the connector needs none.</summary>
    public string? ProtectedAccessToken { get; private set; }
    public bool IsActive { get; private set; } = true;
    /// <summary>Close the POS check once the bill is paid in full, where the POS supports it.</summary>
    public bool AutoClose { get; private set; } = true;

    public DateTimeOffset? LastCheckedAt { get; private set; }
    public bool? LastCheckHealthy { get; private set; }
    public string? LastCheckMessage { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    private PosConnection() { }

    public PosConnection(Guid tenantId, Guid venueId, string provider, DateTimeOffset now)
    {
        TenantId = tenantId;
        VenueId = venueId;
        Provider = provider;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public void Configure(string provider, string? branchRef, bool isActive, bool autoClose, DateTimeOffset now)
    {
        if (provider != Provider)
        {
            // A different POS: its old token and health say nothing about the new one.
            ProtectedAccessToken = null;
            LastCheckedAt = null;
            LastCheckHealthy = null;
            LastCheckMessage = null;
        }
        Provider = provider;
        BranchRef = Text.Optional(branchRef, 100, "Branch");
        IsActive = isActive;
        AutoClose = autoClose;
        UpdatedAt = now;
    }

    /// <param name="protectedToken">Already encrypted; null removes the stored token.</param>
    public void SetAccessToken(string? protectedToken, DateTimeOffset now)
    {
        ProtectedAccessToken = protectedToken;
        UpdatedAt = now;
    }

    public void RecordHealth(bool healthy, string? message, DateTimeOffset now)
    {
        LastCheckedAt = now;
        LastCheckHealthy = healthy;
        LastCheckMessage = message is { Length: > 300 } ? message[..300] : message;
    }
}
