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

public sealed class DependencyAnalyser : IOrganisationsAnalyser, IProjectAnalyser
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);
    private readonly IDependencyDiscoveryServiceFactory _dependencyFactory;
    private readonly IDependencyOrchestrator _orchestrator;
    private readonly ILogger<DependencyAnalyser> _logger;
    private readonly IPlatformMetrics? _metrics;

    public DependencyAnalyser(
        IDependencyDiscoveryServiceFactory dependencyFactory,
        IDependencyOrchestrator orchestrator,
        ILogger<DependencyAnalyser> logger,
        IPlatformMetrics? metrics = null)
    {
        _dependencyFactory = dependencyFactory;
        _orchestrator = orchestrator;
        _logger = logger;
        _metrics = metrics;
    }

    public string Name => "Dependencies";

    // Dependencies analysis runs after inventory consolidation.
    public IReadOnlyList<ModuleDependency> DependsOn => Array.AsReadOnly(new[]
    {
        new ModuleDependency(typeof(InventoryAnalyser), DependencyPhase.Analyse)
    });

    public Task AnalyseAsync(AnalyseContext context, CancellationToken ct)
    {
        // If the caller already created an OrganisationsAnalyseContext (e.g. JobAgentWorker),
        // preserve the organisations it populated rather than discarding them.
        if (context is OrganisationsAnalyseContext orgsContext)
            return AnalyseAsync(orgsContext, ct);

        return AnalyseAsync(new OrganisationsAnalyseContext
        {
            Job = context.Job,
            ArtefactStore = context.ArtefactStore,
            StateStore = context.StateStore,
            ProgressSink = context.ProgressSink,
            Policies = context.Policies,
            Organisations = []
        }, ct);
    }

    public async Task AnalyseAsync(OrganisationsAnalyseContext context, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        using var activity = ActivitySource.StartActivity("analyse.dependencies");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("module", Name);
        _logger.LogInformation("Starting dependency analysis fan-in for {JobId}", context.Job.JobId);

        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Analysing", Message = "Running dependency analysis", Timestamp = DateTimeOffset.UtcNow });

        // Fan-in: consolidate per-project discovery/{org}/{project}/dependencies.csv files.
        var perProjectPaths = new System.Collections.Generic.List<string>();
        await foreach (var path in context.ArtefactStore.EnumerateAsync("discovery/", ct).ConfigureAwait(false))
        {
            if (path.EndsWith("/dependencies.csv", System.StringComparison.OrdinalIgnoreCase))
                perProjectPaths.Add(path);
        }

        string? csv;
        if (perProjectPaths.Count > 0)
        {
            // Consolidate all per-project CSVs (keep one header, append all data rows).
            const string CsvHeader =
                "SourceWorkItemId,SourceWorkItemType,SourceProject,SourceOrganisationUrl," +
                "LinkType,LinkScope,TargetWorkItemId,TargetProject,TargetOrganisation,TargetStatus,LinkChangedDate,SourceWorkItemChangedDate,SourceWorkItemStateCategory";
            var consolidated = new System.Text.StringBuilder();
            consolidated.AppendLine(CsvHeader);
            foreach (var path in perProjectPaths) // already lexicographic per EnumerateAsync contract
            {
                var content = await context.ArtefactStore.ReadAsync(path, ct).ConfigureAwait(false);
                if (string.IsNullOrWhiteSpace(content))
                    continue;
                var lines = content.Split(new[] { '\n' }, System.StringSplitOptions.RemoveEmptyEntries);
                foreach (var line in lines.Skip(1)) // skip each file's header
                    consolidated.AppendLine(line);
            }
            csv = consolidated.ToString();
            await context.ArtefactStore.WriteAsync("dependencies.csv", csv, ct).ConfigureAwait(false);
            _logger.LogInformation(
                "Consolidated {FileCount} per-project dependency files for {JobId}.",
                perProjectPaths.Count, context.Job.JobId);
        }
        else
        {
            // Backward-compat: no per-project captures found — run full orchestrator analysis.
            _logger.LogInformation("No per-project dependency artefacts found — falling back to full orchestrator analysis for {JobId}.", context.Job.JobId);
            var organisations = context.Organisations.ToList();
            var policies = context.Policies;
            var dependencyService = _dependencyFactory.Create(organisations, policies);
            await _orchestrator.AnalyseAsync(
                dependencyService,
                context,
                policies,
                policies.CheckpointIntervalSeconds,
                ct).ConfigureAwait(false);
            csv = await context.ArtefactStore.ReadAsync("dependencies.csv", ct).ConfigureAwait(false);
        }

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

    /// <inheritdoc />
    public async Task CaptureProjectAsync(InventoryContext context, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("capture.dependencies.project");
        activity?.SetTag("job.id", context.Job.JobId);
        activity?.SetTag("organisation.url", context.SourceEndpoint?.ResolvedUrl);
        activity?.SetTag("project.name", context.Project);

        var dependencyService = _dependencyFactory.CreateForProject(
            context.Organisations,
            context.SourceEndpoint?.ResolvedUrl ?? string.Empty,
            context.Project,
            context.Policies);

        await _orchestrator.CaptureProjectAsync(dependencyService, context, context.Policies, ct).ConfigureAwait(false);
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
            var cols = ParseCsvLine(line);
            if (cols.Count < 8)
                continue;

            var sourceProject = cols[2].Trim();
            var targetProject = cols[7].Trim();
            if (string.IsNullOrWhiteSpace(sourceProject) || string.IsNullOrWhiteSpace(targetProject))
                continue;

            graphLines.Add($"    {Sanitize(sourceProject)} --> {Sanitize(targetProject)}");
        }

        return string.Join("\n", graphLines) + "\n";
    }

    private static System.Collections.Generic.List<string> ParseCsvLine(string line)
    {
        var result = new System.Collections.Generic.List<string>();
        var current = new System.Text.StringBuilder();
        bool inQuotes = false;
        foreach (var ch in line)
        {
            if (ch == '"')
            {
                inQuotes = !inQuotes;
            }
            else if (ch == ',' && !inQuotes)
            {
                result.Add(current.ToString());
                current.Clear();
            }
            else
            {
                current.Append(ch);
            }
        }
        result.Add(current.ToString());
        return result;
    }

    private static string Sanitize(string value)
        => string.Concat(value.Select(ch => char.IsLetterOrDigit(ch) ? ch : '_'));
}

