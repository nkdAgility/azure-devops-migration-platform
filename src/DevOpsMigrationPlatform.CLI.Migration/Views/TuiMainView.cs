// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.CLI.Migration;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// The root <see cref="Window"/> that hosts the Terminal.Gui 2 workspace shell.
/// Orchestrates job selection, bootstrap/telemetry refresh, and feed streaming.
/// Q / Ctrl+Q exits the TUI by calling <see cref="Application.RequestStop()"/>.
/// </summary>
public sealed class TuiMainView : Window, IDisposable
{
    private readonly IControlPlaneClient _client;
    private readonly ComboBox _jobSelector;
    private readonly ObservableCollection<string> _jobSelectorItems = [];
    private readonly TuiTaskProgressView _taskProgress;
    private readonly TuiMetricsView _metrics;
    private readonly TuiLogView _feedView;
    private readonly Label _statusBar;
    private readonly Timer _jobRefreshTimer;

    private IReadOnlyList<JobSummary> _jobs = [];
    private Guid? _requestedJobId;
    private ProgressEvent? _lastProgressEvent;
    private bool _suppressSelectorEvents;

    private CancellationTokenSource? _selectionCts;
    private Guid? _selectedJobId;

    public TuiMainView(IControlPlaneClient client, string controlPlaneUrl = "")
    {
        _client = client;
        Title = string.IsNullOrEmpty(controlPlaneUrl)
            ? "DevOps Migration Platform — Terminal UI"
            : $"DevOps Migration Platform — {controlPlaneUrl}";
        CanFocus = true;

        var header = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = 4,
            Text = BuildHeaderText()
        };

        var selectorLabel = new Label
        {
            X = 0,
            Y = Pos.Bottom(header),
            Width = 14,
            Height = 1,
            Text = "Selected Job:"
        };

        _jobSelector = new ComboBox
        {
            X = Pos.Right(selectorLabel) + 1,
            Y = Pos.Top(selectorLabel),
            Width = Dim.Fill(),
            Height = 1,
            ReadOnly = true,
            HideDropdownListOnClick = true
        };
        _jobSelector.SetSource(_jobSelectorItems);

        _taskProgress = new TuiTaskProgressView
        {
            X = 0,
            Y = Pos.Bottom(selectorLabel) + 1,
            Width = Dim.Percent(60),
            Height = Dim.Fill(9)
        };

        _metrics = new TuiMetricsView
        {
            X = Pos.Right(_taskProgress),
            Y = Pos.Top(_taskProgress),
            Width = Dim.Fill(),
            Height = Dim.Height(_taskProgress)
        };

        _feedView = new TuiLogView(client)
        {
            X = 0,
            Y = Pos.Bottom(_taskProgress),
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
            Text = " No job selected | Press Q to quit | Tab cycles Trace/Logs/Metrics-Feed | Esc clears selection"
        };

        Add(header);
        Add(selectorLabel);
        Add(_jobSelector);
        Add(_taskProgress);
        Add(_metrics);
        Add(_feedView);
        Add(_statusBar);

        // ── Event subscriptions ─────────────────────────────────────────────────
        _jobSelector.SelectedItemChanged += OnJobSelectorChanged;
        _feedView.OnJobEnded += OnJobEnded;
        _feedView.OnProgressReceived += OnProgressReceived;
        _feedView.OnModeChanged += OnFeedModeChanged;

        _jobRefreshTimer = new Timer(
            _ => _ = RefreshJobsAsync(),
            null,
            dueTime: 0,
            period: 10_000);

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
        _requestedJobId = jobId;
        Application.Invoke(() => _ = RefreshJobsAsync());
    }

    // ─── Selection handling ──────────────────────────────────────────────────────

    private void OnJobSelectorChanged(object? sender, EventArgs e)
    {
        if (_suppressSelectorEvents)
            return;

        var selectedIndex = _jobSelector.SelectedItem;
        if (selectedIndex < 0 || selectedIndex >= _jobs.Count)
        {
            DeselectJob();
            return;
        }

        HandleJobSelected(_jobs[selectedIndex].JobId);
    }

    private void HandleJobSelected(Guid jobId)
    {
        // Cancel the previous selection's streams
        CancelSelection();

        _selectedJobId = jobId;
        _selectionCts = new CancellationTokenSource();
        var ct = _selectionCts.Token;

        _metrics.SetWaiting();
        _taskProgress.SetWaiting(GetSelectedSummary(jobId)?.Mode);
        _feedView.ClearAndBind(jobId, ct);

        Task.Run(() => PollSelectedJobAsync(jobId, ct), ct);

        UpdateStatusBar(jobId.ToString()[..8], "selected", "Trace");
    }

    private void DeselectJob()
    {
        CancelSelection();
        _selectedJobId = null;
        _lastProgressEvent = null;
        _taskProgress.Clear();
        _feedView.Clear();
        Application.Invoke(() => _statusBar.Text = " No job selected | Press Q to quit | Tab cycles Trace/Logs/Metrics-Feed | Esc clears selection");
    }

    // ─── Polling ────────────────────────────────────────────────────────────────

    private async Task RefreshJobsAsync()
    {
        try
        {
            var jobs = await _client.GetAllJobsAsync(CancellationToken.None).ConfigureAwait(false);
            Application.Invoke(() => ApplyJobList(jobs));
        }
        catch
        {
            // Swallow transient errors — next refresh will retry.
        }
    }

    private async Task PollSelectedJobAsync(Guid jobId, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var bootstrapTask = _client.GetBootstrapAsync(jobId, ct);
                var telemetryTask = _client.GetTelemetryAsync(jobId, ct);
                await Task.WhenAll(bootstrapTask, telemetryTask).ConfigureAwait(false);

                var bootstrap = await bootstrapTask.ConfigureAwait(false);
                var telemetry = await telemetryTask.ConfigureAwait(false);
                var metrics = bootstrap?.Metrics ?? telemetry;

                _metrics.Update(metrics);
                _taskProgress.Update(GetSelectedSummary(jobId), bootstrap?.Tasks, metrics, _lastProgressEvent, bootstrap?.Snapshot);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Swallow transient errors — next poll will retry.
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

    private void ApplyJobList(IReadOnlyList<JobSummary> jobs)
    {
        _jobs = jobs;
        _suppressSelectorEvents = true;
        try
        {
            _jobSelectorItems.Clear();
            foreach (var job in jobs)
                _jobSelectorItems.Add(FormatJobSelectorItem(job));
            _jobSelector.SetSource(_jobSelectorItems);

            var preferredJobId = _selectedJobId;
            if (_requestedJobId.HasValue && jobs.Any(j => j.JobId == _requestedJobId.Value))
                preferredJobId = _requestedJobId.Value;
            else if (preferredJobId.HasValue && !jobs.Any(j => j.JobId == preferredJobId.Value))
                preferredJobId = null;
            else if (!preferredJobId.HasValue && jobs.Count == 1)
                preferredJobId = jobs[0].JobId;

            if (preferredJobId.HasValue)
            {
                var index = jobs.Select((job, idx) => (job, idx)).FirstOrDefault(pair => pair.job.JobId == preferredJobId.Value).idx;
                _jobSelector.SelectedItem = index;
            }
            else if (jobs.Count == 0)
            {
                _jobSelector.SelectedItem = -1;
            }
        }
        finally
        {
            _suppressSelectorEvents = false;
        }

        if (_requestedJobId.HasValue && _selectedJobId != _requestedJobId.Value && jobs.Any(j => j.JobId == _requestedJobId.Value))
            HandleJobSelected(_requestedJobId.Value);
        else if (!_selectedJobId.HasValue && jobs.Count == 1)
            HandleJobSelected(jobs[0].JobId);
        else if (_selectedJobId.HasValue && !jobs.Any(j => j.JobId == _selectedJobId.Value))
            DeselectJob();
    }

    // ─── Job-ended callback ──────────────────────────────────────────────────────

    private void OnProgressReceived(ProgressEvent evt)
    {
        _lastProgressEvent = evt;
    }

    private void OnFeedModeChanged(string mode)
    {
        var shortId = _selectedJobId?.ToString()[..8] ?? "-";
        UpdateStatusBar(shortId, "active", mode);
    }

    private void OnJobEnded(string terminalState)
    {
        var shortId = _selectedJobId?.ToString()[..8] ?? "—";
        Application.Invoke(() =>
            _statusBar.Text = $" Job {shortId} → {terminalState} | Press Q to quit | Tab cycles Trace/Logs/Metrics-Feed | Esc clears selection");
        _ = RefreshJobsAsync();
    }

    // ─── Status bar helper ───────────────────────────────────────────────────────

    private void UpdateStatusBar(string shortId, string state, string logMode)
    {
        Application.Invoke(() =>
            _statusBar.Text = $" Job {shortId} | {state} | Feed {logMode} | Press Q to quit | Tab cycles Trace/Logs/Metrics-Feed | Esc clears selection");
    }

    private JobSummary? GetSelectedSummary(Guid jobId) => _jobs.FirstOrDefault(j => j.JobId == jobId);

    private static string FormatJobSelectorItem(JobSummary job)
        => $"{job.Mode,-12} {job.State,-12} {job.JobId.ToString()[..8]}  {job.SubmittedAt.ToLocalTime():yyyy-MM-dd HH:mm}";

    private static string BuildHeaderText()
    {
        var version = typeof(TuiMainView).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        return string.Join(Environment.NewLine, new[]
        {
            "DevOps Migration",
            "-----------------",
            $"Azure DevOps Migration Platform  v{version}",
            "Created by Martin Hinshelwood"
        });
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
        _jobRefreshTimer.Dispose();
        _jobSelector.SelectedItemChanged -= OnJobSelectorChanged;
        _feedView.OnJobEnded -= OnJobEnded;
        _feedView.OnProgressReceived -= OnProgressReceived;
        _feedView.OnModeChanged -= OnFeedModeChanged;
        CancelSelection();
        _feedView.Dispose();
        base.Dispose();
    }
}
