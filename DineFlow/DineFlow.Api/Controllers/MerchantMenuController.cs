using DineFlow.Api.Common;
using DineFlow.Application.Menus;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace DineFlow.Api.Controllers;

/// <summary>Menu management (blueprint 9.2: GET/POST/PUT /v1/merchant/menu/*).</summary>
[ApiController]
[Authorize(Policy = Policies.MerchantManager)]
public class MerchantMenuController(MenuAdminService menus, MenuImageService images) : ControllerBase
{
    /// <summary>All of a venue's menus, published or not, with categories, items and modifiers.</summary>
    [HttpGet("v1/merchant/venues/{venueId:guid}/menu")]
    [Authorize(Policy = Policies.Merchant)]
    public Task<MenuTreeDto> Tree(Guid venueId, CancellationToken ct) => menus.GetTreeAsync(venueId, ct);

    [HttpPost("v1/merchant/venues/{venueId:guid}/menus")]
    public Task<MenuDto> CreateMenu(Guid venueId, MenuRequest request, CancellationToken ct) =>
        menus.CreateMenuAsync(venueId, request, ct);

    [HttpPut("v1/merchant/menu/menus/{menuId:guid}")]
    public Task<MenuDto> UpdateMenu(Guid menuId, MenuRequest request, CancellationToken ct) =>
        menus.UpdateMenuAsync(menuId, request, ct);

    [HttpDelete("v1/merchant/menu/menus/{menuId:guid}")]
    public async Task<IActionResult> DeleteMenu(Guid menuId, CancellationToken ct)
    {
        await menus.DeleteMenuAsync(menuId, ct);
        return NoContent();
    }

    [HttpPost("v1/merchant/menu/categories")]
    public Task<CategoryDto> CreateCategory(CreateCategoryRequest request, CancellationToken ct) =>
        menus.CreateCategoryAsync(request, ct);

    [HttpPut("v1/merchant/menu/categories/{categoryId:guid}")]
    public Task<CategoryDto> UpdateCategory(Guid categoryId, UpdateCategoryRequest request, CancellationToken ct) =>
        menus.UpdateCategoryAsync(categoryId, request, ct);

    [HttpDelete("v1/merchant/menu/categories/{categoryId:guid}")]
    public async Task<IActionResult> DeleteCategory(Guid categoryId, CancellationToken ct)
    {
        await menus.DeleteCategoryAsync(categoryId, ct);
        return NoContent();
    }

    [HttpPost("v1/merchant/menu/items")]
    public Task<ItemDto> CreateItem(ItemRequest request, CancellationToken ct) =>
        menus.CreateItemAsync(request, ct);

    [HttpPut("v1/merchant/menu/items/{itemId:guid}")]
    public Task<ItemDto> UpdateItem(Guid itemId, ItemRequest request, CancellationToken ct) =>
        menus.UpdateItemAsync(itemId, request, ct);

    /// <summary>Sold out / back in stock. Any staff member may do this during service.</summary>
    [HttpPut("v1/merchant/menu/items/{itemId:guid}/availability")]
    [Authorize(Policy = Policies.Merchant)]
    public Task<ItemDto> SetAvailability(Guid itemId, AvailabilityRequest request, CancellationToken ct) =>
        menus.SetItemAvailabilityAsync(itemId, request.IsAvailable, ct);

    [HttpDelete("v1/merchant/menu/items/{itemId:guid}")]
    public async Task<IActionResult> DeleteItem(Guid itemId, CancellationToken ct)
    {
        await menus.DeleteItemAsync(itemId, ct);
        return NoContent();
    }

    [HttpPost("v1/merchant/menu/reorder")]
    public async Task<IActionResult> Reorder(ReorderRequest request, CancellationToken ct)
    {
        await menus.ReorderAsync(request, ct);
        return NoContent();
    }

    /// <summary>Uploads a menu photo (JPEG, PNG or WebP, up to 5 MB) and returns its URL.</summary>
    [HttpPost("v1/merchant/menu/images")]
    [RequestSizeLimit(MenuImageService.MaxBytes + 64 * 1024)]
    public async Task<MenuImageDto> UploadImage(IFormFile file, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return await images.UploadAsync(stream, file.Length, ct);
    }
}
