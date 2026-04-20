#if !NETFRAMEWORK
using System;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Telemetry;

/// <summary>
/// Concrete implementation of <see cref="IDiscoveryMetrics"/> that registers all instruments
/// under the <see cref="WellKnownMeterNames.Discovery"/> meter.
/// Thread-safe: all OTel instrument operations are lock-free.
/// </summary>
internal sealed class DiscoveryMetrics : IDiscoveryMetrics, IDisposable
{
    private readonly Meter _meter;

    // --- Organisation ---
    private readonly UpDownCounter<int> _organisationsQueued;
    private readonly Counter<long> _organisationsCompleted;
    private readonly Counter<long> _organisationsFailed;
    private readonly Histogram<double> _organisationDuration;
    private int _lastProjectCount;

    // --- Project ---
    private readonly UpDownCounter<int> _projectsQueued;
    private readonly Counter<long> _projectsCompleted;
    private readonly Counter<long> _projectsFailed;
    private readonly Histogram<double> _projectDuration;

    // --- Inventory ---
    private readonly Counter<long> _inventoryWorkItems;
    private readonly Counter<long> _inventoryRevisions;
    private readonly Counter<long> _inventoryRepos;

    // --- Dependencies ---
    private readonly Counter<long> _dependencyLinks;
    private readonly Counter<long> _dependencyWorkItemsAnalysed;

    // --- Operational ---
    private readonly Counter<long> _checkpointsSaved;
    private readonly Histogram<double> _jobDuration;
    private int _activeJobs;

    public DiscoveryMetrics()
    {
        _meter = new Meter(WellKnownMeterNames.Discovery, "1.0");

        // Organisation
        _organisationsQueued = _meter.CreateUpDownCounter<int>(
            WellKnownDiscoveryMetricNames.OrganisationsQueued, unit: "{organisation}");
        _organisationsCompleted = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.OrganisationsCompleted, unit: "{organisation}");
        _organisationsFailed = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.OrganisationsFailed, unit: "{organisation}");
        _organisationDuration = _meter.CreateHistogram<double>(
            WellKnownDiscoveryMetricNames.OrganisationDurationMs, unit: "ms");
        _meter.CreateObservableGauge(
            WellKnownDiscoveryMetricNames.OrganisationProjectCount,
            () => Volatile.Read(ref _lastProjectCount),
            unit: "{project}");

        // Project
        _projectsQueued = _meter.CreateUpDownCounter<int>(
            WellKnownDiscoveryMetricNames.ProjectsQueued, unit: "{project}");
        _projectsCompleted = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.ProjectsCompleted, unit: "{project}");
        _projectsFailed = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.ProjectsFailed, unit: "{project}");
        _projectDuration = _meter.CreateHistogram<double>(
            WellKnownDiscoveryMetricNames.ProjectDurationMs, unit: "ms");

        // Inventory
        _inventoryWorkItems = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.InventoryWorkItems, unit: "{work_item}");
        _inventoryRevisions = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.InventoryRevisions, unit: "{revision}");
        _inventoryRepos = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.InventoryRepos, unit: "{repo}");

        // Dependencies
        _dependencyLinks = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.DependencyLinks, unit: "{link}");
        _dependencyWorkItemsAnalysed = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.DependencyWorkItemsAnalysed, unit: "{work_item}");

        // Operational
        _checkpointsSaved = _meter.CreateCounter<long>(
            WellKnownDiscoveryMetricNames.CheckpointsSaved, unit: "{checkpoint}");
        _jobDuration = _meter.CreateHistogram<double>(
            WellKnownDiscoveryMetricNames.JobDurationMs, unit: "ms");
        _meter.CreateObservableGauge(
            WellKnownDiscoveryMetricNames.JobsActive,
            () => Volatile.Read(ref _activeJobs),
            unit: "{job}");
    }

    // --- Organisation ---
    public void OrganisationStarted(in TagList tags)
    {
        _organisationsQueued.Add(1, tags);
        Interlocked.Increment(ref _activeJobs);
    }

    public void OrganisationCompleted(in TagList tags)
    {
        _organisationsQueued.Add(-1, tags);
        _organisationsCompleted.Add(1, tags);
    }

    public void OrganisationFailed(in TagList tags)
    {
        _organisationsQueued.Add(-1, tags);
        _organisationsFailed.Add(1, tags);
    }

    public void RecordOrganisationDuration(double milliseconds, in TagList tags)
        => _organisationDuration.Record(milliseconds, tags);

    public void SetProjectCount(int count, in TagList tags)
        => Volatile.Write(ref _lastProjectCount, count);

    // --- Project ---
    public void ProjectStarted(in TagList tags)
        => _projectsQueued.Add(1, tags);

    public void ProjectCompleted(in TagList tags)
    {
        _projectsQueued.Add(-1, tags);
        _projectsCompleted.Add(1, tags);
    }

    public void ProjectFailed(in TagList tags)
    {
        _projectsQueued.Add(-1, tags);
        _projectsFailed.Add(1, tags);
    }

    public void RecordProjectDuration(double milliseconds, in TagList tags)
        => _projectDuration.Record(milliseconds, tags);

    // --- Inventory ---
    public void RecordWorkItemsCounted(int count, in TagList tags)
        => _inventoryWorkItems.Add(count, tags);

    public void RecordRevisionsCounted(int count, in TagList tags)
        => _inventoryRevisions.Add(count, tags);

    public void RecordReposCounted(int count, in TagList tags)
        => _inventoryRepos.Add(count, tags);

    // --- Dependencies ---
    public void RecordLinksFound(int count, in TagList tags)
        => _dependencyLinks.Add(count, tags);

    public void RecordWorkItemsAnalysed(int count, in TagList tags)
        => _dependencyWorkItemsAnalysed.Add(count, tags);

    // --- Operational ---
    public void RecordCheckpointSaved(in TagList tags)
        => _checkpointsSaved.Add(1, tags);

    public void RecordJobDuration(double milliseconds, in TagList tags)
    {
        _jobDuration.Record(milliseconds, tags);
        Interlocked.Decrement(ref _activeJobs);
    }

    public void Dispose() => _meter.Dispose();
}
#endif
