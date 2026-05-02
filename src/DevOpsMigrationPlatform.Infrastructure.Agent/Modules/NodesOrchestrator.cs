using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
#endif
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Orchestrates classification-tree (node) export and import operations.
/// Handles checkpointing, progress events, and metrics — delegates the actual
/// tree capture to <see cref="IClassificationTreeCapture"/> and node replication
/// to <see cref="INodeEnsurer"/>.
/// </summary>
internal sealed class NodesOrchestrator
{
    private const string SourceTreePath = "Nodes/source-tree.json";
    private const string ModuleName = "Nodes";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly ILogger _logger;

    public NodesOrchestrator(ILogger logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
#if !NET481
        IMigrationMetrics? migrationMetrics,
#endif
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
            var checkpointing = checkpointingFactory.Create(context.StateStore);
            var cursor = await checkpointing.ReadCursorAsync(ModuleName, ct).ConfigureAwait(false);
            if (cursor?.Stage == CursorStage.Completed
                && await context.ArtefactStore.ExistsAsync(SourceTreePath, ct).ConfigureAwait(false))
            {
                _logger.LogInformation("[Nodes] Already exported (cursor found) — skipping re-export.");
                return;
            }
        }

        var nodeCount = await capture.CaptureAsync(
            context.ArtefactStore, ct
#if !NET481
            , migrationMetrics, context.Job.JobId, context.ProgressSink, ModuleName
#endif
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
            var checkpointing = checkpointingFactory.Create(context.StateStore);
            await checkpointing.WriteCursorAsync(ModuleName, new CursorEntry
            {
                LastProcessed = SourceTreePath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);
        }
    }

#if !NET481
    /// <summary>
    /// Replicates the source classification tree into the target project via <paramref name="nodeEnsurer"/>.
    /// Writes checkpoint on completion.
    /// </summary>
    public async Task ImportAsync(
        INodeEnsurer nodeEnsurer,
        ImportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        IMigrationMetrics? migrationMetrics,
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
            await nodeEnsurer.ReplicateSourceTreeAsync(
                mapping,
                context.ArtefactStore, context.StateStore,
                ct, migrationMetrics, context.Job.JobId).ConfigureAwait(false);
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
            var checkpointing = checkpointingFactory.Create(context.StateStore);
            await checkpointing.WriteCursorAsync(ModuleName, new CursorEntry
            {
                LastProcessed = "Nodes/import",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);
        }
    }
#endif

    /// <summary>
    /// Validates that the source-tree artefact exists and is valid JSON.
    /// </summary>
    public async Task ValidateAsync(IArtefactStore artefactStore, ValidationContext context, CancellationToken ct)
    {
        var exists = await artefactStore.ExistsAsync(SourceTreePath, ct).ConfigureAwait(false);
        if (!exists)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] Required file '{SourceTreePath}' is missing from the package."
            });
            return;
        }

        var content = await artefactStore.ReadAsync(SourceTreePath, ct).ConfigureAwait(false);
        if (content is null)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] File '{SourceTreePath}' exists but could not be read."
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
}
