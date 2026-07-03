// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin <see cref="IModule"/> wrapper for classification tree (node structure) export/import.
/// Delegates all orchestration to <see cref="NodesOrchestrator"/>, which handles tree capture
/// and node replication directly.
/// On net481 (TFS agent): only <see cref="ExportAsync"/> is active; <see cref="ImportAsync"/>
/// is a no-op since TFS is a source-only connector.
/// </summary>
public sealed class NodesModule : IModule
{
    private static readonly ActivitySource DiscoveryActivity = new(WellKnownActivitySourceNames.Discovery);
    private static readonly ActivitySource MigrationActivity = new(WellKnownActivitySourceNames.Migration);

    private readonly IClassificationTreeCapture? _capture;
    private readonly IClassificationTreeReader? _reader;
#if !NET481
    private readonly ITargetEndpointInfo _targetEndpointInfo;
#endif
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<NodesModule> _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly NodesModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly INodesOrchestrator _orchestrator;

    public string Name => "Nodes";

    /// <inheritdoc cref="IModule.Contract"/>
    private static readonly IModuleContract NodesContract = new ModuleContract(
        moduleName: "Nodes",
        selection: [],
        data: [new DataDefinition("ClassificationNodes", Required: true)],
        processing: [new ProcessingDefinition("ReplicateSourceTree", Required: false)]);

    /// <inheritdoc cref="IModule.Contract"/>
    public IModuleContract Contract => NodesContract;
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsInventory => true;
    public bool SupportsPrepare => true;
    public bool SupportsImport => true;
    public bool SupportsValidate => false;

    public NodesModule(
        ILogger<NodesModule> logger,
        IOptions<NodesModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        INodesOrchestrator orchestrator,
        IPlatformMetrics? PlatformMetrics = null,
        IClassificationTreeCapture? capture = null,
#if !NET481
        ITargetEndpointInfo? targetEndpointInfo = null,
#endif
        IClassificationTreeReader? reader = null,
        ICheckpointingServiceFactory? checkpointingFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _PlatformMetrics = PlatformMetrics;
        _capture = capture;
        _reader = reader;
#if !NET481
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
#endif
        _checkpointingFactory = checkpointingFactory;
    }

    public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
        => _orchestrator.CaptureAsync(_reader, context, _sourceEndpointInfo.Url, ct);

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = MigrationActivity.StartActivity("prepare.nodes");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);

        _logger.LogInformation("Preparing {Module}", Name);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Preparing",
            Message = $"Preparing {Name}",
            Timestamp = DateTimeOffset.UtcNow
        });

        // Delegate report generation and persistence (prepare-report.json + metrics)
        // to the orchestrator.
        var (organisation, project) = ResolvePrepareScope(context);
        await _orchestrator.PrepareAsync(context, organisation, project, ct).ConfigureAwait(false);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Prepared", Message = $"{Name} prepare complete", Timestamp = DateTimeOffset.UtcNow });

        return TaskExecutionResult.Completed();
    }

    private (string Organisation, string Project) ResolvePrepareScope(PrepareContext context)
    {
        var organisation = _sourceEndpointInfo.OrganisationSlug;
        if (string.IsNullOrWhiteSpace(organisation))
        {
            organisation = context.TargetEndpoint.OrganisationSlug;
        }

        var project = _sourceEndpointInfo.Project;
        if (string.IsNullOrWhiteSpace(project))
        {
            project = context.TargetEndpoint.Project;
        }

        if (string.IsNullOrWhiteSpace(organisation))
        {
            organisation = "unknown";
        }
        if (string.IsNullOrWhiteSpace(project))
        {
            project = "unknown";
        }

        return (organisation!, project!);
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Nodes] Module disabled — skipping export.");
            return TaskExecutionResult.Skipped("Nodes module disabled for export.");
        }

        if (_capture is null)
        {
            _logger.LogWarning("[Nodes] No IClassificationTreeCapture registered — node export skipped.");
            return TaskExecutionResult.Skipped("No classification tree capture registered.");
        }

        await _orchestrator.ExportAsync(
            _capture, context, _sourceEndpointInfo, _checkpointingFactory,
            ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        _logger.LogDebug("[Nodes] Import not supported on net481 — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
        return TaskExecutionResult.Skipped("Nodes import is not supported on net481.");
#else
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Nodes] Module disabled — skipping import.");
            return TaskExecutionResult.Skipped("Nodes module disabled for import.");
        }

        // FR-007 / GAP-003: when source-tree replication is off, skip without calling the
        // orchestrator at all (the orchestrator must not be invoked in this case).
        if (!_options.Processing.ReplicateSourceTree)
        {
            _logger.LogDebug("[Nodes] ReplicateSourceTree is false — skipping classification-tree import.");
            return TaskExecutionResult.Skipped("Nodes import skipped: ReplicateSourceTree is false.");
        }

        await _orchestrator.ImportAsync(
            context, _sourceEndpointInfo, _targetEndpointInfo,
            _checkpointingFactory, _options.Processing.ReplicateSourceTree, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
#endif
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(
            context.Package,
            _sourceEndpointInfo.OrganisationSlug,
            _sourceEndpointInfo.Project,
            context,
            ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }
}
