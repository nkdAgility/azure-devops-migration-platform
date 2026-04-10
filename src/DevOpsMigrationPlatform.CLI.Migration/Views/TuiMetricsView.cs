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
        $"Work Items Exported  : {s.WorkItemsExported,8}\n" +
        $"Revisions Exported   : {s.RevisionsExported,8}\n" +
        $"Revision Errors      : {s.RevisionErrors,8}\n" +
        $"Links Exported       : {s.LinksExported,8}\n" +
        $"Link Errors          : {s.LinkErrors,8}\n" +
        $"Attachments Attempted: {s.AttachmentsAttempted,8}\n" +
        $"Attachments Succeeded: {s.AttachmentsSucceeded,8}\n" +
        $"Attachments Failed   : {s.AttachmentsFailed,8}\n" +
        $"WI Duration Mean     : {s.WorkItemDurationMeanMs?.ToString("F1") ?? "—",7} ms\n" +
        $"Rev Duration Mean    : {s.RevisionDurationMeanMs?.ToString("F1") ?? "—",7} ms\n" +
        $"Total Export Duration: {s.TotalExportDurationMs?.ToString("F0") ?? "—",7} ms";
}
