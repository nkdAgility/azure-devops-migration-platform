// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Right panel of the TUI: streams live log records for the currently selected job.
/// Two modes toggled by <see cref="KeyCode.Tab"/>:
/// <list type="bullet">
///   <item>
///     <term>Progress</term>
///     <description>Streams <see cref="ProgressEvent"/> lines via <c>GET /jobs/{jobId}/progress?follow=true</c></description>
///   </item>
///   <item>
///     <term>Diagnostics</term>
///     <description>Streams <see cref="DiagnosticLogRecord"/> lines via <c>GET /jobs/{jobId}/diagnostics?follow=true</c></description>
///   </item>
/// </list>
/// All UI mutations must arrive via <see cref="Application.Invoke"/>.
/// </summary>
public sealed class TuiLogView : FrameView
{
    private enum FeedMode { Trace, Logs, MetricsFeed }

    /// <summary>Maximum number of lines kept in the ring buffer before old lines are evicted.</summary>
    private const int MaxLines = 10_000;

    private readonly IControlPlaneClient _client;
    private readonly IUiDispatcher _dispatcher;
    private readonly ListView _listView;
    private readonly ObservableCollection<string> _lines = [];
    private readonly object _linesLock = new();

    private FeedMode _mode = FeedMode.Trace;
    private CancellationTokenSource? _streamCts;
    private Guid? _currentJobId;

    /// <summary>
    /// When true the view auto-scrolls to the newest line on each append.
    /// Cleared when the user scrolls up; restored when they scroll back to the bottom.
    /// </summary>
    private bool _autoScroll = true;

    /// <summary>Minimum log level displayed in Diagnostics mode (default: Information).</summary>
    public string MinLevel { get; set; } = "Information";

    /// <summary>
    /// Thread-safe snapshot of the current line buffer.
    /// Exposed for test assertions via <c>InternalsVisibleTo</c> — do not use in production code.
    /// </summary>
    internal IReadOnlyList<string> Lines
    {
        get { lock (_linesLock) { return [.. _lines]; } }
    }

    /// <summary>Fired when a terminal SSE event (<c>job-ended</c>/<c>job-failed</c>) arrives.</summary>
    public event Action<string>? OnJobEnded;

    /// <summary>Fired for each <see cref="ProgressEvent"/> received in Progress mode.</summary>
    public event Action<ProgressEvent>? OnProgressReceived;
    public event Action<string>? OnModeChanged;

    public TuiLogView(IControlPlaneClient client, IUiDispatcher? dispatcher = null)
    {
        _client = client;
        _dispatcher = dispatcher ?? new TerminalGuiDispatcher();
        Title = "Feed [Trace] (End=follow)";
        CanFocus = true;

        _listView = new ListView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            CanFocus = true
        };
        _listView.SetSource(_lines);

        // Detect manual scrolling — pause auto-scroll when user moves up,
        // resume when they reach the bottom.
        _listView.SelectedItemChanged += OnSelectedItemChanged;

        Add(_listView);

        KeyDown += OnKeyDown;
    }

    // ─── Public API ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Called by <see cref="TuiMainView"/> when the operator selects a job.
    /// Cancels any running stream, clears the view, and starts a fresh stream
    /// for <paramref name="jobId"/> in the current mode.
    /// </summary>
    public void ClearAndBind(Guid jobId, CancellationToken ct)
    {
        CancelCurrentStream();
        ClearLines();
        _currentJobId = jobId;
        _autoScroll = true;

        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task.Run(() => RunStreamLoopAsync(jobId, _streamCts.Token), _streamCts.Token);
    }

    /// <summary>Clears the view and cancels any running stream (called on deselect).</summary>
    public void Clear()
    {
        CancelCurrentStream();
        ClearLines();
        _currentJobId = null;
        _autoScroll = true;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────────

    private void OnSelectedItemChanged(object? sender, ListViewItemEventArgs e)
    {
        if (_lines.Count == 0) return;

        // If user scrolled to the last item, re-enable auto-scroll
        _autoScroll = e.Item >= _lines.Count - 1;

        _dispatcher.Invoke(() =>
        {
            Title = BuildTitle();
            SetNeedsDraw();
        });
    }

    private void OnKeyDown(object? sender, Key e)
    {
        // End key = jump to bottom and resume auto-scroll
        if (e.KeyCode == KeyCode.End && _lines.Count > 0)
        {
            _autoScroll = true;
            _listView.SelectedItem = _lines.Count - 1;
            SetNeedsDraw();
            e.Handled = true;
            return;
        }

        if (e.KeyCode != KeyCode.Tab)
            return;

        _mode = _mode switch
        {
            FeedMode.Trace => FeedMode.Logs,
            FeedMode.Logs => FeedMode.MetricsFeed,
            _ => FeedMode.Trace
        };
        _autoScroll = true;
        _dispatcher.Invoke(() =>
        {
            Title = BuildTitle();
            SetNeedsDraw();
        });
        OnModeChanged?.Invoke(GetModeLabel());

        if (_currentJobId.HasValue)
        {
            var jobId = _currentJobId.Value;
            // Cancel the old stream and start a new one in the new mode.
            CancelCurrentStream();
            ClearLines();

            _streamCts = new CancellationTokenSource();
            Task.Run(() => RunStreamLoopAsync(jobId, _streamCts.Token), _streamCts.Token);
        }

        e.Handled = true;
    }

    private async Task RunStreamLoopAsync(Guid jobId, CancellationToken ct)
    {
        int backoffMs = 1_000;
        const int maxBackoffMs = 30_000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                if (_mode == FeedMode.Trace)
                    await StreamTraceAsync(jobId, ct).ConfigureAwait(false);
                else if (_mode == FeedMode.MetricsFeed)
                    await StreamMetricsFeedAsync(jobId, ct).ConfigureAwait(false);
                else
                    await StreamLogsAsync(jobId, ct).ConfigureAwait(false);

                // Stream ended cleanly (job-ended) — break
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (InvalidOperationException ioe) when (ioe.Message.Contains("Job failed"))
            {
                _dispatcher.Invoke(() => AppendLine("\u2500\u2500 Job Failed \u2500\u2500"));
                OnJobEnded?.Invoke("Failed");
                return;
            }
            catch
            {
                // Transient network/server error — back off and reconnect
                await Task.Delay(backoffMs, ct).ConfigureAwait(false);
                backoffMs = Math.Min(backoffMs * 2, maxBackoffMs);
            }
        }
    }

    private async Task StreamTraceAsync(Guid jobId, CancellationToken ct)
    {
        bool ended = false;
        await foreach (var evt in _client.FollowLogsAsync(jobId, ct).ConfigureAwait(false))
        {
            var time = evt.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            var line = $"{time} [{evt.Module}] [{evt.Stage}] {evt.Message}";
            _dispatcher.Invoke(() => AppendLine(line));
            OnProgressReceived?.Invoke(evt);
            ended = true; // at least one event received
        }

        if (!ended) return;

        _dispatcher.Invoke(() => AppendLine("\u2500\u2500 Job Completed \u2500\u2500"));
        OnJobEnded?.Invoke("Completed");
    }

    private async Task StreamMetricsFeedAsync(Guid jobId, CancellationToken ct)
    {
        bool ended = false;
        await foreach (var evt in _client.FollowLogsAsync(jobId, ct).ConfigureAwait(false))
        {
            OnProgressReceived?.Invoke(evt);

            if (evt.Metrics is null)
                continue;

            var line = FormatMetricsFeedLine(evt);
            _dispatcher.Invoke(() => AppendLine(line));
            ended = true;
        }

        if (!ended) return;

        _dispatcher.Invoke(() => AppendLine("\u2500\u2500 Job Completed \u2500\u2500"));
        OnJobEnded?.Invoke("Completed");
    }

    private async Task StreamLogsAsync(Guid jobId, CancellationToken ct)
    {
        bool ended = false;
        await foreach (var rec in _client.StreamDiagnosticsAsync(jobId, MinLevel, ct).ConfigureAwait(false))
        {
            var time = rec.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            var line = $"{time} {rec.Level,-12} {rec.Message}";
            _dispatcher.Invoke(() => AppendLine(line));
            ended = true;
        }

        if (!ended) return;

        _dispatcher.Invoke(() => AppendLine("\u2500\u2500 Job Completed \u2500\u2500"));
        OnJobEnded?.Invoke("Completed");
    }

    private string BuildTitle()
    {
        var suffix = _autoScroll ? "(following)" : "(paused — End=follow)";
        return $"Feed [{GetModeLabel()}] {suffix}";
    }

    private string GetModeLabel() => _mode switch
    {
        FeedMode.Trace => "Trace",
        FeedMode.Logs => "Logs",
        FeedMode.MetricsFeed => "Metrics-Feed",
        _ => "Trace"
    };

    private static string FormatMetricsFeedLine(ProgressEvent evt)
    {
        var time = evt.Timestamp.ToLocalTime().ToString("HH:mm:ss");
        var scope = evt.Metrics?.Scope;
        var migrationWi = evt.Metrics?.Migration?.WorkItems;
        var dependency = evt.Metrics?.Discovery?.Dependencies;
        var parts = new List<string>
        {
            time,
            evt.Module,
            evt.Stage
        };

        if (migrationWi is not null)
        {
            parts.Add($"attempted={migrationWi.Attempted:N0}");
            parts.Add($"completed={migrationWi.Completed:N0}");
            parts.Add($"revisions={migrationWi.RevisionsProcessed:N0}");
        }

        if (scope is not null)
        {
            parts.Add($"projects={scope.ProjectsCompleted:N0}/{scope.ProjectsTotal:N0}");
        }

        if (dependency is not null)
        {
            parts.Add($"analysed={dependency.WorkItemsAnalysed:N0}");
            parts.Add($"links={dependency.ExternalLinksFound:N0}");
        }

        return string.Join(" | ", parts.Where(p => !string.IsNullOrWhiteSpace(p)));
    }

    private void AppendLine(string line)
    {
        lock (_linesLock)
        {
            _lines.Add(line);

            // Evict oldest lines when the buffer exceeds capacity
            while (_lines.Count > MaxLines)
                _lines.RemoveAt(0);
        }

        // Re-assign source so Terminal.Gui recalculates MaxLength for the new content.
        // These calls are no-ops (or safely ignored) when there is no active Application loop.
        try
        {
            _listView.SetSource(_lines);

            if (_autoScroll)
                _listView.SelectedItem = _lines.Count - 1;

            SetNeedsDraw();
        }
        catch (InvalidOperationException) { /* No Application loop — suppress UI-only errors */ }
        catch (NullReferenceException) { /* Terminal.Gui not initialised — suppress */ }
    }

    private void ClearLines()
    {
        _dispatcher.Invoke(() =>
        {
            lock (_linesLock)
            {
                _lines.Clear();
            }

            try
            {
                _listView.SetSource(_lines);
                SetNeedsDraw();
            }
            catch (InvalidOperationException) { /* No Application loop — suppress */ }
            catch (NullReferenceException) { /* Terminal.Gui not initialised — suppress */ }
        });
    }

    private void CancelCurrentStream()
    {
        var cts = _streamCts;
        _streamCts = null;
        try { cts?.Cancel(); } catch { /* ignore */ }
        cts?.Dispose();
    }

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _listView.SelectedItemChanged -= OnSelectedItemChanged;
            CancelCurrentStream();
        }

        base.Dispose(disposing);
    }
}
