// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Renders the selected job's task/progress workspace.
/// The shell decides which job is selected; this view decides how to render its mode.
/// </summary>
public sealed class TuiTaskProgressView : FrameView
{
    private static readonly string[] s_migrationPhaseOrder = ["inventory", "export", "prepare", "import", "validate"];

    private readonly Label _content;

    public TuiTaskProgressView()
    {
        Title = "Task and Progress";
        CanFocus = false;

        _content = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(),
            Text = "(no job selected)"
        };

        Add(_content);
    }

    public void Clear()
    {
        Application.Invoke(() =>
        {
            _content.Text = "(no job selected)";
            SetNeedsDraw();
        });
    }

    public void SetWaiting(string? mode)
    {
        Application.Invoke(() =>
        {
            var label = string.IsNullOrWhiteSpace(mode) ? "job" : mode;
            _content.Text = $"(waiting for {label} state...)";
            SetNeedsDraw();
        });
    }

    public void Update(
        JobSummary? summary,
        JobTaskList? tasks,
        JobMetrics? metrics,
        ProgressEvent? lastEvent,
        JobSnapshot? snapshot)
    {
        Application.Invoke(() =>
        {
            _content.Text = BuildContent(summary, tasks, metrics, lastEvent, snapshot);
            SetNeedsDraw();
        });
    }

    private static string BuildContent(
        JobSummary? summary,
        JobTaskList? tasks,
        JobMetrics? metrics,
        ProgressEvent? lastEvent,
        JobSnapshot? snapshot)
    {
        if (summary is null)
            return "(no job selected)";

        var mode = summary.Mode ?? string.Empty;
        if (mode.Equals("Inventory", StringComparison.OrdinalIgnoreCase))
            return BuildInventoryWorkspace(summary, tasks, metrics, snapshot);

        if (mode.Equals("Dependencies", StringComparison.OrdinalIgnoreCase))
            return BuildDependenciesWorkspace(summary, tasks, metrics, snapshot);

        return BuildMigrationWorkspace(summary, tasks, metrics, lastEvent);
    }

    private static string BuildMigrationWorkspace(
        JobSummary summary,
        JobTaskList? taskList,
        JobMetrics? metrics,
        ProgressEvent? lastEvent)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mode: {summary.Mode}    State: {summary.State}    Job: {summary.JobId:D}");

        if (taskList is null || taskList.Tasks.Count == 0)
        {
            builder.AppendLine();
            builder.Append("Waiting for task plan...");
            return builder.ToString();
        }

        var tasks = taskList.Tasks.OrderBy(t => t.Order).ToList();
        var currentPhase = DetermineCurrentPhase(tasks, lastEvent, summary.Mode);
        var orderedPhases = tasks
            .Select(GetTaskPhase)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(GetPhaseSortKey)
            .ToList();

        builder.AppendLine(BuildStageStrip(orderedPhases, currentPhase));

        if (metrics?.Migration?.WorkItems is { } wi)
        {
            builder.AppendLine(
                $"WorkItems  attempted {wi.Attempted:N0}  completed {wi.Completed:N0}  revisions {wi.RevisionsProcessed:N0}  failed {wi.Failed:N0}  skipped {wi.Skipped:N0}");
        }

        if (!string.IsNullOrWhiteSpace(lastEvent?.Stage) || !string.IsNullOrWhiteSpace(lastEvent?.Message))
        {
            builder.AppendLine($"Cursor     {lastEvent?.Stage ?? "-"}  {lastEvent?.Message ?? string.Empty}".TrimEnd());
        }

        var activeTask = tasks.FirstOrDefault(t => t.Status == JobTaskStatus.Running)
            ?? tasks.FirstOrDefault(t => t.Status == JobTaskStatus.Pending);

        foreach (var phase in orderedPhases)
        {
            builder.AppendLine();
            builder.AppendLine($"{(phase.Equals(currentPhase, StringComparison.OrdinalIgnoreCase) ? '>' : ' ')} {DisplayPhase(phase)}");

            foreach (var task in tasks.Where(t => GetTaskPhase(t).Equals(phase, StringComparison.OrdinalIgnoreCase)))
            {
                var prefix = activeTask?.Id == task.Id ? ">" : " ";
                var icon = GetStatusIcon(task.Status);
                var bar = BuildBar(task.CompletedCount, task.KnownTotal, 14);
                var counts = FormatCounts(task.CompletedCount, task.KnownTotal);
                var timing = FormatTaskTiming(task);
                builder.AppendLine($"{prefix} {icon} {task.Name,-28} {bar} {counts}{timing}");
            }
        }

        var remainingTasks = tasks.Count(t => !IsTerminal(t.Status));
        var overallEta = ComputeOverallEta(tasks);
        builder.AppendLine();
        builder.Append($"Remaining tasks: {remainingTasks}");
        if (overallEta.HasValue)
            builder.Append($"    Overall ETA: {FormatDuration(overallEta.Value)}");

        if (IsProbableBackoff(metrics))
        {
            builder.AppendLine();
            builder.Append("Warning: recent WorkItems timing suggests probable exponential back-off.");
        }

        return builder.ToString();
    }

    private static string BuildInventoryWorkspace(
        JobSummary summary,
        JobTaskList? taskList,
        JobMetrics? metrics,
        JobSnapshot? snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mode: {summary.Mode}    State: {summary.State}    Job: {summary.JobId:D}");

        var scope = metrics?.Scope;
        var inventory = metrics?.Discovery?.Inventory;
        builder.AppendLine(
            $"Totals     orgs {scope?.OrganisationsCompleted ?? 0}/{scope?.OrganisationsTotal ?? 0}  projects {scope?.ProjectsCompleted ?? 0}/{scope?.ProjectsTotal ?? 0}  work items {scope?.WorkItemsTotal ?? 0:N0}  revisions {inventory?.RevisionsTotal ?? 0:N0}  repos {inventory?.RepositoriesTotal ?? 0:N0}");

        builder.AppendLine();
        builder.AppendLine("Projects");
        builder.AppendLine("Org                          Project                    Status        WorkItems  Revisions  Repos");
        builder.AppendLine("---------------------------  -------------------------  ------------  ---------  ---------  -----");

        foreach (var row in EnumerateDiscoveryRows(snapshot).Take(12))
        {
            builder.AppendLine(
                $"{Truncate(row.Org, 27),-27}  {Truncate(row.Project, 25),-25}  {row.Status,-12}  {row.WorkItems,9:N0}  {row.Revisions,9:N0}  {row.Repos,5:N0}");
        }

        if (snapshot is null || snapshot.Organisations.Count == 0)
            builder.AppendLine("(waiting for project snapshot...)");

        if (taskList is { Tasks.Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("Tasks");

            foreach (var group in taskList.Tasks
                .Where(t => t.TaskKind == TaskKind.Capture)
                .GroupBy(t => GetDiscoveryModule(t.Id))
                .OrderBy(g => GetDiscoveryModuleSortKey(g.Key)))
            {
                var total = group.Count();
                var completed = group.Count(t => t.Status == JobTaskStatus.Completed);
                var running = group.Any(t => t.Status == JobTaskStatus.Running);
                var label = completed == total ? "completed" : running ? "running" : "waiting";
                builder.AppendLine($"{GetStatusIconForSummary(label)} {DisplayDiscoveryModule(group.Key),-16} {completed}/{total} projects  {label}");
            }

            foreach (var analyseTask in taskList.Tasks.Where(t => t.TaskKind == TaskKind.Analyse).OrderBy(t => t.Order))
            {
                var label = analyseTask.Status == JobTaskStatus.Pending ? "waiting on capture tasks" : analyseTask.Status.ToString().ToLowerInvariant();
                builder.AppendLine($"{GetStatusIcon(analyseTask.Status)} {analyseTask.Name,-16} {label}");
            }
        }

        return builder.ToString();
    }

    private static string BuildDependenciesWorkspace(
        JobSummary summary,
        JobTaskList? taskList,
        JobMetrics? metrics,
        JobSnapshot? snapshot)
    {
        var builder = new StringBuilder();
        builder.AppendLine($"Mode: {summary.Mode}    State: {summary.State}    Job: {summary.JobId:D}");

        var scope = metrics?.Scope;
        var dependencies = metrics?.Discovery?.Dependencies;
        builder.AppendLine(
            $"Totals     orgs {scope?.OrganisationsCompleted ?? 0}/{scope?.OrganisationsTotal ?? 0}  projects {scope?.ProjectsCompleted ?? 0}/{scope?.ProjectsTotal ?? 0}  analysed {dependencies?.WorkItemsAnalysed ?? 0:N0}  external {dependencies?.ExternalLinksFound ?? 0:N0}");

        builder.AppendLine();
        builder.AppendLine("Projects");
        builder.AppendLine("Org                          Project                    Status        ExtLinks  CrossProj  CrossOrg");
        builder.AppendLine("---------------------------  -------------------------  ------------  --------  ---------  --------");

        foreach (var row in EnumerateDiscoveryRows(snapshot).Take(12))
        {
            builder.AppendLine(
                $"{Truncate(row.Org, 27),-27}  {Truncate(row.Project, 25),-25}  {row.Status,-12}  {row.ExternalLinks,8:N0}  {row.CrossProjectLinks,9:N0}  {row.CrossOrgLinks,8:N0}");
        }

        if (snapshot is null || snapshot.Organisations.Count == 0)
            builder.AppendLine("(waiting for project snapshot...)");

        if (taskList is { Tasks.Count: > 0 })
        {
            builder.AppendLine();
            builder.AppendLine("Tasks");

            foreach (var task in taskList.Tasks.OrderBy(t => t.Order))
            {
                var progress = FormatCounts(task.CompletedCount, task.KnownTotal);
                builder.AppendLine($"{GetStatusIcon(task.Status)} {task.Name,-24} {progress}");
            }
        }

        return builder.ToString();
    }

    private static IEnumerable<DiscoveryRow> EnumerateDiscoveryRows(JobSnapshot? snapshot)
    {
        if (snapshot is null)
            yield break;

        foreach (var org in snapshot.Organisations)
        {
            foreach (var project in org.Projects.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                yield return new DiscoveryRow(
                    string.IsNullOrWhiteSpace(org.Name) ? org.Url : org.Name,
                    project.Name,
                    project.Status.ToString(),
                    project.Discovery?.Inventory?.WorkItemsTotal ?? 0,
                    project.Discovery?.Inventory?.RevisionsTotal ?? 0,
                    project.Discovery?.Inventory?.RepositoriesTotal ?? 0,
                    project.Discovery?.Dependencies?.ExternalLinksFound ?? 0,
                    project.Discovery?.Dependencies?.CrossProjectLinks ?? 0,
                    project.Discovery?.Dependencies?.CrossOrgLinks ?? 0);
            }
        }
    }

    private static string BuildStageStrip(IEnumerable<string> phases, string currentPhase)
    {
        return "Stages     " + string.Join(
            "  ",
            phases.Select(phase => phase.Equals(currentPhase, StringComparison.OrdinalIgnoreCase)
                ? $"[{DisplayPhase(phase)}]"
                : DisplayPhase(phase)));
    }

    private static string DetermineCurrentPhase(IReadOnlyList<JobTask> tasks, ProgressEvent? lastEvent, string mode)
    {
        var running = tasks.FirstOrDefault(t => t.Status == JobTaskStatus.Running);
        if (running is not null)
            return GetTaskPhase(running);

        var pending = tasks.FirstOrDefault(t => t.Status == JobTaskStatus.Pending);
        if (pending is not null)
            return GetTaskPhase(pending);

        if (!string.IsNullOrWhiteSpace(lastEvent?.Stage))
        {
            var stage = lastEvent.Stage.ToLowerInvariant();
            foreach (var phase in s_migrationPhaseOrder)
            {
                if (stage.Contains(phase, StringComparison.OrdinalIgnoreCase))
                    return phase;
            }
        }

        return mode.Equals("Migrate", StringComparison.OrdinalIgnoreCase)
            ? "inventory"
            : mode.ToLowerInvariant();
    }

    private static string GetTaskPhase(JobTask task)
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

    private static int GetPhaseSortKey(string phase)
    {
        var idx = Array.FindIndex(s_migrationPhaseOrder, p => p.Equals(phase, StringComparison.OrdinalIgnoreCase));
        return idx >= 0 ? idx : int.MaxValue;
    }

    private static string DisplayPhase(string phase) => phase switch
    {
        "inventory" => "Inventory",
        "export" => "Export",
        "prepare" => "Prepare",
        "import" => "Import",
        "validate" => "Validate",
        _ => char.ToUpperInvariant(phase[0]) + phase[1..]
    };

    private static string GetDiscoveryModule(string taskId)
    {
        var parts = taskId.Split('.', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length >= 2 ? parts[1] : taskId;
    }

    private static int GetDiscoveryModuleSortKey(string module) => module.ToLowerInvariant() switch
    {
        "identities" => 0,
        "nodes" => 1,
        "teams" => 2,
        "workitems" => 3,
        "repos" => 4,
        _ => 99
    };

    private static string DisplayDiscoveryModule(string module) => module.ToLowerInvariant() switch
    {
        "workitems" => "WorkItems",
        "repos" => "Repos",
        _ => char.ToUpperInvariant(module[0]) + module[1..]
    };

    private static string BuildBar(long? completed, long? total, int width)
    {
        if (!completed.HasValue || !total.HasValue || total.Value <= 0)
            return "[--------------]";

        var ratio = Math.Clamp((double)completed.Value / total.Value, 0d, 1d);
        var filled = (int)Math.Round(ratio * width, MidpointRounding.AwayFromZero);
        return "[" + new string('#', filled) + new string('-', Math.Max(0, width - filled)) + "]";
    }

    private static string FormatCounts(long? completed, long? total)
    {
        if (completed.HasValue && total.HasValue && total.Value > 0)
            return $" {completed.Value:N0}/{total.Value:N0}";

        if (completed.HasValue)
            return $" {completed.Value:N0}";

        return string.Empty;
    }

    private static string FormatTaskTiming(JobTask task)
    {
        if (task.StartedAt is null)
            return string.Empty;

        var end = task.CompletedAt ?? DateTimeOffset.UtcNow;
        var elapsed = end - task.StartedAt.Value;

        if (task.Status == JobTaskStatus.Completed)
            return $"  done in {FormatDuration(elapsed)}";

        if (task.CompletedCount.HasValue && task.KnownTotal.HasValue && task.CompletedCount.Value > 0 && task.KnownTotal.Value > task.CompletedCount.Value)
        {
            var remaining = task.KnownTotal.Value - task.CompletedCount.Value;
            var msPerItem = elapsed.TotalMilliseconds / task.CompletedCount.Value;
            var eta = TimeSpan.FromMilliseconds(msPerItem * remaining);
            return $"  eta {FormatDuration(eta)}";
        }

        return $"  elapsed {FormatDuration(elapsed)}";
    }

    private static TimeSpan? ComputeOverallEta(IReadOnlyList<JobTask> tasks)
    {
        var completedDurations = tasks
            .Where(t => t.Status == JobTaskStatus.Completed && t.StartedAt.HasValue && t.CompletedAt.HasValue)
            .Select(t => t.CompletedAt!.Value - t.StartedAt!.Value)
            .ToList();

        var remaining = tasks.Count(t => !IsTerminal(t.Status));
        if (completedDurations.Count == 0 || remaining == 0)
            return null;

        var avgTicks = completedDurations.Average(ts => ts.Ticks);
        return TimeSpan.FromTicks((long)(avgTicks * remaining));
    }

    private static bool IsProbableBackoff(JobMetrics? metrics)
    {
        var wi = metrics?.Migration?.WorkItems;
        if (wi is null || wi.LastWorkItemDurationMs <= 0 || wi.AverageWorkItemDurationMs <= 0)
            return false;

        return wi.LastWorkItemDurationMs > wi.AverageWorkItemDurationMs * 3;
    }

    private static bool IsTerminal(JobTaskStatus status) =>
        status is JobTaskStatus.Completed or JobTaskStatus.Failed or JobTaskStatus.Skipped;

    private static string GetStatusIcon(JobTaskStatus status) => status switch
    {
        JobTaskStatus.Running => ">",
        JobTaskStatus.Completed => "v",
        JobTaskStatus.Failed => "x",
        JobTaskStatus.Skipped => "-",
        _ => "."
    };

    private static string GetStatusIconForSummary(string status) => status switch
    {
        "running" => ">",
        "completed" => "v",
        _ => "."
    };

    private static string FormatDuration(TimeSpan value)
    {
        if (value.TotalHours >= 1)
            return $"{(int)value.TotalHours}h {value.Minutes}m";

        if (value.TotalMinutes >= 1)
            return $"{(int)value.TotalMinutes}m {value.Seconds}s";

        return $"{Math.Max(0, (int)value.TotalSeconds)}s";
    }

    private static string Truncate(string value, int width)
    {
        if (string.IsNullOrEmpty(value) || value.Length <= width)
            return value;

        return value[..Math.Max(0, width - 1)] + "~";
    }

    private sealed record DiscoveryRow(
        string Org,
        string Project,
        string Status,
        long WorkItems,
        long Revisions,
        long Repos,
        long ExternalLinks,
        long CrossProjectLinks,
        long CrossOrgLinks);
}