using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Attachments;

public interface ITfsAttachmentDownloader
{
    AttachmentDownloadResult DownloadAttachment(int attachmentId);
}

/// <summary>
/// Downloads attachment binaries from TFS using the <see cref="WorkItemServer"/> proxy.
/// </summary>
public class TfsAttachmentDownloader : ITfsAttachmentDownloader
{
    private readonly WorkItemServer _workItemServer;
    private readonly ILogger<TfsAttachmentDownloader> _logger;
    private readonly IAttachmentDownloadMetrics _metrics;

    public TfsAttachmentDownloader(
        WorkItemStore store,
        ILogger<TfsAttachmentDownloader> logger,
        IAttachmentDownloadMetrics metrics)
    {
        _workItemServer = store.TeamProjectCollection.GetService<WorkItemServer>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public AttachmentDownloadResult DownloadAttachment(int attachmentId)
    {
        using var _dc = DataClassificationScope.Begin(DataClassification.Customer);
        _metrics.RecordAttempt();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            _logger.LogDebug("Downloading attachment {AttachmentId}", attachmentId);
            var path = _workItemServer.DownloadFile(attachmentId);
            stopwatch.Stop();
            _metrics.RecordSuccess();
            _metrics.RecordDuration(stopwatch.Elapsed);
            _logger.LogDebug("Downloaded attachment {AttachmentId} to {Path}", attachmentId, path);
            return AttachmentDownloadResult.Succeeded(path);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _metrics.RecordFailure();
            _metrics.RecordDuration(stopwatch.Elapsed);
            _logger.LogError(ex, "Failed to download attachment {AttachmentId}", attachmentId);
            return AttachmentDownloadResult.Failed(ex);
        }
    }
}
