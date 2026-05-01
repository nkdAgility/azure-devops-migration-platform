using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Jobs;
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

        var includeExport = kind == JobKind.Export || kind == JobKind.Migrate;
        var includeImport = kind == JobKind.Import || kind == JobKind.Migrate;

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
            tasks.AddRange(BuildExportTasks(packageConfig, phaseRecord, workItemKnownTotal, ref order));
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
        JobPhaseRecord? phaseRecord,
        long? workItemKnownTotal,
        ref int order)
    {
        bool exportAlreadyDone = phaseRecord?.ExportCompleted == true;

        var tasks = new List<JobTask>();

        // Export tasks have no dependencies — all modules run concurrently.
        foreach (var module in _modules)
        {
            var taskId = $"export.{module.Name.ToLowerInvariant()}";
            var enabled = IsEnabled(config, module.Name);
            var knownTotal = module.Name.Equals("WorkItems", StringComparison.OrdinalIgnoreCase)
                ? workItemKnownTotal
                : null;

            tasks.Add(MakeTask(
                taskId,
                $"{module.Name} Export",
                "Export",
                enabled,
                exportAlreadyDone,
                order++,
                knownTotal: knownTotal,
                dependsOn: null)); // Export tasks have no inter-module dependencies
        }

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

        // Build task for each enabled module, mapping its DependsOn to task IDs.
        foreach (var module in _modules)
        {
            var taskId = $"import.{module.Name.ToLowerInvariant()}";
            var enabled = IsEnabled(config, module.Name);

            // Map module dependencies to task IDs; skip dependencies that are not enabled or registered.
            var dependsOn = module.DependsOn
                .Where(depName => _modulesByName.ContainsKey(depName) && IsEnabled(config, depName))
                .Select(depName => $"import.{depName.ToLowerInvariant()}")
                .ToList();

            if (module.DependsOn.Count > dependsOn.Count)
            {
                var missing = module.DependsOn.Where(d => !_modulesByName.ContainsKey(d) || !IsEnabled(config, d));
                foreach (var dep in missing)
                {
                    _logger.LogWarning(
                        "Module {Module} depends on {Dependency}, but that module is not enabled or not registered. Dependency omitted from task.",
                        module.Name, dep);
                }
            }

            tasks.Add(MakeTask(
                taskId,
                $"{module.Name} Import",
                "Import",
                enabled,
                importAlreadyDone,
                order++,
                dependsOn: dependsOn.Count > 0 ? dependsOn.AsReadOnly() : null));
        }

        return tasks;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

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
}
