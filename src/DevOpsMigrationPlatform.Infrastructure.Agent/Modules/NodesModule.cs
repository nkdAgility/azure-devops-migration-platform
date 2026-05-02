using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
#endif
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin <see cref="IModule"/> wrapper for classification tree (node structure) export/import.
/// Delegates all orchestration to <see cref="NodesOrchestrator"/>, which in turn delegates
/// the actual tree capture to <see cref="IClassificationTreeCapture"/> and node replication
/// to <see cref="INodeEnsurer"/>.
/// On net481 (TFS agent): only <see cref="ExportAsync"/> is active; <see cref="ImportAsync"/>
/// is a no-op since TFS is a source-only connector.
/// </summary>
public sealed class NodesModule : IModule
{
    private readonly IClassificationTreeCapture? _capture;
#if !NET481
    private readonly INodeEnsurer? _nodeEnsurer;
    private readonly IMigrationMetrics? _migrationMetrics;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
#endif
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<NodesModule> _logger;
    private readonly NodesModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly NodesOrchestrator _orchestrator;

    public string Name => "Nodes";
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsImport => true;

    public NodesModule(
        ILogger<NodesModule> logger,
        IOptions<NodesModuleOptions> options,
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
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _capture = capture;
#if !NET481
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _nodeEnsurer = nodeEnsurer;
        _migrationMetrics = migrationMetrics;
#endif
        _checkpointingFactory = checkpointingFactory;
        _orchestrator = new NodesOrchestrator(logger);
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

        await _orchestrator.ExportAsync(
            _capture, context, _sourceEndpointInfo, _checkpointingFactory,
#if !NET481
            _migrationMetrics,
#endif
            ct).ConfigureAwait(false);
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

        await _orchestrator.ImportAsync(
            _nodeEnsurer, context, _sourceEndpointInfo, _targetEndpointInfo,
            _checkpointingFactory, _migrationMetrics, _options.ReplicateSourceTree, ct).ConfigureAwait(false);
#endif
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(context.ArtefactStore, context, ct).ConfigureAwait(false);
    }
}

