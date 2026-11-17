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

    private readonly ISourceEndpointInfo _source;
    private readonly ILogger<SourceEndpointEmbeddedImageDownloader> _logger;
    private readonly Func<HttpMessageHandler>? _handlerFactory;
    private readonly Func<int, TimeSpan> _retryDelay;

    public SourceEndpointEmbeddedImageDownloader(
        ISourceEndpointInfo source,
        ILogger<SourceEndpointEmbeddedImageDownloader> logger,
        Func<HttpMessageHandler>? handlerFactory = null,
        Func<int, TimeSpan>? retryDelay = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handlerFactory = handlerFactory;
        _retryDelay = retryDelay ?? (attempt => TimeSpan.FromSeconds(2 * attempt));
    }

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
            try
            {
                var (result, temporaryFailure) = await DownloadOnceAsync(client, imageUrl, token, cancellationToken).ConfigureAwait(false);
                if (temporaryFailure is null)
                    return result;
                lastFailure = temporaryFailure;
                lastException = null;
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
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

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
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

            var mediaType = response.Content?.Headers.ContentType?.MediaType;
            var bytes = response.IsSuccessStatusCode && response.Content is not null
                ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
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

    private static bool IsTemporary(System.Net.HttpStatusCode status) => (int)status is 408 or 429 or >= 500;

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
                    && EndsWith(b, iend);
            case "jpg":
                return b.Length >= 4 && b[b.Length - 2] == 0xFF && b[b.Length - 1] == 0xD9;
            case "gif":
                return b.Length >= 14 && b[b.Length - 1] == 0x3B;
            case "webp":
                return b.Length >= 12 && BitConverter.ToUInt32(b, 4) + 8 == (uint)b.Length;
            case "bmp":
                return b.Length >= 14 && BitConverter.ToUInt32(b, 2) == (uint)b.Length;
            default:
                return true;
        }
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
