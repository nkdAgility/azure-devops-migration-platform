// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
using DevOpsMigrationPlatform.Infrastructure.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin <see cref="IModule"/> wrapper for team export/import.
/// Delegates all orchestration to <see cref="TeamsOrchestrator"/>, which handles
/// the enumeration loop, checkpointing, progress events, and metrics, and in turn
/// delegates per-team operations to <see cref="TeamExportOrchestrator"/> and
/// <see cref="TeamImportOrchestrator"/>.
/// </summary>
/// <remarks>
/// <strong>Connector coverage:</strong> Team import is supported for
/// <c>AzureDevOpsServices</c> and <c>Simulated</c> connectors only.
/// TFS (TeamFoundationServer) is a <em>source-only</em> connector — it is always
/// the migration origin, never the destination.
/// </remarks>
public sealed class TeamsModule : IModule
{
    private static readonly ActivitySource DiscoveryActivity = new(WellKnownActivitySourceNames.Discovery);
    private static readonly ActivitySource MigrationActivity = new(WellKnownActivitySourceNames.Migration);

    private readonly ITeamSource? _teamSource;
#if !NET481
    private readonly ITeamTarget? _teamTarget;
#endif
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<TeamsModule> _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly TeamsModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
#if !NET481
    private readonly ITargetEndpointInfo? _targetEndpointInfo;
#endif
    private readonly ITeamsOrchestrator _orchestrator;

    public string Name => "Teams";

    /// <inheritdoc cref="IModule.Contract"/>
    private static readonly IModuleContract TeamsContract = new ModuleContract(
        moduleName: "Teams",
        selection:
        [
            new SelectionDefinition("Scope", Required: true),
            new SelectionDefinition("Filter", Required: false)
        ],
        data:
        [
            new DataDefinition("TeamSettings", Required: false),
            new DataDefinition("TeamIterations", Required: false),
            new DataDefinition("TeamMembers", Required: false),
            new DataDefinition("TeamCapacity", Required: false)
        ],
        processing:
        [
            new ProcessingDefinition("AlwaysExport", Required: false),
            new ProcessingDefinition("NodeTranslation", Required: false),
            new ProcessingDefinition("IdentityLookup", Required: false)
        ]);

    /// <inheritdoc cref="IModule.Contract"/>
    public IModuleContract Contract => TeamsContract;
    public IReadOnlyList<ModuleDependency> DependsOn => new[]
    {
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Import),
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Import)
    };
    public bool SupportsExport => true;
    public bool SupportsInventory => true;
    public bool SupportsPrepare => true;
#if !NET481
    public bool SupportsImport => true;
#else
    public bool SupportsImport => false;
#endif
    public bool SupportsValidate => false;

    public TeamsModule(
        ILogger<TeamsModule> logger,
        IOptions<TeamsModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        ITeamsOrchestrator orchestrator,
        IPlatformMetrics? PlatformMetrics = null,
        ITeamSource? teamSource = null,
        ICheckpointingServiceFactory? checkpointingFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _PlatformMetrics = PlatformMetrics;
        _teamSource = teamSource;
        _checkpointingFactory = checkpointingFactory;
    }

#if !NET481
    public TeamsModule(
        ILogger<TeamsModule> logger,
        IOptions<TeamsModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        ITeamsOrchestrator orchestrator,
        IPlatformMetrics? PlatformMetrics = null,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        ICheckpointingServiceFactory? checkpointingFactory = null)
        : this(logger, options, sourceEndpointInfo, orchestrator, PlatformMetrics, teamSource, checkpointingFactory)
    {
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _teamTarget = teamTarget;
    }
#endif

    public Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
        => _orchestrator.CaptureAsync(_teamSource, context, _sourceEndpointInfo.Url, ct);

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = MigrationActivity.StartActivity("prepare.teams");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        _logger.LogInformation("Preparing {Module}", Name);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Preparing", Message = $"Preparing {Name}", Timestamp = DateTimeOffset.UtcNow });

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
            _logger.LogDebug("[Teams] Module disabled — skipping export.");
            return TaskExecutionResult.Skipped("Teams module disabled for export.");
        }

        if (_teamSource is null)
        {
            _logger.LogWarning("[Teams] No ITeamSource registered — team export skipped.");
            return TaskExecutionResult.Skipped("No team source registered.");
        }

        await _orchestrator.ExportAsync(
            _teamSource, context, _sourceEndpointInfo, _checkpointingFactory,
            _options, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }

    /// <inheritdoc/>
#if !NET481
    public async Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Teams] Module disabled — skipping import.");
            return TaskExecutionResult.Skipped("Teams module disabled for import.");
        }

        if (_teamTarget is null)
        {
            _logger.LogWarning("[Teams] No ITeamTarget registered — team import skipped.");
            return TaskExecutionResult.Skipped("No team target registered.");
        }

        await _orchestrator.ImportAsync(
            context, _sourceEndpointInfo, _targetEndpointInfo!,
            _checkpointingFactory, _options, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }
#else
    public Task<TaskExecutionResult> ImportAsync(ImportContext context, CancellationToken ct)
    {
        _logger.LogWarning("[Teams] Import is not supported on net481.");
        return Task.FromResult(TaskExecutionResult.Skipped("Teams import is not supported on net481."));
    }
#endif

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(context.Package, _sourceEndpointInfo.Url, _sourceEndpointInfo.Project, context, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }

}
