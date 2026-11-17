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
            var handler = _handlerFactory?.Invoke()
                ?? new HttpClientHandler { UseDefaultCredentials = string.IsNullOrEmpty(token) };
            using var client = new HttpClient(handler, disposeHandler: _handlerFactory is null);
            using var request = new HttpRequestMessage(HttpMethod.Get, imageUrl);
            if (!string.IsNullOrEmpty(token))
                request.Headers.Authorization = new AuthenticationHeaderValue(
                    "Basic", Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + token)));

            using var response = await client.SendAsync(request, cancellationToken).ConfigureAwait(false);
            var mediaType = response.Content?.Headers.ContentType?.MediaType;
            if (!response.IsSuccessStatusCode || mediaType is null || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
            {
                using (DataClassificationScope.Begin(DataClassification.Customer))
                    _logger.LogWarning("Embedded image {Url} not downloaded: {Status} {MediaType}.", imageUrl, (int)response.StatusCode, mediaType);
                return null;
            }

            var bytes = await response.Content!.ReadAsByteArrayAsync().ConfigureAwait(false);
            return new EmbeddedImageDownloadResult { Bytes = bytes, Extension = ExtensionFor(mediaType) };
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
