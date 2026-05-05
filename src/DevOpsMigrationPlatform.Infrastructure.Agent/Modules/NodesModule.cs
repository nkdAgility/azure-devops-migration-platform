// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
    private readonly IDiscoveryMetrics? _discoveryMetrics;
    private readonly IMigrationMetrics? _migrationMetrics;
    private readonly NodesModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly INodesOrchestrator _orchestrator;

    public string Name => "Nodes";
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
        IDiscoveryMetrics? discoveryMetrics = null,
        IMigrationMetrics? migrationMetrics = null,
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
        _discoveryMetrics = discoveryMetrics;
        _migrationMetrics = migrationMetrics;
        _capture = capture;
        _reader = reader;
#if !NET481
        _targetEndpointInfo = targetEndpointInfo ?? throw new ArgumentNullException(nameof(targetEndpointInfo));
#endif
        _checkpointingFactory = checkpointingFactory;
    }

    public async Task InventoryAsync(InventoryContext context, CancellationToken ct)
    {
        var projects = (context.Projects ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (projects.Count == 0 && !string.IsNullOrWhiteSpace(_sourceEndpointInfo.Project))
            projects.Add(_sourceEndpointInfo.Project);

        using var activity = DiscoveryActivity.StartActivity("inventory.nodes");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);

        _logger.LogInformation("Inventorying {Module} for {ProjectCount} project(s)", Name, projects.Count);
        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Inventorying",
            Message = $"Inventorying {Name}",
            Timestamp = DateTimeOffset.UtcNow
        });

        var stopwatch = Stopwatch.StartNew();
        var count = 0;
        if (_reader is not null)
        {
            var orgUrl = context.SourceEndpoint.ResolvedUrl;
            var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(orgUrl);
            foreach (var project in projects)
            {
                try
                {
                    var projectCount = await _reader.CountNodesAsync(project, ct).ConfigureAwait(false);
                    count += projectCount;

                    var projectPath = PackagePathResolver.ProjectInventoryPath(orgSlug, project);
                    await ProjectInventoryFile.MergeAsync(
                        context.ArtefactStore, projectPath,
                        orgUrl: orgUrl, project: project,
                        nodes: projectCount, ct: ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    using (_logger.BeginDataScope(DataClassification.Customer))
                        _logger.LogWarning(ex, "Failed to count nodes for project {Project}; skipping.", project);
                }
            }
        }
        stopwatch.Stop();

        _discoveryMetrics?.RecordInventoryNodes(count, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        _logger.LogInformation("Inventoried {Module}: {Count} items in {DurationMs}ms", Name, count, stopwatch.ElapsedMilliseconds);
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
    }

    public async Task PrepareAsync(PrepareContext context, CancellationToken ct)
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

        var report = new PrepareReport
        {
            ModuleName = Name,
            ResolvedCount = 0
        };

        var stopwatch = Stopwatch.StartNew();
        _migrationMetrics?.RecordPrepareNodesResolved(report.ResolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        _migrationMetrics?.RecordPrepareNodesUnresolved(report.UnresolvedCount, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        await context.ArtefactStore.WriteAsync("Nodes/prepare-report.json", JsonSerializer.Serialize(report), ct).ConfigureAwait(false);
        stopwatch.Stop();

        _logger.LogInformation("Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms", Name, report.ResolvedCount, report.UnresolvedCount, stopwatch.ElapsedMilliseconds);
        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Prepared", Message = $"{Name} prepare complete", Timestamp = DateTimeOffset.UtcNow });
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

        await _orchestrator.ImportAsync(
            context, _sourceEndpointInfo, _targetEndpointInfo,
            _checkpointingFactory, _options.ReplicateSourceTree, ct).ConfigureAwait(false);
#endif
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(context.ArtefactStore, context, ct).ConfigureAwait(false);
    }
}
