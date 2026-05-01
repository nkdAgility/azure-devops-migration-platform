using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Abstractions.Streaming;
#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
#endif
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// <see cref="IModule"/> implementation for classification tree (node structure) export/import.
/// Wraps the existing <see cref="IClassificationTreeCapture"/> and <see cref="INodeEnsurer"/>
/// tools with the module lifecycle contract.
/// On net481 (TFS agent): only <see cref="ExportAsync"/> is active; <see cref="ImportAsync"/>
/// is a no-op since TFS is a source-only connector.
/// Note on localised root names: <see cref="IClassificationTreeCapture"/> normalises
/// German "Bereich"/"Iteration" roots to English "Area"/"Iteration" in the captured JSON.
/// </summary>
public sealed class NodesModule : IModule
{
    private const string SourceTreePath = "Nodes/source-tree.json";
    private const string ModuleName = "Nodes";

    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly IClassificationTreeCapture? _capture;
#if !NET481
    private readonly INodeEnsurer? _nodeEnsurer;
    private readonly IMigrationMetrics? _migrationMetrics;
#endif
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<NodesModule> _logger;
    private readonly NodesModuleOptions _options;
    private readonly IAgentJobContext _agentJobContext;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
#if !NET481
    private readonly ITargetEndpointInfo _targetEndpointInfo;
#endif

    public string Name => ModuleName;
    public IReadOnlyList<string> DependsOn => Array.Empty<string>();

    public NodesModule(
        ILogger<NodesModule> logger,
        IOptions<NodesModuleOptions> options,
        IAgentJobContext agentJobContext,
        ISourceEndpointInfo sourceEndpointInfo,
        IClassificationTreeCapture? capture = null,
#if !NET481
        ITargetEndpointInfo? targetEndpointInfo = null,
        INodeEnsurer? nodeEnsurer = null,
#endif
        ICheckpointingServiceFactory? checkpointingFactory = null
#if !NET481
        , IMigrationMetrics? migrationMetrics = null
#endif
        )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _agentJobContext = agentJobContext ?? throw new ArgumentNullException(nameof(agentJobContext));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _capture = capture;
#if !NET481
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _nodeEnsurer = nodeEnsurer;
        _migrationMetrics = migrationMetrics;
#endif
        _checkpointingFactory = checkpointingFactory;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Nodes] Module disabled — skipping export.");
            return;
        }

        if (_capture is null)
        {
            _logger.LogWarning("[Nodes] No IClassificationTreeCapture registered — node export skipped.");
            return;
        }

        using var activity = s_activitySource.StartActivity("nodes.export");

        var exportSink = context.ProgressSink;
        exportSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Nodes.Export.Started",
            Message = $"Starting node tree capture for project '{_sourceEndpointInfo.Project}'.",
        });

        // Idempotency: skip if already completed.
        if (_checkpointingFactory is not null)
        {
            var checkpointing = _checkpointingFactory.Create(context.StateStore);
            var cursor = await checkpointing.ReadCursorAsync(ModuleName, ct).ConfigureAwait(false);
            if (cursor?.Stage == CursorStage.Completed
                && await context.ArtefactStore.ExistsAsync(SourceTreePath, ct).ConfigureAwait(false))
            {
                _logger.LogInformation("[Nodes] Already exported (cursor found) — skipping re-export.");
                return;
            }
        }

        var nodeCount = await _capture.CaptureAsync(
            context.ArtefactStore, ct
#if !NET481
            , _migrationMetrics, context.Job.JobId, context.ProgressSink, ModuleName
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
        if (_checkpointingFactory is not null)
        {
            var checkpointing = _checkpointingFactory.Create(context.StateStore);
            await checkpointing.WriteCursorAsync(ModuleName, new CursorEntry
            {
                LastProcessed = SourceTreePath,
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);
        }
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        // TFS is a source-only connector — import is not supported.
        _logger.LogDebug("[Nodes] Import not supported on net481 (TFS agent) — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
#else
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Nodes] Module disabled — skipping import.");
            return;
        }

        if (_nodeEnsurer is null)
        {
            _logger.LogWarning("[Nodes] No INodeEnsurer registered — node import skipped.");
            return;
        }

        using var activity = s_activitySource.StartActivity("nodes.import");

        var importSink = context.ProgressSink;
        importSink?.Emit(new ProgressEvent
        {
            Module = ModuleName,
            Stage = "Nodes.Import.Started",
            Message = $"Starting node replication for project '{_targetEndpointInfo.Project}'.",
        });

        var project = _targetEndpointInfo.Project;
        var sourceProject = _sourceEndpointInfo.Project;
        var mapping = new ProjectMapping(sourceProject, project);

        if (_options.ReplicateSourceTree)
        {
            _logger.LogInformation("[Nodes] Replicating source tree.");
            await _nodeEnsurer.ReplicateSourceTreeAsync(
                mapping,
                context.ArtefactStore, context.StateStore,
                ct, _migrationMetrics, context.Job.JobId).ConfigureAwait(false);
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
        if (_checkpointingFactory is not null)
        {
            var checkpointing = _checkpointingFactory.Create(context.StateStore);
            await checkpointing.WriteCursorAsync(ModuleName, new CursorEntry
            {
                LastProcessed = "Nodes/import",
                Stage = CursorStage.Completed,
                UpdatedAt = DateTimeOffset.UtcNow
            }, ct).ConfigureAwait(false);
        }
#endif
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        var artefactStore = context.ArtefactStore;

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
            using var doc = JsonDocument.Parse(content);
        }
        catch (JsonException ex)
        {
            context.Errors.Add(new ValidationError
            {
                Path = SourceTreePath,
                Message = $"[Nodes] File '{SourceTreePath}' contains malformed JSON: {ex.Message}"
            });
        }
    }
}
