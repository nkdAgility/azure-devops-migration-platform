using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
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
/// Supports streaming download with in-flight SHA-256 calculation.
/// </summary>
public sealed class AzureDevOpsAttachmentBinarySource : IStreamingAttachmentBinarySource
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
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _httpClientFactory = httpClientFactory ?? throw new ArgumentNullException(nameof(httpClientFactory));
        _pat = pat ?? throw new ArgumentNullException(nameof(pat));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
            using var response = await SendAuthenticatedRequestAsync(url, cancellationToken).ConfigureAwait(false);
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

    /// <summary>
    /// Streams an attachment directly to the artefact store, computing SHA-256 in-flight via
    /// <see cref="CryptoStream"/>. Returns the SHA-256 hex digest and byte count, or <c>null</c>
    /// if the download fails.
    /// </summary>
    public async Task<(string Sha256, long Size)?> StreamToStoreAsync(
        int workItemId,
        int revisionIndex,
        AttachmentMetadata attachment,
        IArtefactStore store,
        string targetPath,
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
            using var response = await SendAuthenticatedRequestAsync(url, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            using var networkStream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var sha256 = SHA256.Create();
            using var countingStream = new CountingStream(networkStream);
            using var cryptoStream = new CryptoStream(countingStream, sha256, CryptoStreamMode.Read);

            await store.WriteStreamAsync(targetPath, cryptoStream, cancellationToken).ConfigureAwait(false);

            // CryptoStream must be fully consumed and finalized before reading the hash.
            // WriteStreamAsync copies the full stream, so the hash is now complete.
            if (!cryptoStream.HasFlushedFinalBlock)
                cryptoStream.FlushFinalBlock();

            var hashHex = Convert.ToHexString(sha256.Hash!).ToLowerInvariant();
            return (hashHex, countingStream.BytesRead);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to stream attachment {Name} for work item {WorkItemId} revision {RevisionIndex} from {Url}",
                attachment.OriginalName, workItemId, revisionIndex, url);
            return null;
        }
    }

    private async Task<HttpResponseMessage> SendAuthenticatedRequestAsync(string url, CancellationToken cancellationToken)
    {
        var client = _httpClientFactory.CreateClient("AttachmentDownload");
        using var request = new HttpRequestMessage(HttpMethod.Get, url);

        var encoded = Convert.ToBase64String(Encoding.ASCII.GetBytes($":{_pat}"));
        request.Headers.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Basic", encoded);

        return await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// A passthrough stream that counts bytes read through it.
    /// </summary>
    private sealed class CountingStream : Stream
    {
        private readonly Stream _inner;
        public long BytesRead { get; private set; }

        public CountingStream(Stream inner) => _inner = inner;

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _inner.Length;
        public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var bytesRead = _inner.Read(buffer, offset, count);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var bytesRead = await _inner.ReadAsync(buffer, offset, count, cancellationToken).ConfigureAwait(false);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var bytesRead = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
            BytesRead += bytesRead;
            return bytesRead;
        }

        public override void Flush() => _inner.Flush();
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) _inner.Dispose();
            base.Dispose(disposing);
        }
    }
}
