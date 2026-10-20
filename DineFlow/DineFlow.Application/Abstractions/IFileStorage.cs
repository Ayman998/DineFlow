namespace DineFlow.Application.Abstractions;

/// <summary>
/// Where uploaded files (menu images, later QR exports and receipts) are kept. Local disk in
/// development; S3-compatible object storage in production (blueprint 5.1).
/// </summary>
public interface IFileStorage
{
    /// <summary>Stores the content under a new unique name and returns its public URL path.</summary>
    Task<string> SaveAsync(Guid tenantId, string folder, string extension, Stream content, CancellationToken ct);
}
