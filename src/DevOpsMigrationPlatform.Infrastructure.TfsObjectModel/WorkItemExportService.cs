using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.WorkItemTracking.Client;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Export;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Extensions;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Services;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// Exports work items from TFS by streaming through date-range chunks.
/// Writes revision.json and attachment binaries to <see cref="IArtefactStore"/>.
/// Uses <see cref="IWorkItemWatermarkStore"/> for cursor-based resumability.
/// </summary>
public class WorkItemExportService : IWorkItemExportService
{
    private readonly IArtefactStore _artefactStore;
    private readonly IWorkItemWatermarkStore _watermarkStore;
    private readonly WorkItemStore _workItemStore;
    private readonly IWorkItemRevisionMapper _revisionMapper;
    private readonly IAttachmentDownloader _attachmentDownloader;
    private readonly ILogger<WorkItemExportService> _logger;
    private readonly IWorkItemExportMetrics _metrics;

    public WorkItemExportService(
        IArtefactStore artefactStore,
        IWorkItemWatermarkStore watermarkStore,
        WorkItemStore workItemStore,
        IWorkItemRevisionMapper revisionMapper,
        IAttachmentDownloader attachmentDownloader,
        ILogger<WorkItemExportService> logger,
        IWorkItemExportMetrics metrics)
    {
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _watermarkStore = watermarkStore ?? throw new ArgumentNullException(nameof(watermarkStore));
        _workItemStore = workItemStore ?? throw new ArgumentNullException(nameof(workItemStore));
        _revisionMapper = revisionMapper ?? throw new ArgumentNullException(nameof(revisionMapper));
        _attachmentDownloader = attachmentDownloader ?? throw new ArgumentNullException(nameof(attachmentDownloader));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _metrics = metrics ?? throw new ArgumentNullException(nameof(metrics));
    }

    public async IAsyncEnumerable<WorkItemMigrationProgress> ExportWorkItemsAsync(
        string tfsServer,
        string project,
        string wiqlQuery,
        IProgressSink progressSink,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        using var activity = MigrationPlatformActivitySources.WorkItemExport.StartActivity(
            "ExportWorkItemsAsync", ActivityKind.Consumer);
        activity?.SetTag("tfsServer", tfsServer);
        activity?.SetTag("project", project);

        _logger.LogInformation("Starting export for project {Project} on {TfsServer}", project, tfsServer);

        var overallStopwatch = Stopwatch.StartNew();
        var progress = new WorkItemMigrationProgress();
        yield return progress;

        if (string.IsNullOrEmpty(wiqlQuery))
            wiqlQuery = $"SELECT * FROM WorkItems WHERE [System.TeamProject] = '{project}'";

        // Count pass
        var cachedCount = await _watermarkStore.GetQueryCountAsync(wiqlQuery, cancellationToken)
            .ConfigureAwait(false);

        if (cachedCount == null)
        {
            foreach (var countChunk in _workItemStore.QueryCountAllByDateChunk(wiqlQuery, progressSink))
            {
                progress.TotalWorkItems = countChunk.CurrentTotal;
                yield return progress;
            }
            await _watermarkStore.UpdateQueryCountAsync(wiqlQuery, progress.TotalWorkItems, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            progress.TotalWorkItems = cachedCount.Value;
            yield return progress;
        }

        // Export pass
        int workItemCounter = 0;
        long revisionCounter = 0;
        long revisionErrors = 0;
        long linksExported = 0;
        long linkErrors = 0;
        long attachmentsAttempted = 0;
        long attachmentsSucceeded = 0;
        long attachmentsFailed = 0;
        const int snapshotInterval = 100; // matches TelemetryOptions.SubprocessSnapshotRevisionInterval default
        foreach (var chunkItem in _workItemStore.QueryAllByDateChunk(wiqlQuery, progressSink))
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var wiActivity = MigrationPlatformActivitySources.WorkItemExport.StartActivity(
                "ProcessWorkItem", ActivityKind.Internal);
            wiActivity?.SetTag("workItem.id", chunkItem.WorkItem.Id);

            var workItemStopwatch = Stopwatch.StartNew();
            var tfsWorkItem = chunkItem.WorkItem;
            var collectionId = tfsWorkItem.Store.TeamProjectCollection.InstanceId;

            progress.WorkItemId = tfsWorkItem.Id;
            progress.ChunkInfo = chunkItem;
            progress.WorkItemsProcessed++;
            workItemCounter++;

            if (workItemCounter % 100 == 0)
                _logger.LogInformation("Processed {Count} work items so far", workItemCounter);

            _logger.LogInformation("Exporting work item {WorkItemId}", tfsWorkItem.Id);
            _metrics.RecordWorkItemExported(collectionId);

            // Resume: skip this work item entirely if watermark is already at or beyond latest revision
            var watermark = await _watermarkStore.GetWatermarkAsync(tfsWorkItem.Id, cancellationToken)
                .ConfigureAwait(false);

            if (watermark.HasValue && watermark.Value + 1 >= tfsWorkItem.Revision)
            {
                _logger.LogDebug("Skipping already-exported work item {WorkItemId}", tfsWorkItem.Id);
                progress.RevisionsProcessed += tfsWorkItem.Revision;
                workItemStopwatch.Stop();
                _metrics.RecordWorkItemProcessingDuration(collectionId, workItemStopwatch.Elapsed);
                yield return progress;
                continue;
            }

            Revision? previousRevision = null;
            foreach (Revision tfsRevision in tfsWorkItem.Revisions)
            {
                cancellationToken.ThrowIfCancellationRequested();

                bool alreadyProcessed = await _watermarkStore
                    .IsRevisionProcessedAsync(tfsWorkItem.Id, tfsRevision.Index, cancellationToken)
                    .ConfigureAwait(false);

                if (alreadyProcessed)
                {
                    _logger.LogDebug("Skipping revision {Rev} for work item {WorkItemId}", tfsRevision.Index, tfsWorkItem.Id);
                    progress.RevisionsProcessed++;
                    continue;
                }

                var revisionStopwatch = Stopwatch.StartNew();
                try
                {
                    _logger.LogInformation("Processing revision {Rev} for work item {WorkItemId}", tfsRevision.Index, tfsWorkItem.Id);
                    _metrics.RecordRevisionExported(collectionId, tfsWorkItem.Id);
                    revisionCounter++;
                    progress.RevisionIndex = tfsRevision.Index;

                    var mapped = _revisionMapper.Map(tfsWorkItem, tfsRevision, previousRevision);
                    progress.FieldsProcessed += mapped.Fields.Count;

                    // Write revision.json via IArtefactStore
                    var folderPath = WorkItemExportOrchestrator.BuildFolderPath(
                        mapped.WorkItemId, mapped.RevisionIndex, mapped.ChangedDate);
                    var json = JsonSerializer.Serialize(mapped);
                    await _artefactStore.WriteAsync($"{folderPath}revision.json", json, cancellationToken)
                        .ConfigureAwait(false);

                    // Download and store attachments beside revision.json
                    await ExportAttachmentsAsync(tfsRevision, previousRevision, folderPath, progress, cancellationToken)
                        .ConfigureAwait(false);

                    // Advance watermark (checkpoint)
                    await _watermarkStore.UpdateWatermarkAsync(tfsWorkItem.Id, tfsRevision.Index, cancellationToken)
                        .ConfigureAwait(false);

                    progress.RevisionsProcessed++;
                    previousRevision = tfsRevision;

                    // Embed a MetricSnapshot in the progress update every snapshotInterval revisions.
                    if (revisionCounter % snapshotInterval == 0)
                    {
                        progress.Metrics = new MetricSnapshot
                        {
                            Timestamp = DateTimeOffset.UtcNow,
                            WorkItemsExported = workItemCounter,
                            RevisionsExported = revisionCounter,
                            RevisionErrors = revisionErrors,
                            AttachmentsAttempted = progress.AttachmentsProcessed + progress.AttachmentsFailed,
                            AttachmentsSucceeded = progress.AttachmentsProcessed,
                            AttachmentsFailed = progress.AttachmentsFailed,
                        };
                    }
                    else
                    {
                        progress.Metrics = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed revision {Rev} for work item {WorkItemId}", tfsRevision.Index, tfsWorkItem.Id);
                    _metrics.RecordRevisionError(collectionId, tfsWorkItem.Id);
                    revisionErrors++;
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
            yield return progress;
        }

        overallStopwatch.Stop();
        _metrics.RecordProcessingDuration(overallStopwatch.Elapsed);

        var elapsed = overallStopwatch.Elapsed;
        _logger.LogInformation(
            "Completed export of {TotalWorkItems} work items from project {Project} in {Elapsed:hh\\:mm\\:ss}",
            progress.TotalWorkItems, project, elapsed);
    }

    private async System.Threading.Tasks.Task ExportAttachmentsAsync(
        Revision currentRevision,
        Revision? previousRevision,
        string folderPath,
        WorkItemMigrationProgress progress,
        CancellationToken cancellationToken)
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
            if (result.Success && result.FilePath != null)
            {
                try
                {
                    var bytes = File.ReadAllBytes(result.FilePath);
                    var base64 = Convert.ToBase64String(bytes);
                    // Store attachment content alongside revision.json
                    await _artefactStore.WriteAsync(
                        $"{folderPath}{attachment.Name}.b64",
                        base64,
                        cancellationToken).ConfigureAwait(false);
                    progress.AttachmentsProcessed++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to copy attachment {Name}", attachment.Name);
                    progress.AttachmentsFailed++;
                }
            }
            else
            {
                progress.AttachmentsFailed++;
                _logger.LogWarning("Attachment download failed for {Name}: {Error}",
                    attachment.Name, result.Error?.Message);
            }
        }
    }
}
