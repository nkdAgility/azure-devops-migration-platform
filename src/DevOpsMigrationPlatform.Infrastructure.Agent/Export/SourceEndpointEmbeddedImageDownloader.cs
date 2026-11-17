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
    private readonly ISourceEndpointInfo _source;
    private readonly ILogger<SourceEndpointEmbeddedImageDownloader> _logger;
    private readonly Func<HttpMessageHandler>? _handlerFactory;

    public SourceEndpointEmbeddedImageDownloader(
        ISourceEndpointInfo source,
        ILogger<SourceEndpointEmbeddedImageDownloader> logger,
        Func<HttpMessageHandler>? handlerFactory = null)
    {
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _handlerFactory = handlerFactory;
    }

    /// <inheritdoc/>
    public async Task<EmbeddedImageDownloadResult?> TryDownloadAsync(string imageUrl, CancellationToken cancellationToken)
    {
        if (!IsOnSourceEndpoint(imageUrl))
            return null;

        var token = _source.ToOrganisationEndpoint().Authentication?.ResolvedAccessToken;
        try
        {
            // Redirects are followed here, not by the handler, so every hop is checked against the
            // source endpoint and credentials never reach another host.
            var handler = _handlerFactory?.Invoke()
                ?? new HttpClientHandler { UseDefaultCredentials = string.IsNullOrEmpty(token), AllowAutoRedirect = false };
            using var client = new HttpClient(handler, disposeHandler: _handlerFactory is null);

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
                        return null;
                    }
                    url = next.AbsoluteUri;
                    continue;
                }

                var mediaType = response.Content?.Headers.ContentType?.MediaType;
                var bytes = response.IsSuccessStatusCode && response.Content is not null
                    ? await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false)
                    : null;
                var extension = bytes is null ? null : ImageExtension(mediaType, bytes);
                if (extension is null)
                {
                    using (DataClassificationScope.Begin(DataClassification.Customer))
                        _logger.LogWarning("Embedded image {Url} not downloaded: {Status} {MediaType}.", imageUrl, (int)response.StatusCode, mediaType);
                    return null;
                }

                return new EmbeddedImageDownloadResult { Bytes = bytes!, Extension = extension };
            }

            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogWarning("Embedded image {Url} not downloaded: too many redirects.", imageUrl);
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            using (DataClassificationScope.Begin(DataClassification.Customer))
                _logger.LogWarning(ex, "Embedded image {Url} could not be downloaded.", imageUrl);
            return null;
        }
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
        if (mediaType is not null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            return ExtensionFor(mediaType);
        if (mediaType is not null && !string.Equals(mediaType, "application/octet-stream", StringComparison.OrdinalIgnoreCase))
            return null;
        return SniffImageExtension(bytes);
    }

    private static string? SniffImageExtension(byte[] b)
    {
        if (b.Length >= 4 && b[0] == 0x89 && b[1] == 0x50 && b[2] == 0x4E && b[3] == 0x47) return "png";
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
