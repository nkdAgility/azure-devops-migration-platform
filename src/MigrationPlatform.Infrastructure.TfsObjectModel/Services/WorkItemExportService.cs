using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Infrastructure.TfsObjectModel.Extensions;
using MigrationPlatform.Infrastructure.TfsObjectModel.Models;
using MigrationPlatform.Infrastructure.TfsObjectModel.Services;

namespace MigrationPlatform.Infrastructure.Services
{
    public class WorkItemExportService : IWorkItemExportService
    {
        private readonly IMigrationRepository _migrationRepository;
        private readonly WorkItemStore _workItemStore;
        private readonly IWorkItemRevisionMapper _workItemRevisionMapper;
        private readonly IAttachmentDownloader _attachmentDownloader;
        private readonly ILogger<TfsAttachmentDownloader> _logger;

        public WorkItemExportService(
            IMigrationRepository migrationRepository,
            WorkItemStore store,
            IWorkItemRevisionMapper workItemRevisionMapper, IAttachmentDownloader attachmentDownloader, ILogger<TfsAttachmentDownloader> logger)
        {
            _migrationRepository = migrationRepository;
            _workItemStore = store;
            _workItemRevisionMapper = workItemRevisionMapper;
            _attachmentDownloader = attachmentDownloader;
            _logger = logger;
        }

        public async IAsyncEnumerable<WorkItemMigrationProgress> ExportWorkItemsAsync(string tfsServer, string project, string wiqlQuery)
        {
            var progressUpdate = new WorkItemMigrationProgress();
            yield return progressUpdate;

            if (string.IsNullOrEmpty(wiqlQuery))
            {
                wiqlQuery = "SELECT * FROM WorkItems WHERE [System.TeamProject] = @project";
            }



            var queryCount = _migrationRepository.GetQueryCount(wiqlQuery);
            if (queryCount == null)
            {
                foreach (WorkItemQueryCountChunk chunk in _workItemStore.QueryCountAllByDateChunk(wiqlQuery))
                {
                    queryCount = chunk.TotalWorkItems;
                    progressUpdate.TotalWorkItems = chunk.TotalWorkItems;
                    yield return progressUpdate;
                }
                // _migrationRepository.UpdateQueryCount(query, queryCount ?? 0);
            }

            progressUpdate.TotalWorkItems = queryCount ?? 0;
            yield return progressUpdate;

            foreach (WorkItemFromChunk chunkItem in _workItemStore.QueryAllByDateChunk(wiqlQuery))
            {
                var tfsWorkItem = chunkItem.WorkItem;
                progressUpdate.ChunkInfo = (WorkItemQueryChunk)chunkItem;
                progressUpdate.WorkItemId = tfsWorkItem.Id;
                progressUpdate.WorkItemsProcessed++;

                if (_migrationRepository.GetWatermark(tfsWorkItem.Id) + 1 == tfsWorkItem.Revision)
                {
                    progressUpdate.RevisionsProcessed += tfsWorkItem.Revision;
                    yield return progressUpdate;
                    continue;
                }

                Revision? previousTfsRevision = null;
                foreach (Revision tfsRevision in tfsWorkItem.Revisions)
                {
                    if (_migrationRepository.IsRevisionProcessed(tfsWorkItem.Id, tfsRevision.Index))
                    {
                        progressUpdate.RevisionsProcessed++;
                        continue;
                    }

                    progressUpdate.RevisionIndex = tfsRevision.Index;

                    var mappedRevision = _workItemRevisionMapper.Map(tfsWorkItem, tfsRevision, previousTfsRevision);
                    progressUpdate.FieldsProcessed += mappedRevision.Fields.Count;
                    progressUpdate.RevisionsProcessed++;
                    ProcessAttachments(mappedRevision, tfsRevision, previousTfsRevision, progressUpdate);

                    _migrationRepository.AddWorkItemRevision(mappedRevision);
                    previousTfsRevision = tfsRevision;
                }

                yield return progressUpdate;
            }
        }

        private void ProcessAttachments(MigrationWorkItemRevision mappedRevision, Revision currentRevision, Revision? previousRevision, WorkItemMigrationProgress progressUpdate)
        {
            var newAttachments = currentRevision.Attachments
                .Cast<Attachment>()
                .Where(a =>
                    previousRevision == null ||
                    !previousRevision.Attachments.Cast<Attachment>().Any(prev => prev.Name == a.Name))
                .ToList();

            foreach (var attachment in newAttachments)
            {
                var result = _attachmentDownloader.DownloadAttachment(attachment.Id);

                if (result.Success)
                {
                    _migrationRepository.AddWorkItemRevisionAttachment(mappedRevision, attachment.Name, result.FilePath);
                    mappedRevision.Attachments.Add(new MigrationWorkItemAttachment(attachment.Name, attachment.Comment));
                    progressUpdate.AttachmentsProcessed++;
                }
                else
                {
                    _logger.LogError(result.Error, "Attachment download failed for {AttachmentName} [ID:{AttachmentId}] on {workItemId}", attachment.Id, attachment.Name, mappedRevision.workItemId);
                    // Maybe retry or handle it differently
                    progressUpdate.AttachmentsFailed++;
                }

            }
        }

    }
}
