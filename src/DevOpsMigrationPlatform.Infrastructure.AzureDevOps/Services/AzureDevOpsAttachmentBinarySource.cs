using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps REST implementation of <see cref="IAttachmentBinarySource"/>.
/// Looks up the download URL from <see cref="AzureDevOpsAttachmentRegistry"/> (populated by
/// <see cref="AzureDevOpsWorkItemRevisionSource"/>) and fetches the binary via HTTP.
/// </summary>
public sealed class AzureDevOpsAttachmentBinarySource : IAttachmentBinarySource
{
    private readonly AzureDevOpsAttachmentRegistry _registry;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly string _pat;
    private readonly ILogger<AzureDevOpsAttachmentBinarySource> _logger;

    public AzureDevOpsAttachmentBinarySource(
        AzureDevOpsAttachmentRegistry registry,
        IHttpClientFactory httpClientFactory,
        string pat,
        ILogger<AzureDevOpsAttachmentBinarySource> logger)
    {
        _registry          = registry          ?? throw new ArgumentNullException(nameof(registry));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _pat               = pat               ?? throw new ArgumentNullException(nameof(pat));
        _logger            = logger            ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async Task<byte[]?> GetBytesAsync(
        int workItemId,
        int revisionIndex,
        AttachmentMetadata attachment,
        CancellationToken cancellationToken)
    {
        var url = _registry.GetDownloadUrl(workItemId, revisionIndex, attachment.OriginalName);

        if (string.IsNullOrEmpty(url))
        {
            _logger.LogWarning(
                "No download URL registered for attachment {Name} on work item {WorkItemId} revision {RevisionIndex}",
                attachment.OriginalName, workItemId, revisionIndex);
            return null;
        }

        try
        {
            var client = _httpClientFactory.CreateClient("AzureDevOps");

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            // PAT authentication: ":{pat}" base64-encoded as Basic auth.
            var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_pat}"));
            request.Headers.Authorization =
                new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);

            using var response = await client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to download attachment {Name} for work item {WorkItemId} revision {RevisionIndex} from {Url}",
                attachment.OriginalName, workItemId, revisionIndex, url);
            return null;
        }
    }
}
