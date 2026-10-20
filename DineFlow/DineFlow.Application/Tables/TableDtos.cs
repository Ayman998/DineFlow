using System.ComponentModel.DataAnnotations;
using DineFlow.Domain.Venues;

namespace DineFlow.Application.Tables;

/// <summary>A venue's areas and locations, as the merchant Tables page shows them.</summary>
public record FloorPlanDto(
    Guid VenueId,
    string VenueName,
    IReadOnlyList<AreaDto> Areas,
    IReadOnlyList<LocationDto> Unassigned);

public record AreaDto(Guid Id, string Name, int SortOrder, IReadOnlyList<LocationDto> Locations);

public record LocationDto(
    Guid Id,
    Guid? DiningAreaId,
    string Type,
    string Label,
    string DisplayName,
    bool IsActive,
    QrDto? Qr);

/// <summary>The location's current QR code. <see cref="Url"/> is what gets printed.</summary>
public record QrDto(Guid Id, int Version, string Status, string Url);

public record CreateAreaRequest([Required, StringLength(80, MinimumLength = 1)] string Name);

public record CreateLocationRequest(
    [Required] Guid VenueId,
    Guid? DiningAreaId,
    [Required] ServiceLocationType Type,
    [Required, StringLength(20, MinimumLength = 1)] string Label);

public record BulkCreateTablesRequest(
    [Required] Guid VenueId,
    Guid? DiningAreaId,
    [Range(1, 999)] int From,
    [Range(1, 999)] int To);
