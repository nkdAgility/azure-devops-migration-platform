using DevOpsMigrationPlatform.Abstractions;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Centre panel: displays the latest <see cref="MetricSnapshot"/> polled from
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
    /// Replaces the displayed snapshot. Pass <c>null</c> to show the placeholder.
    /// Safe to call from any thread — marshals via <see cref="Application.Invoke"/>.
    /// </summary>
    public void Update(MetricSnapshot? snapshot)
    {
        Application.Invoke(() =>
        {
            _content.Text = snapshot is null
                ? "(no job selected)"
                : FormatSnapshot(snapshot);
            SetNeedsDraw();
        });
    }

    /// <summary>
    /// Shows discovery-specific metrics including the full <see cref="DiscoveryMetricSnapshot"/>
    /// and computed throughput rates. Safe to call from any thread.
    /// </summary>
    public void UpdateDiscovery(DiscoveryMetricSnapshot snapshot, DiscoveryComputedMetrics computed)
    {
        Application.Invoke(() =>
        {
            _content.Text = FormatDiscoverySnapshot(snapshot, computed);
            SetNeedsDraw();
        });
    }

    /// <summary>Shows "(waiting for agent…)" when a job is selected but no snapshot has arrived yet.</summary>
    public void SetWaiting()
    {
        Application.Invoke(() =>
        {
            _content.Text = "(waiting for agent\u2026)";
            SetNeedsDraw();
        });
    }

    private static string FormatSnapshot(MetricSnapshot s) =>
        $"Work Items Attempted : {s.WorkItemsAttempted,8}\n" +
        $"Work Items Completed : {s.WorkItemsCompleted,8}\n" +
        $"Work Items Failed    : {s.WorkItemsFailed,8}\n" +
        $"Work Items Retried   : {s.WorkItemsRetried,8}\n" +
        $"In-Flight            : {s.WorkItemsInFlight,8}\n" +
        $"Queue Depth          : {s.QueueDepth,8}\n" +
        $"\n" +
        $"Avg Duration         : {FormatMean(s.WorkItemDurationMeanMs, "ms")}\n" +
        $"Avg Revisions        : {FormatMean(s.RevisionCountMean)}\n" +
        $"Avg Attachments      : {FormatMean(s.AttachmentCountMean)}\n" +
        $"Avg Links            : {FormatMean(s.LinkCountMean)}\n" +
        $"Avg Fields           : {FormatMean(s.FieldCountMean)}\n" +
        $"Avg Payload          : {FormatMean(s.PayloadBytesMean, "B")}\n" +
        $"\n" +
        $"Broken Links         : {s.BrokenLinks,8}\n" +
        $"Missing Work Items   : {s.MissingWorkItems,8}\n" +
        $"Revisions Missing    : {s.RevisionsMissing,8}\n" +
        $"Rev Order Errors     : {s.RevisionOrderErrors,8}";

    private static string FormatDiscoverySnapshot(DiscoveryMetricSnapshot s, DiscoveryComputedMetrics c)
    {
        var text =
            $"── Progress ──────────────────────\n" +
            $"Orgs Completed       : {s.OrganisationsCompleted,8}\n" +
            $"Orgs Failed          : {s.OrganisationsFailed,8}\n" +
            $"Orgs Queued          : {s.OrganisationsQueued,8}\n" +
            $"\n" +
            $"Projects In Progress : {s.ProjectsQueued,8}\n" +
            $"Projects Completed   : {s.ProjectsCompleted,8}\n" +
            $"Projects Failed      : {s.ProjectsFailed,8}\n" +
            $"\n" +
            $"── Inventory ─────────────────────\n" +
            $"Work Items Counted   : {s.WorkItemsCounted,8:N0}\n" +
            $"Revisions Counted    : {s.RevisionsCounted,8:N0}\n" +
            $"Repos Counted        : {s.ReposCounted,8:N0}\n" +
            $"\n" +
            $"── Dependencies ──────────────────\n" +
            $"Links Found          : {s.LinksFound,8:N0}\n" +
            $"Work Items Analysed  : {s.WorkItemsAnalysed,8:N0}\n" +
            $"\n" +
            $"── Throughput ────────────────────\n" +
            $"Work Items / hour    : {FormatRate(c.WorkItemsPerHour)}\n" +
            $"Revisions / hour     : {FormatRate(c.RevisionsPerHour)}\n" +
            $"Analysed / hour      : {FormatRate(c.AnalysedPerHour)}\n" +
            $"Links / hour         : {FormatRate(c.LinksPerHour)}\n" +
            $"Projects / hour      : {FormatRate(c.ProjectsPerHour)}\n" +
            $"Avg Project Duration : {FormatDuration(s.ProjectDurationMeanMs)}\n" +
            $"\n" +
            $"── Operational ───────────────────\n" +
            $"Checkpoints Saved    : {s.CheckpointsSaved,8}\n" +
            $"Elapsed              : {FormatTimeSpan(c.Elapsed)}";

        if (c.EstimatedRemaining.HasValue)
            text += $"\nETA                  : {FormatTimeSpan(c.EstimatedRemaining.Value)}";

        return text;
    }

    private static string FormatMean(double? value, string unit = "")
    {
        if (!value.HasValue) return "       \u2014";
        var suffix = unit.Length > 0 ? $" {unit}" : "";
        return $"{value.Value,8:F1}{suffix}";
    }

    private static string FormatRate(double? rate)
    {
        if (!rate.HasValue || rate.Value < 0.01) return "       \u2014";
        return $"{rate.Value,8:N0}";
    }

    private static string FormatDuration(double? ms)
    {
        if (!ms.HasValue) return "       \u2014";
        var ts = System.TimeSpan.FromMilliseconds(ms.Value);
        return FormatTimeSpan(ts);
    }

    private static string FormatTimeSpan(System.TimeSpan ts)
    {
        if (ts.TotalHours >= 1)
            return $"{(int)ts.TotalHours}h {ts.Minutes:D2}m {ts.Seconds:D2}s";
        if (ts.TotalMinutes >= 1)
            return $"   {(int)ts.TotalMinutes}m {ts.Seconds:D2}s";
        return $"      {ts.Seconds}s";
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
