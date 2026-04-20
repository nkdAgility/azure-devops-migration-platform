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
            Header = new PanelHeader($"Metrics (as of {DateTimeOffset.UtcNow:HH:mm:ss})"),
            Border = BoxBorder.Rounded,
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
            $"Work Items Attempted   : [green]{snap.WorkItemsAttempted,10:N0}[/]     Work Items Failed        : [red]{snap.WorkItemsFailed,6:N0}[/]",
            $"Work Items Completed   : [green]{snap.WorkItemsCompleted,10:N0}[/]     Missing Work Items       : [red]{snap.MissingWorkItems,6:N0}[/]",
            $"Work Items Retried     : [yellow]{snap.WorkItemsRetried,10:N0}[/]     Broken Links             : [red]{snap.BrokenLinks,6:N0}[/]",
            $"In-Flight              : [blue]{snap.WorkItemsInFlight,10:N0}[/]     Revisions Missing        : [red]{snap.RevisionsMissing,6:N0}[/]",
            $"Queue Depth            : [blue]{snap.QueueDepth,10:N0}[/]     Rev Order Errors         : [red]{snap.RevisionOrderErrors,6:N0}[/]",
            $"",
            $"Avg Duration           : [yellow]{FormatMs(snap.WorkItemDurationMeanMs),8}[/]     Avg Revisions            : [yellow]{FormatMean(snap.RevisionCountMean),8}[/]",
            $"Avg Fields             : [yellow]{FormatMean(snap.FieldCountMean),8}[/]     Avg Attachments          : [yellow]{FormatMean(snap.AttachmentCountMean),8}[/]",
            $"Avg Links              : [yellow]{FormatMean(snap.LinkCountMean),8}[/]     Avg Payload              : [yellow]{FormatBytes(snap.PayloadBytesMean),8}[/]",
        });
    }

    private static string FormatMs(double? ms) =>
        ms.HasValue ? $"{ms.Value:F0} ms" : "--";

    private static string FormatMean(double? value) =>
        value.HasValue ? $"{value.Value:F1}" : "--";

    private static string FormatBytes(double? bytes) =>
        bytes.HasValue ? $"{bytes.Value:F0} B" : "--";
}
