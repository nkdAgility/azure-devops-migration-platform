// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
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
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
using Microsoft.Extensions.Configuration;
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
    private readonly IJobConfiguration? _jobConfig;

    // IJobConfiguration.PackageConfig is a shared mutable reference for the entire job scope,
    // so all capture-time overlay mutations must be serialised globally.
    private readonly SemaphoreSlim _packageConfigLock = new(1, 1);

    public JobPlanExecutor(
        IProgressSink? progressSink,
        ILogger<JobPlanExecutor> logger,
        IJobConfiguration? jobConfig = null)
    {
        _progressSink = progressSink;
        _logger = logger;
        _jobConfig = jobConfig;
    }

    public async Task<bool> ExecuteTasksAsync(
        JobTaskList plan,
        IReadOnlyDictionary<string, ICapture> captureHandlersByName,
        IReadOnlyDictionary<string, IAnalyser> analysersByName,
        InventoryContext? baseInventoryContext,
        ExportContext? baseExportContext,
        ImportContext? importContext,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
        IStateStore stateStore,
        CancellationToken ct)
    {
        var tasks = plan.Tasks
            .Where(t => t.Status != JobTaskStatus.Skipped && t.Status != JobTaskStatus.Completed)
            .ToList();

        if (tasks.Count == 0)
        {
            _logger.LogInformation("ExecuteTasksAsync: no tasks to execute (all skipped or completed).");
            return true;
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
                baseExportContext, importContext, endpointsByUrl, stateStore, plan, ct)
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
                            if (idx >= 0)
                            {
                                taskList[idx] = skipped;
                                plan = plan with { Tasks = taskList.AsReadOnly() };
                                await PersistPlanAsync(plan, stateStore, ct).ConfigureAwait(false);

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
            }

            _logger.LogInformation(
                "Tier {TierIndex} complete: {Succeeded} succeeded, {Failed} failed.",
                tierIndex, tier.Count - result.FailedTaskIds.Count, result.FailedTaskIds.Count);
        }

        if (anyFailed)
        {
            _logger.LogWarning("ExecuteTasksAsync completed with {FailedCount} failed task(s).", failedTasks.Count);
            return false;
        }

        return true;
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
                tier, modulesByName.ToDictionary(kvp => kvp.Key, kvp => (ICapture)kvp.Value, StringComparer.OrdinalIgnoreCase), analysersByName: null, baseInventoryContext: null,
                exportContext, importContext: null, endpointsByUrl: null, stateStore, plan, ct)
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
                tier, modulesByName.ToDictionary(kvp => kvp.Key, kvp => (ICapture)kvp.Value, StringComparer.OrdinalIgnoreCase), analysersByName: null, baseInventoryContext: null,
                exportContext: null, importContext, endpointsByUrl: null, stateStore, plan, ct)
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
        IReadOnlyDictionary<string, ICapture> captureHandlersByName,
        IReadOnlyDictionary<string, IAnalyser>? analysersByName,
        InventoryContext? baseInventoryContext,
        ExportContext? exportContext,
        ImportContext? importContext,
        IReadOnlyDictionary<string, OrganisationEndpoint>? endpointsByUrl,
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
                await PersistPlanAsync(updatedPlan, stateStore, ct).ConfigureAwait(false);
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

                if (analyserForTask is not null)
                {
                    // Analyse task: build context from whichever base context is available.
                    var job = (baseInventoryContext?.Job ?? exportContext?.Job ?? importContext?.Job)
                        ?? throw new InvalidOperationException("No job context available to build AnalyseContext.");
                    var artefactStore = (baseInventoryContext?.ArtefactStore ?? exportContext?.ArtefactStore ?? importContext?.ArtefactStore)
                        ?? throw new InvalidOperationException("No ArtefactStore available.");
                    var stateStoreForAnalyse = (baseInventoryContext?.StateStore ?? exportContext?.StateStore ?? importContext?.StateStore)
                        ?? throw new InvalidOperationException("No StateStore available.");
                    var progressSink = baseInventoryContext?.ProgressSink ?? exportContext?.ProgressSink ?? importContext?.ProgressSink;

                    // IOrganisationsAnalyser (e.g. DependencyAnalyser) needs the full organisations list
                    // to run the final fan-in step across all captured projects.
                    AnalyseContext analyseContext =
                        analyserForTask is IOrganisationsAnalyser && baseInventoryContext?.Organisations is { Count: > 0 } orgs
                            ? new OrganisationsAnalyseContext
                            {
                                Job = job,
                                ArtefactStore = artefactStore,
                                StateStore = stateStoreForAnalyse,
                                ProgressSink = progressSink,
                                Policies = baseInventoryContext.Policies,
                                Organisations = orgs
                            }
                            : new AnalyseContext
                            {
                                Job = job,
                                ArtefactStore = artefactStore,
                                StateStore = stateStoreForAnalyse,
                                ProgressSink = progressSink
                            };

                    await analyserForTask.AnalyseAsync(analyseContext, ct).ConfigureAwait(false);
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

                    // Apply a temporary config overlay so IJobConfiguration.PackageConfig
                    // carries the correct Source.Type/Url/Auth for this capture.
                    // The configuration object is shared across the whole job scope,
                    // so mutation must be serialised globally.
                    if (_jobConfig is not null && endpoint is not null)
                    {
                        await _packageConfigLock.WaitAsync(ct).ConfigureAwait(false);
                        try
                        {
                            var prev = _jobConfig.PackageConfig;
                            _jobConfig.PackageConfig = BuildCaptureConfigOverlay(prev, endpoint);
                            try
                            {
                                await captureHandler.CaptureAsync(scopedCtx, ct).ConfigureAwait(false);
                            }
                            finally
                            {
                                _jobConfig.PackageConfig = prev;
                            }
                        }
                        finally
                        {
                            _packageConfigLock.Release();
                        }
                    }
                    else
                    {
                        await captureHandler.CaptureAsync(scopedCtx, ct).ConfigureAwait(false);
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
                                    ArtefactStore = exportContext.ArtefactStore,
                                    StateStore = exportContext.StateStore,
                                    ProgressSink = exportContext.ProgressSink,
                                    MetricsStore = exportContext.MetricsStore,
                                    SnapshotStore = exportContext.SnapshotStore,
                                    Organisations = exportContext.Organisations,
                                    Project = task.ProjectName ?? string.Empty
                                };

                                await module!.ExportAsync(scopedCtx, ct).ConfigureAwait(false);
                                break;
                            }
                        case TaskKind.Import:
                            {
                                if (importContext is null)
                                    throw new InvalidOperationException($"Import task {task.Id} requires an ImportContext.");

                                await module!.ImportAsync(importContext, ct).ConfigureAwait(false);
                                break;
                            }
                        default:
                            _logger.LogWarning(
                                "Task {TaskId} has unsupported TaskKind '{Kind}' — skipping execution.",
                                task.Id, task.TaskKind);
                            break;
                    }
                }

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

                await persistLock.WaitAsync(ct).ConfigureAwait(false);
                try
                {
                    lock (planLock) { updatedPlan = plan; }
                    await PersistPlanAsync(updatedPlan, stateStore, ct).ConfigureAwait(false);
                }
                finally
                {
                    persistLock.Release();
                }

                _progressSink?.Emit(new ProgressEvent
                {
                    Module = handlerName,
                    Stage = $"{task.TaskKind}.Complete",
                    Message = $"{handlerName} {task.TaskKind} completed.",
                    Timestamp = updated.CompletedAt!.Value,
                    TaskId = task.Id,
                    TaskStatus = JobTaskStatus.Completed
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
                    await PersistPlanAsync(updatedPlan, stateStore, ct).ConfigureAwait(false);
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
    /// Builds a configuration that overlays the per-org source endpoint settings on top of
    /// <paramref name="baseConfig"/>.  Used before executing a Capture task so that
    /// <c>ActiveJobSourceEndpointInfo</c> resolves the correct connector type, URL, and
    /// credentials for each organisation.
    /// </summary>
    private static IConfiguration BuildCaptureConfigOverlay(IConfiguration? baseConfig, OrganisationEndpoint endpoint)
    {
        var builder = new ConfigurationBuilder();
        if (baseConfig is not null)
            builder.AddConfiguration(baseConfig);

        var overlay = new Dictionary<string, string?>
        {
            ["MigrationPlatform:Source:Type"] = endpoint.Type,
            ["MigrationPlatform:Source:Url"] = endpoint.ResolvedUrl,
            ["MigrationPlatform:Source:Authentication:Type"] = endpoint.Authentication.Type.ToString(),
            ["MigrationPlatform:Source:Authentication:AccessToken"] = endpoint.Authentication.ResolvedAccessToken,
        };
        if (endpoint.ApiVersion is not null)
            overlay["MigrationPlatform:Source:ApiVersion"] = endpoint.ApiVersion;

        return builder.AddInMemoryCollection(overlay).Build();
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
