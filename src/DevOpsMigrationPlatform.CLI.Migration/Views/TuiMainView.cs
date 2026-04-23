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
    private readonly TuiDerivedView _derived;
    private readonly TuiLogView _logView;
    private readonly Label _statusBar;

    private CancellationTokenSource? _selectionCts;
    private Guid? _selectedJobId;

    // Discovery metrics accumulation — keyed by "org|project"
    private readonly Dictionary<string, DiscoveryProjectMetrics> _discoveryProjects = new();
    private readonly HashSet<string> _discoveryOrgsCompleted = new();
    private readonly HashSet<string> _discoveryOrgsFailed = new();
    private readonly HashSet<string> _discoveryOrgsQueued = new();
    private DateTimeOffset _discoveryStartTime;
    private long _discoveryCheckpointsSaved;
    private volatile bool _discoveryMetricsActive;
    private JobScopeCounters? _inventoryLoadedScope;
    private DiscoveryCounters? _inventoryLoadedDiscovery;

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

        // Reset discovery metrics state for the new job
        _discoveryMetricsActive = false;
        _discoveryProjects.Clear();
        _inventoryLoadedScope = null;
        _inventoryLoadedDiscovery = null;

        _metrics.SetWaiting();
        _derived.SetWaiting();
        _logView.ClearAndBind(jobId, ct);

        // Start telemetry polling (T029)
        Task.Run(() => PollTelemetryAsync(jobId, ct), ct);

        // Start a dedicated progress follower for discovery metrics.
        // This runs independently of the log view's display mode so that
        // switching to Diagnostics tab does not freeze the metrics panel.
        Task.Run(() => FollowProgressForMetricsAsync(jobId, ct), ct);

        UpdateStatusBar(jobId.ToString()[..8], "—", "[Progress]");
    }

    private void DeselectJob()
    {
        CancelSelection();
        _selectedJobId = null;
        _discoveryMetricsActive = false;
        _discoveryProjects.Clear();
        _inventoryLoadedScope = null;
        _inventoryLoadedDiscovery = null;
        _derived.SetWaiting();
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

    /// <summary>
    /// Dedicated progress follower that feeds discovery metrics regardless of
    /// which mode the log panel is displaying. Runs for the lifetime of the job selection.
    /// </summary>
    private async Task FollowProgressForMetricsAsync(Guid jobId, CancellationToken ct)
    {
        int backoffMs = 1_000;
        const int maxBackoffMs = 30_000;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                await foreach (var evt in _client.FollowLogsAsync(jobId, ct).ConfigureAwait(false))
                {
                    AccumulateDiscoveryMetrics(evt);
                }
                // Stream ended cleanly
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch
            {
                // Transient error — back off and reconnect
                try { await Task.Delay(backoffMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return; }
                backoffMs = Math.Min(backoffMs * 2, maxBackoffMs);
            }
        }
    }

    private void AccumulateDiscoveryMetrics(ProgressEvent evt)
    {
        // Only accumulate discovery metrics for Inventory / Dependencies modules
        if (evt.Module != "Inventory" && evt.Module != "Dependencies")
            return;

        if (!_discoveryMetricsActive)
        {
            _discoveryStartTime = DateTimeOffset.UtcNow;
        }
        _discoveryMetricsActive = true;

        // ── Handle InventoryLoaded (aggregate, no project key) ───────────────
        // This event carries the total org/project/work-item counts from the
        // inventory report. We use it to seed _discoveryOrgsQueued when the
        // per-project InventorySeed events have not yet arrived.
        if (evt.Stage == "InventoryLoaded")
        {
            // The InventoryLoaded Message is human-readable, not a structured key.
            // Aggregate totals are stored separately and merged into the final
            // metrics below; per-project data comes via InventorySeed events.
            _inventoryLoadedScope = evt.Metrics?.Scope;
            _inventoryLoadedDiscovery = evt.Metrics?.Discovery;
            return;
        }

        // Extract counters from Metrics envelope when available.
        var disc = evt.Metrics?.Discovery;
        var deps = disc?.Dependencies;
        var inv = disc?.Inventory;

        // Use module+message as a project key since LastProcessed was removed.
        var key = evt.Message;
        if (!string.IsNullOrEmpty(key))
        {
            var pipeIdx = key.IndexOf('|');
            if (pipeIdx <= 0)
                return; // Not a structured key — skip

            var orgUrl = key.Substring(0, pipeIdx);
            _discoveryOrgsQueued.Add(orgUrl);

            // Use module-qualified key so Inventory and Dependencies don't overwrite each other
            var qualifiedKey = $"{evt.Module}:{key}";

            // InventorySeed events carry per-project inventory data; they are
            // emitted by the Dependencies module but represent inventory counts.
            // Store them under an "Inventory:" qualified key so the totals
            // accumulation picks them up in the WorkItems/Revisions/Repos bucket.
            if (evt.Stage == "InventorySeed")
            {
                var seedKey = $"Inventory:{key}";
                _discoveryProjects[seedKey] = new DiscoveryProjectMetrics(
                    WorkItems: evt.Metrics?.Scope?.WorkItemsTotal ?? 0,
                    Revisions: inv?.RevisionsTotal ?? 0,
                    Repos: inv?.RepositoriesTotal ?? 0,
                    LinksFound: 0, WorkItemsAnalysed: 0,
                    TotalWorkItems: 0,
                    IsComplete: true, IsFailed: false,
                    Module: "Inventory");
                return;
            }

            bool isComplete = evt.Stage == "Inventory" || evt.Stage == "Dependencies"
                           || evt.Stage == "ProjectComplete" || evt.Stage == "Reconciled";
            bool isFailed = evt.Stage == "Failed";

            if (evt.Module == "Dependencies")
            {
                _discoveryProjects[qualifiedKey] = new DiscoveryProjectMetrics(
                    WorkItems: 0, Revisions: 0, Repos: 0,
                    LinksFound: deps?.ExternalLinksFound ?? 0,
                    WorkItemsAnalysed: deps?.WorkItemsAnalysed ?? 0,
                    TotalWorkItems: evt.Metrics?.Scope?.WorkItemsTotal ?? 0,
                    IsComplete: isComplete || isFailed, IsFailed: isFailed,
                    Module: evt.Module);
            }
            else
            {
                _discoveryProjects[qualifiedKey] = new DiscoveryProjectMetrics(
                    WorkItems: evt.Metrics?.Scope?.WorkItemsTotal ?? 0,
                    Revisions: inv?.RevisionsTotal ?? 0,
                    Repos: inv?.RepositoriesTotal ?? 0,
                    LinksFound: 0, WorkItemsAnalysed: 0,
                    TotalWorkItems: 0,
                    IsComplete: isComplete || isFailed, IsFailed: isFailed,
                    Module: evt.Module);
            }

            // Track org completion: when all projects for an org are complete
            if (isComplete || isFailed)
            {
                // Check if all projects for this org are complete
                bool allDone = true;
                foreach (var entry in _discoveryProjects)
                {
                    if (!entry.Key.StartsWith($"Dependencies:{orgUrl}|", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (!entry.Value.IsComplete)
                    {
                        allDone = false;
                        break;
                    }
                }
                if (allDone && _discoveryProjects.Keys.Any(k => k.StartsWith($"Dependencies:{orgUrl}|", StringComparison.OrdinalIgnoreCase)))
                {
                    // Check if any project in this org failed
                    bool anyFailed = _discoveryProjects
                        .Where(e => e.Key.StartsWith($"Dependencies:{orgUrl}|", StringComparison.OrdinalIgnoreCase))
                        .Any(e => e.Value.IsFailed);
                    if (anyFailed)
                        _discoveryOrgsFailed.Add(orgUrl);
                    else
                        _discoveryOrgsCompleted.Add(orgUrl);
                }
            }
        }

        // Track checkpoint events
        if (evt.Message?.Contains("checkpoint", StringComparison.OrdinalIgnoreCase) == true)
            _discoveryCheckpointsSaved++;

        // Accumulate totals across all tracked projects.
        // Separate inventory-phase and dependency-phase entries so we can report
        // distinct inventory counters (work items, revisions, repos) and
        // dependency counters (analysed, links) without double-counting.
        int depTotal = 0, depCompleted = 0, depFailed = 0;
        long totalWi = 0, totalRev = 0, totalRepos = 0;
        long totalLinks = 0, totalAnalysed = 0;
        long totalKnown = 0, processedOfKnown = 0;

        foreach (var entry in _discoveryProjects)
        {
            var v = entry.Value;
            if (v.Module == "Inventory")
            {
                // Inventory entries contribute work-item/revision/repo counts
                totalWi += v.WorkItems;
                totalRev += v.Revisions;
                totalRepos += v.Repos;
            }
            else
            {
                // Dependency entries contribute analysed/links counts
                depTotal++;
                if (v.IsFailed) depFailed++;
                else if (v.IsComplete) depCompleted++;
                totalLinks += v.LinksFound;
                totalAnalysed += v.WorkItemsAnalysed;
                if (v.TotalWorkItems > 0)
                {
                    totalKnown += v.TotalWorkItems;
                    processedOfKnown += v.IsComplete ? v.TotalWorkItems : v.WorkItemsAnalysed;
                }
            }
        }

        // Use inventory-loaded aggregate as fallback when per-project seeds
        // haven't arrived or were incomplete
        if (totalWi == 0 && _inventoryLoadedScope is not null)
            totalWi = _inventoryLoadedScope.WorkItemsTotal;
        if (totalRev == 0 && _inventoryLoadedDiscovery?.Inventory is not null)
            totalRev = _inventoryLoadedDiscovery.Inventory.RevisionsTotal;
        if (totalRepos == 0 && _inventoryLoadedDiscovery?.Inventory is not null)
            totalRepos = _inventoryLoadedDiscovery.Inventory.RepositoriesTotal;

        // Org and project totals: prefer inventory-loaded counts when larger
        var orgsTotal = _discoveryOrgsQueued.Count;
        var projectsTotal = depTotal;
        if (_inventoryLoadedScope is not null)
        {
            if (_inventoryLoadedScope.OrganisationsTotal > orgsTotal)
                orgsTotal = (int)_inventoryLoadedScope.OrganisationsTotal;
            if (_inventoryLoadedScope.ProjectsTotal > projectsTotal)
                projectsTotal = (int)_inventoryLoadedScope.ProjectsTotal;
        }

        var remaining = depTotal - depCompleted - depFailed;
        var elapsed = DateTimeOffset.UtcNow - _discoveryStartTime;
        var elapsedHours = elapsed.TotalHours;

        var discoveryMetrics = new JobMetrics
        {
            Scope = new JobScopeCounters
            {
                OrganisationsCompleted = _discoveryOrgsCompleted.Count,
                OrganisationsFailed = _discoveryOrgsFailed.Count,
                OrganisationsTotal = orgsTotal,
                ProjectsCompleted = depCompleted,
                ProjectsFailed = depFailed,
                ProjectsTotal = projectsTotal,
                WorkItemsTotal = totalWi
            },
            Discovery = new DiscoveryCounters
            {
                Inventory = new InventoryCounters
                {
                    RevisionsTotal = totalRev,
                    RepositoriesTotal = totalRepos,
                    CheckpointsSaved = _discoveryCheckpointsSaved
                },
                Dependencies = new DependencyCounters
                {
                    ExternalLinksFound = totalLinks,
                    WorkItemsAnalysed = totalAnalysed
                }
            }
        };

        // Compute avg project duration for derived view
        double? avgProjectDurationMs = depCompleted > 0
            ? elapsed.TotalMilliseconds / depCompleted
            : null;

        // ETA: prefer per-item rate when we have dependency pre-count totals;
        // otherwise fall back to avg-project-duration × remaining projects
        TimeSpan? estimatedRemaining = null;
        if (totalKnown > 0 && processedOfKnown > 0 && processedOfKnown < totalKnown)
        {
            var msPerItem = elapsed.TotalMilliseconds / processedOfKnown;
            estimatedRemaining = TimeSpan.FromMilliseconds(msPerItem * (totalKnown - processedOfKnown));
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

        _metrics.UpdateDiscovery(discoveryMetrics);
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

    // ─── Supporting types ────────────────────────────────────────────────────────

    private sealed record DiscoveryProjectMetrics(
        long WorkItems, long Revisions, long Repos,
        long LinksFound, long WorkItemsAnalysed,
        long TotalWorkItems,
        bool IsComplete, bool IsFailed,
        string Module);
}
