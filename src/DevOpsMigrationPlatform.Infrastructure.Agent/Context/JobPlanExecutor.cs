using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default implementation of <see cref="IJobPlanExecutor"/>.
/// Executes tasks in topological tier order, running independent tasks concurrently
/// via Task.WhenAll, and persists the plan to <see cref="PackagePaths.PlanFile"/>
/// after every task status transition.
/// </summary>
public sealed class JobPlanExecutor : IJobPlanExecutor
{
    private static readonly ActivitySource _activitySource =
        new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    private readonly IProgressSink? _progressSink;
    private readonly ILogger<JobPlanExecutor> _logger;

    public JobPlanExecutor(
        IProgressSink? progressSink,
        ILogger<JobPlanExecutor> logger)
    {
        _progressSink = progressSink;
        _logger = logger;
    }

    public async Task<bool> ExecuteExportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        ExportContext exportContext,
        IStateStore stateStore,
        CancellationToken ct)
    {
        var tasks = plan.Tasks
            .Where(t => t.Phase == "Export" && t.Status != JobTaskStatus.Skipped && t.Status != JobTaskStatus.Completed)
            .ToList();

        if (tasks.Count == 0)
        {
            _logger.LogInformation("Export phase: no tasks to execute (all skipped or completed).");
            return true;
        }

        // Use the same tier-based execution as Import so that tasks with DependsOn
        // (e.g. Inventory → WorkItems) run in the correct order.
        var tiers = ExtractTiers(tasks);
        _logger.LogInformation(
            "Export phase: {TaskCount} task(s) organized into {TierCount} tier(s).",
            tasks.Count, tiers.Count);

        bool anyFailed = false;
        var failedTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
        {
            var tier = tiers[tierIndex];

            using var activity = _activitySource.StartActivity("job.plan.execute.tier");
            activity?.SetTag("job.phase", "Export");
            activity?.SetTag("job.tier_index", tierIndex);
            activity?.SetTag("job.tier_task_count", tier.Count);

            _logger.LogInformation(
                "Executing tier {TierIndex} ({TaskCount} tasks) for Export phase: {TaskIds}",
                tierIndex, tier.Count, string.Join(", ", tier.Select(t => t.Id)));

            var result = await ExecuteTierAsync(
                tier, modulesByName, exportContext, null, stateStore, plan, ct)
                .ConfigureAwait(false);

            plan = result.UpdatedPlan; // Propagate plan updates

            if (result.FailedTaskIds.Count > 0)
            {
                anyFailed = true;
                foreach (var id in result.FailedTaskIds)
                    failedTasks.Add(id);

                // Mark dependent tasks in subsequent tiers as Skipped.
                for (int nextTierIdx = tierIndex + 1; nextTierIdx < tiers.Count; nextTierIdx++)
                {
                    foreach (var task in tiers[nextTierIdx])
                    {
                        if (task.DependsOn is not null && task.DependsOn.Any(dep => failedTasks.Contains(dep)))
                        {
                            var matchedDep = task.DependsOn.First(dep => failedTasks.Contains(dep));
                            var updated = task with
                            {
                                Status = JobTaskStatus.Skipped,
                                SkipReason = $"Dependency '{matchedDep}' failed or was skipped."
                            };

                            var taskList = plan.Tasks.ToList();
                            var idx = taskList.FindIndex(t => t.Id == task.Id);
                            if (idx >= 0)
                            {
                                taskList[idx] = updated;
                                plan = plan with { Tasks = taskList.AsReadOnly() };
                                await PersistPlanAsync(plan, stateStore, ct).ConfigureAwait(false);

                                _progressSink?.Emit(new ProgressEvent
                                {
                                    Module = GetModuleName(task.Id, modulesByName.Keys),
                                    Stage = "Export.Skipped",
                                    Message = $"Skipped due to failed dependency: {matchedDep}",
                                    Timestamp = DateTimeOffset.UtcNow,
                                    TaskId = task.Id,
                                    TaskStatus = JobTaskStatus.Skipped
                                });

                                _logger.LogWarning(
                                    "Task {TaskId} skipped — dependency {Dependency} failed.",
                                    task.Id, matchedDep);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation(
                "Export tier {TierIndex} complete: {Succeeded} succeeded, {Failed} failed.",
                tierIndex, tier.Count - result.FailedTaskIds.Count, result.FailedTaskIds.Count);
        }

        if (anyFailed)
        {
            _logger.LogWarning("Export phase completed with {FailedCount} failed task(s).", failedTasks.Count);
            return false;
        }

        return true;
    }

    public async Task<bool> ExecuteImportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        ImportContext importContext,
        IStateStore stateStore,
        CancellationToken ct)
    {
        // Filter out already-completed or skipped tasks, and mark tasks with skipped dependencies as skipped.
        var skippedOrCompletedIds = plan.Tasks
            .Where(t => t.Phase == "Import" && (t.Status == JobTaskStatus.Skipped || t.Status == JobTaskStatus.Completed || t.Status == JobTaskStatus.Failed))
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Mark tasks depending on skipped/failed tasks as skipped before tier extraction.
        var tasksToExecute = new List<JobTask>();
        foreach (var task in plan.Tasks.Where(t => t.Phase == "Import"))
        {
            if (skippedOrCompletedIds.Contains(task.Id))
                continue; // Already skipped/completed/failed

            // Check if any dependency is skipped or failed
            if (task.DependsOn is not null && task.DependsOn.Any(dep => skippedOrCompletedIds.Contains(dep)))
            {
                var matchedDep = task.DependsOn.First(dep => skippedOrCompletedIds.Contains(dep));
                var updated = task with
                {
                    Status = JobTaskStatus.Skipped,
                    SkipReason = $"Dependency '{matchedDep}' was skipped or failed."
                };

                // Update in plan and persist
                var taskList = plan.Tasks.ToList();
                var idx = taskList.FindIndex(t => t.Id == task.Id);
                if (idx >= 0)
                {
                    taskList[idx] = updated;
                    plan = plan with { Tasks = taskList.AsReadOnly() };
                    await PersistPlanAsync(plan, stateStore, ct).ConfigureAwait(false);

                    _progressSink?.Emit(new ProgressEvent
                    {
                        Module = GetModuleName(task.Id, modulesByName.Keys),
                        Stage = "Import.Skipped",
                        Message = $"Skipped due to dependency: {matchedDep}",
                        Timestamp = DateTimeOffset.UtcNow,
                        TaskId = task.Id,
                        TaskStatus = JobTaskStatus.Skipped
                    });
                }

                skippedOrCompletedIds.Add(task.Id); // Add to skipped set for cascading dependencies
            }
            else
            {
                tasksToExecute.Add(task);
            }
        }

        if (tasksToExecute.Count == 0)
        {
            _logger.LogInformation("Import phase: no tasks to execute (all skipped or completed).");
            return true;
        }

        var tiers = ExtractTiers(tasksToExecute);
        _logger.LogInformation(
            "Import phase: {TaskCount} task(s) organized into {TierCount} tier(s).",
            tasksToExecute.Count, tiers.Count);

        bool anyFailed = false;
        var failedTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
        {
            var tier = tiers[tierIndex];

            using var activity = _activitySource.StartActivity("job.plan.execute.tier");
            activity?.SetTag("job.phase", "Import");
            activity?.SetTag("job.tier_index", tierIndex);
            activity?.SetTag("job.tier_task_count", tier.Count);

            _logger.LogInformation(
                "Executing tier {TierIndex} ({TaskCount} tasks) for Import phase: {TaskIds}",
                tierIndex, tier.Count, string.Join(", ", tier.Select(t => t.Id)));

            var failed = await ExecuteTierAsync(
                tier, modulesByName, null, importContext, stateStore, plan, ct)
                .ConfigureAwait(false);

            plan = failed.UpdatedPlan; // Propagate plan updates from tier execution

            if (failed.FailedTaskIds.Count > 0)
            {
                anyFailed = true;
                foreach (var id in failed.FailedTaskIds)
                    failedTasks.Add(id);

                // Mark dependent tasks in subsequent tiers as Skipped.
                for (int nextTierIdx = tierIndex + 1; nextTierIdx < tiers.Count; nextTierIdx++)
                {
                    foreach (var task in tiers[nextTierIdx])
                    {
                        if (task.DependsOn is not null && task.DependsOn.Any(dep => failedTasks.Contains(dep)))
                        {
                            var matchedDep = task.DependsOn.First(dep => failedTasks.Contains(dep));
                            var updated = task with
                            {
                                Status = JobTaskStatus.Skipped,
                                SkipReason = $"Dependency '{matchedDep}' failed or was skipped."
                            };

                            // Update task in plan and persist.
                            var taskList = plan.Tasks.ToList();
                            var idx = taskList.FindIndex(t => t.Id == task.Id);
                            if (idx >= 0)
                            {
                                taskList[idx] = updated;
                                plan = plan with { Tasks = taskList.AsReadOnly() };
                                await PersistPlanAsync(plan, stateStore, ct).ConfigureAwait(false);

                                _progressSink?.Emit(new ProgressEvent
                                {
                                    Module = GetModuleName(task.Id, modulesByName.Keys),
                                    Stage = "Import.Skipped",
                                    Message = $"Skipped due to failed dependency: {matchedDep}",
                                    Timestamp = DateTimeOffset.UtcNow,
                                    TaskId = task.Id,
                                    TaskStatus = JobTaskStatus.Skipped
                                });

                                _logger.LogWarning(
                                    "Task {TaskId} skipped — dependency {Dependency} failed.",
                                    task.Id, matchedDep);
                            }
                        }
                    }
                }
            }

            _logger.LogInformation(
                "Tier {TierIndex} complete: {Succeeded} succeeded, {Failed} failed, {Skipped} skipped.",
                tierIndex,
                tier.Count - failed.FailedTaskIds.Count,
                failed.FailedTaskIds.Count,
                0); // Skipped count is zero at tier-exec time; skips happen after failures.
        }

        if (anyFailed)
        {
            _logger.LogWarning("Import phase completed with {FailedCount} failed task(s).", failedTasks.Count);
            return false;
        }

        return true;
    }

    // ── Tier Execution ───────────────────────────────────────────────────────

    private record TierExecutionResult(List<string> FailedTaskIds, JobTaskList UpdatedPlan);

    private async Task<TierExecutionResult> ExecuteTierAsync(
        IReadOnlyList<JobTask> tier,
        IReadOnlyDictionary<string, IModule> modulesByName,
        ExportContext? exportContext,
        ImportContext? importContext,
        IStateStore stateStore,
        JobTaskList plan,
        CancellationToken ct)
    {
        var failedTaskIds = new List<string>();
        var planLock = new object();
        var persistLock = new SemaphoreSlim(1, 1); // Serialize file writes

        var tierTasks = tier.Select(task => Task.Run(async () =>
        {
            // Check if this task has been marked as Skipped in the plan (e.g. due to failed dependency)
            var currentTask = plan.Tasks.FirstOrDefault(t => t.Id == task.Id);
            if (currentTask?.Status == JobTaskStatus.Skipped)
            {
                _logger.LogInformation("Skipping task {TaskId} — already marked as Skipped in plan.", task.Id);
                return;
            }

            var moduleName = GetModuleName(task.Id, modulesByName.Keys);
            if (!modulesByName.TryGetValue(moduleName, out var module))
            {
                _logger.LogError(
                    "Task {TaskId} references module {Module}, but that module is not registered. Skipping.",
                    task.Id, moduleName);
                return;
            }

            // Transition to Running.
            var updated = task with
            {
                Status = JobTaskStatus.Running,
                StartedAt = DateTimeOffset.UtcNow
            };

            JobTaskList updatedPlan;
            lock (planLock)
            {
                var taskList = plan.Tasks.ToList();
                var idx = taskList.FindIndex(t => t.Id == task.Id);
                if (idx >= 0)
                {
                    taskList[idx] = updated;
                    updatedPlan = plan with { Tasks = taskList.AsReadOnly() };
                    plan = updatedPlan;
                }
                else
                {
                    updatedPlan = plan;
                }
            }

            await persistLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await PersistPlanAsync(updatedPlan, stateStore, ct).ConfigureAwait(false);
            }
            finally
            {
                persistLock.Release();
            }

            _progressSink?.Emit(new ProgressEvent
            {
                Module = moduleName,
                Stage = exportContext is not null ? "Export.Start" : "Import.Start",
                Message = $"{moduleName} {(exportContext is not null ? "export" : "import")} starting.",
                Timestamp = updated.StartedAt.Value,
                TaskId = task.Id,
                TaskStatus = JobTaskStatus.Running
            });

            try
            {
                _logger.LogInformation("Running module {Module}.{Operation}Async", moduleName,
                    exportContext is not null ? "Export" : "Import");

                if (exportContext is not null)
                    await module.ExportAsync(exportContext, ct).ConfigureAwait(false);
                else if (importContext is not null)
                    await module.ImportAsync(importContext, ct).ConfigureAwait(false);

                // Transition to Completed.
                updated = updated with
                {
                    Status = JobTaskStatus.Completed,
                    CompletedAt = DateTimeOffset.UtcNow
                };

                lock (planLock)
                {
                    var taskList = plan.Tasks.ToList();
                    var idx = taskList.FindIndex(t => t.Id == task.Id);
                    if (idx >= 0)
                    {
                        taskList[idx] = updated;
                        updatedPlan = plan with { Tasks = taskList.AsReadOnly() };
                        plan = updatedPlan;
                    }
                    else
                    {
                        updatedPlan = plan;
                    }
                }

                await PersistPlanAsync(updatedPlan, stateStore, ct).ConfigureAwait(false);

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = moduleName,
                    Stage = exportContext is not null ? "Export.Complete" : "Import.Complete",
                    Message = $"{moduleName} {(exportContext is not null ? "export" : "import")} completed.",
                    Timestamp = updated.CompletedAt.Value,
                    TaskId = task.Id,
                    TaskStatus = JobTaskStatus.Completed
                });
            }
            catch (OperationCanceledException)
            {
                // Cancellation is not a failure — rethrow.
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Module {Module}.{Operation}Async failed.", moduleName,
                    exportContext is not null ? "Export" : "Import");

                // Transition to Failed.
                updated = updated with
                {
                    Status = JobTaskStatus.Failed,
                    CompletedAt = DateTimeOffset.UtcNow
                };

                lock (planLock)
                {
                    var taskList = plan.Tasks.ToList();
                    var idx = taskList.FindIndex(t => t.Id == task.Id);
                    if (idx >= 0)
                    {
                        taskList[idx] = updated;
                        updatedPlan = plan with { Tasks = taskList.AsReadOnly() };
                        plan = updatedPlan;
                    }
                    else
                    {
                        updatedPlan = plan;
                    }
                }

                await persistLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    await PersistPlanAsync(updatedPlan, stateStore, ct).ConfigureAwait(false);
                }
                finally
                {
                    persistLock.Release();
                }

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = moduleName,
                    Stage = exportContext is not null ? "Export.Failed" : "Import.Failed",
                    Message = $"{moduleName} {(exportContext is not null ? "export" : "import")} failed: {ex.Message}",
                    Timestamp = updated.CompletedAt.Value,
                    TaskId = task.Id,
                    TaskStatus = JobTaskStatus.Failed
                });

                lock (failedTaskIds)
                {
                    failedTaskIds.Add(task.Id);
                }

                // Do NOT rethrow — allow sibling tasks to continue.
            }
        }, ct)).ToArray();

        await Task.WhenAll(tierTasks).ConfigureAwait(false);

        return new TierExecutionResult(failedTaskIds, plan);
    }

    // ── Plan Persistence ─────────────────────────────────────────────────────

    private async Task PersistPlanAsync(
        JobTaskList plan,
        IStateStore stateStore,
        CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(plan, _jsonOptions);
            await stateStore.WriteAsync(PackagePaths.PlanFile, json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist execution plan to {Path}. Job will continue, but resume may be incomplete.",
                PackagePaths.PlanFile);
        }
    }

    /// <summary>
    /// Loads the persisted plan from <see cref="PackagePaths.PlanFile"/> and resets
    /// any <c>Running</c> tasks to <c>Pending</c> (crash recovery).
    /// Returns <c>null</c> if the plan file does not exist or cannot be deserialised.
    /// </summary>
    public static async Task<JobTaskList?> LoadOrResetAsync(
        IStateStore stateStore,
        CancellationToken ct)
    {
        try
        {
            var json = await stateStore.ReadAsync(PackagePaths.PlanFile, ct).ConfigureAwait(false);
            if (json is null)
                return null;

            var plan = JsonSerializer.Deserialize<JobTaskList>(json, _jsonOptions);
            if (plan is null)
                return null;

            // Reset Running tasks to Pending (crash recovery).
            var tasks = plan.Tasks.ToList();
            bool anyReset = false;

            for (int i = 0; i < tasks.Count; i++)
            {
                if (tasks[i].Status == JobTaskStatus.Running)
                {
                    tasks[i] = tasks[i] with
                    {
                        Status = JobTaskStatus.Pending,
                        StartedAt = null
                    };
                    anyReset = true;
                }
            }

            if (anyReset)
            {
                plan = plan with { Tasks = tasks.AsReadOnly() };
            }

            return plan;
        }
        catch (Exception)
        {
            // Plan file corrupt or unreadable — return null.
            return null;
        }
    }

    // ── Topological Sort ─────────────────────────────────────────────────────

    /// <summary>
    /// Extracts execution tiers from the task list using Kahn's topological sort.
    /// Returns a list of tiers, where each tier is a maximal set of tasks whose
    /// dependencies have all completed or been skipped.
    /// </summary>
    private static IReadOnlyList<IReadOnlyList<JobTask>> ExtractTiers(
        IReadOnlyList<JobTask> tasks)
    {
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
                    // If dep is not in the filtered task list (already completed/skipped),
                    // treat it as satisfied — do not increment in-degree.
                }
            }
        }

        var tiers = new List<IReadOnlyList<JobTask>>();
        var taskDict = tasks.ToDictionary(t => t.Id, StringComparer.OrdinalIgnoreCase);

        while (inDegree.Count > 0)
        {
            var tier = inDegree
                .Where(kvp => kvp.Value == 0)
                .Select(kvp => taskDict[kvp.Key])
                .ToList();

            if (tier.Count == 0)
            {
                // Cycle detected — should have been caught by JobExecutionPlanBuilder.
                throw new InvalidOperationException(
                    "Circular dependency detected at runtime — this should have been caught during plan build.");
            }

            tiers.Add(tier.AsReadOnly());

            foreach (var task in tier)
            {
                inDegree.Remove(task.Id);
                foreach (var neighbor in graph[task.Id])
                {
                    if (inDegree.ContainsKey(neighbor))
                        inDegree[neighbor]--;
                }
            }
        }

        return tiers.AsReadOnly();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Maps task ID (e.g. "export.workitems") to module name (e.g. "WorkItems").
    /// Convention: task ID = "{phase}.{moduleName.ToLowerInvariant()}".
    /// </summary>
    private static string GetModuleName(string taskId, IEnumerable<string> registeredModuleNames)
    {
        var parts = taskId.Split('.');
        if (parts.Length != 2)
            return taskId; // Fallback if task ID doesn't follow convention.

        var lowerName = parts[1];
        var match = registeredModuleNames.FirstOrDefault(
            name => name.Equals(lowerName, StringComparison.OrdinalIgnoreCase));

        return match ?? lowerName; // Return matched name with correct casing, or fallback.
    }
}
