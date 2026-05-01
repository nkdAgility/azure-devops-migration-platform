using System;
using System.Collections.Generic;
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
/// </list>
/// </summary>
internal sealed class JobExecutionPlanBuilder : IJobExecutionPlanBuilder
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IPhaseTrackingServiceFactory _phaseTrackingFactory;
    private readonly ILogger<JobExecutionPlanBuilder> _logger;

    public JobExecutionPlanBuilder(
        IPhaseTrackingServiceFactory phaseTrackingFactory,
        ILogger<JobExecutionPlanBuilder> logger)
    {
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

    private static List<JobTask> BuildExportTasks(
        IConfiguration config,
        JobPhaseRecord? phaseRecord,
        long? workItemKnownTotal,
        ref int order)
    {
        bool exportAlreadyDone = phaseRecord?.ExportCompleted == true;

        var tasks = new List<JobTask>
        {
            MakeTask("export.identities", "Identities Export", "Export",
                IsEnabled(config, "Identities"), exportAlreadyDone, order++),
            MakeTask("export.nodes", "Nodes Export", "Export",
                IsEnabled(config, "Nodes"), exportAlreadyDone, order++),
            MakeTask("export.teams", "Teams Export", "Export",
                IsEnabled(config, "Teams"), exportAlreadyDone, order++),
            MakeTask("export.workitems", "WorkItems Export", "Export",
                IsEnabled(config, "WorkItems"), exportAlreadyDone, order++,
                knownTotal: workItemKnownTotal),
        };

        return tasks;
    }

    // ── Import phase ─────────────────────────────────────────────────────────

    private static List<JobTask> BuildImportTasks(
        IConfiguration config,
        JobPhaseRecord? phaseRecord,
        ref int order)
    {
        bool importAlreadyDone = phaseRecord?.ImportCompleted == true;

        return new List<JobTask>
        {
            MakeTask("import.identities", "Identities Import", "Import",
                IsEnabled(config, "Identities"), importAlreadyDone, order++),
            MakeTask("import.nodes", "Nodes Import", "Import",
                IsEnabled(config, "Nodes"), importAlreadyDone, order++),
            MakeTask("import.teams", "Teams Import", "Import",
                IsEnabled(config, "Teams"), importAlreadyDone, order++),
            MakeTask("import.workitems", "WorkItems Import", "Import",
                IsEnabled(config, "WorkItems"), importAlreadyDone, order++),
        };
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static JobTask MakeTask(
        string id,
        string name,
        string phase,
        bool enabled,
        bool phaseAlreadyDone,
        int order,
        long? knownTotal = null)
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
            SkipReason = skipReason
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

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("Totals", out var totals) &&
                totals.TryGetProperty("WorkItems", out var wi))
            {
                return wi.GetInt64();
            }
        }
        catch (Exception)
        {
            // inventory.json may not exist or may be malformed — not an error.
        }

        return null;
    }
}
