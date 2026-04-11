using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Export;

/// <summary>
/// Drives the streaming export loop for work item revisions.
/// Receives revisions from <see cref="IWorkItemRevisionSource"/>, writes each one to the
/// package via <see cref="IArtefactStore"/>, then advances the cursor via
/// <see cref="ICheckpointingService"/>. All revisions are processed one at a time — no buffering.
/// When an <see cref="IAttachmentBinarySource"/> is supplied, each attachment binary is
/// downloaded and stored beside <c>revision.json</c> in the same revision folder.
/// 
/// After all revisions for a work item are written, calls <see cref="IWorkItemCommentExportService"/>
/// to export any comments.
/// </summary>
public sealed class WorkItemExportOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly IArtefactStore _artefactStore;
    private readonly ICheckpointingService _checkpointingService;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IProgressSink? _progressSink;
    private readonly IWorkItemCommentExportService? _commentExportService;
    private readonly string? _organisationUrl;
    private readonly string? _project;
    private readonly string? _pat;

    public WorkItemExportOrchestrator(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointingService,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IProgressSink? progressSink = null,
        IWorkItemCommentExportService? commentExportService = null,
        string? organisationUrl = null,
        string? project = null,
        string? pat = null)
    {
        _artefactStore = artefactStore;
        _checkpointingService = checkpointingService;
        _attachmentBinarySource = attachmentBinarySource;
        _progressSink = progressSink;
        _commentExportService = commentExportService;
        _organisationUrl = organisationUrl;
        _project = project;
        _pat = pat;
    }

    /// <summary>
    /// Streams revisions from <paramref name="source"/>, skips any already covered by the cursor,
    /// serialises each revision to revision.json, downloads attachment binaries when a source is
    /// available, and advances the cursor after every write.
    /// When work item ID changes, exports comments for the previous work item before moving on.
    /// </summary>
    public async Task ExportAsync(
        IWorkItemRevisionSource source,
        CancellationToken cancellationToken)
    {
        var cursor = await _checkpointingService
            .ReadCursorAsync("WorkItems", cancellationToken)
            .ConfigureAwait(false);

        int workItemsProcessed = 0;
        int revisionsProcessed = 0;
        int lastWorkItemId = 0;

        await foreach (var revision in source.GetRevisionsAsync(cancellationToken))
        {
            var folderPath = BuildFolderPath(revision.WorkItemId, revision.RevisionIndex, revision.ChangedDate);

            // Skip all revisions at or before the cursor (resume logic).
            if (cursor != null &&
                string.Compare(folderPath, cursor.LastProcessed, StringComparison.Ordinal) <= 0)
            {
                continue;
            }

            // When we transition to a new work item ID, export comments for the previous one.
            if (lastWorkItemId > 0 && revision.WorkItemId != lastWorkItemId)
            {
                if (_commentExportService != null &&
                    !string.IsNullOrEmpty(_organisationUrl) &&
                    !string.IsNullOrEmpty(_project) &&
                    _pat != null)
                {
                    await _commentExportService.ExportAsync(
                        lastWorkItemId, _organisationUrl, _project, _pat, cancellationToken)
                        .ConfigureAwait(false);
                }
            }

            // Write revision.json.
            var json = JsonSerializer.Serialize(revision, JsonOptions);
            await _artefactStore.WriteAsync($"{folderPath}revision.json", json, cancellationToken).ConfigureAwait(false);

            revisionsProcessed++;
            if (revision.WorkItemId != lastWorkItemId)
            {
                // Emit once per work item (not per revision) to avoid flooding the channel.
                workItemsProcessed++;
                lastWorkItemId = revision.WorkItemId;

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = "Export",
                    LastProcessed = folderPath,
                    WorkItemId = revision.WorkItemId,
                    WorkItemsProcessed = workItemsProcessed,
                    RevisionsProcessed = revisionsProcessed,
                    Message = $"[WorkItems] {workItemsProcessed} work items / {revisionsProcessed} revisions written"
                });
            }

            // Write attachment binaries beside revision.json when a binary source is available.
            if (_attachmentBinarySource != null)
            {
                foreach (var attachment in revision.Attachments)
                {
                    var bytes = await _attachmentBinarySource
                        .GetBytesAsync(revision.WorkItemId, revision.RevisionIndex, attachment, cancellationToken)
                        .ConfigureAwait(false);

                    if (bytes != null)
                    {
                        await _artefactStore
                            .WriteBinaryAsync($"{folderPath}{attachment.RelativePath}", bytes, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
            }

            var newCursor = new CursorEntry
            {
                LastProcessed = folderPath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            await _checkpointingService
                .WriteCursorAsync("WorkItems", newCursor, cancellationToken)
                .ConfigureAwait(false);
        }

        // After all revisions are processed, export comments for the last work item.
        if (_commentExportService != null && lastWorkItemId > 0 &&
            !string.IsNullOrEmpty(_organisationUrl) &&
            !string.IsNullOrEmpty(_project) &&
            _pat != null)
        {
            await _commentExportService.ExportAsync(
                lastWorkItemId, _organisationUrl, _project, _pat, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Builds the canonical folder path for a revision.
    /// Format: WorkItems/yyyy-MM-dd/&lt;ticks&gt;-&lt;workItemId&gt;-&lt;revisionIndex&gt;/
    /// </summary>
    public static string BuildFolderPath(int workItemId, int revisionIndex, DateTimeOffset changedDate)
    {
        var date = changedDate.ToString("yyyy-MM-dd");
        var ticks = changedDate.Ticks.ToString("D20");
        return $"WorkItems/{date}/{ticks}-{workItemId}-{revisionIndex}/";
    }
}

