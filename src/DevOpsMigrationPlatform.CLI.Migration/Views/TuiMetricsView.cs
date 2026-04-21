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
    /// Shows discovery-specific metrics (organisations, projects, work items counted).
    /// Safe to call from any thread.
    /// </summary>
    public void UpdateDiscovery(
        int projectsCompleted,
        int projectsFailed,
        int projectsInProgress,
        long workItems,
        long revisions,
        long repos)
    {
        Application.Invoke(() =>
        {
            _content.Text =
                $"Projects In Progress : {projectsInProgress,8}\n" +
                $"Projects Completed   : {projectsCompleted,8}\n" +
                $"Projects Failed      : {projectsFailed,8}\n" +
                $"\n" +
                $"Work Items Counted   : {workItems,8:N0}\n" +
                $"Revisions Counted    : {revisions,8:N0}\n" +
                $"Repos Counted        : {repos,8:N0}";
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

    private static string FormatMean(double? value, string unit = "")
    {
        if (!value.HasValue) return "       \u2014";
        var suffix = unit.Length > 0 ? $" {unit}" : "";
        return $"{value.Value,8:F1}{suffix}";
    }
}
