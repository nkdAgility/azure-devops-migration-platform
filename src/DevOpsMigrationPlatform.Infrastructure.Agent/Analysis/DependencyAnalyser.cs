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
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;
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
        var sw = Stopwatch.StartNew();
        using var activity = ActivitySource.StartActivity("analyse.dependencies");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        _logger.LogInformation("Starting dependency analysis for {JobId}", context.Job.JobId);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Analysing", Message = "Running dependency analysis", Timestamp = DateTimeOffset.UtcNow });

        var organisations = context.Organisations
            .Select(o => new ScopedOrganisationEndpoint
            {
                Endpoint = new OrganisationMigrationEndpointOptions(o),
                Projects = new List<string>()
            })
            .ToList();
        var policies = new JobPolicies();
        var dependencyService = _dependencyFactory.Create(organisations, policies);
        await _orchestrator.AnalyseAsync(dependencyService, context, policies, 300, ct).ConfigureAwait(false);

        var csv = await context.ArtefactStore.ReadAsync("dependencies.csv", ct).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(csv))
        {
            await context.ArtefactStore.WriteAsync("analysis/dependencies.csv", csv!, ct).ConfigureAwait(false);
        }

        var mermaid = BuildMermaid(csv);
        await context.ArtefactStore.WriteAsync("analysis/dependencies.mmd", mermaid, ct).ConfigureAwait(false);

        var tags = new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } };
        _metrics?.RecordDependenciesAnalyseDuration(sw.Elapsed.TotalMilliseconds, tags);
        var rowCount = CountRows(csv);
        _metrics?.RecordLinksFound(rowCount, tags);
        _metrics?.RecordWorkItemsAnalysed(rowCount, tags);
        if (rowCount == 0)
        {
            _metrics?.RecordDependenciesAnalyseErrors(tags);
            _logger.LogWarning(
                "Zero cross-project dependency links written for {JobId} — verify source organisations are reachable and contain linked work items",
                context.Job.JobId);
        }

        _logger.LogInformation(
            "Dependency analysis complete: {Links} cross-project links found across {WorkItems} work items in {DurationMs}ms",
            rowCount,
            rowCount,
            sw.Elapsed.TotalMilliseconds);

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Analysed",
            Message = "Dependency analysis complete",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Discovery = new DiscoveryCounters
                {
                    Dependencies = new DependencyCounters
                    {
                        ExternalLinksFound = rowCount,
                        WorkItemsAnalysed = rowCount
                    }
                }
            }
        });
    }

    private static int CountRows(string? csv)
    {
        var csvContent = csv ?? string.Empty;
        if (string.IsNullOrWhiteSpace(csvContent))
            return 0;

        var lines = csvContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        return Math.Max(0, lines.Length - 1);
    }

    private static string BuildMermaid(string? csv)
    {
        var csvContent = csv ?? string.Empty;
        if (string.IsNullOrWhiteSpace(csvContent))
            return "graph TD\n";

        var lines = csvContent.Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
        var graphLines = new List<string> { "graph TD" };
        foreach (var line in lines.Skip(1))
        {
            var cols = line.Split(',');
            if (cols.Length < 9)
                continue;

            var sourceProject = cols[2].Trim();
            var targetProject = cols[8].Trim();
            if (string.IsNullOrWhiteSpace(sourceProject) || string.IsNullOrWhiteSpace(targetProject))
                continue;

            graphLines.Add($"    {Sanitize(sourceProject)} --> {Sanitize(targetProject)}");
        }

        return string.Join("\n", graphLines) + "\n";
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));

    private sealed class OrganisationMigrationEndpointOptions(OrganisationEndpoint endpoint) : MigrationEndpointOptions
    {
        public override OrganisationEndpoint ToOrganisationEndpoint() => endpoint;
        public override string GetResolvedUrl() => endpoint.ResolvedUrl;
    }
}

