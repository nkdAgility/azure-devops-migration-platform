using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Right-hand panel showing derived / computed metrics: throughput rates,
/// elapsed time, average project duration, and ETA.
/// Raw counts live in <see cref="TuiMetricsView"/>; this panel shows
/// values inferred from those counts and wall-clock time.
/// </summary>
public sealed class TuiDerivedView : FrameView
{
    private readonly Label _content;

    public TuiDerivedView()
    {
        Title = "Derived";
        CanFocus = false;

        _content = new Label
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(),
            Text = "(waiting…)"
        };

        Add(_content);
    }

    /// <summary>
    /// Updates the derived metrics display. Safe to call from any thread.
    /// </summary>
    public void UpdateDiscovery(DiscoveryComputedMetrics c, double? avgProjectDurationMs)
    {
        Application.Invoke(() =>
        {
            _content.Text = FormatDerived(c, avgProjectDurationMs);
            SetNeedsDraw();
        });
    }

    /// <summary>Shows placeholder when no derived data is available.</summary>
    public void SetWaiting()
    {
        Application.Invoke(() =>
        {
            _content.Text = "(waiting…)";
            SetNeedsDraw();
        });
    }

    private static string FormatDerived(DiscoveryComputedMetrics c, double? avgProjectDurationMs)
    {
        var text =
            $"── Throughput ────────────────────\n" +
            $"Work Items / hour    : {FormatRate(c.WorkItemsPerHour)}\n" +
            $"Revisions / hour     : {FormatRate(c.RevisionsPerHour)}\n" +
            $"Analysed / hour      : {FormatRate(c.AnalysedPerHour)}\n" +
            $"Projects / hour      : {FormatRate(c.ProjectsPerHour)}\n" +
            $"Avg Project Duration : {FormatDuration(avgProjectDurationMs)}\n" +
            $"\n" +
            $"── Timing ────────────────────────\n" +
            $"Elapsed              : {FormatTimeSpan(c.Elapsed)}";

        if (c.EstimatedRemaining.HasValue)
            text += $"\nETA (remaining)      : {FormatTimeSpan(c.EstimatedRemaining.Value)}";

        return text;
    }

    private static string FormatRate(double? rate)
    {
        if (!rate.HasValue || rate.Value < 0.01) return "       —";
        return $"{rate.Value,8:N0}";
    }

    private static string FormatDuration(double? ms)
    {
        if (!ms.HasValue) return "       —";
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
