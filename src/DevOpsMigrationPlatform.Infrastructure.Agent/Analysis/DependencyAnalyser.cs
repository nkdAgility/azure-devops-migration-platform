// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

public sealed class DependencyAnalyser : IOrganisationsAnalyser
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);
    private readonly IDependencyDiscoveryServiceFactory _dependencyFactory;
    private readonly IDependencyOrchestrator _orchestrator;
    private readonly ILogger<DependencyAnalyser> _logger;
    private readonly IDiscoveryMetrics? _metrics;

    public DependencyAnalyser(
        IDependencyDiscoveryServiceFactory dependencyFactory,
        IDependencyOrchestrator orchestrator,
        ILogger<DependencyAnalyser> logger,
        IDiscoveryMetrics? metrics = null)
    {
        _dependencyFactory = dependencyFactory;
        _orchestrator = orchestrator;
        _logger = logger;
        _metrics = metrics;
    }

    public string Name => "Dependencies";
    public IReadOnlyList<ModuleDependency> DependsOn => Array.Empty<ModuleDependency>();

    public Task AnalyseAsync(AnalyseContext context, CancellationToken ct)
        => AnalyseAsync(new OrganisationsAnalyseContext
        {
            Job = context.Job,
            ArtefactStore = context.ArtefactStore,
            StateStore = context.StateStore,
            ProgressSink = context.ProgressSink,
            Organisations = []
        }, ct);

    public async Task AnalyseAsync(OrganisationsAnalyseContext context, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("analyse.dependencies");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        _logger.LogInformation("Starting dependency analysis for {JobId}", context.Job.JobId);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Analysing", Message = "Running dependency analysis", Timestamp = DateTimeOffset.UtcNow });

        var organisations = new List<ScopedOrganisationEndpoint>();
        var policies = new JobPolicies();
        var dependencyService = _dependencyFactory.Create(organisations, policies);
        var exportContext = new ExportContext
        {
            Job = context.Job,
            ArtefactStore = context.ArtefactStore,
            StateStore = context.StateStore,
            ProgressSink = context.ProgressSink ?? new NullProgressSink(),
            Organisations = organisations
        };
        await _orchestrator.ExportAsync(dependencyService, exportContext, organisations, policies, 300, ct).ConfigureAwait(false);

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } };
        _metrics?.RecordDependenciesAnalyseDuration(0, tags);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Analysed", Message = "Dependency analysis complete", Timestamp = DateTimeOffset.UtcNow });
    }

    private sealed class NullProgressSink : IProgressSink
    {
        public void Emit(ProgressEvent evt) { }
    }
}

