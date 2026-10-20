using DineFlow.Application.Abstractions;
using Microsoft.Extensions.Options;

namespace DineFlow.Infrastructure.Storage;

public class StorageOptions
{
    public const string Section = "Storage";

    /// <summary>Folder for uploaded files, relative to the API's content root or absolute.</summary>
    public string LocalRoot { get; set; } = "App_Data/media";

    /// <summary>URL path the API serves <see cref="LocalRoot"/> under.</summary>
    public string PublicPath { get; set; } = "/media";
}

/// <summary>
/// Saves files under {LocalRoot}/{tenantId}/{folder}/{random}.{ext}. Names are random and never
/// reused, so the files can be cached forever.
/// </summary>
public class LocalFileStorage(IOptions<StorageOptions> options, string contentRoot) : IFileStorage
{
    public string RootPath { get; } = Path.GetFullPath(Path.Combine(contentRoot, options.Value.LocalRoot));

    public async Task<string> SaveAsync(Guid tenantId, string folder, string extension, Stream content, CancellationToken ct)
    {
        if (folder.Any(c => !char.IsAsciiLetterLower(c))) throw new ArgumentException("Folder must be lower-case letters.", nameof(folder));
        if (extension.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Invalid extension.", nameof(extension));

        var directory = Path.Combine(RootPath, tenantId.ToString("N"), folder);
        Directory.CreateDirectory(directory);

        var fileName = $"{Guid.NewGuid():N}.{extension}";
        await using (var file = File.Create(Path.Combine(directory, fileName)))
        {
            await content.CopyToAsync(file, ct);
        }

        return $"{options.Value.PublicPath.TrimEnd('/')}/{tenantId:N}/{folder}/{fileName}";
    }
}
