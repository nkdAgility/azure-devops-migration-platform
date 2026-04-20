#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Models;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Import;

/// <summary>
/// Streaming import loop for work item revision and comment folders.
/// Enumerates <c>WorkItems/</c> via <see cref="IArtefactStore.EnumerateAsync"/> in strict
/// lexicographic order, resumes from the last cursor, and delegates each folder to
/// <see cref="RevisionFolderProcessor"/> (revision folders) or the comment handler (comment folders).
///
/// Memory guarantee: processes one folder at a time; no in-memory list accumulation.
///
/// Cursor + ID map delta behaviour: Folders at or before the cursor are skipped. Within a
/// work item, folders whose revisionIndex is at or below <c>idmap.last_revision_index</c> are
/// skipped (per-WI watermark check). Comment folders are never subject to revision-index skip.
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
    private readonly IReadOnlyList<DevOpsMigrationPlatform.Abstractions.Models.WorkItemFieldFilterOptions>? _filterOptions;
    private readonly IMigrationMetrics? _metrics;
    private readonly string? _jobId;

    public WorkItemImportOrchestrator(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointing,
        IProgressSink progressSink,
        IWorkItemResolutionStrategy resolutionStrategy,
        IIdMapStore idMapStore,
        IRevisionFolderProcessor processor,
        IWorkItemImportTarget target,
        ILogger<WorkItemImportOrchestrator> logger,
        IReadOnlyList<DevOpsMigrationPlatform.Abstractions.Models.WorkItemFieldFilterOptions>? filterOptions = null,
        IMigrationMetrics? metrics = null,
        string? jobId = null)
    {
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _checkpointing = checkpointing ?? throw new ArgumentNullException(nameof(checkpointing));
        _progressSink = progressSink ?? throw new ArgumentNullException(nameof(progressSink));
        _resolutionStrategy = resolutionStrategy ?? throw new ArgumentNullException(nameof(resolutionStrategy));
        _idMapStore = idMapStore ?? throw new ArgumentNullException(nameof(idMapStore));
        _processor = processor ?? throw new ArgumentNullException(nameof(processor));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _filterOptions = filterOptions;
        _metrics = metrics;
        _jobId = jobId;
    }

    /// <summary>
    /// Runs the import, streaming revision folders from the package.
    /// Sequence:
    /// 1. InitializeAsync (open/create idmap.db)
    /// 2. SeedAsync (rebuild ID map from target provenance markers)
    /// 3. CheckIntegrityAsync (log warnings for stale mappings)
    /// 4. Streaming folder loop (cursor-based + revision-index-based skip logic)
    /// </summary>
    public async Task ImportAsync(
        WorkItemsModuleExtensions ext,
        ResumeMode resumeMode,
        CancellationToken ct)
    {
        using var _dc = DataClassificationScope.Begin(DataClassification.Customer);
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

        // 1. Seed idmap from target strategy (InitializeAsync + SeedAsync).
        //    InitializeAsync opens or creates idmap.db and verifies the schema.
        //    SeedAsync bulk-inserts provenance-based mappings using INSERT OR IGNORE.
        await _idMapStore.InitializeAsync(ct).ConfigureAwait(false);
        await _resolutionStrategy.SeedAsync(_idMapStore, ct).ConfigureAwait(false);

        // 2. Integrity check — streams all idmap.db mappings and logs warnings for stale entries.
        //    Non-blocking: job is not aborted on integrity failures. See FR-010 and spec § US4.
        await CheckIntegrityAsync(ct).ConfigureAwait(false);

        // Pre-filter pass: build the set of work item IDs that pass filter predicates.
        // Enumerates folder names only to find the last revision per WI, then reads one
        // revision.json per work item. Items not in the set are skipped in the main loop.
        HashSet<int>? filteredIds = null;
        if (_filterOptions is { Count: > 0 })
        {
            filteredIds = await BuildFilteredIdSetAsync(_filterOptions, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "[WorkItems] Import pre-filter pass complete: {Count} work items pass the configured filter(s).",
                filteredIds.Count);
        }

        int foldersProcessed = 0;
        int workItemsProcessed = 0;
        int lastImportedWorkItemId = 0;

        var importTags = _metrics != null
            ? MigrationTagList.Create(_jobId ?? "not-set", "import", "workitems")
            : default;
        var workItemStopwatch = Stopwatch.StartNew();

        await foreach (var folderPath in _artefactStore.EnumerateAsync("WorkItems/", ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();

            // Cursor-based skip: folders at or before the cursor are already completed.
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
                // Comment folders are NEVER subject to revision-index skip logic.
                if (ext.Comments.Enabled)
                    await ProcessCommentFolderAsync(folderPath, segments, ext, ct).ConfigureAwait(false);
                else
                    await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
            }
            else if (ext.RevisionsEnabled)
            {
                // Parse the folder name using the centralised parser to get workItemId and revisionIndex.
                // TryParse returns null for comment folders (already handled above) and malformed names.
                var parseResult = WorkItemRevisionFolderParser.TryParse(folderName);
                int wiId = parseResult?.WorkItemId ?? 0;

                // Skip if the work item did not pass the filter pre-pass.
                if (filteredIds != null && wiId > 0 && !filteredIds.Contains(wiId))
                {
                    _logger.LogInformation(
                        "[WorkItems] Work item {WorkItemId} skipped by import filter scope.",
                        wiId);
                    await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
                    foldersProcessed++;
                    continue;
                }

                // Revision-level skip: if last_revision_index >= revisionIndex, this folder has
                // already been applied in a previous run. Advance cursor and continue.
                // Only applies when the folder name is parseable (parseResult != null).
                if (parseResult is not null)
                {
                    var lastRevIdx = await _idMapStore
                        .GetLastRevisionIndexAsync(parseResult.WorkItemId, ct)
                        .ConfigureAwait(false);
                    if (lastRevIdx.HasValue && parseResult.RevisionIndex <= lastRevIdx.Value)
                    {
                        // Already applied — skip without writing a new cursor entry.
                        // The cursor already advanced past this folder on the prior run.
                        _logger.LogDebug(
                            "[WorkItems] Revision {RevIdx} for WI {WiId} already applied (last={Last}) — skipping.",
                            parseResult.RevisionIndex, parseResult.WorkItemId, lastRevIdx.Value);
                        foldersProcessed++;
                        continue;
                    }
                }

                // Revision folder
                // Track work item transitions — only emit per-WI metrics on boundary.
                if (wiId != lastImportedWorkItemId)
                {
                    // Complete the previous work item (if any).
                    if (lastImportedWorkItemId != 0 && _metrics != null)
                    {
                        _metrics.RecordWorkItemCompleted(importTags);
                        _metrics.RecordWorkItemDuration(workItemStopwatch.Elapsed.TotalMilliseconds, importTags);
                        _metrics.DecrementInFlight(importTags);
                    }

                    _metrics?.RecordWorkItemAttempted(importTags);
                    _metrics?.IncrementInFlight(importTags);
                    workItemStopwatch.Restart();
                    lastImportedWorkItemId = wiId;
                    workItemsProcessed++;
                }

                // Process the revision folder.
                var result = await _processor.ProcessAsync(folderPath, ext, resumeAtStage, _resolutionStrategy, ct)
                    .ConfigureAwait(false);

                if (result.IsSkipped)
                {
                    // Skipped revisions (e.g. deleted target): write cursor as Completed and continue.
                    // The skip is already recorded in idmap.db.skipped_revisions by the processor.
                    _logger.LogInformation(
                        "[WorkItems] Revision folder {Folder} skipped: {Reason}. Advancing cursor.",
                        folderPath, result.SkipReason);
                    await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
                }
                else if (parseResult is not null)
                {
                    // Applied successfully — update the last_revision_index watermark BEFORE
                    // the next iteration so that a crash between iterations leaves the watermark
                    // consistent with the already-written cursor.
                    await _idMapStore
                        .UpdateLastRevisionIndexAsync(parseResult.WorkItemId, parseResult.RevisionIndex, ct)
                        .ConfigureAwait(false);
                }
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

        // Record completion of the final work item.
        if (lastImportedWorkItemId != 0 && _metrics != null)
        {
            _metrics.RecordWorkItemCompleted(importTags);
            _metrics.RecordWorkItemDuration(workItemStopwatch.Elapsed.TotalMilliseconds, importTags);
            _metrics.DecrementInFlight(importTags);
        }

        // Emit zero-match warning if filters were active but no items were processed.
        if (_filterOptions is { Count: > 0 } && workItemsProcessed == 0)
        {
            _logger.LogWarning(
                "[WorkItems] Warning: all work items were filtered out by filter scopes. Check your filter configuration.");
        }
    }

    /// <summary>
    /// Streams all <c>idmap.db</c> mappings and logs a <c>LogWarning</c> for each mapping
    /// that references a non-existent target work item.
    /// Non-blocking: exceptions from <see cref="IWorkItemImportTarget.WorkItemExistsAsync"/>
    /// propagate normally (network errors will abort the check).
    /// Called at import startup after <c>SeedAsync</c> completes. See FR-010.
    /// </summary>
    private async Task CheckIntegrityAsync(CancellationToken ct)
    {
        _logger.LogInformation("[WorkItems][IntegrityCheck] Starting ID map integrity check.");
        int checked_ = 0;
        int invalid = 0;

        await foreach (var entry in _idMapStore.EnumerateWorkItemMappingsAsync(ct).ConfigureAwait(false))
        {
            checked_++;
            var exists = await _target.WorkItemExistsAsync(entry.TargetId, ct).ConfigureAwait(false);
            if (!exists)
            {
                invalid++;
                _logger.LogWarning(
                    "[WorkItems][IntegrityCheck] Mapping {SourceId}→{TargetId} points to a non-existent target work item.",
                    entry.SourceId, entry.TargetId);
            }
        }

        _logger.LogInformation(
            "[WorkItems][IntegrityCheck] Integrity check complete. Checked: {Checked}, invalid: {Invalid}.",
            checked_, invalid);
    }

    /// <summary>
    /// Pre-filter pass for import: enumerates revision folder names only to locate the last
    /// revision folder per work item, reads one revision.json per work item, evaluates against
    /// <paramref name="filterOptions"/>, and returns the set of passing work item IDs.
    /// </summary>
    private async Task<HashSet<int>> BuildFilteredIdSetAsync(
        IReadOnlyList<DevOpsMigrationPlatform.Abstractions.Models.WorkItemFieldFilterOptions> filterOptions,
        CancellationToken ct)
    {
        // Pass 1: collect the last folder path for each work item ID (folder names only, no reads).
        var lastFolderPerWi = new Dictionary<int, string>();

        await foreach (var folderPath in _artefactStore.EnumerateAsync("WorkItems/", ct).ConfigureAwait(false))
        {
            ct.ThrowIfCancellationRequested();
            var folderName = GetFolderName(folderPath);
            var segs = folderName.Split('-');
            if (IsCommentFolder(segs)) continue;
            if (!int.TryParse(segs.Length >= 2 ? segs[1] : null, out var wiId)) continue;

            // Lexicographic order = chronological order; overwriting gives us the latest folder.
            lastFolderPerWi[wiId] = folderPath;
        }

        // Pass 2: read one revision.json per work item, evaluate filters.
        var passedIds = new HashSet<int>();

        foreach (var (wiId, folderPath) in lastFolderPerWi)
        {
            ct.ThrowIfCancellationRequested();

            var json = await _artefactStore.ReadAsync($"{folderPath}revision.json", ct).ConfigureAwait(false);
            if (json is null) continue;

            WorkItemRevision? revision = null;
            try { revision = JsonSerializer.Deserialize<WorkItemRevision>(json, _jsonOptions); }
            catch { /* skip unreadable revision */ }

            if (revision is null) continue;

            // Convert WorkItemField list to the FetchedWorkItem fields dictionary.
            var fields = revision.Fields.ToDictionary(
                f => f.ReferenceName,
                f => (object?)f.Value);

            var fetchedItem = new DevOpsMigrationPlatform.Abstractions.Models.FetchedWorkItem(wiId, fields);

            bool passes;
            try
            {
                passes = DevOpsMigrationPlatform.Abstractions.Models.WorkItemFieldFilterEvaluator
                    .PassesFilters(fetchedItem, filterOptions);
            }
            catch (System.Text.RegularExpressions.RegexMatchTimeoutException)
            {
                _logger.LogWarning(
                    "[WorkItems] Regex filter timeout evaluating work item {WorkItemId} — treating as non-match.",
                    wiId);
                passes = false;
            }

            if (passes)
                passedIds.Add(wiId);
        }

        return passedIds;
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
        var trimmed = folderPath.TrimEnd('/');
        var lastSlash = trimmed.LastIndexOf('/');
        return lastSlash >= 0 ? trimmed[(lastSlash + 1)..] : trimmed;
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
