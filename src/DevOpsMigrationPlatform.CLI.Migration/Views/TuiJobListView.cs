using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using Terminal.Gui;

namespace DevOpsMigrationPlatform.CLI.Views;

/// <summary>
/// Left panel of the TUI: a <see cref="TableView"/> showing all visible <see cref="JobSummary"/> rows.
/// Fires <see cref="JobSelected"/> when the user moves to a different row.
/// Auto-refreshes every <c>refreshIntervalMs</c> milliseconds via an internal timer.
/// </summary>
public sealed class TuiJobListView : FrameView, IDisposable
{
    private readonly IControlPlaneClient _client;
    private readonly int _refreshIntervalMs;
    private readonly TableView _table;
    private readonly Label _emptyLabel;
    private readonly Timer _refreshTimer;

    private IReadOnlyList<JobSummary> _jobs = [];

    /// <summary>Raised when the operator moves to a different row. <c>null</c> when the list is empty or deselected.</summary>
    public event Action<Guid?>? JobSelected;

    /// <param name="client">Control-plane client used for background job-list refresh.</param>
    /// <param name="refreshIntervalMs">Refresh period in milliseconds (default 10 000).</param>
    public TuiJobListView(IControlPlaneClient client, int refreshIntervalMs = 10_000)
    {
        _client = client;
        _refreshIntervalMs = refreshIntervalMs;
        Title = "Jobs";
        CanFocus = true;

        _refreshTimer = new Timer(
            _ => _ = FetchAndRefreshAsync(),
            null,
            dueTime: 0,
            period: refreshIntervalMs);

        _emptyLabel = new Label
        {
            Text = "(no jobs)",
            X = Pos.Center(),
            Y = Pos.Center(),
            Visible = true
        };

        _table = new TableView
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            CanFocus = true,
            FullRowSelect = true,
            MultiSelect = false
        };

        _table.SelectedCellChanged += OnSelectedCellChanged;

        Add(_emptyLabel);
        Add(_table);
    }

    /// <summary>Replaces the displayed job list. Must be called from the Terminal.Gui main loop thread.</summary>
    public void UpdateJobs(IReadOnlyList<JobSummary> jobs)
    {
        _jobs = jobs;
        RebuildTable();
    }

    private void RebuildTable()
    {
        if (_jobs.Count == 0)
        {
            _emptyLabel.Visible = true;
            _table.Visible = false;
            SetNeedsDraw();
            return;
        }

        _emptyLabel.Visible = false;
        _table.Visible = true;

        var dt = new System.Data.DataTable();
        dt.Columns.Add("Job ID");
        dt.Columns.Add("Mode");
        dt.Columns.Add("State");
        dt.Columns.Add("Submitted");

        foreach (var j in _jobs)
        {
            dt.Rows.Add(
                j.JobId.ToString()[..8],
                j.Mode,
                j.State,
                j.SubmittedAt.ToLocalTime().ToString("yyyy-MM-dd HH:mm")
            );
        }

        _table.Table = new DataTableSource(dt);
        SetNeedsDraw();
    }

    private void OnSelectedCellChanged(object? sender, SelectedCellChangedEventArgs e)
    {
        if (_jobs.Count == 0 || e.NewRow < 0 || e.NewRow >= _jobs.Count)
        {
            JobSelected?.Invoke(null);
            return;
        }

        JobSelected?.Invoke(_jobs[e.NewRow].JobId);
    }

    // ─── Auto-refresh ─────────────────────────────────────────────────────────────

    private async Task FetchAndRefreshAsync()
    {
        try
        {
            var jobs = await _client.GetAllJobsAsync(CancellationToken.None).ConfigureAwait(false);
            Application.Invoke(() => UpdateJobs(jobs));
        }
        catch
        {
            // Swallow transient errors — next tick will retry
        }
    }

    // ─── Dispose ─────────────────────────────────────────────────────────────────

    /// <inheritdoc/>
    public new void Dispose()
    {
        _refreshTimer.Dispose();
        base.Dispose();
    }
}
