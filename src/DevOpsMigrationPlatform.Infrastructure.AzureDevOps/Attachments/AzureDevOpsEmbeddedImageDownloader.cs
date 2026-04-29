using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Polly;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Attachments;

/// <summary>
/// Downloads embedded images from Azure DevOps URLs using Polly resilience patterns.
/// </summary>
internal class AzureDevOpsEmbeddedImageDownloader : IEmbeddedImageDownloader
{
    private readonly HttpClient _httpClient;
    private readonly IAsyncPolicy<HttpResponseMessage> _resiliencePipeline;
    private readonly ILogger<AzureDevOpsEmbeddedImageDownloader> _logger;

    internal AzureDevOpsEmbeddedImageDownloader(
        HttpClient httpClient,
        ILogger<AzureDevOpsEmbeddedImageDownloader> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        // Build resilience pipeline: 3 retries with exponential backoff
        _resiliencePipeline = Policy
            .Handle<HttpRequestException>()
            .Or<TimeoutException>()
            .OrResult<HttpResponseMessage>(r => !r.IsSuccessStatusCode)
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt =>
                    TimeSpan.FromSeconds(Math.Pow(2, attempt - 1)),
                onRetry: (outcome, timespan, retryCount, context) =>
                    _logger.LogWarning(
                        "Image download retry {retryCount} after {delay}ms",
                        retryCount, timespan.TotalMilliseconds));
    }

    public async Task<EmbeddedImageDownloadResult?> TryDownloadAsync(
        string imageUrl,
        CancellationToken cancellationToken)
    {
        try
        {
            // Validate URL is ADO-hosted
            if (!IsAzureDevOpsUrl(imageUrl))
            {
                _logger.LogWarning("Skipping non-ADO URL: {url}", imageUrl);
                return null;
            }

            // Download with resilience
            var response = await _resiliencePipeline.ExecuteAsync(
                ct => _httpClient.GetAsync(imageUrl, HttpCompletionOption.ResponseContentRead, ct),
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to download image {url}: {statusCode}", imageUrl, response.StatusCode);
                response.Dispose();
                return null;
            }

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            var extension = InferExtensionFromContentType(response.Content.Headers.ContentType?.MediaType);

            response.Dispose();

            return new EmbeddedImageDownloadResult
            {
                Bytes = bytes,
                Extension = extension,
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Error downloading image {url}", imageUrl);
            return null;
        }
    }

    private static bool IsAzureDevOpsUrl(string url)
    {
        return url.Contains("dev.azure.com") || url.Contains("visualstudio.com");
    }

    private static string InferExtensionFromContentType(string? contentType)
    {
        return contentType switch
        {
            "image/png" => "png",
            "image/jpeg" => "jpg",
            "image/gif" => "gif",
            "image/webp" => "webp",
            "image/svg+xml" => "svg",
            _ => "bin",
        };
    }
}
