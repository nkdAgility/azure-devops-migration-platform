// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using DevOpsMigrationPlatform.Infrastructure.Agent.Checkpointing;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
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
    private readonly ITeamTarget? _teamTarget;
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<TeamsModule> _logger;
    private readonly IPlatformMetrics? _PlatformMetrics;
    private readonly TeamsModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly ITargetEndpointInfo _targetEndpointInfo;
    private readonly ITeamsOrchestrator _orchestrator;

    public string Name => "Teams";
    public IReadOnlyList<ModuleDependency> DependsOn => new[]
    {
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Import),
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Import)
    };
    public bool SupportsExport => true;
    public bool SupportsInventory => true;
    public bool SupportsPrepare => true;
    public bool SupportsImport => true;
    public bool SupportsValidate => false;

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
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _PlatformMetrics = PlatformMetrics;
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _checkpointingFactory = checkpointingFactory;
    }

    public async Task<TaskExecutionResult> CaptureAsync(InventoryContext context, CancellationToken ct)
    {
        using var activity = DiscoveryActivity.StartActivity("inventory.teams");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        activity?.SetTag("org", context.SourceEndpoint?.ResolvedUrl ?? string.Empty);
        activity?.SetTag("project", context.Project);

        if (string.IsNullOrWhiteSpace(context.Project))
        {
            _logger.LogError("[Teams] CaptureAsync called with empty Project — executor contract violated. Skipping.");
            return TaskExecutionResult.Skipped("CaptureAsync called with empty project.");
        }

        _logger.LogInformation("Inventorying {Module}", Name);
        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Inventorying", Message = $"Inventorying {Name}", Timestamp = DateTimeOffset.UtcNow });

        var count = 0;
        if (_teamSource is not null)
        {
            var project = context.Project;
            var orgUrl = context.SourceEndpoint?.ResolvedUrl ?? _sourceEndpointInfo.Url;
            var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(orgUrl);

            try
            {
                await foreach (var _ in _teamSource.EnumerateTeamsAsync(project, ct).ConfigureAwait(false))
                    count++;

                var projectPath = PackagePathResolver.ProjectInventoryPath(orgSlug, project);
                await ProjectInventoryFile.MergeAsync(
                    context.ArtefactStore, projectPath,
                    orgUrl: orgUrl, project: project,
                    teams: count, ct: ct).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                using (_logger.BeginDataScope(DataClassification.Customer))
                    _logger.LogWarning(ex, "Failed to enumerate teams for project {Project}; skipping.", project);
            }
        }

        _PlatformMetrics?.RecordInventoryTeams(count, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        _logger.LogInformation("Inventoried {Module}: {Count} items", Name, count);
        if (count == 0)
            _logger.LogWarning("Zero items inventoried for {Module}", Name);

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Inventoried",
            Message = $"{Name} inventory complete",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Discovery = new DiscoveryCounters
                {
                    Inventory = new InventoryCounters { RevisionsTotal = count }
                }
            }
        });

        return TaskExecutionResult.Completed();
    }

    public async Task<TaskExecutionResult> PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = MigrationActivity.StartActivity("prepare.teams");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        _logger.LogInformation("Preparing {Module}", Name);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Preparing", Message = $"Preparing {Name}", Timestamp = DateTimeOffset.UtcNow });
        var report = new PrepareReport { ModuleName = Name, ResolvedCount = 0 };
        _PlatformMetrics?.RecordPrepareTeamsResolved(report.ResolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        _PlatformMetrics?.RecordPrepareTeamsUnresolved(report.UnresolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        await context.ArtefactStore.WriteAsync("Teams/prepare-report.json", JsonSerializer.Serialize(report), ct).ConfigureAwait(false);
        _logger.LogInformation("Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms", Name, report.ResolvedCount, report.UnresolvedCount, 0);
        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Prepared", Message = $"{Name} prepare complete", Timestamp = DateTimeOffset.UtcNow });

        return TaskExecutionResult.Completed();
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
            context, _sourceEndpointInfo, _targetEndpointInfo,
            _checkpointingFactory, _options, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }

    /// <inheritdoc/>
    public async Task<TaskExecutionResult> ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(context.ArtefactStore, context, ct).ConfigureAwait(false);

        return TaskExecutionResult.Completed();
    }
}
#endif
