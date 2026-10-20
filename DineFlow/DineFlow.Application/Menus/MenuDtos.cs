using System.ComponentModel.DataAnnotations;
using DineFlow.Domain.Menus;

namespace DineFlow.Application.Menus;

/// <summary>A venue's menus as a tree: menu → category → subcategory → item → modifier groups.</summary>
public record MenuTreeDto(Guid VenueId, string Currency, IReadOnlyList<MenuDto> Menus);

/// <param name="Days">Days the menu runs, e.g. ["Monday", "Tuesday"].</param>
/// <param name="StartTime">"HH:mm" in venue local time; null with EndTime means all day.</param>
/// <param name="AvailableNow">Whether guests can order from it at this moment.</param>
public record MenuDto(
    Guid Id,
    string Name,
    string? NameAr,
    int SortOrder,
    bool IsPublished,
    IReadOnlyList<DayOfWeek> Days,
    string? StartTime,
    string? EndTime,
    bool AvailableNow,
    IReadOnlyList<CategoryDto> Categories);

public record CategoryDto(
    Guid Id,
    Guid MenuId,
    Guid? ParentId,
    string Name,
    string? NameAr,
    int SortOrder,
    IReadOnlyList<ItemDto> Items,
    IReadOnlyList<CategoryDto> Subcategories);

public record ItemDto(
    Guid Id,
    Guid CategoryId,
    string Name,
    string? NameAr,
    string? Description,
    string? DescriptionAr,
    long PriceMinor,
    string? ImageUrl,
    bool IsAvailable,
    IReadOnlyList<Allergen> Allergens,
    IReadOnlyList<string> Tags,
    int SortOrder,
    IReadOnlyList<ModifierGroupDto> ModifierGroups);

public record ModifierGroupDto(
    Guid Id, string Name, string? NameAr, int MinSelect, int MaxSelect, IReadOnlyList<ModifierOptionDto> Options);

public record ModifierOptionDto(
    Guid Id, string Name, string? NameAr, long PriceDeltaMinor, bool IsDefault, bool IsAvailable);

public record MenuRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [StringLength(80)] string? NameAr,
    bool IsPublished,
    [Required, MinLength(1)] IReadOnlyList<DayOfWeek> Days,
    [RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Use HH:mm, e.g. 07:30.")] string? StartTime,
    [RegularExpression(@"^\d{2}:\d{2}$", ErrorMessage = "Use HH:mm, e.g. 11:30.")] string? EndTime);

public record CreateCategoryRequest(
    [Required] Guid MenuId,
    Guid? ParentId,
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [StringLength(80)] string? NameAr);

public record UpdateCategoryRequest(
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [StringLength(80)] string? NameAr);

public record ItemRequest(
    [Required] Guid CategoryId,
    [Required, StringLength(120, MinimumLength = 1)] string Name,
    [StringLength(120)] string? NameAr,
    [StringLength(500)] string? Description,
    [StringLength(500)] string? DescriptionAr,
    [Range(0, 100_000_000)] long PriceMinor,
    [StringLength(500)] string? ImageUrl,
    bool IsAvailable,
    IReadOnlyList<Allergen>? Allergens,
    IReadOnlyList<string>? Tags,
    IReadOnlyList<ModifierGroupRequest>? ModifierGroups);

public record ModifierGroupRequest(
    Guid? Id,
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [StringLength(80)] string? NameAr,
    [Range(0, 20)] int MinSelect,
    [Range(1, 20)] int MaxSelect,
    [Required] IReadOnlyList<ModifierOptionRequest> Options);

public record ModifierOptionRequest(
    Guid? Id,
    [Required, StringLength(80, MinimumLength = 1)] string Name,
    [StringLength(80)] string? NameAr,
    [Range(0, 100_000_000)] long PriceDeltaMinor,
    bool IsDefault,
    bool IsAvailable = true);

public record AvailabilityRequest(bool IsAvailable);

/// <param name="Kind">"menus", "categories" or "items".</param>
/// <param name="Ids">Every sibling, in the new order.</param>
public record ReorderRequest(
    [Required, RegularExpression("^(menus|categories|items)$")] string Kind,
    [Required, MinLength(1)] IReadOnlyList<Guid> Ids);

public record MenuImageDto(string Url);
