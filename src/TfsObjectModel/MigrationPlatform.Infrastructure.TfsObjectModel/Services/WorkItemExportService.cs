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

            _logger.LogInformation("Starting work item export for project {Project} on server {TfsServer}", project, tfsServer);

            var overallStopwatch = Stopwatch.StartNew();
            var progressUpdate = new WorkItemMigrationProgress();
            yield return progressUpdate;

            if (string.IsNullOrEmpty(wiqlQuery))
            {
                _logger.LogWarning("No WIQL query provided; using default query for project {Project}", project);
                wiqlQuery = "SELECT * FROM WorkItems WHERE [System.TeamProject] = @project";
            }

            using (var queryActivity = MigrationPlatformActivitySources.WorkItemExport.StartActivity("QueryCount", ActivityKind.Internal))
            {
                _logger.LogInformation("Querying total work item count for project {Project}", project);

                var queryCount = _migrationRepository.GetQueryCount(wiqlQuery);
                if (queryCount == null)
                {
                    _logger.LogInformation("Work item count not cached; querying by date chunks...");
                    foreach (WorkItemQueryCountChunk chunk in _workItemStore.QueryCountAllByDateChunk(wiqlQuery))
                    {
                        queryCount = chunk.CurrentTotal;
                        progressUpdate.TotalWorkItems = chunk.CurrentTotal;
                        queryActivity?.SetTag("chunk.totalWorkItems", chunk.CurrentTotal);
                        _logger.LogDebug("Chunk count received: {ChunkWorkItemCount} work items", chunk.CurrentTotal);
                        yield return progressUpdate;
                    }
                }

                progressUpdate.TotalWorkItems = queryCount ?? 0;
                _logger.LogInformation("Total work items to export: {TotalWorkItems}", progressUpdate.TotalWorkItems);
                yield return progressUpdate;
            }

            int workItemCounter = 0;

            foreach (WorkItemFromChunk chunkItem in _workItemStore.QueryAllByDateChunk(wiqlQuery))
            {
                using var wiActivity = MigrationPlatformActivitySources.WorkItemExport.StartActivity("ProcessWorkItem", ActivityKind.Internal);
                wiActivity?.SetTag("workItem.id", chunkItem.WorkItem.Id);



                var workItemStopwatch = Stopwatch.StartNew();
                var tfsWorkItem = chunkItem.WorkItem;
                progressUpdate.ChunkInfo = (WorkItemQueryChunk)chunkItem;
                progressUpdate.WorkItemId = tfsWorkItem.Id;
                progressUpdate.WorkItemsProcessed++;

                workItemCounter++;
                if (workItemCounter % 100 == 0)
                {
                    _logger.LogInformation("Processed {WorkItemsProcessed} work items so far", workItemCounter);
                }

                _logger.LogInformation("Exporting: {WorkItemId}", chunkItem.WorkItem.Id);

                var collectionId = chunkItem.WorkItem.Store.TeamProjectCollection.InstanceId;

                _metrics.RecordWorkItemExported(collectionId);

                if (_migrationRepository.GetWatermark(tfsWorkItem.Id) + 1 == tfsWorkItem.Revision)
                {
                    _logger.LogDebug("Skipping revisions for unchanged work item {WorkItemId}", tfsWorkItem.Id);

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
                        _logger.LogDebug("Skipping already processed revision {RevisionIndex} for work item {WorkItemId}", tfsRevision.Index, tfsWorkItem.Id);
                        progressUpdate.RevisionsProcessed++;
                        continue;
                    }

                    var revisionStopwatch = Stopwatch.StartNew();

                    try
                    {
                        _logger.LogInformation("Processing revision {RevisionIndex} for work item {WorkItemId}", tfsRevision.Index, tfsWorkItem.Id);

                        _metrics.RecordRevisionExported(collectionId, tfsWorkItem.Id);
                        progressUpdate.RevisionIndex = tfsRevision.Index;

                        var mappedRevision = _workItemRevisionMapper.Map(tfsWorkItem, tfsRevision, previousTfsRevision);
                        progressUpdate.FieldsProcessed += mappedRevision.Fields.Count;
                        progressUpdate.RevisionsProcessed++;
                        //ProcessAttachments(mappedRevision, tfsRevision, previousTfsRevision, progressUpdate);

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

            TimeSpan elapsed = overallStopwatch.Elapsed;
            string readable = string.Format("{0:D2}:{1:D2}:{2:D2}.{3:D3}",
                elapsed.Hours, elapsed.Minutes, elapsed.Seconds, elapsed.Milliseconds);

            _logger.LogInformation("Completed export of {TotalWorkItems} work items from project {Project} in {time}", progressUpdate.TotalWorkItems, project, readable);
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
