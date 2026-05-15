// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Analysis;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Lease;
using DevOpsMigrationPlatform.Abstractions.Agent.Modules;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using DevOpsMigrationPlatform.Infrastructure.Agent.Discovery;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default implementation of <see cref="IJobPlanExecutor"/>.
/// Executes tasks in topological tier order, running independent tasks concurrently
/// via Task.WhenAll, and persists the plan to <c>.migration/plan.json</c>
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
    private readonly ICurrentJobEndpointAccessor? _currentJobEndpointAccessor;
    private readonly IPackageAccess? _package;

    // The current source endpoint accessor is shared across the whole job scope,
    // so capture-time source endpoint swaps must be serialised globally.
    private readonly SemaphoreSlim _sourceEndpointLock = new(1, 1);
    private readonly SemaphoreSlim _targetEndpointLock = new(1, 1);

    public JobPlanExecutor(
        IProgressSink? progressSink,
        ILogger<JobPlanExecutor> logger,
        ICurrentJobEndpointAccessor? currentJobEndpointAccessor = null,
        IPackageAccess? package = null)
    {
        _progressSink = progressSink;
        _logger = logger;
        _currentJobEndpointAccessor = currentJobEndpointAccessor;
        _package = package;
    }

    public async Task<bool> ExecuteTasksAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, ICapture> captureHandlersByName,
        IReadOnlyDictionary<string, IAnalyser> analysersByName,
        InventoryContext? baseInventoryContext,
        ExportContext? baseExportContext,
        ImportContext? importContext,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
        CancellationToken ct)
    {
        plan = await MarkBlockedDependenciesSkippedAsync(
            plan,
            _ => true,
            "Task.Skipped",
            captureHandlersByName.Keys.Concat(analysersByName.Keys),
            ct).ConfigureAwait(false);

        var hasCanonicalFailures = HasFailedTasks(plan, _ => true);
        var tasks = plan.Tasks
            .Where(t => t.Status != JobTaskStatus.Skipped
                && t.Status != JobTaskStatus.Completed
                && t.Status != JobTaskStatus.Failed)
            .ToList();

        if (tasks.Count == 0)
        {
            _logger.LogInformation(
                "ExecuteTasksAsync: no tasks to execute (all skipped, completed, or failed). Failed task(s) present: {HasFailedTasks}.",
                hasCanonicalFailures);
            return !hasCanonicalFailures;
        }

        var tiers = ExtractTiers(tasks);
        _logger.LogInformation(
            "ExecuteTasksAsync: {TaskCount} task(s) in {TierCount} tier(s).",
            tasks.Count, tiers.Count);

        bool anyFailed = false;
        var failedTasks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int tierIndex = 0; tierIndex < tiers.Count; tierIndex++)
        {
            var tier = tiers[tierIndex];

            using var activity = _activitySource.StartActivity("job.plan.execute.tier");
            activity?.SetTag("job.tier_index", tierIndex);
            activity?.SetTag("job.tier_task_count", tier.Count);

            _logger.LogInformation(
                "Executing tier {TierIndex} ({TaskCount} tasks): {TaskIds}",
                tierIndex, tier.Count, string.Join(", ", tier.Select(t => t.Id)));

            var result = await ExecuteTierAsync(
                tier, captureHandlersByName, analysersByName, baseInventoryContext,
                baseExportContext, importContext, endpointsByUrl, plan, ct)
                .ConfigureAwait(false);

            plan = result.UpdatedPlan;

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
                            var skipped = task with
                            {
                                Status = JobTaskStatus.Skipped,
                                SkipReason = $"Dependency '{matchedDep}' failed or was skipped."
                            };

                            var taskList = plan.Tasks.ToList();
                            var idx = taskList.FindIndex(t => t.Id == task.Id);
                            taskList[idx] = skipped;
                                plan = plan with { Tasks = taskList.AsReadOnly() };
                                await PersistPlanAsync(plan, ct).ConfigureAwait(false);

                                _progressSink?.Emit(new ProgressEvent
                                {
                                    Module = GetModuleName(task.Id, captureHandlersByName.Keys),
                                    Stage = $"{task.TaskKind}.Skipped",
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

            _logger.LogInformation(
                "Tier {TierIndex} complete: {Succeeded} succeeded, {Failed} failed.",
                tierIndex, tier.Count - result.FailedTaskIds.Count, result.FailedTaskIds.Count);
        }

        if (anyFailed || hasCanonicalFailures)
        {
            _logger.LogWarning(
                "ExecuteTasksAsync completed with {FailedCount} newly failed task(s). Persisted failed task(s) present: {HasFailedTasks}.",
                failedTasks.Count,
                hasCanonicalFailures);
            return false;
        }

        return true;
    }

    private static bool HasFailedTasks(JobTaskList plan, Func<JobTask, bool> isInScope)
        => plan.Tasks.Any(t => isInScope(t) && t.Status == JobTaskStatus.Failed);

    private async Task<JobTaskList> MarkBlockedDependenciesSkippedAsync(
        JobTaskList plan,
        Func<JobTask, bool> isInScope,
        string stage,
        IEnumerable<string> registeredNames,
        CancellationToken ct)
    {
        var scopedTasks = plan.Tasks.Where(isInScope).ToList();
        var blockedTaskIds = scopedTasks
            .Where(t => t.Status is JobTaskStatus.Skipped or JobTaskStatus.Failed)
            .Select(t => t.Id)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (blockedTaskIds.Count == 0)
            return plan;

        bool changed;
        do
        {
            changed = false;
            foreach (var task in plan.Tasks.Where(isInScope))
            {
                if (task.Status is JobTaskStatus.Completed or JobTaskStatus.Skipped or JobTaskStatus.Failed)
                    continue;

                if (task.DependsOn is null || !task.DependsOn.Any(dep => blockedTaskIds.Contains(dep)))
                    continue;

                var matchedDep = task.DependsOn.First(dep => blockedTaskIds.Contains(dep));
                var updated = task with
                {
                    Status = JobTaskStatus.Skipped,
                    SkipReason = $"Dependency '{matchedDep}' failed or was skipped."
                };

                var taskList = plan.Tasks.ToList();
                var idx = taskList.FindIndex(t => t.Id == task.Id);
                if (idx < 0)
                    continue;

                taskList[idx] = updated;
                plan = plan with { Tasks = taskList.AsReadOnly() };
                await PersistPlanAsync(plan, ct).ConfigureAwait(false);

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = GetModuleName(task.Id, registeredNames),
                    Stage = stage,
                    Message = $"Skipped due to failed dependency: {matchedDep}",
                    Timestamp = DateTimeOffset.UtcNow,
                    TaskId = task.Id,
                    TaskStatus = JobTaskStatus.Skipped
                });

                _logger.LogWarning(
                    "Task {TaskId} skipped — dependency {Dependency} failed or was skipped.",
                    task.Id, matchedDep);

                blockedTaskIds.Add(task.Id);
                changed = true;
            }
        }
        while (changed);

        return plan;
    }

    public async Task<bool> ExecuteExportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        IReadOnlyDictionary<string, IAnalyser> analysersByName,
        InventoryContext? baseInventoryContext,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
        ExportContext exportContext,
        CancellationToken ct)
    {
        plan = await MarkBlockedDependenciesSkippedAsync(
            plan,
            t => t.Phase == "Export" || t.TaskKind is TaskKind.Capture or TaskKind.Analyse,
            "Export.Skipped",
            modulesByName.Keys.Concat(analysersByName.Keys),
            ct).ConfigureAwait(false);

        var hasCanonicalFailures = HasFailedTasks(plan, t => t.Phase == "Export" || t.TaskKind is TaskKind.Capture or TaskKind.Analyse);
        var tasks = plan.Tasks
            .Where(t => (t.Phase == "Export" || t.TaskKind is TaskKind.Capture or TaskKind.Analyse)
                && t.Status != JobTaskStatus.Skipped
                && t.Status != JobTaskStatus.Completed
                && t.Status != JobTaskStatus.Failed)
            .ToList();

        if (tasks.Count == 0)
        {
            _logger.LogInformation(
                "Export phase: no tasks to execute (all skipped, completed, or failed). Failed task(s) present: {HasFailedTasks}.",
                hasCanonicalFailures);
            return !hasCanonicalFailures;
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
                tier,
                modulesByName.ToDictionary(kvp => kvp.Key, kvp => (ICapture)kvp.Value, StringComparer.OrdinalIgnoreCase),
                analysersByName,
                baseInventoryContext,
                exportContext,
                importContext: null,
                endpointsByUrl,
                plan,
                ct)
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
                                await PersistPlanAsync(plan, ct).ConfigureAwait(false);

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

        if (anyFailed || hasCanonicalFailures)
        {
            _logger.LogWarning(
                "Export phase completed with {FailedCount} newly failed task(s). Persisted failed task(s) present: {HasFailedTasks}.",
                failedTasks.Count,
                hasCanonicalFailures);
            return false;
        }

        return true;
    }

    private static async Task<InventoryReport?> TryReadInventoryReportAsync(
        IPackageAccess? package,
        CancellationToken ct)
    {
        try
        {
            var json = await ReadPackageTextAsync(package, "inventory.json", ct).ConfigureAwait(false);
            if (json is null)
                return null;

            return JsonSerializer.Deserialize<InventoryReport>(json, _jsonOptions);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static (long? KnownTotal, long? CompletedCount) TryResolveTaskProgressSnapshot(
        JobTask task,
        InventoryReport? inventoryReport,
        ProjectInventoryData? projectInventory = null)
    {
        var moduleName = GetTaskModuleName(task.Id);
        if (moduleName is null)
            return (null, null);

        var hasProjectInventory = projectInventory is not null
            && (!string.IsNullOrWhiteSpace(projectInventory.Project)
                || !string.IsNullOrWhiteSpace(projectInventory.OrgUrl));

        var project = inventoryReport?.Organisations
            .FirstOrDefault(org => string.Equals(org.Url, task.OrganisationUrl, StringComparison.OrdinalIgnoreCase))?
            .Projects
            .FirstOrDefault(p => string.Equals(p.Name, task.ProjectName, StringComparison.OrdinalIgnoreCase));

        long? knownTotal = moduleName switch
        {
            "workitems" => hasProjectInventory ? projectInventory!.WorkItems : project?.WorkItems ?? inventoryReport?.Totals.WorkItems,
            "repos" => hasProjectInventory ? projectInventory!.Repos : project?.Repos ?? inventoryReport?.Totals.Repos,
            "identities" => hasProjectInventory ? projectInventory!.Identities : project?.Identities ?? inventoryReport?.Totals.Identities,
            "nodes" => hasProjectInventory ? projectInventory!.Nodes : project?.Nodes ?? inventoryReport?.Totals.Nodes,
            "teams" => hasProjectInventory ? projectInventory!.Teams : project?.Teams ?? inventoryReport?.Totals.Teams,
            "inventory" => hasProjectInventory ? projectInventory!.WorkItems : project?.WorkItems ?? inventoryReport?.Totals.WorkItems,
            "dependencies" => hasProjectInventory ? projectInventory!.WorkItems : project?.WorkItems ?? inventoryReport?.Totals.WorkItems,
            _ => null
        };

        return knownTotal.HasValue ? (knownTotal, knownTotal) : (null, null);
    }

    private static async Task<(long? KnownTotal, long? CompletedCount)> TryResolveTaskProgressSnapshotAsync(
        JobTask task,
        IPackageAccess? package,
        CancellationToken ct)
    {
        var resolvedPackage = ResolvePackage(package);

        ProjectInventoryData? projectInventory = null;
        if (!string.IsNullOrWhiteSpace(task.OrganisationUrl) && !string.IsNullOrWhiteSpace(task.ProjectName))
        {
            var orgUrl = task.OrganisationUrl!;
            var projectName = task.ProjectName!;
            var orgSlug = PackagePathResolver.DeriveInventoryOrgSlug(orgUrl);
            var projectPath = PackagePathResolver.ProjectInventoryPath(orgSlug, projectName);
            projectInventory = await ProjectInventoryFile.ReadAsync(resolvedPackage, projectPath, ct).ConfigureAwait(false);
        }

        var inventoryReport = await TryReadInventoryReportAsync(resolvedPackage, ct).ConfigureAwait(false);
        return TryResolveTaskProgressSnapshot(task, inventoryReport, projectInventory);
    }

    private static string? GetTaskModuleName(string taskId)
    {
        var parts = taskId.Split(new[] { '.' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : null;
    }

    public async Task<bool> ExecuteImportPhaseAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, IModule> modulesByName,
        ImportContext importContext,
        CancellationToken ct)
    {
        plan = await MarkBlockedDependenciesSkippedAsync(
            plan,
            t => t.Phase == "Import",
            "Import.Skipped",
            modulesByName.Keys,
            ct).ConfigureAwait(false);

        var hasCanonicalFailures = HasFailedTasks(plan, t => t.Phase == "Import");
        var tasksToExecute = plan.Tasks
            .Where(t => t.Phase == "Import"
                && t.Status != JobTaskStatus.Skipped
                && t.Status != JobTaskStatus.Completed
                && t.Status != JobTaskStatus.Failed)
            .ToList();

        if (tasksToExecute.Count == 0)
        {
            _logger.LogInformation(
                "Import phase: no tasks to execute (all skipped, completed, or failed). Failed task(s) present: {HasFailedTasks}.",
                hasCanonicalFailures);
            return !hasCanonicalFailures;
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
                tier, modulesByName.ToDictionary(kvp => kvp.Key, kvp => (ICapture)kvp.Value, StringComparer.OrdinalIgnoreCase), analysersByName: null, baseInventoryContext: null,
                exportContext: null, importContext, endpointsByUrl: null, plan, ct)
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
                                await PersistPlanAsync(plan, ct).ConfigureAwait(false);

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

        if (anyFailed || hasCanonicalFailures)
        {
            _logger.LogWarning(
                "Import phase completed with {FailedCount} newly failed task(s). Persisted failed task(s) present: {HasFailedTasks}.",
                failedTasks.Count,
                hasCanonicalFailures);
            return false;
        }

        return true;
    }

    // ── Tier Execution ───────────────────────────────────────────────────────

    private record TierExecutionResult(List<string> FailedTaskIds, JobTaskList UpdatedPlan);

    private async Task<TierExecutionResult> ExecuteTierAsync(
        IReadOnlyList<JobTask> tier,
        IReadOnlyDictionary<string, ICapture> captureHandlersByName,
        IReadOnlyDictionary<string, IAnalyser>? analysersByName,
        InventoryContext? baseInventoryContext,
        ExportContext? exportContext,
        ImportContext? importContext,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
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

            // Resolve the handler: Analyse tasks go to IAnalyser; Capture tasks go to ICapture; all others go to IModule (cast from ICapture).
            string handlerName;
            IModule? module = null;
            IAnalyser? analyserForTask = null;
            ICapture? captureHandler = null;
            Exception? resolutionError = null;

            if (task.TaskKind == TaskKind.Analyse)
            {
                handlerName = GetModuleName(task.Id, analysersByName?.Keys ?? Enumerable.Empty<string>());
                if (analysersByName is null || !analysersByName.TryGetValue(handlerName, out analyserForTask))
                {
                    resolutionError = new InvalidOperationException(
                        $"Task {task.Id} references analyser '{handlerName}', but it is not registered.");
                }
            }
            else if (task.TaskKind == TaskKind.Capture)
            {
                handlerName = GetModuleName(task.Id, captureHandlersByName.Keys);
                if (!captureHandlersByName.TryGetValue(handlerName, out captureHandler))
                {
                    resolutionError = new InvalidOperationException(
                        $"Task {task.Id} references capture handler '{handlerName}', but it is not registered.");
                }
            }
            else
            {
                handlerName = GetModuleName(task.Id, captureHandlersByName.Keys);
                if (!captureHandlersByName.TryGetValue(handlerName, out var handler) || handler is not IModule resolvedModule)
                {
                    resolutionError = new InvalidOperationException(
                        $"Task {task.Id} references module '{handlerName}', but it is not registered.");
                }
                else
                {
                    module = resolvedModule;
                }
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
                lock (planLock) { updatedPlan = plan; }
                await PersistPlanAsync(updatedPlan, ct).ConfigureAwait(false);
            }
            finally
            {
                persistLock.Release();
            }

            _progressSink?.Emit(new ProgressEvent
            {
                Module = handlerName,
                Stage = $"{task.TaskKind}.Start",
                Message = $"{handlerName} {task.TaskKind} starting.",
                Timestamp = updated.StartedAt!.Value,
                TaskId = task.Id,
                TaskStatus = JobTaskStatus.Running
            });

            try
            {
                if (resolutionError is not null)
                {
                    switch (task.TaskKind)
                    {
                        case TaskKind.Analyse:
                            _logger.LogError(
                                "Task {TaskId} references analyser '{Analyser}', but it is not registered.",
                                task.Id, handlerName);
                            break;
                        case TaskKind.Capture:
                            _logger.LogError(
                                "Task {TaskId} references capture handler '{HandlerName}', but it is not registered.",
                                task.Id, handlerName);
                            break;
                        default:
                            _logger.LogError(
                                "Task {TaskId} references module '{Module}', but it is not registered.",
                                task.Id, handlerName);
                            break;
                    }

                    throw resolutionError;
                }

                _logger.LogInformation("Running {Handler}.{Kind}Async for task {TaskId}",
                    handlerName, task.TaskKind, task.Id);

                TaskExecutionResult executionResult;

                if (analyserForTask is not null)
                {
                    // Analyse task: build context from whichever base context is available.
                    var job = (baseInventoryContext?.Job ?? exportContext?.Job ?? importContext?.Job)
                        ?? throw new InvalidOperationException("No job context available to build AnalyseContext.");
                    var packageAccess = (baseInventoryContext?.Package ?? exportContext?.Package ?? importContext?.Package)
                        ?? throw new InvalidOperationException("No IPackageAccess available.");
                    var progressSink = baseInventoryContext?.ProgressSink ?? exportContext?.ProgressSink ?? importContext?.ProgressSink;

                    // IOrganisationsAnalyser (e.g. DependencyAnalyser) needs the full organisations list
                    // to run the final fan-in step across all captured projects.
                    AnalyseContext analyseContext =
                        analyserForTask is IOrganisationsAnalyser && baseInventoryContext?.Organisations is { Count: > 0 } orgs
                            ? new OrganisationsAnalyseContext
                            {
                                Job = job,
                                Package = packageAccess,
                                ProgressSink = progressSink,
                                Policies = baseInventoryContext.Policies,
                                Organisations = orgs
                            }
                            : new AnalyseContext
                            {
                                Job = job,
                                Package = packageAccess,
                                ProgressSink = progressSink
                            };

                    executionResult = await analyserForTask.AnalyseAsync(analyseContext, ct).ConfigureAwait(false);
                }
                else if (captureHandler is not null)
                {
                    // Capture task: dispatch to the unified ICapture handler.
                    if (baseInventoryContext is null)
                        throw new InvalidOperationException($"Capture task {task.Id} requires a base InventoryContext.");

                    var orgUrl = task.OrganisationUrl;
                    OrganisationEndpoint endpoint;
                    if (orgUrl is { Length: > 0 })
                    {
                        if (endpointsByUrl?.TryGetValue(orgUrl, out var resolvedEndpoint) != true
                            || resolvedEndpoint is null)
                        {
                            throw new InvalidOperationException(
                                $"Capture task {task.Id} references organisation '{orgUrl}', but no matching endpoint was resolved.");
                        }

                        endpoint = resolvedEndpoint;
                    }
                    else
                    {
                        endpoint = baseInventoryContext.SourceEndpoint
                            ?? throw new InvalidOperationException(
                                $"Capture task {task.Id} requires a resolved source endpoint.");
                    }

                    var scopedCtx = baseInventoryContext with
                    {
                        Project = task.ProjectName ?? string.Empty,
                        SourceEndpoint = endpoint!
                    };

                    // Apply a temporary explicit source endpoint swap so singleton endpoint
                    // adapters resolve the correct per-organisation source settings.
                    if (_currentJobEndpointAccessor is not null && endpoint is not null)
                    {
                        await _sourceEndpointLock.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            var previousSource = _currentJobEndpointAccessor.Source;
                            _currentJobEndpointAccessor.SetSource(
                                BuildCaptureSourceEndpointInfo(endpoint, scopedCtx.Project));
                            try
                            {
                                executionResult = await captureHandler.CaptureAsync(scopedCtx, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                if (previousSource is not null)
                                {
                                    _currentJobEndpointAccessor.SetSource(previousSource);
                                }
                                else
                                {
                                    _currentJobEndpointAccessor.ClearSource();
                                }
                            }
                        }
                        finally
                        {
                            _sourceEndpointLock.Release();
                        }
                    }
                    else
                    {
                        executionResult = await captureHandler.CaptureAsync(scopedCtx, ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    // Module task (Export, Import, etc.): dispatch on TaskKind.
                    switch (task.TaskKind)
                    {
                        case TaskKind.Export:
                            {
                                if (exportContext is null)
                                    throw new InvalidOperationException($"Export task {task.Id} requires an ExportContext.");

                                var scopedCtx = new ExportContext
                                {
                                    Job = exportContext.Job,
                                    Package = exportContext.Package,
                                    ProgressSink = exportContext.ProgressSink,
                                    MetricsStore = exportContext.MetricsStore,
                                    SnapshotStore = exportContext.SnapshotStore,
                                    Organisations = exportContext.Organisations,
                                    Project = task.ProjectName ?? string.Empty,
                                    TaskId = task.Id
                                };

                                if (_currentJobEndpointAccessor is not null)
                                {
                                    await _sourceEndpointLock.WaitAsync(ct).ConfigureAwait(false);
                                    try
                                    {
                                        var previousSource = _currentJobEndpointAccessor.Source;
                                        if (TryBuildTaskSourceEndpointInfo(task, endpointsByUrl, previousSource, out var scopedSource))
                                        {
                                            _currentJobEndpointAccessor.SetSource(scopedSource);
                                        }

                                        try
                                        {
                                            executionResult = await module!.ExportAsync(scopedCtx, ct).ConfigureAwait(false);
                                        }
                                        finally
                                        {
                                            if (TryBuildTaskSourceEndpointInfo(task, endpointsByUrl, previousSource, out _))
                                            {
                                                if (previousSource is not null)
                                                {
                                                    _currentJobEndpointAccessor.SetSource(previousSource);
                                                }
                                                else
                                                {
                                                    _currentJobEndpointAccessor.ClearSource();
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        _sourceEndpointLock.Release();
                                    }
                                }
                                else
                                {
                                    executionResult = await module!.ExportAsync(scopedCtx, ct).ConfigureAwait(false);
                                }
                                break;
                            }
                        case TaskKind.Import:
                            {
                                if (importContext is null)
                                    throw new InvalidOperationException($"Import task {task.Id} requires an ImportContext.");

                                if (_currentJobEndpointAccessor is not null)
                                {
                                    await _targetEndpointLock.WaitAsync(ct).ConfigureAwait(false);
                                    try
                                    {
                                        var previousTarget = _currentJobEndpointAccessor.Target;
                                        if (TryBuildTaskTargetEndpointInfo(task, previousTarget, out var scopedTarget))
                                        {
                                            _currentJobEndpointAccessor.SetTarget(scopedTarget);
                                        }

                                        try
                                        {
                                            executionResult = await module!.ImportAsync(importContext, ct).ConfigureAwait(false);
                                        }
                                        finally
                                        {
                                            if (TryBuildTaskTargetEndpointInfo(task, previousTarget, out _))
                                            {
                                                if (previousTarget is not null)
                                                {
                                                    _currentJobEndpointAccessor.SetTarget(previousTarget);
                                                }
                                                else
                                                {
                                                    _currentJobEndpointAccessor.ClearTarget();
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        _targetEndpointLock.Release();
                                    }
                                }
                                else
                                {
                                    executionResult = await module!.ImportAsync(importContext, ct).ConfigureAwait(false);
                                }
                                break;
                            }
                        default:
                            _logger.LogWarning(
                                "Task {TaskId} has unsupported TaskKind '{Kind}' — skipping execution.",
                                task.Id, task.TaskKind);
                            executionResult = TaskExecutionResult.Skipped($"Unsupported task kind '{task.TaskKind}'.");
                            break;
                    }
                }

                var packageForTask = baseInventoryContext?.Package
                    ?? exportContext?.Package
                    ?? importContext?.Package
                    ?? _package;
                var (resolvedKnownTotal, resolvedCompletedCount) = await TryResolveTaskProgressSnapshotAsync(
                    task,
                    packageForTask,
                    ct).ConfigureAwait(false);

                var knownTotal = executionResult.KnownTotal ?? resolvedKnownTotal;
                var completedCount = executionResult.CompletedCount ?? resolvedCompletedCount;

                // Transition to the terminal state reported by the executee.
                updated = updated with
                {
                    Status = executionResult.Status,
                    CompletedAt = DateTimeOffset.UtcNow,
                    KnownTotal = knownTotal ?? updated.KnownTotal,
                    CompletedCount = completedCount ?? updated.CompletedCount,
                    SkipReason = executionResult.Status == JobTaskStatus.Skipped
                        ? executionResult.StatusMessage
                        : null
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
                    lock (planLock) { updatedPlan = plan; }
                    await PersistPlanAsync(updatedPlan, ct).ConfigureAwait(false);
                }
                finally
                {
                    persistLock.Release();
                }

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = handlerName,
                    Stage = $"{task.TaskKind}.{updated.Status}",
                    Message = updated.Status == JobTaskStatus.Skipped
                        ? (updated.SkipReason ?? $"{handlerName} {task.TaskKind} skipped.")
                        : $"{handlerName} {task.TaskKind} completed.",
                    Timestamp = updated.CompletedAt!.Value,
                    TaskId = task.Id,
                    TaskStatus = updated.Status,
                    KnownTotal = updated.KnownTotal,
                    CompletedCount = updated.CompletedCount
                });
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{Handler}.{Kind}Async failed for task {TaskId}.",
                    handlerName, task.TaskKind, task.Id);

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
                    lock (planLock) { updatedPlan = plan; }
                    await PersistPlanAsync(updatedPlan, ct).ConfigureAwait(false);
                }
                finally
                {
                    persistLock.Release();
                }

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = handlerName,
                    Stage = $"{task.TaskKind}.Failed",
                    Message = $"{handlerName} {task.TaskKind} failed: {ex.Message}",
                    Timestamp = updated.CompletedAt!.Value,
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
        CancellationToken ct)
    {
        try
        {
            var json = JsonSerializer.Serialize(plan, _jsonOptions);
            await WritePlanStateAsync(_package, ".migration/plan.json", json, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "Failed to persist execution plan to {Path}. Job will continue, but resume may be incomplete.",
                ".migration/plan.json");
        }
    }

    private static async Task<string?> ReadPackageTextAsync(IPackageAccess? package, string relativePath, CancellationToken ct)
    {
        var resolved = ResolvePackage(package);
        var payload = await resolved.RequestContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(relativePath)),
            ct).ConfigureAwait(false);
        if (payload is null)
            return null;

        if (payload.Content.CanSeek)
            payload.Content.Position = 0;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        return await reader.ReadToEndAsync().ConfigureAwait(false);
    }

    private static async Task<string?> ReadPlanStateAsync(IPackageAccess? package, string key, CancellationToken ct)
    {
        var resolved = ResolvePackage(package);
        if (string.Equals(key, ".migration/plan.json", StringComparison.Ordinal))
        {
            var planMeta = await resolved.RequestMetaAsync(
                new PackageMetaContext(PackageMetaKind.ExecutionPlan),
                ct).ConfigureAwait(false);
            if (planMeta.Payload is null)
                return null;

            if (planMeta.Payload.Content.CanSeek)
                planMeta.Payload.Content.Position = 0;
            using var reader = new StreamReader(planMeta.Payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
            return await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        return await ReadPackageTextAsync(resolved, key, ct).ConfigureAwait(false);
    }

    private static async Task WritePlanStateAsync(IPackageAccess? package, string key, string value, CancellationToken ct)
    {
        var resolved = ResolvePackage(package);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(value), writable: false);
        if (string.Equals(key, ".migration/plan.json", StringComparison.Ordinal))
        {
            await resolved.PersistMetaAsync(
                new PackageMetaContext(PackageMetaKind.ExecutionPlan),
                new PackageMetaPayload(stream),
                ct).ConfigureAwait(false);
            return;
        }

        await resolved.PersistContentAsync(
            new PackageContentContext(PackageContentKind.Artefact, Address: new RelativePathAddress(key)),
            new PackagePayload(stream),
            ct).ConfigureAwait(false);
    }

    private sealed class RelativePathAddress(string relativePath) : IPackageContentAddress
    {
        public string RelativePath => relativePath.Replace('\\', '/').TrimStart('/');
    }

    private static IPackageAccess ResolvePackage(IPackageAccess? package)
        => package ?? throw new InvalidOperationException($"{nameof(IPackageAccess)} is required for plan persistence operations.");

    private static ISourceEndpointInfo BuildCaptureSourceEndpointInfo(OrganisationEndpoint endpoint, string project)
        => new CaptureSourceEndpointInfo(
            endpoint.ResolvedUrl ?? string.Empty,
            project,
            endpoint.Type ?? string.Empty,
            endpoint.ApiVersion,
            endpoint.Authentication.Type,
            endpoint.Authentication.ResolvedAccessToken);

    private sealed record CaptureSourceEndpointInfo(
        string Url,
        string Project,
        string ConnectorType,
        string? ApiVersion,
        AuthenticationType AuthenticationType,
        string? AccessToken) : ISourceEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType,
            ApiVersion = ApiVersion,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType,
                ResolvedAccessToken = AccessToken
            }
        };
    }

    private sealed record TaskTargetEndpointInfo(
        string Url,
        string Project,
        string ConnectorType,
        string? ApiVersion,
        AuthenticationType AuthenticationType,
        string? AccessToken) : ITargetEndpointInfo
    {
        public OrganisationEndpoint ToOrganisationEndpoint() => new()
        {
            ResolvedUrl = Url,
            Type = ConnectorType,
            ApiVersion = ApiVersion,
            Authentication = new OrganisationEndpointAuthentication
            {
                Type = AuthenticationType,
                ResolvedAccessToken = AccessToken
            }
        };
    }

    private static bool TryBuildTaskSourceEndpointInfo(
        JobTask task,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
        ISourceEndpointInfo? fallbackSource,
        out ISourceEndpointInfo endpointInfo)
    {
        var project = task.ProjectName;
        if (string.IsNullOrWhiteSpace(project))
        {
            endpointInfo = null!;
            return false;
        }

        var projectName = project!;

        if (task.OrganisationUrl is { Length: > 0 } orgUrl &&
            endpointsByUrl?.TryGetValue(orgUrl, out var resolvedEndpoint) == true &&
            resolvedEndpoint is not null)
        {
            endpointInfo = BuildCaptureSourceEndpointInfo(resolvedEndpoint, projectName);
            return true;
        }

        if (fallbackSource is not null)
        {
            endpointInfo = new CaptureSourceEndpointInfo(
            fallbackSource.Url ?? string.Empty,
                projectName,
                fallbackSource.ConnectorType,
                fallbackSource.ToOrganisationEndpoint().ApiVersion,
                fallbackSource.ToOrganisationEndpoint().Authentication.Type,
                fallbackSource.ToOrganisationEndpoint().Authentication.ResolvedAccessToken);
            return true;
        }

        endpointInfo = null!;
        return false;
    }

    private static bool TryBuildTaskTargetEndpointInfo(
        JobTask task,
        ITargetEndpointInfo? fallbackTarget,
        out ITargetEndpointInfo endpointInfo)
    {
        var project = task.ProjectName;
        if (string.IsNullOrWhiteSpace(project) || fallbackTarget is null)
        {
            endpointInfo = null!;
            return false;
        }

        var projectName = project!;

        endpointInfo = new TaskTargetEndpointInfo(
            fallbackTarget.Url ?? string.Empty,
            projectName,
            fallbackTarget.ConnectorType,
            fallbackTarget.ToOrganisationEndpoint().ApiVersion,
            fallbackTarget.ToOrganisationEndpoint().Authentication.Type,
            fallbackTarget.ToOrganisationEndpoint().Authentication.ResolvedAccessToken);
        return true;
    }

    /// <summary>
    /// Loads the persisted plan from <c>.migration/plan.json</c> and resets
    /// any <c>Running</c> tasks to <c>Pending</c> (crash recovery).
    /// Returns <c>null</c> if the plan file does not exist or cannot be deserialised.
    /// </summary>
    public static async Task<JobTaskList?> LoadOrResetAsync(
        IPackageAccess? package,
        CancellationToken ct)
    {
        try
        {
            string? json;
            json = await ReadPlanStateAsync(package, ".migration/plan.json", ct).ConfigureAwait(false);

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
        if (parts.Length < 2)
            return taskId; // Fallback if task ID doesn't follow convention.

        // parts[0] = kind (capture/export/import/…), parts[1] = module name.
        // Additional segments are org/project slugs — ignored here.
        var lowerName = parts[1];
        var match = registeredModuleNames.FirstOrDefault(
            name => name.Equals(lowerName, StringComparison.OrdinalIgnoreCase));

        return match ?? lowerName; // Return matched name with correct casing, or fallback.
    }
}
