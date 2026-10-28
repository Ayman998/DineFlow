using System.ComponentModel.DataAnnotations;
using DineFlow.Domain.Reviews;

namespace DineFlow.Application.Venues;

/// <param name="NameAr">The branch's name for guests reading Arabic; null shows <paramref name="Name"/>.</param>
public record VenueDto(
    Guid Id,
    string Name,
    string? NameAr,
    string Currency,
    string TimeZone,
    string DefaultLocale,
    string? LogoUrl,
    string BrandColor,
    bool MenuEnabled,
    bool PayAtTableEnabled,
    bool IsActive,
    int ServiceChargeBasisPoints,
    int TaxBasisPoints,
    bool PricesIncludeTax,
    bool TipsEnabled,
    IReadOnlyList<int> TipPresetsBasisPoints,
    int? MostCommonTipBasisPoints,
    bool ReviewsEnabled,
    ReviewPlatform? ReviewPlatform,
    string? ReviewUrl,
    string? FeedbackTitle);

public record VenueSettingsRequest(
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [Required, RegularExpression("^[A-Za-z]{3}$", ErrorMessage = "Use a 3-letter ISO 4217 currency code.")] string Currency,
    [Required, StringLength(64)] string TimeZone,
    [Required, RegularExpression("^(en|ar)$", ErrorMessage = "Supported languages are en and ar.")] string DefaultLocale,
    [Url, StringLength(500)] string? LogoUrl,
    [Required, RegularExpression("^#[0-9A-Fa-f]{6}$", ErrorMessage = "Use a hex colour such as #0B3B38.")] string BrandColor,
    bool MenuEnabled,
    bool PayAtTableEnabled,
    /// <summary>Service charge in basis points: 1000 = 10%.</summary>
    [Range(0, 3000)] int ServiceChargeBasisPoints = 0,
    /// <summary>VAT/sales tax in basis points: 500 = 5%.</summary>
    [Range(0, 3000)] int TaxBasisPoints = 0,
    bool PricesIncludeTax = true,
    bool TipsEnabled = true,
    /// <summary>One to four tip suggestions in basis points, e.g. [500, 1000, 1500].</summary>
    IReadOnlyList<int>? TipPresetsBasisPoints = null,
    /// <summary>The suggestion highlighted as "Most common"; must be one of the presets.</summary>
    int? MostCommonTipBasisPoints = 1000,
    /// <summary>Ask guests to rate their visit after paying.</summary>
    bool ReviewsEnabled = true,
    /// <summary>Where 4–5 star guests are invited to post publicly; null for nowhere.</summary>
    ReviewPlatform? ReviewPlatform = null,
    [Url, StringLength(500)] string? ReviewUrl = null,
    /// <summary>Heading of the private feedback form for 1–3 stars.</summary>
    [StringLength(80)] string? FeedbackTitle = null,
    /// <summary>The branch's name for guests reading Arabic; empty shows the name to everyone.</summary>
    [StringLength(120)] string? NameAr = null);
