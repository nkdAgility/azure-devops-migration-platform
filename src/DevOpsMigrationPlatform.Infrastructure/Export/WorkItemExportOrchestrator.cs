using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Export;

/// <summary>
/// Drives the streaming export loop for work item revisions.
/// Receives revisions from <see cref="IWorkItemRevisionSource"/>, writes each one to the
/// package via <see cref="IArtefactStore"/>, then advances the cursor via
/// <see cref="ICheckpointingService"/>. All revisions are processed one at a time — no buffering.
/// When an <see cref="IAttachmentBinarySource"/> is supplied, each attachment binary is
/// downloaded and stored beside <c>revision.json</c> in the same revision folder.
/// For comment edit/delete revisions, inline comment versions are fetched via
/// <see cref="IWorkItemCommentSourceFactory"/> and written as comment.json.
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
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly string? _organisationUrl;
    private readonly string? _project;
    private readonly string? _pat;

    public WorkItemExportOrchestrator(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointingService,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IProgressSink? progressSink = null,
        string? organisationUrl = null,
        string? project = null,
        string? pat = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null)
    {
        _artefactStore = artefactStore;
        _checkpointingService = checkpointingService;
        _attachmentBinarySource = attachmentBinarySource;
        _progressSink = progressSink;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
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



            // Write revision.json.
            var json = JsonSerializer.Serialize(revision, JsonOptions);
            await _artefactStore.WriteAsync($"{folderPath}revision.json", json, cancellationToken).ConfigureAwait(false);

            // For comment edit/delete revisions, fetch the matching comment versions by timestamp
            // and write them as comment.json beside revision.json in the same revision folder.
            // FR-5: Comment API failures are non-fatal — log via progress and continue.
            if (_inlineCommentSourceFactory != null &&
                !string.IsNullOrEmpty(_organisationUrl) &&
                !string.IsNullOrEmpty(_project) &&
                _pat != null &&
                IsCommentEditOrDeleteRevision(revision))
            {
                try
                {
                    var commentSource = _inlineCommentSourceFactory.Create(_organisationUrl, _project, _pat);
                    var matchingComments = new List<WorkItemComment>();

                    await foreach (var comment in commentSource.GetCommentsAsync(
                        revision.WorkItemId, includeDeleted: true, cancellationToken))
                    {
                        var deltaSeconds = Math.Abs((comment.ModifiedDate - revision.ChangedDate).TotalSeconds);
                        if (deltaSeconds <= 1.0)
                            matchingComments.Add(comment);
                    }

                    if (matchingComments.Count > 0)
                    {
                        var commentJson = JsonSerializer.Serialize(matchingComments, JsonOptions);
                        await _artefactStore
                            .WriteAsync($"{folderPath}comment.json", commentJson, cancellationToken)
                            .ConfigureAwait(false);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // Always propagate cancellation.
                }
                catch (Exception ex)
                {
                    // Comment API failures are non-fatal per FR-5: log and continue export.
                    _progressSink?.Emit(new ProgressEvent
                    {
                        Module = "WorkItems",
                        Stage = "Export",
                        LastProcessed = folderPath,
                        WorkItemId = revision.WorkItemId,
                        Message = $"[WorkItems] Warning: inline comment fetch failed for work item {revision.WorkItemId} revision {revision.RevisionIndex}: {ex.Message}"
                    });
                }
            }

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

    }

    /// <summary>
    /// Returns true if the revision represents a comment edit or delete.
    /// Detection criteria: System.CommentCount is present (changed in this revision)
    /// AND System.History is absent or empty (no new comment text was added).
    /// Comment additions have System.History present; edits/deletes do not.
    /// RevisionIndex 0 is always the creation revision and is excluded (all fields
    /// appear as "changed" when previous is null, making CommentCount unreliable).
    /// </summary>
    internal static bool IsCommentEditOrDeleteRevision(WorkItemRevision revision)
    {
        // RevisionIndex 0 is the initial creation revision. Because there is no previous
        // revision, ALL fields are included in the delta — System.CommentCount will always
        // be present regardless of whether any comment action occurred. Skip it.
        if (revision.RevisionIndex == 0)
            return false;

        bool hasHistory = false;
        bool hasCommentCount = false;

        foreach (var field in revision.Fields)
        {
            if (field.ReferenceName == "System.History" && !string.IsNullOrEmpty(field.Value))
                hasHistory = true;
            if (field.ReferenceName == "System.CommentCount")
                hasCommentCount = true;
        }

        return hasCommentCount && !hasHistory;
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

