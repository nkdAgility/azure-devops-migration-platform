using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// The root <see cref="Window"/> that hosts the three TUI panels side-by-side:
/// <list type="bullet">
///   <item><see cref="TuiJobListView"/> — left ~30%</item>
///   <item><see cref="TuiMetricsView"/> — centre ~35%</item>
///   <item><see cref="TuiLogView"/> — right fill</item>
/// </list>
/// Orchestrates job selection, telemetry polling, and log streaming.
/// Q / Ctrl+Q exits the TUI by calling <see cref="Application.RequestStop()"/>.
/// </summary>
public sealed class TuiMainView : Window, IDisposable
{
    private readonly IControlPlaneClient _client;
    private readonly TuiJobListView _jobList;
    private readonly TuiMetricsView _metrics;
    private readonly TuiLogView _logView;
    private readonly Label _statusBar;

    private CancellationTokenSource? _selectionCts;
    private Guid? _selectedJobId;

    // Discovery metrics accumulation — keyed by "org|project"
    private readonly Dictionary<string, DiscoveryProjectMetrics> _discoveryProjects = new();
    private volatile bool _discoveryMetricsActive;

    public TuiMainView(IControlPlaneClient client, string controlPlaneUrl = "")
    {
        _client = client;
        Title = string.IsNullOrEmpty(controlPlaneUrl)
            ? "DevOps Migration Platform — Job Dashboard"
            : $"DevOps Migration Platform — {controlPlaneUrl}";
        CanFocus = true;

        // ── Panels ──────────────────────────────────────────────────────────────
        // Top row: Jobs (left 40%) | Metrics (right 60%)
        // Bottom row: Log/Trace spanning full width
        _jobList = new TuiJobListView(client)
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(40),
            Height = Dim.Percent(45)
        };

        _metrics = new TuiMetricsView
        {
            X = Pos.Right(_jobList),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Percent(45)
        };

        _logView = new TuiLogView(client)
        {
            X = 0,
            Y = Pos.Bottom(_jobList),
            Width = Dim.Fill(),
            Height = Dim.Fill(1)   // leave room for status bar
        };

        // ── Status bar ──────────────────────────────────────────────────────────
        _statusBar = new Label
        {
            X = 0,
            Y = Pos.AnchorEnd(1),
            Width = Dim.Fill(),
            Height = 1,
            Text = " — | Press Q to quit | Tab toggles Log mode"
        };

        Add(_jobList);
        Add(_metrics);
        Add(_logView);
        Add(_statusBar);

        // ── Event subscriptions ─────────────────────────────────────────────────
        _jobList.JobSelected += OnJobSelected;
        _logView.OnJobEnded += OnJobEnded;
        _logView.OnProgressReceived += OnProgressReceived;

        // ── Key handling ────────────────────────────────────────────────────────
        // Use Application.KeyDown so Q / Ctrl+Q / Ctrl+C always work,
        // even when a child TableView or ListView has focus and consumes
        // normal key events before they bubble to this Window.
        Application.KeyDown += OnApplicationKeyDown;
    }

    private void OnApplicationKeyDown(object? sender, Key e)
    {
        if (e.KeyCode == KeyCode.Q
            || e.KeyCode == (KeyCode.Q | KeyCode.CtrlMask)
            || e.KeyCode == (KeyCode.C | KeyCode.CtrlMask))
        {
            Application.RequestStop(this);
            e.Handled = true;
        }
        else if (e.KeyCode == KeyCode.Esc && _selectedJobId.HasValue)
        {
            DeselectJob();
            e.Handled = true;
        }
    }

    // ─── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Pre-selects a job (used when launched with <c>--job &lt;id&gt;</c>).
    /// Must be called before <c>Application.Run(mainView)</c>.
    /// </summary>
    public void PreSelectJob(Guid jobId)
    {
        // Trigger selection after the main loop initialises
        Application.Invoke(() => HandleJobSelected(jobId));
    }

    // ─── Selection handling ──────────────────────────────────────────────────────

    private void OnJobSelected(Guid? jobId)
    {
        if (jobId is null)
        {
            DeselectJob();
            return;
        }

        HandleJobSelected(jobId.Value);
    }

    private void HandleJobSelected(Guid jobId)
    {
        // Cancel the previous selection's streams
        CancelSelection();

        _selectedJobId = jobId;
        _selectionCts = new CancellationTokenSource();
        var ct = _selectionCts.Token;

        // Reset discovery metrics state for the new job
        _discoveryMetricsActive = false;
        _discoveryProjects.Clear();

        _metrics.SetWaiting();
        _logView.ClearAndBind(jobId, ct);

        // Start telemetry polling (T029)
        Task.Run(() => PollTelemetryAsync(jobId, ct), ct);

        UpdateStatusBar(jobId.ToString()[..8], "—", "[Progress]");
    }

    private void DeselectJob()
    {
        CancelSelection();
        _selectedJobId = null;
        _discoveryMetricsActive = false;
        _discoveryProjects.Clear();
        _metrics.Update(null);
        _logView.Clear();
        Application.Invoke(() => _statusBar.Text = " — | Press Q to quit | Tab toggles Log mode");
    }

    // ─── Telemetry polling ───────────────────────────────────────────────────────

    private async Task PollTelemetryAsync(Guid jobId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Discovery events have taken over the metrics panel — stop polling
            if (_discoveryMetricsActive)
                return;

            try
            {
                var snapshot = await _client.GetTelemetryAsync(jobId, ct).ConfigureAwait(false);
                if (!_discoveryMetricsActive)
                    _metrics.Update(snapshot);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Swallow transient errors — next poll will retry
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(5), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    // ─── Discovery metrics accumulation ──────────────────────────────────────────

    private void OnProgressReceived(ProgressEvent evt)
    {
        // Only accumulate discovery metrics for Inventory / Dependencies modules
        if (evt.Module != "Inventory" && evt.Module != "Dependencies")
            return;

        _discoveryMetricsActive = true;

        var key = evt.LastProcessed;
        if (!string.IsNullOrEmpty(key))
        {
            bool isComplete = evt.Stage == "Inventory" || evt.Stage == "Dependencies";
            bool isFailed = evt.Stage == "Failed";

            _discoveryProjects[key] = new DiscoveryProjectMetrics(
                evt.TotalWorkItems, evt.RevisionsProcessed, evt.AttachmentsProcessed,
                isComplete || isFailed, isFailed);
        }

        // Accumulate totals across all tracked projects
        int completed = 0, failed = 0, inProgress = 0;
        long totalWi = 0, totalRev = 0, totalRepos = 0;
        foreach (var entry in _discoveryProjects)
        {
            var v = entry.Value;
            if (v.IsFailed) failed++;
            else if (v.IsComplete) completed++;
            else inProgress++;
            totalWi += v.WorkItems;
            totalRev += v.Revisions;
            totalRepos += v.Repos;
        }

        _metrics.UpdateDiscovery(completed, failed, inProgress, totalWi, totalRev, totalRepos);
    }

    // ─── Job-ended callback ──────────────────────────────────────────────────────

    private void OnJobEnded(string terminalState)
    {
        var shortId = _selectedJobId?.ToString()[..8] ?? "—";
        var colour = terminalState == "Completed" ? "green" : "red";
        Application.Invoke(() =>
            _statusBar.Text = $" Job {shortId} → {terminalState} | Press Q to quit | Tab toggles Log mode");
    }

    // ─── Status bar helper ───────────────────────────────────────────────────────

    private void UpdateStatusBar(string shortId, string state, string logMode)
    {
        Application.Invoke(() =>
            _statusBar.Text = $" Job {shortId} | {state} | {logMode} | Press Q to quit | Tab toggles Log mode");
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────────────

    private void CancelSelection()
    {
        var cts = _selectionCts;
        _selectionCts = null;
        try { cts?.Cancel(); } catch { /* ignore */ }
        cts?.Dispose();
    }

    /// <inheritdoc/>
    public new void Dispose()
    {
        Application.KeyDown -= OnApplicationKeyDown;
        _logView.OnProgressReceived -= OnProgressReceived;
        CancelSelection();
        _logView.Dispose();
        base.Dispose();
    }

    // ─── Supporting types ────────────────────────────────────────────────────────

    private sealed record DiscoveryProjectMetrics(
        long WorkItems, long Revisions, long Repos,
        bool IsComplete, bool IsFailed);
}
