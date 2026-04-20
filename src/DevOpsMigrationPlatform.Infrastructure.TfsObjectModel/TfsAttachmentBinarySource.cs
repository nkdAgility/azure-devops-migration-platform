using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// TFS Object Model implementation of <see cref="IAttachmentBinarySource"/>.
/// Resolves the TFS integer attachment ID from <see cref="TfsAttachmentRegistry"/>,
/// delegates to <see cref="ITfsAttachmentDownloader"/> for the binary download,
/// and returns raw bytes — no base64 encoding.
/// </summary>
public sealed class TfsAttachmentBinarySource : IAttachmentBinarySource
{
    private readonly ITfsAttachmentDownloader _downloader;
    private readonly TfsAttachmentRegistry _registry;
    private readonly ILogger<TfsAttachmentBinarySource> _logger;

    public TfsAttachmentBinarySource(
        ITfsAttachmentDownloader downloader,
        TfsAttachmentRegistry registry,
        ILogger<TfsAttachmentBinarySource> logger)
    {
        _downloader = downloader ?? throw new ArgumentNullException(nameof(downloader));
        _registry = registry ?? throw new ArgumentNullException(nameof(registry));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public Task<byte[]?> GetBytesAsync(
        int workItemId,
        int revisionIndex,
        AttachmentMetadata attachment,
        CancellationToken cancellationToken)
    {
        using var _dc = DataClassificationScope.Begin(DataClassification.Customer);
        if (!_registry.TryGet(workItemId, revisionIndex, attachment.OriginalName, out var tfsAttachmentId))
        {
            _logger.LogWarning(
                "No TFS attachment ID registered for work item {WorkItemId} revision {RevisionIndex} attachment '{Name}'",
                workItemId, revisionIndex, attachment.OriginalName);
            return Task.FromResult<byte[]?>(null);
        }

        var result = _downloader.DownloadAttachment(tfsAttachmentId);

        if (!result.Success || result.FilePath is null)
        {
            _logger.LogWarning(
                "Attachment download failed for work item {WorkItemId} revision {RevisionIndex} attachment '{Name}': {Error}",
                workItemId, revisionIndex, attachment.OriginalName, result.Error?.Message);
            return Task.FromResult<byte[]?>(null);
        }

        try
        {
            var bytes = File.ReadAllBytes(result.FilePath);
            return Task.FromResult<byte[]?>(bytes);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to read downloaded file for work item {WorkItemId} revision {RevisionIndex} attachment '{Name}'",
                workItemId, revisionIndex, attachment.OriginalName);
            return Task.FromResult<byte[]?>(null);
        }
    }
}
