using System;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Result of a single embedded image download attempt.
/// </summary>
public record EmbeddedImageDownloadResult
{
    /// <summary>
    /// Raw image bytes downloaded from the source.
    /// </summary>
    public byte[] Bytes { get; init; } = Array.Empty<byte>();

    /// <summary>
    /// File extension of the image (e.g., "png", "jpg", "gif"), inferred from Content-Type.
    /// </summary>
    public string Extension { get; init; } = string.Empty;
}
/// <summary>
/// Abstraction for downloading embedded images from URLs in field values.
/// </summary>
public interface IEmbeddedImageDownloader
{
    /// <summary>
    /// Attempts to download an image from a URL.
    /// </summary>
    /// <param name="imageUrl">The absolute URL of the image.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// An <see cref="EmbeddedImageDownloadResult"/> if the URL is hosted on the source organisation and the download succeeds.
    /// Returns null if the URL is external (not on the source org), or if the download fails (404, timeout, network error, etc).
    /// Failures are logged as warnings but do not throw.
    /// </returns>
    Task<EmbeddedImageDownloadResult?> TryDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken);
}
