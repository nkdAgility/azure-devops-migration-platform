using System;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
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
    private enum LogMode { Progress, Diagnostics }

    private readonly IControlPlaneClient _client;
    private readonly ListView _listView;
    private readonly ObservableCollection<string> _lines = [];

    private LogMode _mode = LogMode.Progress;
    private CancellationTokenSource? _streamCts;
    private Guid? _currentJobId;

    /// <summary>Minimum log level displayed in Diagnostics mode (default: Information).</summary>
    public string MinLevel { get; set; } = "Information";

    /// <summary>Fired when a terminal SSE event (<c>job-ended</c>/<c>job-failed</c>) arrives.</summary>
    public event Action<string>? OnJobEnded;

    /// <summary>Fired for each <see cref="ProgressEvent"/> received in Progress mode.</summary>
    public event Action<ProgressEvent>? OnProgressReceived;

    public TuiLogView(IControlPlaneClient client)
    {
        _client = client;
        Title = "Log [Progress]";
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

        _streamCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task.Run(() => RunStreamLoopAsync(jobId, _streamCts.Token), _streamCts.Token);
    }

    /// <summary>Clears the view and cancels any running stream (called on deselect).</summary>
    public void Clear()
    {
        CancelCurrentStream();
        ClearLines();
        _currentJobId = null;
    }

    // ─── Private helpers ─────────────────────────────────────────────────────────

    private void OnKeyDown(object? sender, Key e)
    {
        if (e.KeyCode != KeyCode.Tab)
            return;

        _mode = _mode == LogMode.Progress ? LogMode.Diagnostics : LogMode.Progress;
        Application.Invoke(() =>
        {
            Title = _mode == LogMode.Progress ? "Log [Progress]" : "Log [Diagnostics]";
            SetNeedsDraw();
        });

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
                if (_mode == LogMode.Progress)
                    await StreamProgressAsync(jobId, ct).ConfigureAwait(false);
                else
                    await StreamDiagnosticsAsync(jobId, ct).ConfigureAwait(false);

                // Stream ended cleanly (job-ended) — break
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (InvalidOperationException ioe) when (ioe.Message.Contains("Job failed"))
            {
                Application.Invoke(() => AppendLine("\u2500\u2500 Job Failed \u2500\u2500"));
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

    private async Task StreamProgressAsync(Guid jobId, CancellationToken ct)
    {
        bool ended = false;
        await foreach (var evt in _client.FollowLogsAsync(jobId, ct).ConfigureAwait(false))
        {
            var time = evt.Timestamp.ToLocalTime().ToString("HH:mm:ss");
            var line = $"{time} [{evt.Module}] [{evt.Stage}] {evt.Message}";
            Application.Invoke(() => AppendLine(line));
            OnProgressReceived?.Invoke(evt);
            ended = true; // at least one event received
        }

        if (!ended) return;

        Application.Invoke(() => AppendLine("\u2500\u2500 Job Completed \u2500\u2500"));
        OnJobEnded?.Invoke("Completed");
    }

    private async Task StreamDiagnosticsAsync(Guid jobId, CancellationToken ct)
    {
        bool ended = false;
        await foreach (var rec in _client.StreamDiagnosticsAsync(jobId, MinLevel, ct).ConfigureAwait(false))
        {
            var time = rec.Timestamp.ToLocalTime().ToString("HH:mm:ss.fff");
            var line = $"{time} {rec.Level,-12} {rec.Message}";
            Application.Invoke(() => AppendLine(line));
            ended = true;
        }

        if (!ended) return;

        Application.Invoke(() => AppendLine("\u2500\u2500 Job Completed \u2500\u2500"));
        OnJobEnded?.Invoke("Completed");
    }

    private void AppendLine(string line)
    {
        _lines.Add(line);
        _listView.SelectedItem = _lines.Count - 1; // scroll to bottom
        SetNeedsDraw();
    }

    private void ClearLines()
    {
        Application.Invoke(() =>
        {
            _lines.Clear();
            SetNeedsDraw();
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
            CancelCurrentStream();

        base.Dispose(disposing);
    }
}
