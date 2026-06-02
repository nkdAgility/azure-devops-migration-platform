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
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using DevOpsMigrationPlatform.Infrastructure.Agent.Modules;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Analysis;

public sealed class InventoryAnalyser : IAnalyser
{
    private static readonly ActivitySource ActivitySource = new(WellKnownActivitySourceNames.Discovery);
    private readonly ILogger<InventoryAnalyser> _logger;
    private readonly IPlatformMetrics? _metrics;

    public InventoryAnalyser(ILogger<InventoryAnalyser> logger, IPlatformMetrics? metrics = null)
    {
        _logger = logger;
        _metrics = metrics;
    }

    public string Name => "Inventory";

    // --- InventoryAnalyser depends on all inventory-capable modules, including Teams on net481 export paths. ---
    public IReadOnlyList<ModuleDependency> DependsOn => new[]
    {
        new ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Inventory),
        new ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Inventory),
        new ModuleDependency(typeof(NodesModule), DependencyPhase.Inventory),
        new ModuleDependency(typeof(TeamsModule), DependencyPhase.Inventory)
    };

    public async Task<TaskExecutionResult> AnalyseAsync(AnalyseContext context, CancellationToken ct)
    {
        using var activity = ActivitySource.StartActivity("analyse.inventory");
        activity?.SetTag("job.id", context.Job.JobId);

        _logger.LogInformation("Starting inventory analysis for {JobId}", context.Job.JobId);
        context.ProgressSink?.Emit(new ProgressEvent { Module = Name, Stage = "Analysing", Message = "Consolidating inventory outputs", Timestamp = DateTimeOffset.UtcNow });
        var startedAt = DateTime.UtcNow;

        try
        {
            // Read the InventoryReport written by InventoryOrchestrator (root inventory.json).
            // It contains org/project structure with WorkItems/Revisions/Repos counts.
            var rootJson = await ReadRootInventoryAsync(context.Package, ct).ConfigureAwait(false);

            InventoryReport? report = null;
            if (!string.IsNullOrWhiteSpace(rootJson))
            {
                try { report = JsonSerializer.Deserialize<InventoryReport>(rootJson!, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not parse root inventory.json as InventoryReport; will re-aggregate from per-project files."); }
            }

            var orgs = report?.Organisations ?? Array.Empty<OrganisationInventory>();

            var updatedOrgs = new List<OrganisationInventory>();
            foreach (var org in orgs)
            {
                var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(org.Url);
                var updatedProjects = new List<ProjectInventory>();

                foreach (var project in org.Projects)
                {
                    var perProject = await ProjectInventoryFile.ReadAsync(context.Package, orgSlug, project.Name, ct).ConfigureAwait(false);

                    updatedProjects.Add(project with
                    {
                        Identities = perProject.Identities > 0 ? perProject.Identities : project.Identities,
                        Nodes = perProject.Nodes > 0 ? perProject.Nodes : project.Nodes,
                        Teams = perProject.Teams > 0 ? perProject.Teams : project.Teams,
                        WorkItems = project.WorkItems == 0 && perProject.WorkItems > 0 ? perProject.WorkItems : project.WorkItems,
                        Revisions = project.Revisions == 0 && perProject.Revisions > 0 ? perProject.Revisions : project.Revisions,
                        Repos = project.Repos == 0 && perProject.Repos > 0 ? (int)perProject.Repos : project.Repos,
                    });
                }

                var orgTotals = new InventoryTotals
                {
                    WorkItems = updatedProjects.Sum(p => p.WorkItems),
                    Revisions = updatedProjects.Sum(p => p.Revisions),
                    Repos = updatedProjects.Sum(p => p.Repos),
                    Projects = updatedProjects.Count,
                    Identities = updatedProjects.Sum(p => p.Identities),
                    Nodes = updatedProjects.Sum(p => p.Nodes),
                    Teams = updatedProjects.Sum(p => p.Teams),
                };

                var updatedOrg = org with { Totals = orgTotals, Projects = updatedProjects };
                updatedOrgs.Add(updatedOrg);

                // Write per-org inventory file.
                var orgReport = new InventoryReport { GeneratedAt = DateTimeOffset.UtcNow, Totals = orgTotals, Organisations = new[] { updatedOrg } };
                await WriteInventoryAsync(
                    context.Package,
                    new PackageIndexContext("inventory.json", Organisation: orgSlug),
                    JsonSerializer.Serialize(orgReport, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
                    ct).ConfigureAwait(false);
            }

            var rootTotals = new InventoryTotals
            {
                WorkItems = updatedOrgs.Sum(o => o.Totals.WorkItems),
                Revisions = updatedOrgs.Sum(o => o.Totals.Revisions),
                Repos = updatedOrgs.Sum(o => o.Totals.Repos),
                Projects = updatedOrgs.Sum(o => o.Totals.Projects),
                Identities = updatedOrgs.Sum(o => o.Totals.Identities),
                Nodes = updatedOrgs.Sum(o => o.Totals.Nodes),
                Teams = updatedOrgs.Sum(o => o.Totals.Teams),
            };

            var finalReport = new InventoryReport
            {
                GeneratedAt = DateTimeOffset.UtcNow,
                Organisations = updatedOrgs,
                Totals = rootTotals
            };

            var finalJson = JsonSerializer.Serialize(finalReport, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            await WriteInventoryAsync(context.Package, new PackageIndexContext("inventory.json"), finalJson, ct).ConfigureAwait(false);

            // Write root CSV summarising all orgs/projects.
            var csvLines = new System.Text.StringBuilder();
            csvLines.AppendLine("Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,Identities,Nodes,Teams,IsComplete");
            foreach (var org in updatedOrgs)
                foreach (var p in org.Projects)
                    csvLines.AppendLine($"{org.Url},{p.Name},{p.WorkItems},{p.Revisions},{p.Repos},{p.Identities},{p.Nodes},{p.Teams},{p.IsComplete}");

            await WriteInventoryAsync(context.Package, new PackageIndexContext("inventory.csv"), csvLines.ToString(), ct).ConfigureAwait(false);

            var total = rootTotals.WorkItems + rootTotals.Identities + rootTotals.Nodes + rootTotals.Teams;
            if (total == 0)
                _logger.LogWarning("Zero consolidated inventory total for {JobId}", context.Job.JobId);

            var elapsed = (DateTime.UtcNow - startedAt).TotalMilliseconds;
            _metrics?.RecordInventoryConsolidated((int)total, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
            _metrics?.RecordInventoryConsolidatedDuration(elapsed, new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });

            _logger.LogInformation(
                "Completed inventory analysis for {JobId}: workItems={WorkItems} revisions={Revisions} repos={Repos} identities={Identities} nodes={Nodes} teams={Teams} in {DurationMs}ms",
                context.Job.JobId,
                rootTotals.WorkItems, rootTotals.Revisions, rootTotals.Repos,
                rootTotals.Identities, rootTotals.Nodes, rootTotals.Teams,
                elapsed);

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
        catch
        {
            _metrics?.RecordInventoryConsolidatedErrors(new MetricsTagList { { "job.id", context.Job.JobId }, { "module", Name } });
            throw;
        }

        return TaskExecutionResult.Completed();
    }

    private static async Task<string?> ReadRootInventoryAsync(IPackageAccess package, CancellationToken cancellationToken)
    {
        var payload = await package.RequestIndexAsync(
            new PackageIndexContext("inventory.json"),
            cancellationToken).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new System.IO.StreamReader(payload.Content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static async Task WriteInventoryAsync(IPackageAccess package, PackageIndexContext context, string content, CancellationToken cancellationToken)
    {
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(content), writable: false);
        await package.PersistIndexAsync(context, new PackagePayload(stream, "application/json"), cancellationToken).ConfigureAwait(false);
    }
}
