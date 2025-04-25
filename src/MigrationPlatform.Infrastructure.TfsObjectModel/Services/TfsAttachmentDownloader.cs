using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using Microsoft.TeamFoundation.WorkItemTracking.Proxy;
using MigrationPlatform.Abstractions.Models;

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

        public TfsAttachmentDownloader(WorkItemStore store, ILogger<TfsAttachmentDownloader> logger)
        {
            _workItemServer = store.TeamProjectCollection.GetService<WorkItemServer>();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public AttachmentDownloadResult DownloadAttachment(int attachmentId)
        {
            try
            {
                _logger.LogDebug("Attempting to download attachment with ID {AttachmentId}", attachmentId);
                var path = _workItemServer.DownloadFile(attachmentId);
                _logger.LogInformation("Successfully downloaded attachment ID {AttachmentId} to path {Path}", attachmentId, path);
                return AttachmentDownloadResult.Succeeded(path);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download attachment ID {AttachmentId}", attachmentId);
                return AttachmentDownloadResult.Failed(ex);
            }
        }
    }

}
