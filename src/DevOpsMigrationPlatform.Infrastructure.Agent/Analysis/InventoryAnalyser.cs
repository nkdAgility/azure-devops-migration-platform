// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

public sealed class InventoryAnalyser : IAnalyser
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);
    private readonly ILogger<InventoryAnalyser> _logger;
    private readonly IDiscoveryMetrics? _metrics;

    public InventoryAnalyser(ILogger<InventoryAnalyser> logger, IDiscoveryMetrics? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public string Name => "Inventory";

    public IReadOnlyList<ModuleDependency> DependsOn => new[]
    {
        new ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Inventory),
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Inventory),
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Inventory),
#if !NET481
        new ModuleDependency(typeof(TeamsModule), DependencyPhase.Inventory)
#else
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Inventory)
#endif
    };

    public async Task AnalyseAsync(AnalyseContext context, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("analyse.inventory");
        activity?.SetTag("job.id", context.Job.JobId);

        _logger.LogInformation("Starting inventory analysis for {JobId}", context.Job.JobId);
        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Analysing", Message = "Consolidating inventory outputs", Timestamp = DateTimeOffset.UtcNow });

        var moduleFiles = new Dictionary<string, string>
        {
            ["WorkItems"] = "WorkItems/inventory.json",
            ["Identities"] = "Identities/inventory.json",
            ["Nodes"] = "Nodes/inventory.json",
            ["Teams"] = "Teams/inventory.json"
        };

        var rows = new List<(string Module, long Count)>();
        foreach (var pair in moduleFiles)
        {
            var json = await context.ArtefactStore.ReadAsync(pair.Value, ct).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger.LogWarning("Missing module inventory file for {Module}: {Path}", pair.Key, pair.Value);
                continue;
            }

            using var doc = JsonDocument.Parse(json!);
            var root = doc.RootElement;
            long count = 0;
            if (root.TryGetProperty("workItems", out var wi)) count = wi.GetInt64();
            else if (root.TryGetProperty("identities", out var id)) count = id.GetInt64();
            else if (root.TryGetProperty("nodes", out var nodes)) count = nodes.GetInt64();
            else if (root.TryGetProperty("teams", out var teams)) count = teams.GetInt64();
            rows.Add((pair.Key, count));
        }

        var total = rows.Sum(r => r.Count);
        if (total == 0)
            _logger.LogWarning("Zero consolidated inventory total for {JobId}", context.Job.JobId);

        var csv = new StringBuilder();
        csv.AppendLine("Module,Count");
        foreach (var row in rows)
            csv.AppendLine($"{row.Module},{row.Count}");

        var consolidated = new
        {
            generatedAt = DateTimeOffset.UtcNow,
            totals = new { workItems = rows.Where(r => r.Module == "WorkItems").Sum(r => r.Count), all = total },
            modules = rows.Select(r => new { name = r.Module, count = r.Count }).ToArray()
        };

        await context.ArtefactStore.WriteAsync("inventory.csv", csv.ToString(), ct).ConfigureAwait(false);
        await context.ArtefactStore.WriteAsync("inventory.json", JsonSerializer.Serialize(consolidated), ct).ConfigureAwait(false);

        _metrics?.RecordInventoryConsolidated((int)total, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
        _metrics?.RecordInventoryConsolidatedDuration(0, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });

        context.ProgressSink?.Emit(new ProgressEvent
        {
            Module = Name,
            Stage = "Analysed",
            Message = "Inventory analysis complete",
            Timestamp = DateTimeOffset.UtcNow,
            Metrics = new JobMetrics
            {
                Migration = new MigrationCounters
                {
                    Inventory = new ModulePhaseCounters { Completed = total }
                }
            }
        });
    }
}

