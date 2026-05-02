// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Centre panel: displays the latest <see cref="JobMetrics"/> polled from
/// <c>GET /jobs/{jobId}/telemetry</c>.
/// All mutations must arrive via <see cref="Update"/> which marshals onto the
/// Terminal.Gui main-loop thread.
/// </summary>
public sealed class TuiMetricsView : FrameView
{
    private readonly Label _content;

    public TuiMetricsView()
    {
        Title = "Metrics";
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

    /// <summary>
    /// Replaces the displayed metrics. Pass <c>null</c> to show the placeholder.
    /// Safe to call from any thread — marshals via <see cref="Application.Invoke"/>.
    /// </summary>
    public void Update(JobMetrics? metrics)
    {
        Application.Invoke(() =>
        {
            if (metrics is null)
                _content.Text = "(no job selected)";
            else if (metrics.Discovery is not null)
                _content.Text = FormatDiscoveryMetrics(metrics);
            else
                _content.Text = FormatMigrationMetrics(metrics);
            SetNeedsDraw();
        });
    }

    /// <summary>Shows "(waiting for agent…)" when a job is selected but no metrics have arrived yet.</summary>
    public void SetWaiting()
    {
        Application.Invoke(() =>
        {
            _content.Text = "(waiting for agent\u2026)";
            SetNeedsDraw();
        });
    }

    private static string FormatMigrationMetrics(JobMetrics m)
    {
        var wi = m.Migration?.WorkItems;
        var diag = m.Migration?.Diagnostics;

        var lastMs = wi?.LastWorkItemDurationMs ?? 0;
        var avgMs = wi?.AverageWorkItemDurationMs ?? 0;
        var throttleIndicator = lastMs > 0 && avgMs > 0 && lastMs > avgMs * 3
            ? "  *** POSSIBLE BACK-OFF ***"
            : string.Empty;

        return
            $"Work Items Attempted : {wi?.Attempted ?? 0,8}\n" +
            $"Work Items Completed : {wi?.Completed ?? 0,8}\n" +
            $"Revisions Written    : {wi?.RevisionsProcessed ?? 0,8:N0}\n" +
            $"Work Items Failed    : {wi?.Failed ?? 0,8}\n" +
            $"Work Items Skipped   : {wi?.Skipped ?? 0,8}\n" +
            $"In-Flight            : {diag?.WorkItemsInFlight ?? 0,8}\n" +
            $"Queue Depth          : {diag?.QueueDepth ?? 0,8}\n" +
            $"\n" +
            $"Last WI Duration     : {FormatMean(lastMs > 0 ? lastMs : null, "ms")}{throttleIndicator}\n" +
            $"Avg WI Duration      : {FormatMean(avgMs > 0 ? avgMs : diag?.WorkItemDurationMeanMs, "ms")}\n" +
            $"Avg Revisions        : {FormatMean(diag?.RevisionCountMean)}\n" +
            $"Avg Attachments      : {FormatMean(diag?.AttachmentCountMean)}\n" +
            $"Avg Links            : {FormatMean(diag?.LinkCountMean)}\n" +
            $"Avg Fields           : {FormatMean(diag?.FieldCountMean)}\n" +
            $"Avg Payload          : {FormatMean(diag?.PayloadBytesMean, "B")}\n" +
            $"\n" +
            $"Broken Links         : {diag?.BrokenLinks ?? 0,8}\n" +
            $"Missing Work Items   : {diag?.MissingWorkItems ?? 0,8}\n" +
            $"Revisions Missing    : {diag?.RevisionsMissing ?? 0,8}\n" +
            $"Rev Order Errors     : {diag?.RevisionOrderErrors ?? 0,8}";
    }

    private static string FormatDiscoveryMetrics(JobMetrics m)
    {
        var scope = m.Scope;
        var inv = m.Discovery?.Inventory;
        var deps = m.Discovery?.Dependencies;

        return
            $"── Progress ──────────────────────\n" +
            $"Orgs Completed       : {scope.OrganisationsCompleted,8}\n" +
            $"Orgs Failed          : {scope.OrganisationsFailed,8}\n" +
            $"Orgs Total           : {scope.OrganisationsTotal,8}\n" +
            $"\n" +
            $"Projects Total       : {scope.ProjectsTotal,8}\n" +
            $"Projects Completed   : {scope.ProjectsCompleted,8}\n" +
            $"Projects Failed      : {scope.ProjectsFailed,8}\n" +
            $"\n" +
            $"── Inventory ─────────────────────\n" +
            $"Work Items Total     : {scope.WorkItemsTotal,8:N0}\n" +
            $"Revisions Total      : {inv?.RevisionsTotal ?? 0,8:N0}\n" +
            $"Repos Total          : {inv?.RepositoriesTotal ?? 0,8:N0}\n" +
            $"\n" +
            $"── Dependencies ──────────────────\n" +
            $"External Links Found : {deps?.ExternalLinksFound ?? 0,8:N0}\n" +
            $"Work Items Analysed  : {deps?.WorkItemsAnalysed ?? 0,8:N0}\n" +
            $"\n" +
            $"── Operational ───────────────────\n" +
            $"Checkpoints Saved    : {(inv?.CheckpointsSaved ?? 0) + (deps?.CheckpointsSaved ?? 0),8}";
    }

    private static string FormatMean(double? value, string unit = "")
    {
        if (!value.HasValue) return "       —";
        var suffix = unit.Length > 0 ? $" {unit}" : "";
        return $"{value.Value,8:F1}{suffix}";
    }
}

/// <summary>
/// Computed (inferred) metrics derived from snapshot deltas and wall-clock time.
/// </summary>
public sealed record DiscoveryComputedMetrics
{
    public double? WorkItemsPerHour { get; init; }
    public double? RevisionsPerHour { get; init; }
    public double? LinksPerHour { get; init; }
    public double? AnalysedPerHour { get; init; }
    public double? ProjectsPerHour { get; init; }
    public System.TimeSpan Elapsed { get; init; }
    public System.TimeSpan? EstimatedRemaining { get; init; }
}
