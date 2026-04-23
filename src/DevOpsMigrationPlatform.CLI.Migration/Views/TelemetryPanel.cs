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
    private JobMetrics? _metrics;

    /// <summary>Updates the stored metrics. Thread-safe (called from the polling thread).</summary>
    public void Update(JobMetrics? metrics) => _metrics = metrics;

    /// <summary>Writes the panel to the given console.</summary>
    public void Render(IAnsiConsole console)
    {
        var snap = _metrics;

        var panel = new Panel(BuildContent(snap))
        {
            Header = new PanelHeader($"Metrics (as of {DateTimeOffset.UtcNow:HH:mm:ss})"),
            Border = BoxBorder.Rounded,
            Padding = new Padding(1, 0)
        };

        console.Write(panel);
    }

    private static string BuildContent(JobMetrics? snap)
    {
        if (snap is null)
            return "[grey](waiting for agent…)[/]";

        var wi = snap.Migration?.WorkItems;
        var diag = snap.Migration?.Diagnostics;

        return string.Join("\n", new[]
        {
            $"Work Items Attempted   : [green]{wi?.Attempted ?? 0,10:N0}[/]     Work Items Failed        : [red]{wi?.Failed ?? 0,6:N0}[/]",
            $"Work Items Completed   : [green]{wi?.Completed ?? 0,10:N0}[/]     Missing Work Items       : [red]{diag?.MissingWorkItems ?? 0,6:N0}[/]",
            $"Work Items Skipped     : [yellow]{wi?.Skipped ?? 0,10:N0}[/]     Broken Links             : [red]{diag?.BrokenLinks ?? 0,6:N0}[/]",
            $"In-Flight              : [blue]{diag?.WorkItemsInFlight ?? 0,10:N0}[/]     Revisions Missing        : [red]{diag?.RevisionsMissing ?? 0,6:N0}[/]",
            $"Queue Depth            : [blue]{diag?.QueueDepth ?? 0,10:N0}[/]     Rev Order Errors         : [red]{diag?.RevisionOrderErrors ?? 0,6:N0}[/]",
            $"",
            $"Avg Duration           : [yellow]{FormatMs(diag?.WorkItemDurationMeanMs),8}[/]     Avg Revisions            : [yellow]{FormatMean(diag?.RevisionCountMean),8}[/]",
            $"Avg Fields             : [yellow]{FormatMean(diag?.FieldCountMean),8}[/]     Avg Attachments          : [yellow]{FormatMean(diag?.AttachmentCountMean),8}[/]",
            $"Avg Links              : [yellow]{FormatMean(diag?.LinkCountMean),8}[/]     Avg Payload              : [yellow]{FormatBytes(diag?.PayloadBytesMean),8}[/]",
        });
    }

    private static string FormatMs(double? ms) =>
        ms.HasValue ? $"{ms.Value:F0} ms" : "--";

    private static string FormatMean(double? value) =>
        value.HasValue ? $"{value.Value:F1}" : "--";

    private static string FormatBytes(double? bytes) =>
        bytes.HasValue ? $"{bytes.Value:F0} B" : "--";
}
