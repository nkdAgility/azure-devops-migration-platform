using System;
using DevOpsMigrationPlatform.Abstractions;
using Spectre.Console;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Renders a live metrics panel for a running job using Spectre.Console.
/// Call <see cref="Render"/> to print the latest values (or a waiting message).
/// </summary>
public sealed class TelemetryPanel
{
    private MetricSnapshot? _snapshot;

    /// <summary>Updates the stored snapshot. Thread-safe (called from the polling thread).</summary>
    public void Update(MetricSnapshot? snapshot) => _snapshot = snapshot;

    /// <summary>Writes the panel to the given console.</summary>
    public void Render(IAnsiConsole console)
    {
        var snap = _snapshot;

        var panel = new Panel(BuildContent(snap))
        {
            Header  = new PanelHeader($"Metrics (as of {DateTimeOffset.UtcNow:HH:mm:ss})"),
            Border  = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        };

        console.Write(panel);
    }

    private static string BuildContent(MetricSnapshot? snap)
    {
        if (snap is null)
            return "[grey](waiting for agent…)[/]";

        return string.Join("\n", new[]
        {
            $"Work Items Exported    : [green]{snap.WorkItemsExported,10:N0}[/]     Revision Errors          : [red]{snap.RevisionErrors,6:N0}[/]",
            $"Revisions Exported     : [green]{snap.RevisionsExported,10:N0}[/]     Link Errors              : [red]{snap.LinkErrors,6:N0}[/]",
            $"Links Exported         : [green]{snap.LinksExported,10:N0}[/]     Attachments Failed       : [red]{snap.AttachmentsFailed,6:N0}[/]",
            $"Attachments Attempted  : [green]{snap.AttachmentsAttempted,10:N0}[/]     Avg Work Item Duration   : [yellow]{FormatMs(snap.WorkItemDurationMeanMs),8}[/]",
            $"Attachments Succeeded  : [green]{snap.AttachmentsSucceeded,10:N0}[/]     Avg Revision Duration    : [yellow]{FormatMs(snap.RevisionDurationMeanMs),8}[/]",
        });
    }

    private static string FormatMs(double? ms) =>
        ms.HasValue ? $"{ms.Value:F0} ms" : "--";
}
