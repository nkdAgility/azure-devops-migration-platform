// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

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

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly IPackageAccess _package;
    private readonly string _organisation;
    private readonly string _project;
    private readonly ICheckpointingService _checkpointing;
    private readonly IProgressSink _progressSink;
    private readonly IWorkItemResolutionStrategy _resolutionStrategy;
    private readonly IIdMapStore _idMapStore;
    private readonly IRevisionFolderProcessor _processor;
    private readonly IWorkItemImportTarget _target;
    private readonly ILogger<WorkItemImportOrchestrator> _logger;
    private readonly IReadOnlyList<WorkItemFieldFilterOptions>? _filterOptions;
    private readonly IPlatformMetrics? _metrics;
    private readonly string? _jobId;

    public WorkItemImportOrchestrator(
        IPackageAccess package,
        string organisation,
        string project,
        ICheckpointingService checkpointing,
        IProgressSink progressSink,
        IWorkItemResolutionStrategy resolutionStrategy,
        IIdMapStore idMapStore,
        IRevisionFolderProcessor processor,
        IWorkItemImportTarget target,
        ILogger<WorkItemImportOrchestrator> logger,
        IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions = null,
        IPlatformMetrics? metrics = null,
        string? jobId = null)
    {
        _package = package ?? throw new ArgumentNullException(nameof(package));
        _organisation = organisation ?? throw new ArgumentNullException(nameof(organisation));
        _project = project ?? throw new ArgumentNullException(nameof(project));
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
    /// </summary>
    public async Task ImportAsync(
        WorkItemsModuleExtensions ext,
        ResumeMode resumeMode,
        CancellationToken ct)
    {
        using var rootActivity = ActivitySource.StartActivity("workitems.import", ActivityKind.Internal);
        rootActivity?.SetTag("job.id", _jobId ?? "not-set");

        using var _dc = DataClassificationScope.Begin(DataClassification.Customer);
        // ForceFresh: delete cursor (but preserve idmap.db)
        if (resumeMode == ResumeMode.ForceFresh)
        {
            await _checkpointing.DeleteCursorAsync("import.workitems", ct).ConfigureAwait(false);
            _logger.LogInformation("[WorkItems] Force-fresh: cursor deleted. idmap.db preserved.");
        }

        // Read existing cursor for resume
        var cursor = await _checkpointing.ReadCursorAsync("import.workitems", ct).ConfigureAwait(false);
        var lastProcessed = cursor?.LastProcessed ?? string.Empty;
        var lastStage = cursor?.Stage;

        _logger.LogInformation(
            "[WorkItems] Starting import. Resume cursor: {Cursor} at stage {Stage}",
            string.IsNullOrEmpty(lastProcessed) ? "(start)" : lastProcessed,
            lastStage ?? "(none)");

        // Seed idmap from target strategy
        await _idMapStore.InitializeAsync(ct).ConfigureAwait(false);
        await _resolutionStrategy.SeedAsync(_idMapStore, ct).ConfigureAwait(false);

        // Integrity check: warn about stale mappings before import begins (non-blocking)
        var staleMappings = await _idMapStore.CheckIntegrityAsync(
            (tid, token) => _target.WorkItemExistsAsync(tid, token), ct).ConfigureAwait(false);
        foreach (var stale in staleMappings)
        {
            _logger.LogWarning(
                "[WorkItems] Integrity check: source {SourceId} → target {TargetId} is stale (target no longer exists).",
                stale.SourceId, stale.TargetId);
        }
        if (staleMappings.Count > 0)
        {
            _logger.LogWarning(
                "[WorkItems] Integrity check complete: {Count} stale mapping(s) found. Import will continue.",
                staleMappings.Count);
        }

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
        int revisionsForCurrentWorkItem = 0;

        var importTags = MetricsTagList.Create(_jobId ?? "not-set", "import", "workitems");
        var workItemStopwatch = Stopwatch.StartNew();
        Activity? workItemActivity = null;

        try
        {
            await foreach (var folderPath in EnumerateWorkItemFoldersAsync(ct).ConfigureAwait(false))
            {
                ct.ThrowIfCancellationRequested();

                var resumeDecision = ImportResumeDecisionResolver.Resolve(folderPath, cursor);
                if (resumeDecision.ShouldSkip)
                    continue;

                var resumeAtStage = resumeDecision.ResumeAtStage;

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
                    // Revision folder — parse work item ID and revision index
                    var revisionSegments = folderName.Split('-');
                    int.TryParse(revisionSegments.Length >= 2 ? revisionSegments[1] : null, out var wiId);
                    int.TryParse(revisionSegments.Length >= 3 ? revisionSegments[2] : null, out var revIdx);

                    // Skip if the work item did not pass the filter pre-pass.
                    if (filteredIds != null && !filteredIds.Contains(wiId))
                    {
                        _logger.LogInformation(
                            "[WorkItems] Work item {WorkItemId} skipped by import filter scope.",
                            wiId);
                        await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
                        foldersProcessed++;
                        continue;
                    }

                    // Revision-index watermark: skip folders at or below the last applied revision
                    var lastRevIdx = await _idMapStore.GetLastRevisionIndexAsync(wiId, ct).ConfigureAwait(false);
                    if (lastRevIdx.HasValue && revIdx <= lastRevIdx.Value)
                    {
                        _logger.LogDebug(
                            "[WorkItems] WI {WorkItemId} rev {Rev} at or below watermark {Watermark} — skipped.",
                            wiId, revIdx, lastRevIdx.Value);
                        await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
                        foldersProcessed++;
                        continue;
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
                            _metrics.RecordRevisionCount(revisionsForCurrentWorkItem, importTags);
                            _metrics.DecrementInFlight(importTags);
                        }
                        workItemActivity?.Dispose();

                        _metrics?.RecordWorkItemAttempted(importTags);
                        _metrics?.IncrementInFlight(importTags);
                        workItemStopwatch.Restart();
                        revisionsForCurrentWorkItem = 1;
                        lastImportedWorkItemId = wiId;
                        workItemsProcessed++;

                        workItemActivity = ActivitySource.StartActivity("workitem.import", ActivityKind.Internal);
                        workItemActivity?.SetTag("job.id", _jobId ?? "not-set");
                        workItemActivity?.SetTag("workitem.id", wiId);
                    }
                    else
                    {
                        revisionsForCurrentWorkItem++;
                    }

                    using var revisionActivity = ActivitySource.StartActivity("revision.process", ActivityKind.Internal);
                    revisionActivity?.SetTag("workitem.id", wiId);
                    revisionActivity?.SetTag("revision.index", revIdx);

                    await _processor.ProcessAsync(folderPath, ext, resumeAtStage, _resolutionStrategy, ct)
                        .ConfigureAwait(false);

                    // Update revision-index watermark after successful processing
                    await _idMapStore.UpdateLastRevisionIndexAsync(wiId, revIdx, ct).ConfigureAwait(false);
                }
                else
                {
                    // Revisions extension disabled — skip but record cursor
                    await WriteCompletedCursorAsync(folderPath, ct).ConfigureAwait(false);
                }

                // Parse work item ID for the progress event (same logic, available for all branch paths)
                var eventSegments = GetFolderName(folderPath).Split('-');
                int.TryParse(eventSegments.Length >= 2 ? eventSegments[1] : null, out var eventWiId);

                foldersProcessed++;
                _progressSink.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = CursorStage.Completed,
                    Timestamp = DateTimeOffset.UtcNow,
                    LastCheckpointAt = DateTimeOffset.UtcNow,
                    NextCheckpointDueAt = null // per-revision checkpoint — always safe to cancel
                });
            }
        }
        finally
        {
            // Ensure InFlight is always decremented even on exception.
            if (lastImportedWorkItemId != 0 && _metrics != null)
                _metrics.DecrementInFlight(importTags);
            workItemActivity?.Dispose();
        }

        _logger.LogInformation(
            "[WorkItems] Import complete. Folders processed: {Count}, work items: {WI}",
            foldersProcessed, workItemsProcessed);

        // Record completion of the final work item.
        if (lastImportedWorkItemId != 0 && _metrics != null)
        {
            _metrics.RecordWorkItemCompleted(importTags);
            _metrics.RecordWorkItemDuration(workItemStopwatch.Elapsed.TotalMilliseconds, importTags);
            _metrics.RecordRevisionCount(revisionsForCurrentWorkItem, importTags);
        }

        // Emit zero-match warning if filters were active but no items were processed.
        if (_filterOptions is { Count: > 0 } && workItemsProcessed == 0)
        {
            _logger.LogWarning(
                "[WorkItems] Warning: all work items were filtered out by filter scopes. Check your filter configuration.");
        }
    }

    /// <summary>
    /// Pre-filter pass for import: enumerates revision folder names only to locate the last
    /// revision folder per work item, reads one revision.json per work item, evaluates against
    /// <paramref name="filterOptions"/>, and returns the set of passing work item IDs.
    /// </summary>
    private async Task<HashSet<int>> BuildFilteredIdSetAsync(
        IReadOnlyList<WorkItemFieldFilterOptions> filterOptions,
        CancellationToken ct)
    {
        // Pass 1: collect the last folder path for each work item ID (folder names only, no reads).
        var lastFolderPerWi = new Dictionary<int, string>();

        await foreach (var folderPath in EnumerateWorkItemFoldersAsync(ct).ConfigureAwait(false))
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

            var json = await ReadPackageTextAsync($"{folderPath}revision.json", ct).ConfigureAwait(false);
            if (json is null) continue;

            WorkItemRevision? revision = null;
            try { revision = JsonSerializer.Deserialize<WorkItemRevision>(json, _jsonOptions); }
            catch { /* skip unreadable revision */ }

            if (revision is null) continue;

            // Convert WorkItemField list to the FetchedWorkItem fields dictionary.
            var fields = revision.Fields.ToDictionary(
                f => f.ReferenceName,
                f => (object?)f.Value);

            var fetchedItem = new FetchedWorkItem(wiId, fields);

            bool passes;
            try
            {
                passes = WorkItemFieldFilterEvaluator
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

        var commentJson = await ReadPackageTextAsync($"{folderPath}/comment.json", ct).ConfigureAwait(false);
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

    private async IAsyncEnumerable<string> EnumerateWorkItemFoldersAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct)
    {
        var paths = _package.EnumerateContentAsync(
            new PackageContentContext(
                PackageContentKind.Collection,
                Organisation: _organisation,
                Project: _project,
                Module: "WorkItems",
                IsCollectionRequest: true),
            ct);
        if (paths is null)
            yield break;

        await foreach (var path in paths.ConfigureAwait(false))
            yield return path;
    }

    private async Task<string?> ReadPackageTextAsync(string path, CancellationToken ct)
    {
        var payload = await _package.RequestContentAsync(
            CreateArtefactContext(path),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new System.IO.StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private PackageContentContext CreateArtefactContext(string path)
    {
        if (path.EndsWith("revision.json", StringComparison.OrdinalIgnoreCase))
        {
            return new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: _organisation,
                Project: _project,
                Module: "WorkItems",
                Address: new WorkItemRevisionAddress(GetRevisionFolderPath(path)));
        }

        return new PackageContentContext(
            PackageContentKind.Artefact,
            Organisation: _organisation,
            Project: _project,
            Module: "WorkItems",
            Address: new WorkItemAttachmentAddress(GetRevisionFolderPath(path), GetFileName(path)));
    }

    private static string GetRevisionFolderPath(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        if (normalized.StartsWith("WorkItems/", StringComparison.OrdinalIgnoreCase))
            normalized = normalized["WorkItems/".Length..];

        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[..lastSlash] : normalized;
    }

    private static string GetFileName(string path)
    {
        var normalized = path.Replace('\\', '/').TrimEnd('/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash >= 0 ? normalized[(lastSlash + 1)..] : normalized;
    }

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

    private Task WriteCompletedCursorAsync(string folderPath, CancellationToken ct)
        => _checkpointing.WriteCursorAsync("import.workitems", new CursorEntry
        {
            LastProcessed = folderPath,
            Stage = CursorStage.Completed,
            UpdatedAt = DateTimeOffset.UtcNow
        }, ct);
}
#endif

