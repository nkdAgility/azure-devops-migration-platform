// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default implementation of <see cref="IJobExecutionPlanBuilder"/>.
/// Constructs an ordered <see cref="JobTaskList"/> at job start by inspecting:
/// <list type="bullet">
///   <item>The job kind to select the applicable phases (Export / Import / Migrate).</item>
///   <item>Module-enabled flags in <c>migration-config.json</c>.</item>
///   <item>Phase records in the state store to detect already-completed phases on resume.</item>
///   <item><c>inventory.json</c> in the artefact store to populate <see cref="JobTask.KnownTotal"/> for WorkItems tasks.</item>
///   <item>Module <c>DependsOn</c> declarations to build the dependency graph for Import-phase tasks.</item>
/// </list>
/// </summary>
internal sealed class JobExecutionPlanBuilder : IJobExecutionPlanBuilder
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IEnumerable<IModule> _modules;
    private readonly Dictionary<string, IModule> _modulesByName;
    private readonly IPhaseTrackingServiceFactory _phaseTrackingFactory;
    private readonly ILogger<JobExecutionPlanBuilder> _logger;

    public JobExecutionPlanBuilder(
        IEnumerable<IModule> modules,
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        ILogger<JobExecutionPlanBuilder> logger)
    {
        _modules = modules;
        _modulesByName = modules.ToDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);
        _phaseTrackingFactory = phaseTrackingFactory;
        _logger = logger;

        // Diagnostic: log all discovered modules at startup
        _logger.LogInformation(
            "JobExecutionPlanBuilder initialized with {Count} modules: {Modules}",
            _modules.Count(),
            string.Join(", ", _modules.Select(m => $"{m.Name}(Export:{m.SupportsExport},Import:{m.SupportsImport})")));
    }

    public async Task<JobTaskList> BuildPlanAsync(
        IConfiguration packageConfig,
        JobKind kind,
        IArtefactStore artefactStore,
        IStateStore stateStore,
        CancellationToken ct)
    {
        var tasks = new List<JobTask>();
        int order = 0;

        var includeExport = kind is JobKind.Export or JobKind.Migrate
                            or JobKind.Inventory or JobKind.Dependencies;
        var includeImport = kind is JobKind.Import or JobKind.Migrate;

        // Read phase record once (only relevant for Migrate kind).
        JobPhaseRecord? phaseRecord = null;
        if (kind == JobKind.Migrate)
        {
            var phaseTracker = _phaseTrackingFactory.Create(stateStore);
            phaseRecord = await phaseTracker.ReadPhaseRecordAsync(ct).ConfigureAwait(false);
        }

        // Try to read inventory.json for KnownTotal on WorkItems export task.
        long? workItemKnownTotal = await TryReadWorkItemTotalAsync(artefactStore, ct)
            .ConfigureAwait(false);

        if (includeExport)
        {
            tasks.AddRange(BuildExportTasks(packageConfig, kind, phaseRecord, workItemKnownTotal, ref order));
        }

        if (includeImport)
        {
            tasks.AddRange(BuildImportTasks(packageConfig, phaseRecord, ref order));
        }

        // Validate no circular dependencies in Import phase before returning.
        if (includeImport)
        {
            ValidateNoCycles(tasks.Where(t => t.Phase == "Import").ToList());
        }

        _logger.LogInformation(
            "Built execution plan with {TaskCount} tasks for job kind {Kind}.",
            tasks.Count, kind);

        return new JobTaskList
        {
            Tasks = tasks.AsReadOnly(),
            PushedAt = DateTimeOffset.UtcNow
        };
    }

    // ── Export phase ─────────────────────────────────────────────────────────

    private List<JobTask> BuildExportTasks(
        IConfiguration config,
        JobKind kind,
        JobPhaseRecord? phaseRecord,
        long? workItemKnownTotal,
        ref int order)
    {
        bool exportAlreadyDone = phaseRecord?.ExportCompleted == true;

        var exportModules = _modules.Where(m => m.SupportsExport).ToList();

        _logger.LogDebug(
            "Discovered {Count} export modules: {Modules}",
            exportModules.Count,
            string.Join(", ", exportModules.Select(m => m.Name)));

        // Determine which modules are needed based on job kind.
        // For Inventory/Dependencies, only the specific module (+ transitive deps) is needed.
        // For Export/Migrate, modules listed in config (+ transitive deps) are needed.
        var needed = ResolveNeededExportModules(config, kind, exportModules);

        // Pre-compute the set of export task IDs so we can resolve inter-module DependsOn.
        var exportTaskIds = new HashSet<string>(
            exportModules
                .Where(m => needed.Contains(m.Name))
                .Select(m => $"export.{m.Name.ToLowerInvariant()}"),
            StringComparer.OrdinalIgnoreCase);

        var tasks = new List<JobTask>();

        foreach (var module in exportModules)
        {
            // Module must be needed: either explicitly enabled in config,
            // or transitively required by a module that is.
            if (!needed.Contains(module.Name))
            {
                _logger.LogDebug("Skipping module {ModuleName}: not needed (not in config and no enabled module depends on it)", module.Name);
                continue;
            }

            _logger.LogDebug(
                "Module {ModuleName}: Needed={Needed}, SupportsExport={SupportsExport}",
                module.Name, needed.Contains(module.Name), module.SupportsExport);

            var taskId = $"export.{module.Name.ToLowerInvariant()}";
            var knownTotal = module.Name.Equals("WorkItems", StringComparison.OrdinalIgnoreCase)
                ? workItemKnownTotal
                : null;

            // Export tasks honor module.DependsOn entries where phase is Export or Both.
            var exportDeps = new List<string>();
            foreach (var dep in module.DependsOn.Where(d => d.AppliesToExport))
            {
                var depTaskId = $"export.{dep.ModuleName.ToLowerInvariant()}";
                if (exportTaskIds.Contains(depTaskId) && !exportDeps.Contains(depTaskId, StringComparer.OrdinalIgnoreCase))
                    exportDeps.Add(depTaskId);
            }

            var dependsOn = exportDeps.Count > 0 ? exportDeps.AsReadOnly() : null;

            tasks.Add(MakeTask(
                taskId,
                $"{module.Name} Export",
                "Export",
                enabled: true,
                exportAlreadyDone,
                0, // placeholder — reassigned after topological sort
                knownTotal: knownTotal,
                dependsOn: dependsOn));
        }

        // Topologically sort so Order reflects dependency order in the CLI display
        // (e.g. Inventory appears before WorkItems which depends on it).
        tasks = TopologicalSort(tasks);
        for (int i = 0; i < tasks.Count; i++)
            tasks[i] = tasks[i] with { Order = order++ };

        return tasks;
    }

    // ── Import phase ─────────────────────────────────────────────────────────

    private List<JobTask> BuildImportTasks(
        IConfiguration config,
        JobPhaseRecord? phaseRecord,
        ref int order)
    {
        bool importAlreadyDone = phaseRecord?.ImportCompleted == true;

        var tasks = new List<JobTask>();

        // Build task for each module that supports import AND is enabled.
        foreach (var module in _modules.Where(m => m.SupportsImport))
        {
            var enabled = IsEnabled(config, module.Name);

            // Skip disabled modules - no task should be created.
            if (!enabled)
                continue;

            var taskId = $"import.{module.Name.ToLowerInvariant()}";

            // Map module dependencies that apply to import phase to task IDs; skip dependencies that are not enabled or registered.
            var dependsOn = module.DependsOn
                .Where(dep => dep.AppliesToImport)
                .Where(dep => _modulesByName.ContainsKey(dep.ModuleName) && IsEnabled(config, dep.ModuleName))
                .Select(dep => $"import.{dep.ModuleName.ToLowerInvariant()}")
                .ToList();

            var importDeps = module.DependsOn.Where(d => d.AppliesToImport).ToList();
            if (importDeps.Count > dependsOn.Count)
            {
                var missing = importDeps.Where(d => !_modulesByName.ContainsKey(d.ModuleName) || !IsEnabled(config, d.ModuleName));
                foreach (var dep in missing)
                {
                    _logger.LogWarning(
                        "Module {Module} depends on {Dependency} for import, but that module is not enabled or not registered. Dependency omitted from task.",
                        module.Name, dep.ModuleName);
                }
            }

            tasks.Add(MakeTask(
                taskId,
                $"{module.Name} Import",
                "Import",
                enabled: true,  // Always true here since we skipped disabled modules above
                importAlreadyDone,
                order++,
                dependsOn: dependsOn.Count > 0 ? dependsOn.AsReadOnly() : null));
        }

        return tasks;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    internal AnalyseContext ResolveAnalyseContextForAnalyser(
        IAnalyser analyser,
        Job job,
        IConfiguration packageConfig,
        IArtefactStore artefactStore,
        IStateStore stateStore,
        IProgressSink? progressSink)
    {
        if (analyser is IEndpointPairAnalyser)
        {
            return new EndpointPairAnalyseContext
            {
                Job = job,
                ArtefactStore = artefactStore,
                StateStore = stateStore,
                ProgressSink = progressSink,
                SourceEndpoint = BuildSourceEndpointInfo(packageConfig),
                TargetEndpoint = BuildTargetEndpointInfo(packageConfig)
            };
        }

        if (analyser is IOrganisationsAnalyser)
        {
            return new OrganisationsAnalyseContext
            {
                Job = job,
                ArtefactStore = artefactStore,
                StateStore = stateStore,
                ProgressSink = progressSink,
                Organisations = BuildOrganisationEndpoints(packageConfig)
            };
        }

        return new AnalyseContext
        {
            Job = job,
            ArtefactStore = artefactStore,
            StateStore = stateStore,
            ProgressSink = progressSink
        };
    }

    private static JobTask MakeTask(
        string id,
        string name,
        string phase,
        bool enabled,
        bool phaseAlreadyDone,
        int order,
        long? knownTotal = null,
        IReadOnlyList<string>? dependsOn = null)
    {
        var (status, skipReason) = !enabled
            ? (JobTaskStatus.Skipped, "Module disabled in configuration.")
            : phaseAlreadyDone
                ? (JobTaskStatus.Skipped, "Phase already completed on previous run.")
                : (JobTaskStatus.Pending, (string?)null);

        return new JobTask
        {
            Id = id,
            Name = name,
            Phase = phase,
            Order = order,
            Status = status,
            KnownTotal = knownTotal,
            SkipReason = skipReason,
            DependsOn = dependsOn
        };
    }

    private static bool IsEnabled(IConfiguration config, string moduleName)
    {
        var raw = config[$"MigrationPlatform:Modules:{moduleName}:Enabled"];
        if (raw is null)
            return true; // default on when not explicitly set
        return !string.Equals(raw, "false", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines which export modules are needed for the given job kind.
    /// <list type="bullet">
    ///   <item><b>Inventory</b>: only InventoryDiscovery (+ transitive deps).</item>
    ///   <item><b>Dependencies</b>: only DependencyDiscovery (+ transitive deps).</item>
    ///   <item><b>Export / Migrate</b>: modules explicitly listed in config (+ transitive deps).</item>
    /// </list>
    /// Transitive deps are walked via export-phase <see cref="ModuleDependency"/> entries.
    /// </summary>
    private HashSet<string> ResolveNeededExportModules(
        IConfiguration config,
        JobKind kind,
        List<IModule> exportModules)
    {
        // Seed the set based on job kind.
        var seeds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        switch (kind)
        {
            case JobKind.Inventory:
                seeds.Add("InventoryDiscovery");
                break;

            case JobKind.Dependencies:
                seeds.Add("Dependencies");
                break;

            default: // Export, Migrate
                foreach (var module in exportModules)
                {
                    if (IsExplicitlyEnabled(config, module.Name))
                        seeds.Add(module.Name);
                }
                break;
        }

        // Walk export-phase dependencies transitively.
        var needed = new HashSet<string>(seeds, StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<string>(seeds);
        while (queue.Count > 0)
        {
            var name = queue.Dequeue();
            if (!_modulesByName.TryGetValue(name, out var mod))
                continue;
            foreach (var dep in mod.DependsOn.Where(d => d.AppliesToExport))
            {
                if (needed.Add(dep.ModuleName))
                    queue.Enqueue(dep.ModuleName);
            }
        }

        _logger.LogDebug(
            "Export modules needed for {Kind} (seeds + transitive): {Modules}",
            kind, string.Join(", ", needed));

        return needed;
    }

    /// <summary>
    /// Returns true if the module has a config section AND is not explicitly disabled.
    /// A module with no config section at all is NOT considered explicitly enabled —
    /// it can only appear in the plan if transitively required by another module's DependsOn.
    /// This prevents discovery modules (Dependencies, Inventory) from appearing unless
    /// pulled in by a dependency or explicitly listed in the config.
    /// </summary>
    private static bool IsExplicitlyEnabled(IConfiguration config, string moduleName)
    {
        var section = config.GetSection($"MigrationPlatform:Modules:{moduleName}");
        if (!section.Exists())
            return false; // not listed in config — not explicitly enabled
        var raw = section["Enabled"];
        if (raw is null)
            return true; // section exists but no Enabled key — default on
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }



    private static async Task<long?> TryReadWorkItemTotalAsync(
        IArtefactStore artefactStore,
        CancellationToken ct)
    {
        try
        {
            var json = await artefactStore.ReadAsync("inventory.json", ct).ConfigureAwait(false);
            if (json is null)
                return null;

            // inventory.json is serialised with camelCase naming policy, so use the
            // deserialiser (case-insensitive by default) rather than JsonElement
            // TryGetProperty (case-sensitive) to avoid a silent miss.
            var report = JsonSerializer.Deserialize<InventoryReport>(json, _jsonOptions);
            if (report?.Totals.WorkItems > 0)
                return report.Totals.WorkItems;
        }
        catch (Exception)
        {
            // inventory.json may not exist or may be malformed — not an error.
        }

        return null;
    }

    private static IReadOnlyList<OrganisationEndpoint> BuildOrganisationEndpoints(IConfiguration packageConfig)
    {
        var organisations = new List<OrganisationEndpoint>();
        var configured = packageConfig.GetSection("MigrationPlatform:Organisations").GetChildren();
        foreach (var child in configured)
        {
            var url = child["Url"];
            if (string.IsNullOrWhiteSpace(url))
                continue;

            organisations.Add(new OrganisationEndpoint
            {
                Type = child["Type"] ?? "Unknown",
                ResolvedUrl = url!
            });
        }

        if (organisations.Count == 0)
        {
            var sourceUrl = packageConfig["MigrationPlatform:Source:Url"];
            if (!string.IsNullOrWhiteSpace(sourceUrl))
            {
                organisations.Add(new OrganisationEndpoint
                {
                    Type = packageConfig["MigrationPlatform:Source:Type"] ?? "Unknown",
                    ResolvedUrl = sourceUrl!
                });
            }
        }

        return organisations;
    }

    private static ISourceEndpointInfo BuildSourceEndpointInfo(IConfiguration packageConfig)
        => new ConfigSourceEndpointInfo(
            packageConfig["MigrationPlatform:Source:Url"] ?? string.Empty,
            packageConfig["MigrationPlatform:Source:Project"] ?? string.Empty,
            packageConfig["MigrationPlatform:Source:Type"] ?? string.Empty);

    private static ITargetEndpointInfo BuildTargetEndpointInfo(IConfiguration packageConfig)
        => new ConfigTargetEndpointInfo(
            packageConfig["MigrationPlatform:Target:Url"] ?? string.Empty,
            packageConfig["MigrationPlatform:Target:Project"] ?? string.Empty,
            packageConfig["MigrationPlatform:Target:Type"] ?? string.Empty);

    private sealed class ConfigSourceEndpointInfo(string url, string project, string connectorType) : ISourceEndpointInfo
    {
        public string Url { get; } = url;
        public string Project { get; } = project;
        public string ConnectorType { get; } = connectorType;
        public OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
    }

    private sealed class ConfigTargetEndpointInfo(string url, string project, string connectorType) : ITargetEndpointInfo
    {
        public string Url { get; } = url;
        public string Project { get; } = project;
        public string ConnectorType { get; } = connectorType;
        public OrganisationEndpoint ToOrganisationEndpoint() => new() { ResolvedUrl = Url, Type = ConnectorType };
    }

    /// <summary>
    /// Validates that the Import-phase tasks have no circular dependencies.
    /// Uses Kahn's algorithm (topological sort) to detect cycles.
    /// Throws <see cref="InvalidOperationException"/> if a cycle is found.
    /// </summary>
    private static void ValidateNoCycles(List<JobTask> importTasks)
    {
        if (importTasks.Count == 0)
            return;

        // Build adjacency list and in-degree count.
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in importTasks)
        {
            graph[task.Id] = new List<string>();
            inDegree[task.Id] = 0;
        }

        foreach (var task in importTasks)
        {
            if (task.DependsOn is not null)
            {
                foreach (var dep in task.DependsOn)
                {
                    if (graph.ContainsKey(dep))
                    {
                        graph[dep].Add(task.Id);
                        inDegree[task.Id]++;
                    }
                    // If dep is not in the graph (disabled/not registered), it's already filtered out upstream.
                }
            }
        }

        // Kahn's algorithm: process tasks with no dependencies.
        var queue = new Queue<string>(inDegree.Where(kvp => kvp.Value == 0).Select(kvp => kvp.Key));
        var processed = 0;

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            processed++;

            foreach (var neighbor in graph[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    queue.Enqueue(neighbor);
            }
        }

        if (processed < importTasks.Count)
        {
            // Cycle detected — find one cycle for the error message.
            var cycle = FindCycle(graph, inDegree);
            throw new InvalidOperationException(
                $"Circular dependency detected in Import-phase modules: {string.Join(" → ", cycle)}");
        }
    }

    /// <summary>
    /// Finds one cycle in the dependency graph for error reporting.
    /// Uses DFS from a node with non-zero in-degree (known to be in a cycle).
    /// </summary>
    private static List<string> FindCycle(
        Dictionary<string, List<string>> graph,
        Dictionary<string, int> inDegree)
    {
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recStack = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var path = new List<string>();

        // Start from any node with in-degree > 0 (known to be in a cycle).
        var start = inDegree.First(kvp => kvp.Value > 0).Key;

        bool Dfs(string node)
        {
            visited.Add(node);
            recStack.Add(node);
            path.Add(node);

            foreach (var neighbor in graph[node])
            {
                if (!visited.Contains(neighbor))
                {
                    if (Dfs(neighbor))
                        return true;
                }
                else if (recStack.Contains(neighbor))
                {
                    // Cycle found — trim path to the cycle.
                    var cycleStart = path.IndexOf(neighbor);
                    path = path.Skip(cycleStart).ToList();
                    path.Add(neighbor); // Close the cycle.
                    return true;
                }
            }

            recStack.Remove(node);
            path.RemoveAt(path.Count - 1);
            return false;
        }

        Dfs(start);
        return path;
    }

    /// <summary>
    /// Topologically sorts tasks by their <see cref="JobTask.DependsOn"/> edges using Kahn's algorithm.
    /// Tasks with no dependencies come first; tasks that depend on others come after their dependencies.
    /// Within the same tier, original list order is preserved.
    /// </summary>
    private static List<JobTask> TopologicalSort(List<JobTask> tasks)
    {
        if (tasks.Count <= 1)
            return tasks;

        var taskDict = tasks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);
        var graph = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var inDegree = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var task in tasks)
        {
            graph[task.Id] = new List<string>();
            inDegree[task.Id] = 0;
        }

        foreach (var task in tasks)
        {
            if (task.DependsOn is not null)
            {
                foreach (var dep in task.DependsOn)
                {
                    if (graph.ContainsKey(dep))
                    {
                        graph[dep].Add(task.Id);
                        inDegree[task.Id]++;
                    }
                }
            }
        }

        var sorted = new List<JobTask>();
        var ready = new Queue<string>(
            tasks.Where(t => inDegree[t.Id] == 0).Select(t => t.Id));

        while (ready.Count > 0)
        {
            var current = ready.Dequeue();
            sorted.Add(taskDict[current]);

            foreach (var neighbor in graph[current])
            {
                inDegree[neighbor]--;
                if (inDegree[neighbor] == 0)
                    ready.Enqueue(neighbor);
            }
        }

        return sorted;
    }
}
