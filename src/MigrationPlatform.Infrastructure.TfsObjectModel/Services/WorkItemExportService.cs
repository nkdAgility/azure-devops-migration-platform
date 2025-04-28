using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using MigrationPlatform.Abstractions.Models;
using MigrationPlatform.Abstractions.Repositories;
using MigrationPlatform.Abstractions.Services;
using MigrationPlatform.Abstractions.Telemetry;
using MigrationPlatform.Infrastructure.Telemetry;
using MigrationPlatform.Infrastructure.TfsObjectModel.Extensions;
using MigrationPlatform.Infrastructure.TfsObjectModel.Models;
using MigrationPlatform.Infrastructure.TfsObjectModel.Services;
using System.Diagnostics;

namespace MigrationPlatform.Infrastructure.Services
{
    public class WorkItemExportService : IWorkItemExportService
    {
        private readonly IMigrationRepository _migrationRepository;
        private readonly WorkItemStore _workItemStore;
        private readonly IWorkItemRevisionMapper _workItemRevisionMapper;
        private readonly IAttachmentDownloader _attachmentDownloader;
        private readonly ILogger<TfsAttachmentDownloader> _logger;
        private readonly IWorkItemExportMetrics _metrics;

        public WorkItemExportService(
            IMigrationRepository migrationRepository,
            WorkItemStore store,
            IWorkItemRevisionMapper workItemRevisionMapper,
            IAttachmentDownloader attachmentDownloader,
            ILogger<TfsAttachmentDownloader> logger,
            IWorkItemExportMetrics metrics)
        {
            _migrationRepository = migrationRepository;
            _workItemStore = store;
            _workItemRevisionMapper = workItemRevisionMapper;
            _attachmentDownloader = attachmentDownloader;
            _logger = logger;
            _metrics = metrics;
        }

        public async IAsyncEnumerable<WorkItemMigrationProgress> ExportWorkItemsAsync(string tfsServer, string project, string wiqlQuery)
        {
            using var activity = MigrationPlatformActivitySources.WorkItemExport.StartActivity("ExportWorkItemsAsync", ActivityKind.Consumer);
            activity?.SetTag("tfsServer", tfsServer);
            activity?.SetTag("project", project);
            activity?.SetTag("wiqlQuery", wiqlQuery);

            var overallStopwatch = Stopwatch.StartNew();
            var progressUpdate = new WorkItemMigrationProgress();
            yield return progressUpdate;

            if (string.IsNullOrEmpty(wiqlQuery))
            {
                wiqlQuery = "SELECT * FROM WorkItems WHERE [System.TeamProject] = @project";
            }

            using (var queryActivity = MigrationPlatformActivitySources.WorkItemExport.StartActivity("QueryCount", ActivityKind.Internal))
            {
                var queryCount = _migrationRepository.GetQueryCount(wiqlQuery);
                if (queryCount == null)
                {
                    foreach (WorkItemQueryCountChunk chunk in _workItemStore.QueryCountAllByDateChunk(wiqlQuery))
                    {
                        queryCount = chunk.TotalWorkItems;
                        progressUpdate.TotalWorkItems = chunk.TotalWorkItems;
                        queryActivity?.SetTag("chunk.totalWorkItems", chunk.TotalWorkItems);
                        yield return progressUpdate;
                    }
                }

                progressUpdate.TotalWorkItems = queryCount ?? 0;
                yield return progressUpdate;
            }

            foreach (WorkItemFromChunk chunkItem in _workItemStore.QueryAllByDateChunk(wiqlQuery))
            {
                using var wiActivity = MigrationPlatformActivitySources.WorkItemExport.StartActivity("ProcessWorkItem", ActivityKind.Internal);
                wiActivity?.SetTag("workItem.id", chunkItem.WorkItem.Id);

                var workItemStopwatch = Stopwatch.StartNew();
                var tfsWorkItem = chunkItem.WorkItem;
                progressUpdate.ChunkInfo = (WorkItemQueryChunk)chunkItem;
                progressUpdate.WorkItemId = tfsWorkItem.Id;
                progressUpdate.WorkItemsProcessed++;

                var collectionId = chunkItem.WorkItem.Store.TeamProjectCollection.InstanceId;

                _metrics.RecordWorkItemExported(collectionId);

                if (_migrationRepository.GetWatermark(tfsWorkItem.Id) + 1 == tfsWorkItem.Revision)
                {
                    progressUpdate.RevisionsProcessed += tfsWorkItem.Revision;
                    workItemStopwatch.Stop();
                    _metrics.RecordWorkItemProcessingDuration(collectionId, workItemStopwatch.Elapsed);
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

                    var revisionStopwatch = Stopwatch.StartNew();

                    try
                    {
                        _metrics.RecordRevisionExported(collectionId, tfsWorkItem.Id);
                        progressUpdate.RevisionIndex = tfsRevision.Index;

                        var mappedRevision = _workItemRevisionMapper.Map(tfsWorkItem, tfsRevision, previousTfsRevision);
                        progressUpdate.FieldsProcessed += mappedRevision.Fields.Count;
                        progressUpdate.RevisionsProcessed++;
                        ProcessAttachments(mappedRevision, tfsRevision, previousTfsRevision, progressUpdate);

                        _migrationRepository.AddWorkItemRevision(mappedRevision);
                        previousTfsRevision = tfsRevision;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process revision {RevisionIndex} for work item {WorkItemId}", tfsRevision.Index, tfsWorkItem.Id);
                        _metrics.RecordRevisionError(collectionId, tfsWorkItem.Id);
                        throw;
                    }
                    finally
                    {
                        revisionStopwatch.Stop();
                        _metrics.RecordRevisionProcessingDuration(collectionId, tfsWorkItem.Id, revisionStopwatch.Elapsed);
                    }
                }

                workItemStopwatch.Stop();
                _metrics.RecordWorkItemProcessingDuration(collectionId, workItemStopwatch.Elapsed);

                yield return progressUpdate;
            }

            overallStopwatch.Stop();
            _metrics.RecordProcessingDuration(overallStopwatch.Elapsed);
        }

        private void ProcessAttachments(MigrationWorkItemRevision mappedRevision, Revision currentRevision, Revision? previousRevision, WorkItemMigrationProgress progressUpdate)
        {
            using var activity = MigrationPlatformActivitySources.WorkItemExport.StartActivity("ProcessAttachments", ActivityKind.Internal);
            activity?.SetTag("workItemId", mappedRevision.workItemId);
            activity?.SetTag("currentRevisionIndex", currentRevision.Index);
            activity?.SetTag("previousRevisionIndex", previousRevision?.Index);

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
                    _logger.LogError(result.Error, "Attachment download failed for {AttachmentName} [ID:{AttachmentId}] on {workItemId}", attachment.Name, attachment.Id, mappedRevision.workItemId);
                    progressUpdate.AttachmentsFailed++;
                }
            }
        }
    }
}
