using System;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

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
#pragma warning disable CS0618 // Obsolete — retained until all call sites migrate to IMigrationMetrics
    private readonly IAttachmentDownloadMetrics _metrics;

    public TfsAttachmentDownloader(
        WorkItemStore store,
        ILogger<TfsAttachmentDownloader> logger,
        IAttachmentDownloadMetrics metrics)
#pragma warning restore CS0618
    {
        _workItemServer = store.TeamProjectCollection.GetService<WorkItemServer>();
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public AttachmentDownloadResult DownloadAttachment(int attachmentId)
    {
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
