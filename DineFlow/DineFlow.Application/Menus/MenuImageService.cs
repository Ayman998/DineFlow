using DineFlow.Application.Abstractions;
using DineFlow.Application.Common;

namespace DineFlow.Application.Menus;

/// <summary>Accepts menu photos: JPEG, PNG or WebP up to 5 MB, checked by content, not file name.</summary>
public class MenuImageService(IFileStorage storage, ITenantContext tenant)
{
    public const long MaxBytes = 5 * 1024 * 1024;

    public async Task<MenuImageDto> UploadAsync(Stream content, long length, CancellationToken ct)
    {
        if (length <= 0) throw AppException.BadRequest("image_empty", "Choose an image to upload.");
        if (length > MaxBytes) throw AppException.BadRequest("image_too_large", "Images must be 5 MB or smaller.");

        using var buffer = new MemoryStream();
        await content.CopyToAsync(buffer, ct);
        if (buffer.Length > MaxBytes) throw AppException.BadRequest("image_too_large", "Images must be 5 MB or smaller.");

        var extension = DetectImageType(buffer.GetBuffer().AsSpan(0, (int)Math.Min(buffer.Length, 16)))
            ?? throw AppException.BadRequest("image_type", "Upload a JPEG, PNG or WebP image.");

        buffer.Position = 0;
        var url = await storage.SaveAsync(tenant.RequireTenantId(), "menu", extension, buffer, ct);
        return new MenuImageDto(url);
    }

    /// <summary>File extension for a JPEG, PNG or WebP signature; null for anything else.</summary>
    public static string? DetectImageType(ReadOnlySpan<byte> header)
    {
        if (header.Length >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF) return "jpg";
        if (header.Length >= 8 && header[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "png";
        if (header.Length >= 12 && header[..4].SequenceEqual("RIFF"u8) && header[8..12].SequenceEqual("WEBP"u8)) return "webp";
        return null;
    }
}
