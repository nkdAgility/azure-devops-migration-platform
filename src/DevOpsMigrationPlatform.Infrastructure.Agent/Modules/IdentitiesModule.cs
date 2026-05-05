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
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Modules;

/// <summary>
/// Thin <see cref="IModule"/> wrapper for identity export/import.
/// Delegates all orchestration to <see cref="IdentitiesOrchestrator"/>, which handles
/// JSONL streaming, checkpointing, progress events, and metrics.
/// </summary>
public sealed class IdentitiesModule : IModule
{
    private static readonly ActivitySource DiscoveryActivity = new(WellKnownActivitySourceNames.Discovery);
    private static readonly ActivitySource MigrationActivity = new(WellKnownActivitySourceNames.Migration);

    private readonly IIdentitySource? _identitySource;
#if !NET481
    private readonly IIdentityLookupTool? _identityLookupTool;
#endif
    private readonly ICheckpointingServiceFactory? _checkpointingFactory;
    private readonly ILogger<IdentitiesModule> _logger;
    private readonly IDiscoveryMetrics? _discoveryMetrics;
    private readonly IMigrationMetrics? _migrationMetrics;
    private readonly IdentitiesModuleOptions _options;
    private readonly ISourceEndpointInfo _sourceEndpointInfo;
    private readonly IIdentitiesOrchestrator _orchestrator;

    public string Name => "Identities";
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();
    public bool SupportsExport => true;
    public bool SupportsInventory => true;
    public bool SupportsPrepare => true;
    public bool SupportsImport => true;
    public bool SupportsValidate => false;

    public IdentitiesModule(
        ILogger<IdentitiesModule> logger,
    IOptions<IdentitiesModuleOptions> options,
        ISourceEndpointInfo sourceEndpointInfo,
        IIdentitiesOrchestrator orchestrator,
        IDiscoveryMetrics? discoveryMetrics = null,
        IMigrationMetrics? migrationMetrics = null,
        IIdentitySource? identitySource = null,
        ICheckpointingServiceFactory? checkpointingFactory = null
#if !NET481
        , IIdentityLookupTool? identityLookupTool = null
#endif
        )
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _sourceEndpointInfo = sourceEndpointInfo ?? throw new ArgumentNullException(nameof(sourceEndpointInfo));
        _orchestrator = orchestrator ?? throw new ArgumentNullException(nameof(orchestrator));
        _discoveryMetrics = discoveryMetrics;
        _migrationMetrics = migrationMetrics;
        _identitySource = identitySource;
        _checkpointingFactory = checkpointingFactory;
#if !NET481
        _identityLookupTool = identityLookupTool;
#endif
    }

    public async Task InventoryAsync(InventoryContext context, CancellationToken ct)
    {
        var projects = (context.Projects ?? Array.Empty<string>())
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (projects.Count == 0 && !string.IsNullOrWhiteSpace(_sourceEndpointInfo.Project))
            projects.Add(_sourceEndpointInfo.Project);

        using var activity = DiscoveryActivity.StartActivity("inventory.identities");
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

        var count = 0;
        if (_identitySource is not null)
        {
            foreach (var project in projects)
            {
                try
                {
                    await foreach (var _ in _identitySource.EnumerateIdentitiesAsync(project, ct).ConfigureAwait(false))
                        count++;
                }
                catch (Exception ex)
                {
                    using (_logger.BeginDataScope(DataClassification.Customer))
                        _logger.LogWarning(ex, "Failed to enumerate identities for project {Project}; skipping.", project);
                }
            }
        }

        var tags = new MetricsTagList
        {
            { "job.id", context.Job.JobId },
            { "module", Name }
        };
        _discoveryMetrics?.RecordInventoryIdentities(count, tags);

        var payload = JsonSerializer.Serialize(new { module = Name, identities = count, generatedAt = DateTimeOffset.UtcNow });
        await context.ArtefactStore.WriteAsync("Identities/inventory.json", payload, ct).ConfigureAwait(false);

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
    }

    public async Task PrepareAsync(PrepareContext context, CancellationToken ct)
    {
        using var activity = MigrationActivity.StartActivity("prepare.identities");
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
            ResolvedCount = 0,
            UnresolvedItems = []
        };

        var tags = new MetricsTagList
        {
            { "job.id", context.Job.JobId },
            { "module", Name }
        };
        _migrationMetrics?.RecordPrepareIdentitiesResolved(report.ResolvedCount, tags);
        _migrationMetrics?.RecordPrepareIdentitiesUnresolved(report.UnresolvedCount, tags);

        await context.ArtefactStore.WriteAsync("Identities/prepare-report.json", JsonSerializer.Serialize(report), ct).ConfigureAwait(false);

        _logger.LogInformation(
            "Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms",
            Name,
            report.ResolvedCount,
            report.UnresolvedCount,
            0);

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Prepared",
            Message = $"{Name} prepare complete",
            Timestamp = DateTimeOffset.UtcNow
        });
    }

    /// <inheritdoc/>
    public async Task ExportAsync(ExportContext context, CancellationToken ct)
    {
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping export.");
            return;
        }

        if (_identitySource is null)
        {
            _logger.LogWarning("[Identities] No IIdentitySource registered — identity export skipped.");
            return;
        }

        await _orchestrator.ExportAsync(
            _identitySource, context, _sourceEndpointInfo.Project,
            _checkpointingFactory, ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task ImportAsync(ImportContext context, CancellationToken ct)
    {
#if NET481
        // TFS is a source-only connector — import is not supported.
        _logger.LogDebug("[Identities] Import not supported on net481 (TFS agent) — skipping.");
        await Task.CompletedTask.ConfigureAwait(false);
#else
        if (!_options.Enabled)
        {
            _logger.LogDebug("[Identities] Module disabled — skipping import.");
            return;
        }

        await _orchestrator.ImportAsync(
            _identityLookupTool, context, _checkpointingFactory, ct).ConfigureAwait(false);
#endif
    }

    /// <inheritdoc/>
    public async Task ValidateAsync(ValidationContext context, CancellationToken ct)
    {
        await _orchestrator.ValidateAsync(
            context.ArtefactStore, context,
            ct).ConfigureAwait(false);
    }
}
