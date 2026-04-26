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
    private readonly IArtefactStore _artefactStore;
    private readonly IStateStore _stateStore;
    private readonly ILogger<NodeEnsurer> _logger;

    public NodeEnsurer(
        IOptions<NodeStructureOptions> options,
        INodeStructureTool tool,
        INodeCreator nodeCreator,
        IArtefactStore artefactStore,
        IStateStore stateStore,
        ILogger<NodeEnsurer> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _tool = tool ?? throw new ArgumentNullException(nameof(tool));
        _nodeCreator = nodeCreator ?? throw new ArgumentNullException(nameof(nodeCreator));
        _artefactStore = artefactStore ?? throw new ArgumentNullException(nameof(artefactStore));
        _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Reads Nodes/source-tree.json and ensures all nodes exist in the target.
    /// No-op when ReplicateSourceTree is false.
    /// </summary>
    public async Task ReplicateSourceTreeAsync(ProjectMapping context, CancellationToken ct)
    {
        if (!_options.ReplicateSourceTree)
        {
            _logger.LogDebug("[NodeStructure] ReplicateSourceTree disabled — skipping.");
            return;
        }

        var json = await _artefactStore.ReadAsync(SourceTreePath, ct).ConfigureAwait(false);
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

        var progress = await LoadProgressAsync(ct).ConfigureAwait(false);

        using var activity = s_activitySource.StartActivity("nodes.import.replicate");
        var sw = Stopwatch.StartNew();
        int count = 0, skipped = 0;

        foreach (var areaPath in snapshot.AreaNodes)
        {
            var translated = _tool.TranslatePath("System.AreaPath", areaPath, context);
            var targetPath = translated.TargetPath ?? areaPath;

            if (progress.ReplicatedPaths.Contains(targetPath)) { skipped++; continue; }

            await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Area, targetPath, ct).ConfigureAwait(false);
            progress.ReplicatedPaths.Add(targetPath);
            progress.UpdatedAt = DateTimeOffset.UtcNow;
            await SaveProgressAsync(progress, ct).ConfigureAwait(false);
            count++;
        }

        foreach (var iterEntry in snapshot.IterationNodes)
        {
            var translated = _tool.TranslatePath("System.IterationPath", iterEntry.Path, context);
            var targetPath = translated.TargetPath ?? iterEntry.Path;

            if (progress.ReplicatedPaths.Contains(targetPath)) { skipped++; continue; }

            await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, targetPath, ct).ConfigureAwait(false);

            if (iterEntry.StartDate.HasValue || iterEntry.FinishDate.HasValue)
            {
                try
                {
                    await _nodeCreator.SetIterationDatesAsync(targetPath, iterEntry.StartDate, iterEntry.FinishDate, ct)
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
            await SaveProgressAsync(progress, ct).ConfigureAwait(false);
            count++;
        }

        sw.Stop();
        _logger.LogInformation(
            "[NodeStructure] Tree replication complete: {Count} created, {Skipped} skipped in {DurationMs}ms.",
            count, skipped, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Reads Nodes/referenced-paths.json and ensures all translated paths exist in the target.
    /// No-op when AutoCreateNodes is false.
    /// </summary>
    public async Task EnsureReferencedPathsAsync(ProjectMapping context, CancellationToken ct)
    {
        if (!_options.AutoCreateNodes)
        {
            _logger.LogDebug("[NodeStructure] AutoCreateNodes disabled — skipping pre-collection.");
            return;
        }

        var json = await _artefactStore.ReadAsync(ReferencedPathsPath, ct).ConfigureAwait(false);
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

        foreach (var areaPath in artifact.AreaPaths)
        {
            var translated = _tool.TranslatePath("System.AreaPath", areaPath, context);
            if (translated.TargetPath is null) continue;
            await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Area, translated.TargetPath, ct).ConfigureAwait(false);
            count++;
        }

        foreach (var iterPath in artifact.IterationPaths)
        {
            var translated = _tool.TranslatePath("System.IterationPath", iterPath, context);
            if (translated.TargetPath is null) continue;
            await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, translated.TargetPath, ct).ConfigureAwait(false);
            count++;
        }

        sw.Stop();
        _logger.LogInformation("[NodeStructure] Pre-collection complete: {Count} paths processed in {DurationMs}ms.",
            count, sw.ElapsedMilliseconds);
    }

    private async Task<NodeReplicationProgress> LoadProgressAsync(CancellationToken ct)
    {
        var json = await _stateStore.ReadAsync(NodeReplicationProgress.StateKey, ct).ConfigureAwait(false);
        if (json is null) return new NodeReplicationProgress();
        return JsonSerializer.Deserialize<NodeReplicationProgress>(json, s_jsonOptions) ?? new NodeReplicationProgress();
    }

    private async Task SaveProgressAsync(NodeReplicationProgress progress, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(progress, s_jsonOptions);
        await _stateStore.WriteAsync(NodeReplicationProgress.StateKey, json, ct).ConfigureAwait(false);
    }
}
#endif
