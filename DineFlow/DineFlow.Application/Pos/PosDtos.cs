using System.ComponentModel.DataAnnotations;
using DineFlow.Application.Abstractions;
using DineFlow.Domain.Pos;

namespace DineFlow.Application.Pos;

/// <summary>A connector DineFlow has, with what its POS can do (the capability matrix, blueprint E3).</summary>
public record PosProviderDto(string Provider, string DisplayName, IReadOnlyList<string> Capabilities, bool RequiresAccessToken);

/// <param name="HasAccessToken">A token is stored; the token itself is never returned.</param>
public record PosConnectionDto(
    string Provider,
    string? BranchRef,
    bool HasAccessToken,
    bool IsActive,
    bool AutoClose,
    DateTimeOffset? LastCheckedAt,
    bool? LastCheckHealthy,
    string? LastCheckMessage);

public record PosTableMappingDto(Guid LocationId, string DisplayName, string? AreaName, string? ExternalRef);

public record PosProductMappingDto(Guid ItemId, string Name, string Category, string? PosProductRef);

/// <param name="Connection">Null until the venue connects a POS.</param>
/// <param name="Waiting">Commands waiting more than 15 minutes, or refused by the POS: what staff should look at.</param>
public record PosSettingsDto(
    PosConnectionDto? Connection,
    IReadOnlyList<PosProviderDto> Providers,
    IReadOnlyList<PosTableMappingDto> Tables,
    IReadOnlyList<PosProductMappingDto> Products,
    int Waiting);

/// <param name="AccessToken">Null keeps the stored token; an empty string removes it.</param>
public record PosConnectionRequest(
    [Required, StringLength(30)] string Provider,
    [StringLength(100)] string? BranchRef,
    [StringLength(4000)] string? AccessToken,
    bool IsActive,
    bool AutoClose);

public record PosTableMappingRequest([Required] Guid LocationId, [StringLength(100)] string? ExternalRef);

public record PosProductMappingRequest([Required] Guid ItemId, [StringLength(100)] string? PosProductRef);

public record PosMappingRequest(IReadOnlyList<PosTableMappingRequest>? Tables, IReadOnlyList<PosProductMappingRequest>? Products);

public record PosCommandQuery([Required] Guid VenueId, PosCommandStatus? Status = null, [Range(1, 10_000)] int Page = 1, [Range(1, 100)] int PageSize = 25);

/// <param name="Description">E.g. "Order #1012 · Table 7" or "Payment DF-1A2B3C4D · AED 55.00 · Table 7".</param>
public record PosCommandDto(
    Guid Id,
    PosCommandKind Kind,
    string Description,
    PosCommandStatus Status,
    bool NeedsAttention,
    int Attempts,
    DateTimeOffset CreatedAt,
    DateTimeOffset? NextAttemptAt,
    DateTimeOffset? CompletedAt,
    string? LastError,
    string? ResultRef,
    Guid? PaymentId);

public record PosCommandPageDto(IReadOnlyList<PosCommandDto> Items, int Total, int Page, int PageSize, int Waiting);

/// <summary>How a payment stands with the POS, shown on transactions.</summary>
public enum PosPostingState { Pending, Posted, Failed }

internal static class PosCapabilityNames
{
    public static IReadOnlyList<string> Of(PosCapabilities capabilities) =>
        Enum.GetValues<PosCapabilities>().Where(c => c != PosCapabilities.None && capabilities.HasFlag(c)).Select(c => c.ToString()).ToList();
}
