// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// Downloads embedded images from the active job's source endpoint (#169, #154), for the Azure
/// DevOps and TFS connectors on both runtimes. Only URLs under the source endpoint's URL are
/// fetched; anything else is external and keeps its original link. Requests carry the source
/// endpoint's access token as Basic auth, or Windows credentials when there is no token
/// (on-premises TFS). Only image responses are accepted, so a sign-in page is never saved as one.
/// </summary>
public sealed class SourceEndpointEmbeddedImageDownloader : IEmbeddedImageDownloader
{
    private const int MaxAttempts = 3;

    /// <summary>Largest embedded image accepted (50 MB). A larger response is refused from its headers.</summary>
    public const long MaxImageBytes = 50L * 1024 * 1024;

    private readonly ISourceEndpointInfo _source;
    private readonly ILogger<SourceEndpointEmbeddedImageDownloader> _logger;
    private readonly Func<HttpMessageHandler>? _handlerFactory;
    private readonly Func<int, TimeSpan> _retryDelay;

    public SourceEndpointEmbeddedImageDownloader(
        ISourceEndpointInfo source,
        ILogger<SourceEndpointEmbeddedImageDownloader> logger,
        Func<HttpMessageHandler>? handlerFactory = null,
        Func<int, TimeSpan>? retryDelay = null,
        TimeSpan? attemptTimeout = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handlerFactory = handlerFactory;
        _retryDelay = retryDelay ?? (attempt => TimeSpan.FromSeconds(2 * attempt));
        _attemptTimeout = attemptTimeout ?? TimeSpan.FromSeconds(100);
    }

    // Covers the whole attempt, body included: with ResponseHeadersRead the HttpClient timeout
    // ends at the headers, so a body that stops sending would otherwise hold the export open.
    private readonly TimeSpan _attemptTimeout;

    /// <inheritdoc/>
    /// <remarks>
    /// Returns null for an image that is not on the source endpoint or is permanently unavailable
    /// (it keeps its original link). A temporary failure (throttling, a server error, a timeout)
    /// is retried; if it persists, <see cref="EmbeddedImageDownloadException"/> fails the revision
    /// so the next run retries it instead of recording it as done without the image.
    /// </remarks>
    public async Task<EmbeddedImageDownloadResult?> TryDownloadAsync(string imageUrl, CancellationToken cancellationToken)
    {
        if (!IsOnSourceEndpoint(imageUrl))
            return null;

        var token = _source.ToOrganisationEndpoint().Authentication?.ResolvedAccessToken;
        // Redirects are followed here, not by the handler, so every hop is checked against the
        // source endpoint and credentials never reach another host.
        var handler = _handlerFactory?.Invoke()
            ?? new HttpClientHandler { UseDefaultCredentials = string.IsNullOrEmpty(token), AllowAutoRedirect = false };
        using var client = new HttpClient(handler, disposeHandler: _handlerFactory is null);

        string lastFailure = string.Empty;
        Exception? lastException = null;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            if (attempt > 1)
                await Task.Delay(_retryDelay(attempt - 1), cancellationToken).ConfigureAwait(false);
            using var attemptCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            attemptCts.CancelAfter(_attemptTimeout);
            try
            {
                var (result, temporaryFailure) = await DownloadOnceAsync(client, imageUrl, token, attemptCts.Token).ConfigureAwait(false);
                if (temporaryFailure is null)
                    return result;
                lastFailure = temporaryFailure;
                lastException = null;
            }
            // Transport failures, including a connection that closes mid-body (an IOException on
            // .NET 10) and the attempt deadline, are retried; the caller's cancellation is not.
            catch (Exception ex) when (ex is HttpRequestException or System.IO.IOException or OperationCanceledException
                                       && !cancellationToken.IsCancellationRequested)
            {
                lastFailure = ex.Message;
                lastException = ex;
            }

            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogWarning(lastException, "Embedded image {Url} attempt {Attempt} of {MaxAttempts} failed temporarily: {Failure}.", imageUrl, attempt, MaxAttempts, lastFailure);
        }

        throw new EmbeddedImageDownloadException(
            $"Embedded image could not be downloaded after {MaxAttempts} attempts ({lastFailure}). Run the export again to retry it.",
            lastException);
    }

    /// <summary>One download. A non-null temporary failure means the caller should try again.</summary>
    private async Task<(EmbeddedImageDownloadResult? Result, string? TemporaryFailure)> DownloadOnceAsync(
        HttpClient client, string imageUrl, string? token, CancellationToken cancellationToken)
    {
        var url = imageUrl;
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + token)));

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (IsRedirect(response.StatusCode) && response.Headers.Location is { } location)
            {
                var next = location.IsAbsoluteUri ? location : new Uri(new Uri(url), location);
                if (!IsOnSourceEndpoint(next.AbsoluteUri))
                {
                    using (DataClassificationScope.Begin(DataClassification.Customer))
                        _logger.LogWarning("Embedded image {Url} not downloaded: it redirects off the source endpoint.", imageUrl);
                    return (null, null);
                }
                url = next.AbsoluteUri;
                continue;
            }

            if (IsTemporary(response.StatusCode))
                return (null, $"HTTP {(int)response.StatusCode}");

            // Headers only so far: check status, type and size before reading any of the body.
            var mediaType = response.Content?.Headers.ContentType?.MediaType;
            var acceptableType = mediaType is null
                || mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)
                || string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase);
            var tooLarge = response.Content?.Headers.ContentLength > MaxImageBytes;
            var bytes = response.IsSuccessStatusCode && response.Content is not null && acceptableType && !tooLarge
                ? await ReadCappedAsync(response.Content, cancellationToken).ConfigureAwait(false)
                : null;
            var extension = bytes is null ? null : ImageExtension(mediaType, bytes);
            if (extension is null)
            {
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogWarning("Embedded image {Url} not downloaded: {Status} {MediaType}.", imageUrl, (int)response.StatusCode, mediaType);
                return (null, null);
            }

            return (new EmbeddedImageDownloadResult { Bytes = bytes!, Extension = extension }, null);
        }

        using (DataClassificationScope.Begin(DataClassification.Customer))
            _logger.LogWarning("Embedded image {Url} not downloaded: too many redirects.", imageUrl);
        return (null, null);
    }

    // Server errors and throttling are retried; one that persists fails the revision, so the work
    // item stops and is flagged instead of keeping a link back to the source (Martin, 2026-11-17).
    private static bool IsTemporary(System.Net.HttpStatusCode status) => (int)status is 408 or 429 or 500 or 502 or 503 or 504;

    /// <summary>Reads the body, stopping (null) once it passes <see cref="MaxImageBytes"/>.</summary>
    private static async Task<byte[]?> ReadCappedAsync(HttpContent content, CancellationToken ct)
    {
        using var source = await content.ReadAsStreamAsync().ConfigureAwait(false);
        using var buffer = new System.IO.MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await source.ReadAsync(chunk, 0, chunk.Length, ct).ConfigureAwait(false)) > 0)
        {
            if (buffer.Length + read > MaxImageBytes)
                return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private bool IsOnSourceEndpoint(string imageUrl)
    {
        if (string.IsNullOrWhiteSpace(imageUrl) || string.IsNullOrWhiteSpace(_source.Url))
            return false;
        if (!Uri.TryCreate(imageUrl, UriKind.Absolute, out var image) || !Uri.TryCreate(_source.Url, UriKind.Absolute, out var source))
            return false;

        var sourcePath = source.AbsolutePath.TrimEnd('/') + "/";
        return string.Equals(image.Scheme, source.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(image.Authority, source.Authority, StringComparison.OrdinalIgnoreCase)
            && (image.AbsolutePath + "/").StartsWith(sourcePath, StringComparison.OrdinalIgnoreCase);
    }

    private const int MaxRedirects = 5;

    private static bool IsRedirect(System.Net.HttpStatusCode status) => (int)status is 301 or 302 or 303 or 307 or 308;

    /// <summary>
    /// The file extension for an image response, or null when it is not an image. A generic
    /// binary type (the attachments API may answer <c>application/octet-stream</c>) is accepted
    /// only when the bytes start with a known image signature.
    /// </summary>
    private static string? ImageExtension(string? mediaType, byte[] bytes)
    {
        var declaredImage = mediaType is not null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        if (!declaredImage && mediaType is not null && !string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return null;

        // A recognised format must also be complete, so a truncated response is never saved
        // as a broken image. Formats not checked here (e.g. SVG) are trusted only when declared.
        var sniffed = SniffImageExtension(bytes);
        if (sniffed is not null)
            return IsComplete(sniffed, bytes) ? sniffed : null;
        return declaredImage && bytes.Length > 0 && !IsSniffable(mediaType!) ? ExtensionFor(mediaType!) : null;
    }

    private static bool IsSniffable(string mediaType) => ExtensionFor(mediaType) is "png" or "jpg" or "gif" or "webp" or "bmp";

    /// <summary>True when the bytes end where the format says the image ends.</summary>
    private static bool IsComplete(string extension, byte[] b)
    {
        switch (extension)
        {
            case "png":
                // Signature, then IHDR first, and the IEND chunk (with its CRC) last.
                byte[] iend = [0x00, 0x00, 0x00, 0x00, 0x49, 0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82];
                return b.Length >= 8 + 25 + 12
                    && b[12] == (byte)'I' && b[13] == (byte)'H' && b[14] == (byte)'D' && b[15] == (byte)'R'
                    && ContainsChunk(b, "IDAT")
                    && EndsWith(b, iend);
            case "jpg":
                {
                    // Padding may follow the end-of-image marker; a truncated file has none.
                    var end = b.Length;
                    while (end > 2 && b[end - 1] == 0x00) end--;
                    return end >= 4 && b[end - 2] == 0xFF && b[end - 1] == 0xD9;
                }
            case "gif":
                return b.Length >= 14 && b[b.Length - 1] == 0x3B;
            case "webp":
                return b.Length >= 12 && BitConverter.ToUInt32(b, 4) + 8 == (uint)b.Length;
            case "bmp":
                {
                    // Some writers leave the size field 0; otherwise it must match. Either way the
                    // header's pixel extent must be present (PR #222 review).
                    if (b.Length < 26) return false;
                    var size = BitConverter.ToUInt32(b, 2);
                    if (size != 0 && size != (uint)b.Length) return false;
                    var pixelOffset = BitConverter.ToUInt32(b, 10);
                    var headerSize = BitConverter.ToUInt32(b, 14);
                    if (pixelOffset >= (uint)b.Length) return false;
                    if (headerSize < 40 || b.Length < 14 + 40)
                        return true; // OS/2 core header: the offset check above is all it allows
                    var width = Math.Abs((long)BitConverter.ToInt32(b, 18));
                    var height = Math.Abs((long)BitConverter.ToInt32(b, 22));
                    var bitsPerPixel = BitConverter.ToUInt16(b, 28);
                    var compression = BitConverter.ToUInt32(b, 30);
                    var imageSize = BitConverter.ToUInt32(b, 34);
                    // Uncompressed (BI_RGB, BI_BITFIELDS): rows padded to 4 bytes; else the stated size.
                    long pixelBytes = compression is 0 or 3
                        ? ((bitsPerPixel * width + 31) / 32) * 4 * height
                        : imageSize;
                    return pixelBytes > 0 && pixelOffset + pixelBytes <= b.Length;
                }
            default:
                return true;
        }
    }

    private static bool ContainsChunk(byte[] b, string type)
    {
        for (var i = 8; i + 8 <= b.Length; i++)
            if (b[i + 4] == type[0] && b[i + 5] == type[1] && b[i + 6] == type[2] && b[i + 7] == type[3])
                return true;
        return false;
    }

    private static bool EndsWith(byte[] b, byte[] tail)
    {
        if (b.Length < tail.Length) return false;
        for (var i = 0; i < tail.Length; i++)
            if (b[b.Length - tail.Length + i] != tail[i]) return false;
        return true;
    }

    private static string? SniffImageExtension(byte[] b)
    {
        if (b.Length >= 8 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47
            && b[4] == 0x0D && b[5] == 0x0A && b[6] == 0x1A && b[7] == 0x0A) return "png";
        if (b.Length >= 3 && b[0] == 0xFF && b[1] == 0xD8 && b[2] == 0xFF) return "jpg";
        if (b.Length >= 4 && b[0] == (byte)'G' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'8') return "gif";
        if (b.Length >= 12 && b[0] == (byte)'R' && b[1] == (byte)'I' && b[2] == (byte)'F' && b[3] == (byte)'F'
            && b[8] == (byte)'W' && b[9] == (byte)'E' && b[10] == (byte)'B' && b[11] == (byte)'P') return "webp";
        if (b.Length >= 2 && b[0] == (byte)'B' && b[1] == (byte)'M') return "bmp";
        return null;
    }

    private static string ExtensionFor(string mediaType) => mediaType.ToLowerInvariant() switch
    {
        "image/png" => "png",
        "image/jpeg" => "jpg",
        "image/gif" => "gif",
        "image/webp" => "webp",
        "image/svg+xml" => "svg",
        "image/bmp" => "bmp",
        _ => "bin",
    };
}
