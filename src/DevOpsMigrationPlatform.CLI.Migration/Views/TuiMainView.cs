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
    private readonly TuiDerivedView _derived;
    private readonly TuiLogView _logView;
    private readonly Label _statusBar;

    private CancellationTokenSource? _selectionCts;
    private Guid? _selectedJobId;

    // Discovery timing — set when the first non-null metrics arrive
    private DateTimeOffset? _discoveryStartTime;

    public TuiMainView(IControlPlaneClient client, string controlPlaneUrl = "")
    {
        _client = client;
        Title = string.IsNullOrEmpty(controlPlaneUrl)
            ? "DevOps Migration Platform — Job Dashboard"
            : $"DevOps Migration Platform — {controlPlaneUrl}";
        CanFocus = true;

        // ── Panels ──────────────────────────────────────────────────────────────
        // Top row: Jobs (left 30%) | Metrics (centre 35%) | Derived (right 35%)
        // Bottom row: Log/Trace spanning full width
        _jobList = new TuiJobListView(client)
        {
            X = 0,
            Y = 0,
            Width = Dim.Percent(30),
            Height = Dim.Percent(45)
        };

        _metrics = new TuiMetricsView
        {
            X = Pos.Right(_jobList),
            Y = 0,
            Width = Dim.Percent(35),
            Height = Dim.Percent(45)
        };

        _derived = new TuiDerivedView
        {
            X = Pos.Right(_metrics),
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
        Add(_derived);
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

        // Reset discovery timing for the new job
        _discoveryStartTime = null;

        _metrics.SetWaiting();
        _derived.SetWaiting();
        _logView.ClearAndBind(jobId, ct);

        // Bootstrap: fetch snapshot + metrics + lastEventSequence in one atomic call
        // so a late-joining TUI immediately has state. Fire-and-forget — polling picks
        // up if the bootstrap call fails.
        Task.Run(async () =>
        {
            try
            {
                var bootstrap = await _client.GetBootstrapAsync(jobId, ct).ConfigureAwait(false);
                if (bootstrap is not null)
                {
                    _metrics.Update(bootstrap.Metrics);
                    if (bootstrap.Metrics?.Discovery is not null)
                        UpdateDerivedFromMetrics(bootstrap.Metrics);
                }
            }
            catch
            {
                // Swallow — polling will catch up
            }
        }, ct);

        // Start telemetry polling — Channel 2 metrics drive both metrics and derived panels
        Task.Run(() => PollTelemetryAsync(jobId, ct), ct);

        UpdateStatusBar(jobId.ToString()[..8], "—", "[Progress]");
    }

    private void DeselectJob()
    {
        CancelSelection();
        _selectedJobId = null;
        _discoveryStartTime = null;
        _derived.SetWaiting();
        _logView.Clear();
        Application.Invoke(() => _statusBar.Text = " — | Press Q to quit | Tab toggles Log mode");
    }

    // ─── Telemetry polling (Channel 2) ─────────────────────────────────────────

    private async Task PollTelemetryAsync(Guid jobId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var metrics = await _client.GetTelemetryAsync(jobId, ct).ConfigureAwait(false);
                _metrics.Update(metrics);

                // Compute derived metrics (throughput, ETA) for discovery jobs
                if (metrics?.Discovery is not null)
                    UpdateDerivedFromMetrics(metrics);
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

    /// <summary>
    /// Computes throughput rates and ETA from Channel 2 <see cref="JobMetrics"/>
    /// using wall-clock elapsed time. Replaces the previous Channel 1 accumulation.
    /// </summary>
    private void UpdateDerivedFromMetrics(JobMetrics metrics)
    {
        // Start the clock on first non-null metrics
        _discoveryStartTime ??= DateTimeOffset.UtcNow;

        var scope = metrics.Scope;
        var disc = metrics.Discovery;
        var deps = disc?.Dependencies;
        var inv = disc?.Inventory;

        var elapsed = DateTimeOffset.UtcNow - _discoveryStartTime.Value;
        var elapsedHours = elapsed.TotalHours;

        var totalWi = scope?.WorkItemsTotal ?? 0;
        var totalRev = inv?.RevisionsTotal ?? 0;
        var totalLinks = deps?.ExternalLinksFound ?? 0;
        var totalAnalysed = deps?.WorkItemsAnalysed ?? 0;
        var depCompleted = (int)(scope?.ProjectsCompleted ?? 0);
        var depFailed = (int)(scope?.ProjectsFailed ?? 0);
        var depTotal = (int)(scope?.ProjectsTotal ?? 0);
        var remaining = depTotal - depCompleted - depFailed;

        double? avgProjectDurationMs = depCompleted > 0
            ? elapsed.TotalMilliseconds / depCompleted
            : null;

        // ETA from per-item rate when dependency pre-count totals are available;
        // otherwise fall back to avg-project-duration × remaining projects
        TimeSpan? estimatedRemaining = null;
        if (totalAnalysed > 0 && totalWi > 0 && totalAnalysed < totalWi)
        {
            var msPerItem = elapsed.TotalMilliseconds / totalAnalysed;
            estimatedRemaining = TimeSpan.FromMilliseconds(msPerItem * (totalWi - totalAnalysed));
        }
        else if (depCompleted > 0 && remaining > 0)
        {
            estimatedRemaining = TimeSpan.FromMilliseconds(elapsed.TotalMilliseconds / depCompleted * remaining);
        }

        var computed = new DiscoveryComputedMetrics
        {
            WorkItemsPerHour = elapsedHours > 0.001 ? totalWi / elapsedHours : null,
            RevisionsPerHour = elapsedHours > 0.001 ? totalRev / elapsedHours : null,
            LinksPerHour = elapsedHours > 0.001 && totalLinks > 0 ? totalLinks / elapsedHours : null,
            AnalysedPerHour = elapsedHours > 0.001 && totalAnalysed > 0 ? totalAnalysed / elapsedHours : null,
            ProjectsPerHour = elapsedHours > 0.001 && depCompleted > 0 ? depCompleted / elapsedHours : null,
            Elapsed = elapsed,
            EstimatedRemaining = estimatedRemaining
        };

        _derived.UpdateDiscovery(computed, avgProjectDurationMs);
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
