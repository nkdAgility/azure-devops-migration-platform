using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// Drives the streaming export loop for work item revisions.
/// Receives revisions from <see cref="IWorkItemRevisionSource"/>, writes each one to the
/// package via <see cref="IArtefactStore"/>, then advances the cursor via
/// <see cref="ICheckpointingService"/>. All revisions are processed one at a time — no buffering.
/// When an <see cref="IAttachmentBinarySource"/> is supplied, each attachment binary is
/// downloaded and stored beside <c>revision.json</c> in the same revision folder.
/// For comment edit/delete revisions, inline comment versions are fetched via
/// <see cref="IWorkItemCommentSourceFactory"/> and written as comment.json.
/// <para>
/// When <see cref="FilterOptions"/> is non-empty, a pre-filter pass is performed before the
/// main export loop via <see cref="IWorkItemFetchService"/>. Only filter-referenced fields are
/// fetched. Work items that do not pass the filter are skipped entirely — no revision API calls
/// are made for filtered-out items.
/// </para>
/// </summary>
public sealed class WorkItemExportOrchestrator
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly IArtefactStore _artefactStore;
    private readonly ICheckpointingService _checkpointingService;
    private readonly IAttachmentBinarySource? _attachmentBinarySource;
    private readonly IProgressSink? _progressSink;
    private readonly IWorkItemCommentSourceFactory? _inlineCommentSourceFactory;
    private readonly MigrationEndpointOptions? _endpoint;
    private readonly string? _project;
    private readonly string? _wiqlQuery;
    private readonly IWorkItemFetchService? _fetchService;
    private readonly IReadOnlyList<WorkItemFieldFilterOptions>? _filterOptions;
    private readonly IMigrationMetrics? _metrics;
    private readonly string? _jobId;
    private readonly ILogger? _logger;
    private readonly IWorkItemDiscoveryService? _discoveryService;

    public WorkItemExportOrchestrator(
        IArtefactStore artefactStore,
        ICheckpointingService checkpointingService,
        IAttachmentBinarySource? attachmentBinarySource = null,
        IProgressSink? progressSink = null,
        MigrationEndpointOptions? endpoint = null,
        string? project = null,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory = null,
        IWorkItemFetchService? fetchService = null,
        IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions = null,
        IMigrationMetrics? metrics = null,
        string? jobId = null,
        ILogger? logger = null,
        string? wiqlQuery = null,
        IWorkItemDiscoveryService? discoveryService = null)
    {
        _artefactStore = artefactStore;
        _checkpointingService = checkpointingService;
        _attachmentBinarySource = attachmentBinarySource;
        _progressSink = progressSink;
        _inlineCommentSourceFactory = inlineCommentSourceFactory;
        _endpoint = endpoint;
        _project = project;
        _wiqlQuery = wiqlQuery;
        _fetchService = fetchService;
        _filterOptions = filterOptions;
        _metrics = metrics;
        _jobId = jobId;
        _logger = logger;
        _discoveryService = discoveryService;
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
        using var rootActivity = ActivitySource.StartActivity("workitems.export", ActivityKind.Internal);
        rootActivity?.SetTag("job.id", _jobId ?? "not-set");

        // Pre-filter pass: build the set of work item IDs that pass filter predicates.
        // If filters are configured, fetch only filter-referenced fields via IWorkItemFetchService.
        // Items not in the set are skipped in the main loop — no revision API calls are made.
        HashSet<int>? filteredIds = null;
        if (_filterOptions is { Count: > 0 } && _fetchService != null && _endpoint != null &&
            !string.IsNullOrEmpty(_project))
        {
            _logger?.LogInformation("[WorkItems] Starting pre-filter pass — fetching work items to evaluate {FilterCount} filter(s).", _filterOptions.Count);
            _progressSink?.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "Filtering",
                Message = $"[WorkItems] Pre-filter pass starting — evaluating {_filterOptions.Count} filter(s)…"
            });

            filteredIds = await BuildFilteredIdSetAsync(_filterOptions, cancellationToken)
                .ConfigureAwait(false);

            _logger?.LogInformation("[WorkItems] Pre-filter pass complete: {FilteredCount} work items pass filters.", filteredIds.Count);
            _progressSink?.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "Filtering",
                Message = $"[WorkItems] Pre-filter pass complete: {filteredIds.Count} work items pass the configured filter(s)."
            });
        }

        var cursor = await _checkpointingService
            .ReadCursorAsync("workitems", cancellationToken)
            .ConfigureAwait(false);

        if (cursor != null)
        {
            _logger?.LogInformation(
                "[WorkItems] Resuming export from cursor: {Cursor} (previously {WorkItemsProcessed} work items / {RevisionsProcessed} revisions)",
                cursor.LastProcessed, cursor.WorkItemsProcessed, cursor.RevisionsProcessed);
            _progressSink?.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "Resuming",
                Message = $"[WorkItems] Resuming from cursor — {cursor.WorkItemsProcessed} work items / {cursor.RevisionsProcessed} revisions already exported."
            });
        }

        // Seed counters from cursor so progress is accurate on resume.
        int workItemsProcessed = cursor?.WorkItemsProcessed ?? 0;
        int revisionsProcessed = cursor?.RevisionsProcessed ?? 0;
        int lastWorkItemId = cursor?.LastWorkItemId ?? 0;
        double lastWorkItemDurationMs = 0;
        double totalWorkItemDurationMs = 0;
        int lastCompletedRevisions = 0;

        // Determine total work items: use cached cursor value on resume, otherwise count via
        // discovery service. Counting happens here in the Agent — never in the CLI.
        int totalWorkItems = 0;
        if (cursor?.TotalWorkItems > 0)
        {
            totalWorkItems = cursor.TotalWorkItems;
            _logger?.LogInformation("[WorkItems] Total work items (from cursor): {TotalWorkItems}", totalWorkItems);
        }
        else if (_discoveryService != null && _endpoint != null && !string.IsNullOrEmpty(_project))
        {
            _logger?.LogInformation("[WorkItems] Counting work items in scope…");
            _progressSink?.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "Counting",
                Message = "[WorkItems] Counting work items in scope…"
            });

            await foreach (var snapshot in _discoveryService.CountWorkItemsAsync(
                _endpoint.ToOrganisationEndpoint(), _project!, _wiqlQuery, cancellationToken)
                .ConfigureAwait(false))
            {
                if (snapshot.IsWorkItemComplete)
                    totalWorkItems = snapshot.WorkItemsCount;
            }

            _logger?.LogInformation("[WorkItems] Work items in scope: {TotalWorkItems}", totalWorkItems);
        }

        // Emit scope-resolved event so the CLI progress bar can set its total.
        // Only emitted when we have a known total — avoids spurious events in tests
        // and in partial setups where no discovery service is wired.
        if (totalWorkItems > 0)
        {
            _progressSink?.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "ScopeResolved",
                Message = $"[WorkItems] Scope resolved: {totalWorkItems:N0} work items",
                Metrics = new JobMetrics
                {
                    Scope = new JobScopeCounters { WorkItemsTotal = totalWorkItems },
                    Migration = new MigrationCounters
                    {
                        WorkItems = new WorkItemCounters
                        {
                            Completed = workItemsProcessed,
                            RevisionsProcessed = revisionsProcessed
                        }
                    }
                }
            });
        }
        int attachmentsProcessed = 0;
        int attachmentsFailed = 0;
        double totalAttachmentDurationMs = 0;
        double lastAttachmentDurationMs = 0;
        long totalAttachmentBytes = 0;
        long lastAttachmentBytes = 0;
        var attachmentStopwatch = new Stopwatch();

        // Per-work-item timing for duration histogram.
        var workItemStopwatch = Stopwatch.StartNew();
        // Per-revision timing so the CLI can show last/avg revision latency.
        var revisionStopwatch = Stopwatch.StartNew();
        double lastRevisionDurationMs = 0;
        double totalRevisionDurationMs = 0;
        TagList exportTags = _metrics != null
            ? MigrationTagList.Create(_jobId ?? "not-set", "export", "workitems")
            : default;
        int revisionsForCurrentWorkItem = 0;

        // Delta detection: track download URLs from the previous revision to skip
        // re-downloading identical attachments on adjacent revisions.
        var previousAttachmentUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // Detect streaming support once rather than per-attachment.
        var streamingSource = _attachmentBinarySource as IStreamingAttachmentBinarySource;

        Activity? workItemActivity = null;

        int resumeSkipLastWorkItemId = 0;
        // Separate counter for fast-forwarded (already-exported) work items during resume.
        // This is NOT part of workItemsProcessed (which counts exported items only).
        int workItemsSkipped = 0;
        // Separate counter for filter-excluded work items.
        int workItemsFilterSkipped = 0;
        string lastWorkItemStatus = string.Empty;

        await foreach (var revision in source.GetRevisionsAsync(cancellationToken))
        {
            var folderPath = BuildFolderPath(revision.WorkItemId, revision.RevisionIndex, revision.ChangedDate);

            // Skip all revisions at or before the cursor (resume logic).
            if (cursor != null &&
                string.Compare(folderPath, cursor.LastProcessed, StringComparison.Ordinal) <= 0)
            {
                // Emit a progress event each time we cross a new work item boundary during
                // the skip phase so the CLI shows "Resuming…" activity rather than silence.
                if (revision.WorkItemId != resumeSkipLastWorkItemId)
                {
                    resumeSkipLastWorkItemId = revision.WorkItemId;
                    workItemsSkipped++;
                    lastWorkItemStatus = "Skipped";
                    _progressSink?.Emit(new ProgressEvent
                    {
                        Module = "WorkItems",
                        Stage = "Resuming",
                        Message = $"[WorkItems] Resuming — skipping WI {revision.WorkItemId} (already exported)",
                        Metrics = new JobMetrics
                        {
                            Scope = new JobScopeCounters { WorkItemsTotal = totalWorkItems },
                            Migration = new MigrationCounters
                            {
                                WorkItems = new WorkItemCounters
                                {
                                    Completed = workItemsProcessed,
                                    Skipped = workItemsSkipped,
                                    RevisionsProcessed = revisionsProcessed,
                                    CurrentWorkItemId = revision.WorkItemId,
                                    LastWorkItemId = revision.WorkItemId,
                                    LastWorkItemStatus = "Skipped"
                                }
                            }
                        }
                    });
                }
                continue;
            }

            // Skip work items that did not pass the pre-filter.
            if (filteredIds != null && !filteredIds.Contains(revision.WorkItemId))
            {
                if (revision.RevisionIndex == 0)
                {
                    workItemsFilterSkipped++;
                    lastWorkItemStatus = "Skipped";
                    _progressSink?.Emit(new ProgressEvent
                    {
                        Module = "WorkItems",
                        Stage = "Export",
                        Message = $"[WorkItems] Work item {revision.WorkItemId} skipped by filter scope.",
                        Metrics = new JobMetrics
                        {
                            Scope = new JobScopeCounters { WorkItemsTotal = totalWorkItems },
                            Migration = new MigrationCounters
                            {
                                WorkItems = new WorkItemCounters
                                {
                                    Completed = workItemsProcessed,
                                    Skipped = workItemsSkipped + workItemsFilterSkipped,
                                    RevisionsProcessed = revisionsProcessed,
                                    CurrentWorkItemId = revision.WorkItemId,
                                    LastWorkItemId = revision.WorkItemId,
                                    LastWorkItemStatus = "Skipped"
                                }
                            }
                        }
                    });

                    if (_logger != null)
                        _logger.LogDebug("[WorkItems] Work item {WorkItemId} skipped by filter scope.", revision.WorkItemId);
                }
                continue;
            }



            // Write revision.json.
            var json = JsonSerializer.Serialize(revision, JsonOptions);
            await _artefactStore.WriteAsync($"{folderPath}revision.json", json, cancellationToken).ConfigureAwait(false);

            // Record payload complexity metrics per revision.
            if (_metrics != null)
            {
                _metrics.RecordFieldCount(revision.Fields.Count, exportTags);
                _metrics.RecordAttachmentCount(revision.Attachments.Count, exportTags);
                _metrics.RecordLinkCount(
                    revision.ExternalLinks.Count + revision.RelatedLinks.Count + revision.Hyperlinks.Count,
                    exportTags);
                _metrics.RecordPayloadBytes(json.Length, exportTags);
            }

            if (_logger != null)
                _logger.LogDebug(
                        "[WorkItems] WI {WorkItemId} rev {RevisionIndex}: fields={FieldCount}, attachments={AttachmentCount}, links={LinkCount}, bytes={PayloadBytes}",
                        revision.WorkItemId, revision.RevisionIndex, revision.Fields.Count,
                        revision.Attachments.Count,
                        revision.ExternalLinks.Count + revision.RelatedLinks.Count + revision.Hyperlinks.Count,
                        json.Length);

            // For comment edit/delete revisions, fetch the matching comment versions by timestamp
            // and write them as comment.json beside revision.json in the same revision folder.
            // FR-5: Comment API failures are non-fatal — log via progress and continue.
            if (_inlineCommentSourceFactory != null &&
                _endpoint != null &&
                !string.IsNullOrEmpty(_project) &&
                IsCommentEditOrDeleteRevision(revision))
            {
                try
                {
                    var commentSource = _inlineCommentSourceFactory.Create(_endpoint!, _project!);
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
                        Message = $"[WorkItems] Warning: inline comment fetch failed for work item {revision.WorkItemId} revision {revision.RevisionIndex}: {ex.Message}"
                    });

                    if (_logger != null)
                        _logger.LogWarning(ex,
                                "[WorkItems] Inline comment fetch failed for WI {WorkItemId} rev {RevisionIndex}.",
                                revision.WorkItemId, revision.RevisionIndex);
                }
            }

            // Capture revision duration before the WI-boundary check so it covers the
            // full cost of writing revision.json + comments (but not attachments).
            lastRevisionDurationMs = revisionStopwatch.Elapsed.TotalMilliseconds;
            totalRevisionDurationMs += lastRevisionDurationMs;
            revisionStopwatch.Restart();

            revisionsProcessed++;

            if (revision.WorkItemId != lastWorkItemId)
            {
                // Record completion of the previous work item (if any).
                if (lastWorkItemId != 0)
                {
                    lastWorkItemDurationMs = workItemStopwatch.Elapsed.TotalMilliseconds;
                    totalWorkItemDurationMs += lastWorkItemDurationMs;
                    lastCompletedRevisions = revisionsForCurrentWorkItem;
                    if (_metrics != null)
                    {
                        _metrics.RecordWorkItemCompleted(exportTags);
                        _metrics.RecordWorkItemDuration(lastWorkItemDurationMs, exportTags);
                        _metrics.RecordRevisionCount(revisionsForCurrentWorkItem, exportTags);
                        _metrics.DecrementInFlight(exportTags);
                    }
                    workItemActivity?.Dispose();
                    workItemActivity = null;
                }

                // Record attempted for the new work item and restart timing.
                _metrics?.RecordWorkItemAttempted(exportTags);
                _metrics?.IncrementInFlight(exportTags);
                workItemStopwatch.Restart();
                revisionsForCurrentWorkItem = 1; // This is the first revision of the new work item.

                workItemActivity = ActivitySource.StartActivity("workitem.export", ActivityKind.Internal);
                workItemActivity?.SetTag("job.id", _jobId ?? "not-set");
                workItemActivity?.SetTag("workitem.id", revision.WorkItemId);

                // Emit once per work item (not per revision) to avoid flooding the channel.
                workItemsProcessed++;
                lastWorkItemId = revision.WorkItemId;
                lastWorkItemStatus = "Exported";

                if (_logger != null)
                    _logger.LogInformation(
                            "[WorkItems] Exporting WI {WorkItemId} ({WorkItemsProcessed} items / {RevisionsProcessed} revisions so far).",
                            revision.WorkItemId, workItemsProcessed, revisionsProcessed);

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = "Export",
                    Message = $"[WorkItems] {workItemsProcessed} work items / {revisionsProcessed} revisions written",
                    LastCheckpointAt = DateTimeOffset.UtcNow,
                    NextCheckpointDueAt = null, // per-revision checkpoint — always safe to cancel
                    Metrics = new JobMetrics
                    {
                        Migration = new MigrationCounters
                        {
                            WorkItems = new WorkItemCounters
                            {
                                Completed = workItemsProcessed,
                                Skipped = workItemsSkipped + workItemsFilterSkipped,
                                RevisionsProcessed = revisionsProcessed,
                                LastWorkItemDurationMs = lastWorkItemDurationMs,
                                AverageWorkItemDurationMs = workItemsProcessed > 1
                                    ? totalWorkItemDurationMs / (workItemsProcessed - 1)
                                    : lastWorkItemDurationMs,
                                LastWorkItemRevisions = lastCompletedRevisions,
                                CurrentWorkItemId = revision.WorkItemId,
                                CurrentWorkItemIndex = workItemsProcessed,
                                CurrentWorkItemRevisionsWritten = revisionsForCurrentWorkItem,
                                LastRevisionDurationMs = lastRevisionDurationMs,
                                AverageRevisionDurationMs = revisionsProcessed > 0
                                    ? totalRevisionDurationMs / revisionsProcessed
                                    : lastRevisionDurationMs,
                                LastWorkItemId = lastWorkItemId,
                                LastWorkItemStatus = lastWorkItemStatus
                            }
                        }
                    }
                });
            }
            else
            {
                revisionsForCurrentWorkItem++;

                // Emit a lightweight per-revision event so consumers can track intra-WI progress.
                _progressSink?.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = "Export",
                    Message = $"[WorkItems] WI {revision.WorkItemId} rev {revisionsForCurrentWorkItem}",
                    LastCheckpointAt = DateTimeOffset.UtcNow,
                    NextCheckpointDueAt = null,
                    Metrics = new JobMetrics
                    {
                        Migration = new MigrationCounters
                        {
                            WorkItems = new WorkItemCounters
                            {
                                Completed = workItemsProcessed,
                                Skipped = workItemsSkipped + workItemsFilterSkipped,
                                RevisionsProcessed = revisionsProcessed,
                                LastWorkItemRevisions = lastCompletedRevisions,
                                CurrentWorkItemId = revision.WorkItemId,
                                CurrentWorkItemIndex = workItemsProcessed,
                                CurrentWorkItemRevisionsWritten = revisionsForCurrentWorkItem,
                                LastRevisionDurationMs = lastRevisionDurationMs,
                                AverageRevisionDurationMs = revisionsProcessed > 0
                                    ? totalRevisionDurationMs / revisionsProcessed
                                    : lastRevisionDurationMs,
                                LastWorkItemId = lastWorkItemId,
                                LastWorkItemStatus = lastWorkItemStatus
                            }
                        }
                    }
                });
            }

            // Write attachment binaries beside revision.json when a binary source is available.
            // Delta detection: skip re-downloading when the same URL appears on adjacent revisions.
            var currentAttachmentUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_attachmentBinarySource != null)
            {
                foreach (var attachment in revision.Attachments)
                {
                    // Track current revision's URLs for next-revision delta comparison.
                    var downloadUrl = attachment.DownloadUrl;
                    if (downloadUrl is { Length: > 0 })
                        currentAttachmentUrls.Add(downloadUrl);

                    // Delta detection: skip download if same URL was already downloaded
                    // for the previous revision (adjacent revision optimization).
                    if (downloadUrl is { Length: > 0 } &&
                        previousAttachmentUrls.Contains(downloadUrl))
                    {
                        if (_logger != null)
                            _logger.LogDebug("[Attachments] WI {WorkItemId} rev {RevisionIndex}: attachment delta-skipped (same URL as previous revision).",
                                    revision.WorkItemId, revision.RevisionIndex);
                        continue;
                    }

                    var attachmentName = attachment.OriginalName is { Length: > 0 } n ? n : attachment.RelativePath ?? "(unknown)";

                    // ── Found ──
                    _logger?.LogInformation("[Attachments] WI {WorkItemId} rev {RevisionIndex}: found attachment '{Name}' ({Bytes} bytes).",
                        revision.WorkItemId, revision.RevisionIndex, attachmentName, attachment.Size);
                    _progressSink?.Emit(new ProgressEvent
                    {
                        Module = "WorkItems",
                        Stage = "Attachment",
                        Message = $"[Attachments] WI {revision.WorkItemId} rev {revision.RevisionIndex}: found '{attachmentName}' ({FormatBytes(attachment.Size)})",
                        Metrics = new JobMetrics
                        {
                            Migration = new MigrationCounters
                            {
                                WorkItems = new WorkItemCounters
                                {
                                    Completed = workItemsProcessed,
                                    RevisionsProcessed = revisionsProcessed,
                                    CurrentWorkItemId = revision.WorkItemId,
                                    Attachments = new AttachmentCounters
                                    {
                                        Processed = attachmentsProcessed,
                                        Failed = attachmentsFailed,
                                        TotalBytes = totalAttachmentBytes,
                                        LastDownloadDurationMs = lastAttachmentDurationMs,
                                        AverageDownloadDurationMs = attachmentsProcessed > 0 ? totalAttachmentDurationMs / attachmentsProcessed : 0,
                                        LastSizeBytes = lastAttachmentBytes,
                                        AverageSizeBytes = attachmentsProcessed > 0 ? totalAttachmentBytes / attachmentsProcessed : 0,
                                        CurrentAttachmentName = attachmentName
                                    }
                                }
                            }
                        }
                    });

                    var targetPath = $"{folderPath}{attachment.RelativePath}";

                    using var attachmentActivity = ActivitySource.StartActivity("attachment.download", ActivityKind.Internal);
                    attachmentActivity?.SetTag("workitem.id", revision.WorkItemId);

                    attachmentStopwatch.Restart();
                    long downloadedBytes = 0;
                    bool downloadSucceeded;

                    // Prefer streaming path when the source supports it (no byte[] buffering).
                    if (streamingSource != null)
                    {
                        var result = await streamingSource
                            .StreamToStoreAsync(revision.WorkItemId, revision.RevisionIndex, attachment,
                                _artefactStore, targetPath, cancellationToken)
                            .ConfigureAwait(false);

                        downloadSucceeded = result.HasValue;
                        if (result.HasValue)
                            downloadedBytes = result.Value.Size;
                    }
                    else
                    {
                        // Fallback: buffer via GetBytesAsync + WriteBinaryAsync.
                        var bytes = await _attachmentBinarySource
                            .GetBytesAsync(revision.WorkItemId, revision.RevisionIndex, attachment, cancellationToken)
                            .ConfigureAwait(false);

                        if (bytes != null)
                        {
                            await _artefactStore
                                .WriteBinaryAsync(targetPath, bytes, cancellationToken)
                                .ConfigureAwait(false);
                            downloadedBytes = bytes.Length;
                            downloadSucceeded = true;
                        }
                        else
                        {
                            downloadSucceeded = false;
                        }
                    }

                    lastAttachmentDurationMs = attachmentStopwatch.Elapsed.TotalMilliseconds;

                    if (downloadSucceeded)
                    {
                        attachmentsProcessed++;
                        totalAttachmentDurationMs += lastAttachmentDurationMs;
                        lastAttachmentBytes = downloadedBytes;
                        totalAttachmentBytes += downloadedBytes;

                        // Record per-download OTel metrics.
                        if (_metrics != null)
                        {
                            _metrics.RecordAttachmentDownloadDuration(lastAttachmentDurationMs, exportTags);
                            _metrics.RecordAttachmentDownloadBytes(downloadedBytes, exportTags);
                        }

                        // ── Done ──
                        _logger?.LogInformation("[Attachments] WI {WorkItemId} rev {RevisionIndex}: downloaded '{Name}' — {Bytes} in {Ms:F0}ms.",
                            revision.WorkItemId, revision.RevisionIndex, attachmentName, FormatBytes(downloadedBytes), lastAttachmentDurationMs);
                        _progressSink?.Emit(new ProgressEvent
                        {
                            Module = "WorkItems",
                            Stage = "Attachment",
                            Message = $"[Attachments] WI {revision.WorkItemId}: '{attachmentName}' done — {FormatBytes(downloadedBytes)} in {lastAttachmentDurationMs:F0}ms",
                            Metrics = new JobMetrics
                            {
                                Migration = new MigrationCounters
                                {
                                    WorkItems = new WorkItemCounters
                                    {
                                        Completed = workItemsProcessed,
                                        RevisionsProcessed = revisionsProcessed,
                                        CurrentWorkItemId = revision.WorkItemId,
                                        Attachments = new AttachmentCounters
                                        {
                                            Processed = attachmentsProcessed,
                                            Failed = attachmentsFailed,
                                            TotalBytes = totalAttachmentBytes,
                                            LastDownloadDurationMs = lastAttachmentDurationMs,
                                            AverageDownloadDurationMs = totalAttachmentDurationMs / attachmentsProcessed,
                                            LastSizeBytes = lastAttachmentBytes,
                                            AverageSizeBytes = totalAttachmentBytes / attachmentsProcessed
                                        }
                                    }
                                }
                            }
                        });
                    }
                    else
                    {
                        attachmentsFailed++;
                        _logger?.LogWarning("[Attachments] WI {WorkItemId} rev {RevisionIndex}: failed to download '{Name}'.",
                            revision.WorkItemId, revision.RevisionIndex, attachmentName);
                        _progressSink?.Emit(new ProgressEvent
                        {
                            Module = "WorkItems",
                            Stage = "Attachment",
                            Message = $"[Attachments] WI {revision.WorkItemId}: '{attachmentName}' FAILED",
                            Metrics = new JobMetrics
                            {
                                Migration = new MigrationCounters
                                {
                                    WorkItems = new WorkItemCounters
                                    {
                                        Completed = workItemsProcessed,
                                        RevisionsProcessed = revisionsProcessed,
                                        CurrentWorkItemId = revision.WorkItemId,
                                        Attachments = new AttachmentCounters
                                        {
                                            Processed = attachmentsProcessed,
                                            Failed = attachmentsFailed,
                                            TotalBytes = totalAttachmentBytes,
                                            LastDownloadDurationMs = lastAttachmentDurationMs,
                                            AverageDownloadDurationMs = attachmentsProcessed > 0 ? totalAttachmentDurationMs / attachmentsProcessed : 0,
                                            LastSizeBytes = lastAttachmentBytes,
                                            AverageSizeBytes = attachmentsProcessed > 0 ? totalAttachmentBytes / attachmentsProcessed : 0
                                        }
                                    }
                                }
                            }
                        });
                    }
                }
            }

            previousAttachmentUrls = currentAttachmentUrls;

            var newCursor = new CursorEntry
            {
                LastProcessed = folderPath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow,
                WorkItemsProcessed = workItemsProcessed,
                RevisionsProcessed = revisionsProcessed,
                LastWorkItemId = lastWorkItemId,
                TotalWorkItems = totalWorkItems
            };
            // Use CancellationToken.None — the cursor write is critical safety state and
            // must complete after a successful revision.json write even if the job is being
            // cancelled. Using the job token here causes the write to be aborted on shutdown,
            // leaving no cursor on disk and forcing the next run to start from the beginning.
            await _checkpointingService
                .WriteCursorAsync("workitems", newCursor, CancellationToken.None)
                .ConfigureAwait(false);
        }

        // Emit zero-match warning if filters were active but no items were processed.
        if (_filterOptions is { Count: > 0 } && workItemsProcessed == 0)
        {
            _progressSink?.Emit(new ProgressEvent
            {
                Module = "WorkItems",
                Stage = "Export",
                Message = "[WorkItems] Warning: all work items were filtered out by filter scopes. Check your filter configuration."
            });
            _logger?.LogWarning("[WorkItems] All work items were filtered out by filter scopes.");
        }

        // Record completion of the final work item.
        if (lastWorkItemId != 0 && _metrics != null)
        {
            _metrics.RecordWorkItemCompleted(exportTags);
            _metrics.RecordWorkItemDuration(workItemStopwatch.Elapsed.TotalMilliseconds, exportTags);
            _metrics.RecordRevisionCount(revisionsForCurrentWorkItem, exportTags);
            _metrics.DecrementInFlight(exportTags);
        }
        workItemActivity?.Dispose();

        _logger?.LogInformation(
                "[WorkItems] Export complete. WorkItems={WorkItemsProcessed}, Revisions={RevisionsProcessed}, Attachments={AttachmentsProcessed}, AttachmentsFailed={AttachmentsFailed}.",
                workItemsProcessed, revisionsProcessed, attachmentsProcessed, attachmentsFailed);

        rootActivity?.SetTag("workitems.count", workItemsProcessed);
        rootActivity?.SetTag("revisions.count", revisionsProcessed);
    }

    /// <summary>
    /// Runs the pre-filter pass via <see cref="IWorkItemFetchService"/>, fetching only the fields
    /// referenced by <paramref name="filterOptions"/>. Returns the set of work item IDs that pass all filters.
    /// </summary>
    private async Task<HashSet<int>> BuildFilteredIdSetAsync(
        IReadOnlyList<WorkItemFieldFilterOptions> filterOptions,
        CancellationToken cancellationToken)
    {
        var ids = new HashSet<int>();

        // Fetch only the fields referenced by filter predicates — minimal payload.
        var filterFields = filterOptions.Select(f => f.FieldName).Distinct().ToArray();
        var scope = new WorkItemFetchScope(Fields: filterFields, FilterOptions: filterOptions);

        var orgEndpoint = _endpoint!.ToOrganisationEndpoint();
        int fetched = 0;

        await foreach (var item in _fetchService!.FetchAsync(orgEndpoint, _project!, scope, cancellationToken)
            .ConfigureAwait(false))
        {
            ids.Add(item.Id);
            fetched++;

            // Emit progress every 100 items so the operator can see the fetch is alive.
            if (fetched % 100 == 0)
            {
                _logger?.LogInformation("[WorkItems] Pre-filter pass: {Fetched} work items fetched so far…", fetched);
                _progressSink?.Emit(new ProgressEvent
                {
                    Module = "WorkItems",
                    Stage = "Filtering",
                    Message = $"[WorkItems] Pre-filter pass: {fetched:N0} work items fetched so far…"
                });
            }
        }

        return ids;
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
    internal static string BuildFolderPath(int workItemId, int revisionIndex, DateTimeOffset changedDate)
    {
        var date = changedDate.ToString("yyyy-MM-dd");
        var ticks = changedDate.Ticks.ToString("D20");
        return $"WorkItems/{date}/{ticks}-{workItemId}-{revisionIndex}/";
    }

    private static string FormatBytes(long bytes) =>
        bytes switch
        {
            >= 1_073_741_824 => $"{bytes / 1_073_741_824.0:F1} GB",
            >= 1_048_576 => $"{bytes / 1_048_576.0:F1} MB",
            >= 1_024 => $"{bytes / 1_024.0:F1} KB",
            _ => $"{bytes} B"
        };
}

