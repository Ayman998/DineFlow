using System.ComponentModel.DataAnnotations;

namespace DineFlow.Application.Venues;

public record VenueDto(
    Guid Id,
    string Name,
    string Currency,
    string TimeZone,
    string DefaultLocale,
    string? LogoUrl,
    string BrandColor,
    bool MenuEnabled,
    bool PayAtTableEnabled,
    bool IsActive);

public record VenueSettingsRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Required, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Use a 3-letter ISO 4217 currency code.")] string Currency,
    [Required, StringLength(64)] string TimeZone,
    [Required, RegularExpression("^(en|ar)$", ErrorMessage = "Supported languages are en and ar.")] string DefaultLocale,
    [Url, StringLength(500)] string? LogoUrl,
    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a hex colour such as #0B3B38.")] string BrandColor,
    bool MenuEnabled,
    bool PayAtTableEnabled);
