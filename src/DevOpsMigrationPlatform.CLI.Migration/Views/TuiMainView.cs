using System;
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

    public TuiMainView(IControlPlaneClient client)
    {
        _client = client;
        Title = "DevOps Migration Platform — Job Dashboard";
        CanFocus = true;

        // ── Panels ──────────────────────────────────────────────────────────────
        _jobList = new TuiJobListView(client)
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(30),
            Height = Dim.Fill(1)   // leave room for status bar
        };

        _metrics = new TuiMetricsView
        {
            X = Pos.Right(_jobList),
            Y = 0,
            Width = Dim.Percent(35),
            Height = Dim.Fill(1)
        };

        _logView = new TuiLogView(client)
        {
            X = Pos.Right(_metrics),
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(1)
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
        _metrics.Update(null);
        _logView.Clear();
        Application.Invoke(() => _statusBar.Text = " — | Press Q to quit | Tab toggles Log mode");
    }

    // ─── Telemetry polling ───────────────────────────────────────────────────────

    private async Task PollTelemetryAsync(Guid jobId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var snapshot = await _client.GetTelemetryAsync(jobId, ct).ConfigureAwait(false);
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
        CancelSelection();
        _logView.Dispose();
        base.Dispose();
    }
}
