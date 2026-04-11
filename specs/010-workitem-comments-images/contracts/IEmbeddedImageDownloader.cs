// ============================================================
// DevOpsMigrationPlatform.Abstractions
// Namespace: DevOpsMigrationPlatform.Abstractions.Services
// ============================================================

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Downloads a single ADO-hosted attachment/image URL and returns its raw bytes.
/// Returns <c>null</c> if the URL is not hosted on the source organisation
/// (external URLs), or if the download fails.
/// Failures produce a warning log entry and are not thrown, to avoid aborting
/// the entire export over a single inaccessible image.
/// </summary>
public interface IEmbeddedImageDownloader
{
    /// <summary>
    /// Attempts to download the resource at <paramref name="imageUrl"/>.
    /// </summary>
    /// <param name="imageUrl">
    /// The original URL extracted from an HTML <c>img src</c> or Markdown
    /// <c>![](url)</c> element.
    /// </param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    /// <returns>
    /// Download result containing raw bytes and inferred file extension,
    /// or <c>null</c> if the URL should be left as-is.
    /// </returns>
    Task<EmbeddedImageDownloadResult?> TryDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken);
}

/// <summary>
/// The result of a successful <see cref="IEmbeddedImageDownloader"/> call.
/// </summary>
public record EmbeddedImageDownloadResult
{
    /// <summary>Raw bytes of the downloaded image.</summary>
    public required byte[] Bytes { get; init; }

    /// <summary>
    /// File extension inferred from the <c>Content-Type</c> response header,
    /// including the leading dot (e.g. <c>.png</c>, <c>.jpg</c>).
    /// </summary>
    public required string Extension { get; init; }
}
