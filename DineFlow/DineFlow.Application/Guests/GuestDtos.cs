namespace DineFlow.Application.Guests;

/// <summary>Returned when a guest scans a QR code: the new session plus its bearer token.</summary>
public record QrResolutionDto(string SessionToken, GuestContextDto Context);

/// <summary>Everything the guest landing screen needs (blueprint 4.2 step 1).</summary>
public record GuestContextDto(
    Guid SessionId,
    DateTimeOffset ExpiresAt,
    string Locale,
    GuestVenueDto Venue,
    GuestLocationDto Location,
    GuestFeaturesDto Features);

public record GuestVenueDto(
    string BrandName,
    string BranchName,
    string? LogoUrl,
    string BrandColor,
    string Currency);

public record GuestLocationDto(string Type, string Label, string DisplayName, string? AreaName);

/// <summary>Which actions the landing screen offers; only supported ones are shown (P1-QR-03).</summary>
public record GuestFeaturesDto(bool Menu, bool PayAtTable);
