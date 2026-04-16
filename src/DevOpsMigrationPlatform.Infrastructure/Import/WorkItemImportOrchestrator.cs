#if !NET481
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Infrastructure.Checkpointing;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Import;

/// <summary>
/// Streaming import loop for work item revision and comment folders.
/// Enumerates <c>WorkItems/</c> via <see cref="IArtefactStore.EnumerateAsync"/> in strict
/// lexicographic order, resumes from the last cursor, and delegates each folder to
/// <see cref="RevisionFolderProcessor"/> (revision folders) or the comment handler (comment folders).
///
/// Memory guarantee: processes one folder at a time; no in-memory list accumulation.
/// </summary>
public sealed class WorkItemImportOrchestrator
{
    private static readonly System.Text.Json.JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IArtefactStore _artefactStore;
    private readonly ICheckpointingService _checkpointing;
    private readonly IProgressSink _progressSink;
    private readonly IWorkItemResolutionStrategy _resolutionStrategy;
    private readonly IIdMapStore _idMapStore;
    private readonly IRevisionFolderProcessor _processor;
    private readonly IWorkItemImportTarget _target;
    private readonly ILogger<WorkItemImportOrchestrator> _logger;

    public WorkItemImportOrchestrator(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointing,
        IProgressSink progressSink,
        IWorkItemResolutionStrategy resolutionStrategy,
        IIdMapStore idMapStore,
        IRevisionFolderProcessor processor,
        IWorkItemImportTarget target,
        ILogger<WorkItemImportOrchestrator> logger)
    {
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _checkpointing = checkpointing ?? throw new ArgumentNullException(nameof(checkpointing));
        _progressSink = progressSink ?? throw new ArgumentNullException(nameof(progressSink));
        _resolutionStrategy = resolutionStrategy ?? throw new ArgumentNullException(nameof(resolutionStrategy));
        _idMapStore = idMapStore ?? throw new ArgumentNullException(nameof(idMapStore));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Runs the import, streaming revision folders from the package.
    /// </summary>
    public async Task ImportAsync(
        WorkItemsModuleExtensions ext,
        ResumeMode resumeMode,
        CancellationToken ct)
    {
        // ForceFresh: delete cursor (but preserve idmap.db)
        if (resumeMode == ResumeMode.ForceFresh)
        {
            await _checkpointing.DeleteCursorAsync("workitems", ct).ConfigureAwait(false);
            _logger.LogInformation("[WorkItems] Force-fresh: cursor deleted. idmap.db preserved.");
        }

        // Read existing cursor for resume
        var cursor = await _checkpointing.ReadCursorAsync("workitems", ct).ConfigureAwait(false);
        var lastProcessed = cursor?.LastProcessed ?? string.Empty;
        var lastStage = cursor?.Stage;

        _logger.LogInformation(
            "[WorkItems] Starting import. Resume cursor: {Cursor} at stage {Stage}",
            string.IsNullOrEmpty(lastProcessed) ? "(start)" : lastProcessed,
            lastStage ?? "(none)");

        // Seed idmap from target strategy
        await _idMapStore.InitializeAsync(ct).ConfigureAwait(false);
        await _resolutionStrategy.SeedAsync(_idMapStore, ct).ConfigureAwait(false);

        int foldersProcessed = 0;
        int workItemsProcessed = 0;

        await foreach (var folderPath in _artefactStore.EnumerateAsync("WorkItems/", ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            // Skip folders at or before the cursor (already completed)
            if (!string.IsNullOrEmpty(lastProcessed)
                && string.CompareOrdinal(folderPath, lastProcessed) < 0)
            {
                continue;
            }

            // Determine resume stage for the cursor folder (mid-folder resume)
            string? resumeAtStage = null;
            if (string.Equals(folderPath, lastProcessed, StringComparison.Ordinal)
                && lastStage is not null
                && !string.Equals(lastStage, CursorStage.Completed, StringComparison.Ordinal))
            {
                resumeAtStage = GetNextStage(lastStage);
            }
            else if (string.Equals(folderPath, lastProcessed, StringComparison.Ordinal)
                && string.Equals(lastStage, CursorStage.Completed, StringComparison.Ordinal))
            {
                // This exact folder was fully completed — skip it
                continue;
            }

            // Parse folder name to distinguish revision vs comment folders
            var folderName = GetFolderName(folderPath);
            var segments = folderName.Split('-');

            if (IsCommentFolder(segments))
            {
                // Comment sub-folder: <ticks>-<workItemId>-c<commentId>
                if (ext.Comments.Enabled)
                    await ProcessCommentFolderAsync(folderPath, segments, ext, ct).ConfigureAwait(false);
                else
                    await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
            }
            else if (ext.RevisionsEnabled)
            {
                // Revision folder — parse work item ID for the progress event
                var revisionSegments = folderName.Split('-');
                int.TryParse(revisionSegments.Length >= 2 ? revisionSegments[1] : null, out var wiId);

                // Revision folder
                await _processor.ProcessAsync(folderPath, ext, resumeAtStage, _resolutionStrategy, ct)
                    .ConfigureAwait(false);
                workItemsProcessed++;
            }
            else
            {
                // Revisions extension disabled — skip but record cursor
                await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
            }

            // Parse work item ID for the progress event (same logic, available for all branch paths)
            var eventSegments = folderName.Split('-');
            int.TryParse(eventSegments.Length >= 2 ? eventSegments[1] : null, out var eventWiId);

            foldersProcessed++;
            _progressSink.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = CursorStage.Completed,
                LastProcessed = folderPath,
                WorkItemId = eventWiId,
                WorkItemsProcessed = workItemsProcessed,
                RevisionsProcessed = foldersProcessed,
                Timestamp = DateTimeOffset.UtcNow
            });
        }

        _logger.LogInformation(
            "[WorkItems] Import complete. Folders processed: {Count}, work items: {WI}",
            foldersProcessed, workItemsProcessed);
    }

    // --- Comment folder handling ---

    private async Task ProcessCommentFolderAsync(
        string folderPath,
        string[] segments,
        WorkItemsModuleExtensions ext,
        CancellationToken ct)
    {
        // Resolve source work item ID from folder name segment[1]
        if (!int.TryParse(segments[1], out var sourceWorkItemId))
        {
            _logger.LogWarning("[WorkItems] Cannot parse work item ID from comment folder {Folder} — skipping.", folderPath);
            await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
            return;
        }

        var targetId = await _idMapStore.GetTargetWorkItemIdAsync(sourceWorkItemId, ct).ConfigureAwait(false);
        if (targetId is null)
        {
            _logger.LogWarning("[WorkItems] No target mapping for source {SourceId} — skipping comment folder {Folder}.", sourceWorkItemId, folderPath);
            await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
            return;
        }

        var commentJson = await _artefactStore.ReadAsync($"{folderPath}/comment.json", ct).ConfigureAwait(false);
        if (commentJson is null)
        {
            _logger.LogWarning("[WorkItems] comment.json not found in {Folder} — skipping.", folderPath);
            await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
            return;
        }

        WorkItemComment? comment;
        try
        {
            comment = System.Text.Json.JsonSerializer.Deserialize<WorkItemComment>(commentJson, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[WorkItems] Failed to deserialise comment.json in {Folder} — skipping.", folderPath);
            await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
            return;
        }

        if (comment is not null && !comment.IsDeleted)
        {
            var text = comment.RenderedText ?? comment.Text;
            await _target.CreateCommentAsync(targetId.Value, text, ct).ConfigureAwait(false);
        }

        await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
    }

    // --- Helpers ---

    private static string GetFolderName(string folderPath)
    {
        var lastSlash = folderPath.LastIndexOf('/');
        return lastSlash >= 0 ? folderPath[(lastSlash + 1)..] : folderPath;
    }

    /// <summary>
    /// Returns true if the folder's third segment starts with 'c' (comment folder convention).
    /// Format: <c>&lt;ticks&gt;-&lt;workItemId&gt;-c&lt;commentId&gt;</c>
    /// </summary>
    private static bool IsCommentFolder(string[] segments)
        => segments.Length >= 3 && segments[2].StartsWith("c", StringComparison.OrdinalIgnoreCase);

    private static string? GetNextStage(string currentStage) => currentStage switch
    {
        CursorStage.CreatedOrUpdated => CursorStage.AppliedFields,
        CursorStage.AppliedFields => CursorStage.AppliedLinks,
        CursorStage.AppliedLinks => CursorStage.UploadedAttachments,
        _ => null
    };

    private Task WriteCompletedCursorAsync(string folderPath, CancellationToken ct)
        => _checkpointing.WriteCursorAsync("workitems", new CursorEntry
        {
            LastProcessed = folderPath,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);
}
#endif
