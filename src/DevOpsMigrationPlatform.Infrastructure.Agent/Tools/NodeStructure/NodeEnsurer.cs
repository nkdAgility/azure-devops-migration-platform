#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// Orchestrates import pre-processing:
/// 1. ReplicateSourceTree — reads Nodes/source-tree.json when <c>ReplicateSourceTree: true</c>
/// 2. AutoCreateNodes pre-collection — reads Nodes/referenced-paths.json when <c>AutoCreateNodes: true</c>
/// </summary>
public sealed class NodeEnsurer
{
    private const string ReferencedPathsPath = "Nodes/referenced-paths.json";
    private const string SourceTreePath = "Nodes/source-tree.json";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };

    private readonly NodeStructureOptions _options;
    private readonly INodeStructureTool _tool;
    private readonly INodeCreator _nodeCreator;
    private readonly ILogger<NodeEnsurer> _logger;

    public NodeEnsurer(
        IOptions<NodeStructureOptions> options,
        INodeStructureTool tool,
        INodeCreator nodeCreator,
        ILogger<NodeEnsurer> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _nodeCreator = nodeCreator ?? throw new ArgumentNullException(nameof(nodeCreator));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads Nodes/source-tree.json and ensures all nodes exist in the target.
    /// No-op when ReplicateSourceTree is false.
    /// Emits <c>migration.nodes.import.replicate.*</c> OTel metrics.
    /// </summary>
    public async Task ReplicateSourceTreeAsync(
        ProjectMapping context,
        MigrationEndpointOptions endpoint,
        IArtefactStore artefactStore,
        IStateStore stateStore,
        CancellationToken ct,
        IMigrationMetrics? metrics = null,
        string? jobId = null)
    {
        if (!_options.ReplicateSourceTree)
        {
            _logger.LogDebug("[NodeStructure] ReplicateSourceTree disabled — skipping.");
            return;
        }

        var json = await artefactStore.ReadAsync(SourceTreePath, ct).ConfigureAwait(false);
        if (json is null)
        {
            _logger.LogWarning("[NodeStructure] {Path} not found in package — skipping ReplicateSourceTree.", SourceTreePath);
            return;
        }

        var snapshot = JsonSerializer.Deserialize<ClassificationTreeSnapshot>(json, s_jsonOptions);
        if (snapshot is null)
        {
            _logger.LogWarning("[NodeStructure] Failed to deserialize {Path} — skipping.", SourceTreePath);
            return;
        }

        var progress = await LoadProgressAsync(stateStore, ct).ConfigureAwait(false);

        using var activity = s_activitySource.StartActivity("nodes.import.replicate");
        var sw = Stopwatch.StartNew();
        int count = 0, skipped = 0, errors = 0;
        var tags = MigrationTagList.Create(jobId ?? string.Empty, "import", "NodeStructure");

        metrics?.IncrementNodeImportReplicateInFlight(tags);
        try
        {
            foreach (var areaPath in snapshot.AreaNodes)
            {
                var translated = _tool.TranslatePath("System.AreaPath", areaPath, context);
                var targetPath = translated.TargetPath ?? areaPath;

                if (progress.ReplicatedPaths.Contains(targetPath))
                {
                    skipped++;
                    metrics?.RecordNodeImportReplicateSkipped(tags);
                    continue;
                }

                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Area, targetPath, endpoint, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors++;
                    metrics?.RecordNodeImportReplicateError(tags);
                    _logger.LogError(ex, "[NodeStructure] Failed to ensure area node {Path}.", targetPath);
                    throw;
                }

                progress.ReplicatedPaths.Add(targetPath);
                progress.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveProgressAsync(stateStore, progress, ct).ConfigureAwait(false);
                count++;
                metrics?.RecordNodeImportReplicateCount(tags);
            }

            foreach (var iterEntry in snapshot.IterationNodes)
            {
                var translated = _tool.TranslatePath("System.IterationPath", iterEntry.Path, context);
                var targetPath = translated.TargetPath ?? iterEntry.Path;

                if (progress.ReplicatedPaths.Contains(targetPath))
                {
                    skipped++;
                    metrics?.RecordNodeImportReplicateSkipped(tags);
                    continue;
                }

                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, targetPath, endpoint, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors++;
                    metrics?.RecordNodeImportReplicateError(tags);
                    _logger.LogError(ex, "[NodeStructure] Failed to ensure iteration node {Path}.", targetPath);
                    throw;
                }

                if (iterEntry.StartDate.HasValue || iterEntry.FinishDate.HasValue)
                {
                    try
                    {
                        await _nodeCreator.SetIterationDatesAsync(targetPath, iterEntry.StartDate, iterEntry.FinishDate, endpoint, ct)
                            .ConfigureAwait(false);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex,
                            "[NodeStructure] Failed to set dates for iteration node {Path} — non-blocking.", targetPath);
                    }
                }

                progress.ReplicatedPaths.Add(targetPath);
                progress.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveProgressAsync(stateStore, progress, ct).ConfigureAwait(false);
                count++;
                metrics?.RecordNodeImportReplicateCount(tags);
            }
        }
        finally
        {
            metrics?.DecrementNodeImportReplicateInFlight(tags);
        }

        sw.Stop();
        metrics?.RecordNodeImportReplicateDuration(sw.Elapsed.TotalMilliseconds, tags);
        activity?.SetTag("nodes.replicated", count);
        activity?.SetTag("nodes.skipped", skipped);

        _logger.LogInformation(
            "[NodeStructure] Tree replication complete: {Count} created, {Skipped} skipped in {DurationMs}ms.",
            count, skipped, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Reads Nodes/referenced-paths.json and ensures all translated paths exist in the target.
    /// No-op when AutoCreateNodes is false.
    /// Emits <c>migration.nodes.import.precollect.*</c> OTel metrics.
    /// </summary>
    public async Task EnsureReferencedPathsAsync(
        ProjectMapping context,
        MigrationEndpointOptions endpoint,
        IArtefactStore artefactStore,
        CancellationToken ct,
        IMigrationMetrics? metrics = null,
        string? jobId = null)
    {
        if (!_options.AutoCreateNodes)
        {
            _logger.LogDebug("[NodeStructure] AutoCreateNodes disabled — skipping pre-collection.");
            return;
        }

        var json = await artefactStore.ReadAsync(ReferencedPathsPath, ct).ConfigureAwait(false);
        if (json is null)
        {
            _logger.LogDebug("[NodeStructure] {Path} not found — skipping pre-collection.", ReferencedPathsPath);
            return;
        }

        var artifact = JsonSerializer.Deserialize<ReferencedPathsArtifact>(json, s_jsonOptions);
        if (artifact is null) return;

        using var activity = s_activitySource.StartActivity("nodes.import.precollect");
        var sw = Stopwatch.StartNew();
        int count = 0;
        var tags = MigrationTagList.Create(jobId ?? string.Empty, "import", "NodeStructure");

        metrics?.IncrementNodeImportPreCollectInFlight(tags);
        try
        {
            foreach (var areaPath in artifact.AreaPaths)
            {
                var translated = _tool.TranslatePath("System.AreaPath", areaPath, context);
                if (translated.TargetPath is null) continue;
                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Area, translated.TargetPath, endpoint, ct).ConfigureAwait(false);
                    count++;
                    metrics?.RecordNodeImportPreCollectCount(tags);
                }
                catch (Exception ex)
                {
                    metrics?.RecordNodeImportPreCollectError(tags);
                    _logger.LogError(ex, "[NodeStructure] Failed to ensure pre-collected area node {Path}.", translated.TargetPath);
                    throw;
                }
            }

            foreach (var iterPath in artifact.IterationPaths)
            {
                var translated = _tool.TranslatePath("System.IterationPath", iterPath, context);
                if (translated.TargetPath is null) continue;
                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, translated.TargetPath, endpoint, ct).ConfigureAwait(false);
                    count++;
                    metrics?.RecordNodeImportPreCollectCount(tags);
                }
                catch (Exception ex)
                {
                    metrics?.RecordNodeImportPreCollectError(tags);
                    _logger.LogError(ex, "[NodeStructure] Failed to ensure pre-collected iteration node {Path}.", translated.TargetPath);
                    throw;
                }
            }
        }
        finally
        {
            metrics?.DecrementNodeImportPreCollectInFlight(tags);
        }

        sw.Stop();
        metrics?.RecordNodeImportPreCollectDuration(sw.Elapsed.TotalMilliseconds, tags);
        activity?.SetTag("nodes.precollected", count);

        _logger.LogInformation("[NodeStructure] Pre-collection complete: {Count} paths processed in {DurationMs}ms.",
            count, sw.ElapsedMilliseconds);
    }

    private async Task<NodeReplicationProgress> LoadProgressAsync(IStateStore stateStore, CancellationToken ct)
    {
        var json = await stateStore.ReadAsync(NodeReplicationProgress.StateKey, ct).ConfigureAwait(false);
        if (json is null) return new NodeReplicationProgress();
        return JsonSerializer.Deserialize<NodeReplicationProgress>(json, s_jsonOptions) ?? new NodeReplicationProgress();
    }

    private async Task SaveProgressAsync(IStateStore stateStore, NodeReplicationProgress progress, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(progress, s_jsonOptions);
        await stateStore.WriteAsync(NodeReplicationProgress.StateKey, json, ct).ConfigureAwait(false);
    }
}
#endif
