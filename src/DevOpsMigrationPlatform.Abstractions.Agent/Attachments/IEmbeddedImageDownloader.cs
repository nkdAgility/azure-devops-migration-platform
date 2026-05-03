// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

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
