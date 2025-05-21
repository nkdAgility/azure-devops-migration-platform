using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Telemetry;
using System.Diagnostics;

namespace MigrationPlatform.Infrastructure.TfsObjectModel.Services
{
    public interface IAttachmentDownloader
    {
        AttachmentDownloadResult DownloadAttachment(int attachmentId);
    }

    public class TfsAttachmentDownloader : IAttachmentDownloader
    {
        private readonly WorkItemServer _workItemServer;
        private readonly ILogger<TfsAttachmentDownloader> _logger;
        private readonly IAttachmentDownloadMetrics _attachmentDownloadMetrics;

        public TfsAttachmentDownloader(WorkItemStore store, ILogger<TfsAttachmentDownloader> logger, IAttachmentDownloadMetrics attachmentDownloadMetrics)
        {
            _workItemServer = store.TeamProjectCollection.GetService<WorkItemServer>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _attachmentDownloadMetrics = attachmentDownloadMetrics;
        }

        public AttachmentDownloadResult DownloadAttachment(int attachmentId)
        {
            _attachmentDownloadMetrics.RecordAttempt();
            var stopwatch = Stopwatch.StartNew();

            try
            {
                _logger.LogDebug("Attempting to download attachment with ID {AttachmentId}", attachmentId);
                var path = _workItemServer.DownloadFile(attachmentId);
                stopwatch.Stop();
                _attachmentDownloadMetrics.RecordSuccess();
                _attachmentDownloadMetrics.RecordDuration(stopwatch.Elapsed);

                _logger.LogDebug("Successfully downloaded attachment ID {AttachmentId} to path {Path}", attachmentId, path);
                return AttachmentDownloadResult.Succeeded(path);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _attachmentDownloadMetrics.RecordFailure();
                _attachmentDownloadMetrics.RecordDuration(stopwatch.Elapsed);

                _logger.LogError(ex, "Failed to download attachment ID {AttachmentId}", attachmentId);
                return AttachmentDownloadResult.Failed(ex);
            }
        }

    }

}
