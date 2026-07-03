// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using DevOpsMigrationPlatform.Abstractions.Jobs;

namespace DevOpsMigrationPlatform.CLI.Migration.Services;

/// <summary>
/// Read-model service for job task summaries consumed by CLI progress rendering.
/// Owns phase resolution, phase membership, discovery-module derivation, task
/// aggregation, duration/ETA statistics, and blocking-findings evaluation so
/// commands (e.g. <c>QueueCommand</c>) and views (e.g. <c>TuiTaskProgressView</c>)
/// only render the resulting view model. Contains no console output.
/// </summary>
internal static class JobTaskSummaryService
{
    /// <summary>Returns the ordered phase names for a task list, falling back to distinct per-task phases.</summary>
    internal static IReadOnlyList<string> GetOrderedTaskPhases(JobTaskList taskList)
    {
        if (taskList.Phases.Count > 0)
        {
            return taskList.Phases
                .OrderBy(phase => phase.Order)
                .Select(phase => phase.Name)
                .ToList();
        }

        return taskList.Tasks
            .Select(t => t.Phase)
            .Where(phase => !string.IsNullOrWhiteSpace(phase))
            .Select(phase => phase!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Determines the phase the job is currently in: the first running task's phase,
    /// then the first pending task's phase, then a stage-name match, then the last
    /// terminal task's phase, finally the last known phase.
    /// </summary>
    internal static string DetermineCurrentTaskPhase(JobTaskList taskList, string? stage, IReadOnlyList<string> phases)
    {
        var runningPhase = taskList.Tasks
            .Where(t => t.Status == JobTaskStatus.Running)
            .Select(t => ResolveTaskPhase(taskList, t))
            .Where(phase => !string.IsNullOrWhiteSpace(phase))
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(runningPhase))
            return runningPhase;

        var pendingPhase = taskList.Tasks
            .Where(t => t.Status == JobTaskStatus.Pending)
            .Select(t => ResolveTaskPhase(taskList, t))
            .Where(phase => !string.IsNullOrWhiteSpace(phase))
            .FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(pendingPhase))
            return pendingPhase;

        if (!string.IsNullOrWhiteSpace(stage))
        {
            var match = phases.FirstOrDefault(phase => stage.Contains(phase, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrWhiteSpace(match))
                return match;
        }

        var terminalPhase = taskList.Tasks
            .Where(t => IsTerminal(t.Status))
            .OrderBy(t => t.Order)
            .Select(t => ResolveTaskPhase(taskList, t))
            .Where(phase => !string.IsNullOrWhiteSpace(phase))
            .LastOrDefault();
        if (!string.IsNullOrWhiteSpace(terminalPhase))
            return terminalPhase;

        return phases[phases.Count - 1];
    }

    /// <summary>Returns whether a task belongs to the given phase filter (null filter renders all).</summary>
    internal static bool ShouldRenderTaskInPhase(JobTaskList taskList, JobTask task, string? phaseFilter)
    {
        if (string.IsNullOrWhiteSpace(phaseFilter))
            return true;

        if (taskList.Phases.Count > 0)
        {
            var phaseSummary = taskList.Phases
                .FirstOrDefault(phase => string.Equals(phase.Name, phaseFilter, StringComparison.OrdinalIgnoreCase));
            if (phaseSummary is not null)
            {
                return phaseSummary.TaskIds.Contains(task.Id, StringComparer.OrdinalIgnoreCase);
            }
        }

        return string.Equals(ResolveTaskPhase(taskList, task), phaseFilter, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resolves the phase name of a task from explicit phase summaries, task phase, or task kind.</summary>
    internal static string ResolveTaskPhase(JobTaskList taskList, JobTask task)
    {
        if (taskList.Phases.Count > 0)
        {
            var phaseName = taskList.Phases
                .OrderBy(phase => phase.Order)
                .FirstOrDefault(phase => phase.TaskIds.Contains(task.Id, StringComparer.OrdinalIgnoreCase))
                ?.Name;
            if (!string.IsNullOrWhiteSpace(phaseName))
                return phaseName!;
        }

        return task.Phase ?? task.TaskKind.ToString();
    }

    /// <summary>
    /// Extracts the discovery module segment (parts[1]) from a task ID like
    /// <c>capture.workitems.orgslug.project</c>, falling back to the full task ID.
    /// </summary>
    internal static string GetDiscoveryTaskModule(string taskId)
    {
        var parts = taskId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : taskId;
    }

    /// <summary>
    /// Canonical lowercase phase for a task: its explicit <c>Phase</c> when present,
    /// otherwise the phase implied by its <see cref="TaskKind"/>.
    /// </summary>
    internal static string GetCanonicalTaskPhase(JobTask task)
    {
        if (!string.IsNullOrWhiteSpace(task.Phase))
            return task.Phase!.Trim().ToLowerInvariant();

        return task.TaskKind switch
        {
            TaskKind.Export => "export",
            TaskKind.Prepare => "prepare",
            TaskKind.Import => "import",
            TaskKind.Validate => "validate",
            TaskKind.Capture => "inventory",
            TaskKind.Analyse => "inventory",
            TaskKind.Dependencies => "dependencies",
            _ => "work"
        };
    }

    /// <summary>
    /// Returns whether a task is a member of the given phase, preferring explicit
    /// phase summaries and falling back to the canonical (kind-derived) task phase.
    /// </summary>
    internal static bool TaskBelongsToPhase(JobTaskList taskList, JobTask task, string phase)
    {
        if (taskList.Phases.Count > 0)
        {
            var phaseSummary = taskList.Phases
                .FirstOrDefault(summary => string.Equals(summary.Name, phase, StringComparison.OrdinalIgnoreCase));
            if (phaseSummary is not null)
                return phaseSummary.TaskIds.Contains(task.Id, StringComparer.OrdinalIgnoreCase);
        }

        return GetCanonicalTaskPhase(task).Equals(phase, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the phase name of a task from explicit phase summaries, falling back
    /// to the canonical (kind-derived) task phase.
    /// </summary>
    internal static string ResolveCanonicalTaskPhase(JobTaskList taskList, JobTask task)
    {
        if (taskList.Phases.Count > 0)
        {
            var phaseName = taskList.Phases
                .OrderBy(summary => summary.Order)
                .FirstOrDefault(summary => summary.TaskIds.Contains(task.Id, StringComparer.OrdinalIgnoreCase))
                ?.Name;
            if (!string.IsNullOrWhiteSpace(phaseName))
                return phaseName!;
        }

        return GetCanonicalTaskPhase(task);
    }

    /// <summary>Whether a task status is terminal (Completed, Failed, or Skipped).</summary>
    internal static bool IsTerminal(JobTaskStatus status) =>
        status is JobTaskStatus.Completed or JobTaskStatus.Failed or JobTaskStatus.Skipped;

    /// <summary>Elapsed duration of a task, or null when start/completion timestamps are missing or inverted.</summary>
    internal static TimeSpan? TryGetTaskElapsed(JobTask task)
    {
        if (!task.StartedAt.HasValue || !task.CompletedAt.HasValue)
            return null;

        var elapsed = task.CompletedAt.Value - task.StartedAt.Value;
        return elapsed > TimeSpan.Zero ? elapsed : null;
    }

    /// <summary>
    /// Estimates total remaining time across all non-terminal tasks: revision-based for
    /// WorkItems, throughput-based for running counted tasks, otherwise the average
    /// completed-task duration. Returns null when no estimate is possible.
    /// </summary>
    internal static TimeSpan? ComputeOverallTaskEta(
        JobTaskList? taskList,
        int revisionsWritten,
        double avgRevDurationMs,
        int estimatedTotalRevisions)
    {
        if (taskList is null)
            return null;

        var completedDurations = taskList.Tasks
            .Select(TryGetTaskElapsed)
            .Where(duration => duration.HasValue)
            .Select(duration => duration!.Value)
            .ToArray();

        var averageCompletedTaskDuration = completedDurations.Length > 0
            ? TimeSpan.FromMilliseconds(completedDurations.Average(duration => duration.TotalMilliseconds))
            : (TimeSpan?)null;

        TimeSpan totalRemaining = TimeSpan.Zero;
        var hasEstimate = false;

        foreach (var task in taskList.Tasks)
        {
            if (IsTerminal(task.Status))
                continue;

            if (task.Id.Contains("workitems", StringComparison.OrdinalIgnoreCase))
            {
                var workItemsEta = ComputeRemainingRevisionTime(revisionsWritten, estimatedTotalRevisions, avgRevDurationMs);
                if (workItemsEta.HasValue)
                {
                    totalRemaining += workItemsEta.Value;
                    hasEstimate = true;
                    continue;
                }
            }

            if (task.Status == JobTaskStatus.Running
                && task.StartedAt.HasValue
                && task.KnownTotal.HasValue
                && task.KnownTotal.Value > 0
                && task.CompletedCount.HasValue
                && task.CompletedCount.Value > 0)
            {
                var elapsed = DateTimeOffset.UtcNow - task.StartedAt.Value;
                var remainingCount = task.KnownTotal.Value - task.CompletedCount.Value;
                if (remainingCount > 0)
                {
                    totalRemaining += TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / task.CompletedCount.Value * remainingCount);
                    hasEstimate = true;
                    continue;
                }
            }

            if (averageCompletedTaskDuration.HasValue)
            {
                totalRemaining += averageCompletedTaskDuration.Value;
                hasEstimate = true;
            }
        }

        return hasEstimate ? totalRemaining : null;
    }

    /// <summary>Remaining revision-processing time, or null when inputs cannot support an estimate.</summary>
    internal static TimeSpan? ComputeRemainingRevisionTime(int revisionsWritten, int estimatedTotalRevisions, double avgRevDurationMs)
    {
        if (avgRevDurationMs <= 0 || estimatedTotalRevisions <= 0 || revisionsWritten <= 0)
            return null;

        var remainingRevisions = Math.Max(0, estimatedTotalRevisions - revisionsWritten);
        return remainingRevisions > 0
            ? TimeSpan.FromMilliseconds(remainingRevisions * avgRevDurationMs)
            : TimeSpan.Zero;
    }

    /// <summary>Formatted revision ETA string ("--:--:--" when no estimate is possible).</summary>
    internal static string ComputeRevisionEta(int revisionsWritten, int estimatedTotalRevisions, double avgRevDurationMs)
    {
        if (avgRevDurationMs <= 0 || estimatedTotalRevisions <= 0 || revisionsWritten <= 0)
            return "--:--:--";
        var remainingRevisions = Math.Max(0, estimatedTotalRevisions - revisionsWritten);
        var remainingSecs = remainingRevisions * avgRevDurationMs / 1000.0;
        var eta = TimeSpan.FromSeconds(remainingSecs);
        return eta.TotalHours >= 1
            ? $"{(int)eta.TotalHours}:{eta.Minutes:D2}:{eta.Seconds:D2}"
            : $"--:{eta.Minutes:D2}:{eta.Seconds:D2}";
    }

    /// <summary>
    /// Evaluates the package's prepare report (<c>WorkItems/prepare-report.json</c>) for
    /// blocking import-readiness findings. Returns true when one or more blocking finding
    /// messages exist; any parse or I/O failure yields false.
    /// </summary>
    internal static bool TryGetBlockingPrepareFindings(string outputPath, out List<string> blockingFindings)
    {
        blockingFindings = [];

        try
        {
            var prepareReportPath = Path.Combine(outputPath, "WorkItems", "prepare-report.json");
            if (!File.Exists(prepareReportPath))
                return false;

            using var doc = JsonDocument.Parse(File.ReadAllText(prepareReportPath));
            if (!doc.RootElement.TryGetProperty("ImportReadinessReport", out var readiness)
                || readiness.ValueKind != JsonValueKind.Object
                || !readiness.TryGetProperty("BlockingFindings", out var findings)
                || findings.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            foreach (var finding in findings.EnumerateArray())
            {
                if (finding.TryGetProperty("Message", out var messageElement)
                    && messageElement.ValueKind == JsonValueKind.String)
                {
                    var message = messageElement.GetString();
                    if (!string.IsNullOrWhiteSpace(message))
                        blockingFindings.Add(message!);
                }
            }

            return blockingFindings.Count > 0;
        }
        catch
        {
            return false;
        }
    }
}
