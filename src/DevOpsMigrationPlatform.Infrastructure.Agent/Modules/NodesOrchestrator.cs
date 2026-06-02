// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
#if !NET481
using System.Text.Json;
using System.Text.Json.Serialization;
using DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;
using Microsoft.Extensions.Options;
#endif
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Orchestrates classification-tree (node) export, import, and validation operations.
/// Handles checkpointing, progress events, and metrics — delegates the actual
/// tree capture to <see cref="IClassificationTreeCapture"/> and node replication/
/// referenced-path pre-creation inline.
/// </summary>
internal sealed class NodesOrchestrator : INodesOrchestrator
{
    private const string SourceTreePath = "Nodes/source-tree.json";
    private const string ReferencedPathsPath = "Nodes/referenced-paths.json";
    private const string ReplicationProgressPath = "Nodes/replication-progress.json";
    private const string ModuleName = "Nodes";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

#if !NET481
    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNameCaseInsensitive = true
    };
#endif

    private readonly ILogger _logger;
    private readonly IPackageAccess? _package;
#if !NET481
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly INodeTranslationTool _nodeTranslationTool;
    private readonly INodeCreator _nodeCreator;
    private readonly IOptionsMonitor<NodeTranslationOptions> _nodeTranslationOptions;
#endif

    public NodesOrchestrator(
        ILogger<NodesOrchestrator> logger
#if !NET481
        , INodeTranslationTool nodeTranslationTool,
        INodeCreator nodeCreator,
        IOptionsMonitor<NodeTranslationOptions> nodeTranslationOptions,
        IPlatformMetrics? PlatformMetrics = null,
        IPackageAccess? package = null
#else
        , IPackageAccess? package = null
#endif
    )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _package = package;
#if !NET481
        _nodeTranslationTool = nodeTranslationTool ?? throw new ArgumentNullException(nameof(nodeTranslationTool));
        _nodeCreator = nodeCreator ?? throw new ArgumentNullException(nameof(nodeCreator));
        _nodeTranslationOptions = nodeTranslationOptions ?? throw new ArgumentNullException(nameof(nodeTranslationOptions));
        _PlatformMetrics = PlatformMetrics;
#endif
    }

    /// <summary>
    /// Captures the classification tree from the source endpoint via <paramref name="capture"/>.
    /// Writes checkpoint on completion. Idempotent — skips if already completed.
    /// </summary>
    public async Task ExportAsync(
        IClassificationTreeCapture capture,
        ExportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("nodes.export");

        var exportSink = context.ProgressSink;
        exportSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Nodes.Export.Started",
            Message = $"Starting node tree capture for project '{sourceEndpointInfo.Project}'.",
        });

        // Idempotency: skip if already completed.
        if (checkpointingFactory is not null)
        {
            var checkpointing = checkpointingFactory.Create(context.Package);
            var cursor = await checkpointing.ReadCursorAsync("export.nodes", ct).ConfigureAwait(false);
            if (cursor?.Stage == CursorStage.Completed
                && await ContentExistsAsync(context.Package, sourceEndpointInfo.OrganisationSlug, sourceEndpointInfo.Project, SourceTreePath, ct).ConfigureAwait(false))
            {
                _logger.LogInformation("[Nodes] Already exported (cursor found) — skipping re-export.");
                return;
            }
        }

        var nodeCount = await capture.CaptureAsync(
            context.Package,
            sourceEndpointInfo.OrganisationSlug,
            sourceEndpointInfo.Project,
            ct,
#if !NET481
            _PlatformMetrics,
#else
            null,
#endif
            context.Job.JobId, context.ProgressSink, ModuleName
            ).ConfigureAwait(false);

        exportSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Nodes.Export.Complete",
            Message = $"Node tree capture complete — {nodeCount} nodes captured.",
            Metrics = new JobMetrics
            {
                Migration = new MigrationCounters
                {
                    Nodes = new NodesCounters { Exported = nodeCount }
                }
            }
        });

        // Write cursor after successful export.
        if (checkpointingFactory is not null)
        {
            var checkpointing = checkpointingFactory.Create(context.Package);
            await checkpointing.WriteCursorAsync("export.nodes", new CursorEntry
            {
                LastProcessed = SourceTreePath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);
        }
    }

#if !NET481
    /// <summary>
    /// Replicates the source classification tree into the target project.
    /// Writes checkpoint on completion.
    /// </summary>
    public async Task ImportAsync(
        ImportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        bool replicateSourceTree,
        CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("nodes.import");

        var importSink = context.ProgressSink;
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Nodes.Import.Started",
            Message = $"Starting node replication for project '{targetEndpointInfo.Project}'.",
        });

        var project = targetEndpointInfo.Project;
        var sourceProject = sourceEndpointInfo.Project;
        var mapping = new ProjectMapping(sourceProject, project);

        if (replicateSourceTree)
        {
            _logger.LogInformation("[Nodes] Replicating source tree.");
            await ReplicateSourceTreeAsync(
                mapping,
                context.Package,
                sourceEndpointInfo.OrganisationSlug,
                sourceProject,
                ct,
                _PlatformMetrics,
                context.Job.JobId).ConfigureAwait(false);
            importSink?.Emit(new ProgressEvent
            {
                Module = ModuleName,
                Stage = "Nodes.Import.Complete",
                Message = "Node replication complete.",
            });
        }
        else
        {
            _logger.LogDebug("[Nodes] ReplicateSourceTree disabled — nothing to import.");
        }

        // Write cursor after successful import.
        if (checkpointingFactory is not null)
        {
            var checkpointing = checkpointingFactory.Create(context.Package);
            await checkpointing.WriteCursorAsync("import.nodes", new CursorEntry
            {
                LastProcessed = "Nodes/import",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Reads <c>Nodes/referenced-paths.json</c> and ensures all translated paths exist in the target.
    /// No-op when AutoCreateNodes is false.
    /// Emits <c>migration.nodes.import.precollect.*</c> OTel metrics.
    /// </summary>
    public async Task EnsureReferencedPathsAsync(
        ProjectMapping context,
        IPackageAccess package,
        string organisation,
        string project,
        CancellationToken ct,
        IPlatformMetrics? metrics = null,
        string? jobId = null)
    {
        var options = _nodeTranslationOptions.CurrentValue;
        if (!options.AutoCreateNodes)
        {
            _logger.LogDebug("[NodeTranslation] AutoCreateNodes disabled — skipping pre-collection.");
            return;
        }

        var json = await ReadPackageContentAsync(package, organisation, project, ReferencedPathsPath, ct).ConfigureAwait(false);
        if (json is null)
        {
            _logger.LogDebug("[NodeTranslation] {Path} not found — skipping pre-collection.", ReferencedPathsPath);
            return;
        }

        var artifact = JsonSerializer.Deserialize<ReferencedPathsArtifact>(json, s_jsonOptions);
        if (artifact is null) return;

        using var activity = s_activitySource.StartActivity("nodes.import.precollect");
        var sw = Stopwatch.StartNew();
        int count = 0;
        var tags = MetricsTagList.Create(jobId ?? string.Empty, "import", "NodeTranslation");

        metrics?.IncrementNodeImportPreCollectInFlight(tags);
        try
        {
            foreach (var areaPath in artifact.AreaPaths)
            {
                var translated = _nodeTranslationTool.TranslatePath("System.AreaPath", areaPath, context);
                if (translated.TargetPath is null) continue;
                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Area, translated.TargetPath, ct).ConfigureAwait(false);
                    count++;
                    metrics?.RecordNodeImportPreCollectCount(tags);
                }
                catch (Exception ex)
                {
                    metrics?.RecordNodeImportPreCollectError(tags);
                    _logger.LogError(ex, "[NodeTranslation] Failed to ensure pre-collected area node {Path}.", translated.TargetPath);
                    throw;
                }
            }

            foreach (var iterPath in artifact.IterationPaths)
            {
                var translated = _nodeTranslationTool.TranslatePath("System.IterationPath", iterPath, context);
                if (translated.TargetPath is null) continue;
                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, translated.TargetPath, ct).ConfigureAwait(false);
                    count++;
                    metrics?.RecordNodeImportPreCollectCount(tags);
                }
                catch (Exception ex)
                {
                    metrics?.RecordNodeImportPreCollectError(tags);
                    _logger.LogError(ex, "[NodeTranslation] Failed to ensure pre-collected iteration node {Path}.", translated.TargetPath);
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

        _logger.LogInformation("[NodeTranslation] Pre-collection complete: {Count} paths processed in {DurationMs}ms.",
            count, sw.ElapsedMilliseconds);
    }

    /// <summary>
    /// Reads <c>Nodes/source-tree.json</c> and ensures all nodes exist in the target.
    /// Emits <c>migration.nodes.import.replicate.*</c> OTel metrics.
    /// </summary>
    private async Task ReplicateSourceTreeAsync(
        ProjectMapping context,
        IPackageAccess package,
        string organisation,
        string project,
        CancellationToken ct,
        IPlatformMetrics? metrics = null,
        string? jobId = null)
    {
        var json = await ReadPackageContentAsync(package, organisation, project, SourceTreePath, ct).ConfigureAwait(false);
        if (json is null)
        {
            _logger.LogWarning("[NodeTranslation] {Path} not found in package — skipping ReplicateSourceTree.", SourceTreePath);
            return;
        }

        var snapshot = JsonSerializer.Deserialize<ClassificationTreeSnapshot>(json, s_jsonOptions);
        if (snapshot is null)
        {
            _logger.LogWarning("[NodeTranslation] Failed to deserialize {Path} — skipping.", SourceTreePath);
            return;
        }

        var progress = await LoadProgressAsync(package, organisation, project, ct).ConfigureAwait(false);

        using var activity = s_activitySource.StartActivity("nodes.import.replicate");
        var sw = Stopwatch.StartNew();
        int count = 0, skipped = 0, errors = 0;
        var tags = MetricsTagList.Create(jobId ?? string.Empty, "import", "NodeTranslation");

        metrics?.IncrementNodeImportReplicateInFlight(tags);
        try
        {
            foreach (var areaPath in snapshot.AreaNodes)
            {
                var translated = _nodeTranslationTool.TranslatePath("System.AreaPath", areaPath, context);
                var targetPath = translated.TargetPath ?? areaPath;

                if (progress.ReplicatedPaths.Contains(targetPath))
                {
                    skipped++;
                    metrics?.RecordNodeImportReplicateSkipped(tags);
                    continue;
                }

                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Area, targetPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors++;
                    metrics?.RecordNodeImportReplicateError(tags);
                    _logger.LogError(ex, "[NodeTranslation] Failed to ensure area node {Path}.", targetPath);
                    throw;
                }

                progress.ReplicatedPaths.Add(targetPath);
                progress.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveProgressAsync(package, organisation, project, progress, ct).ConfigureAwait(false);
                count++;
                metrics?.RecordNodeImportReplicateCount(tags);
                metrics?.RecordNodeImportReplicateAreaCount(tags);
            }

            foreach (var iterEntry in snapshot.IterationNodes)
            {
                var translated = _nodeTranslationTool.TranslatePath("System.IterationPath", iterEntry.Path, context);
                var targetPath = translated.TargetPath ?? iterEntry.Path;

                if (progress.ReplicatedPaths.Contains(targetPath))
                {
                    skipped++;
                    metrics?.RecordNodeImportReplicateSkipped(tags);
                    continue;
                }

                try
                {
                    await _nodeCreator.EnsureExistsAsync(ClassificationNodeType.Iteration, targetPath, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    errors++;
                    metrics?.RecordNodeImportReplicateError(tags);
                    _logger.LogError(ex, "[NodeTranslation] Failed to ensure iteration node {Path}.", targetPath);
                    throw;
                }

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
                            "[NodeTranslation] Failed to set dates for iteration node {Path} — non-blocking.", targetPath);
                    }
                }

                progress.ReplicatedPaths.Add(targetPath);
                progress.UpdatedAt = DateTimeOffset.UtcNow;
                await SaveProgressAsync(package, organisation, project, progress, ct).ConfigureAwait(false);
                count++;
                metrics?.RecordNodeImportReplicateCount(tags);
                metrics?.RecordNodeImportReplicateIterationCount(tags);
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
            "[NodeTranslation] Tree replication complete: {Count} created, {Skipped} skipped in {DurationMs}ms.",
            count, skipped, sw.ElapsedMilliseconds);
    }

    private async Task<NodeReplicationProgress> LoadProgressAsync(IPackageAccess package, string organisation, string project, CancellationToken ct)
    {
        var json = await ReadPackageContentAsync(package, organisation, project, ReplicationProgressPath, ct).ConfigureAwait(false);
        if (json is null) return new NodeReplicationProgress();
        return JsonSerializer.Deserialize<NodeReplicationProgress>(json, s_jsonOptions) ?? new NodeReplicationProgress();
    }

    private async Task SaveProgressAsync(IPackageAccess package, string organisation, string project, NodeReplicationProgress progress, CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(progress, s_jsonOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await package.PersistContentAsync(
            CreatePackageContentContext(organisation, project, ReplicationProgressPath),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);
    }
#endif

    /// <summary>
    /// Validates that the source-tree artefact exists and is valid JSON.
    /// </summary>
    public async Task ValidateAsync(IPackageAccess package, string organisation, string project, ValidationContext context, CancellationToken ct)
    {
        var content = await ReadPackageContentAsync(package, organisation, project, SourceTreePath, ct).ConfigureAwait(false);
        if (content is null)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] Required file '{SourceTreePath}' is missing from the package."
            });
            return;
        }

        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(content);
        }
        catch (System.Text.Json.JsonException ex)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] File '{SourceTreePath}' contains malformed JSON: {ex.Message}"
            });
        }
    }

    private static async Task<string?> ReadPackageContentAsync(IPackageAccess package, string organisation, string project, string relativePath, CancellationToken ct)
    {
        var payload = await package.RequestContentAsync(
            CreatePackageContentContext(organisation, project, relativePath),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static async Task<bool> ContentExistsAsync(IPackageAccess package, string organisation, string project, string relativePath, CancellationToken ct)
        => await package.ContentExistsAsync(
            CreatePackageContentContext(organisation, project, relativePath),
            ct).ConfigureAwait(false);

    private static PackageContentContext CreatePackageContentContext(string organisation, string project, string relativePath)
        => relativePath switch
        {
            SourceTreePath => new(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: ModuleName,
                Address: new NodeSourceTreeAddress()),
            ReferencedPathsPath => new(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: ModuleName,
                Address: new ReferencedPathsContentAddress()),
            ReplicationProgressPath => new(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: project,
                Module: ModuleName,
                Address: new ReplicationProgressAddress()),
            _ => throw new InvalidOperationException(
                $"Unknown Nodes package path '{relativePath}'. Add an explicit case to CreatePackageContentContext.")
        };

    private sealed class ReferencedPathsContentAddress : IPackageContentAddress
    {
        public string RelativePath => "referenced-paths.json";
    }

    private sealed class ReplicationProgressAddress : IPackageContentAddress
    {
        public string RelativePath => "replication-progress.json";
    }
}
