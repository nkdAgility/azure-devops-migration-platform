// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
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
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
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
    private readonly IDiscoveryMetrics? _discoveryMetrics;
    private readonly IMigrationMetrics? _migrationMetrics;
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
        IDiscoveryMetrics? discoveryMetrics = null,
        IMigrationMetrics? migrationMetrics = null,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        ICheckpointingServiceFactory? checkpointingFactory = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _discoveryMetrics = discoveryMetrics;
        _migrationMetrics = migrationMetrics;
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _checkpointingFactory = checkpointingFactory;
    }

    public async Task InventoryAsync(InventoryContext context, CancellationToken ct)
    {
        using var activity = DiscoveryActivity.StartActivity("inventory.teams");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);

        _logger.LogInformation("Inventorying {Module} for {Project}", Name, _sourceEndpointInfo.Project);
        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Inventorying", Message = $"Inventorying {Name}", Timestamp = DateTimeOffset.UtcNow });

        var count = 0;
        if (_teamSource is not null)
        {
            await foreach (var _ in _teamSource.EnumerateTeamsAsync(_sourceEndpointInfo.Project, ct).ConfigureAwait(false))
                count++;
        }

        await context.ArtefactStore.WriteAsync("Teams/inventory.json", JsonSerializer.Serialize(new { module = Name, teams = count, generatedAt = DateTimeOffset.UtcNow }), ct).ConfigureAwait(false);
        _discoveryMetrics?.RecordInventoryTeams(count, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        _logger.LogInformation("Inventoried {Module}: {Count} items in {DurationMs}ms", Name, count, 0);
        if (count == 0)
            _logger.LogWarning("Zero items inventoried for {Module} in {Project}", Name, _sourceEndpointInfo.Project);

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
    }

    public async Task PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = MigrationActivity.StartActivity("prepare.teams");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        _logger.LogInformation("Preparing {Module}", Name);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Preparing", Message = $"Preparing {Name}", Timestamp = DateTimeOffset.UtcNow });
        var report = new PrepareReport { ModuleName = Name, ResolvedCount = 0 };
        _migrationMetrics?.RecordPrepareTeamsResolved(report.ResolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        _migrationMetrics?.RecordPrepareTeamsUnresolved(report.UnresolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        await context.ArtefactStore.WriteAsync("Teams/prepare-report.json", JsonSerializer.Serialize(report), ct).ConfigureAwait(false);
        _logger.LogInformation("Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms", Name, report.ResolvedCount, report.UnresolvedCount, 0);
        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Prepared", Message = $"{Name} prepare complete", Timestamp = DateTimeOffset.UtcNow });
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Teams] Module disabled — skipping export.");
            return;
        }

        if (_teamSource is null)
        {
            _logger.LogWarning("[Teams] No ITeamSource registered — team export skipped.");
            return;
        }

        await _orchestrator.ExportAsync(
            _teamSource, context, _sourceEndpointInfo, _checkpointingFactory,
            _options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Teams] Module disabled — skipping import.");
            return;
        }

        if (_teamTarget is null)
        {
            _logger.LogWarning("[Teams] No ITeamTarget registered — team import skipped.");
            return;
        }

        await _orchestrator.ImportAsync(
            context, _sourceEndpointInfo, _targetEndpointInfo,
            _checkpointingFactory, _options, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(context.ArtefactStore, context, ct).ConfigureAwait(false);
    }
}
#endif
