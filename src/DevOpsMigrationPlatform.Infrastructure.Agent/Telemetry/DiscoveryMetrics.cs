using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Telemetry;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Telemetry;

/// <summary>
/// Concrete implementation of <see cref="IDiscoveryMetrics"/> that registers all instruments
/// under the <see cref="WellKnownMeterNames.Discovery"/> meter.
/// Thread-safe: all OTel instrument operations are lock-free.
/// </summary>
public sealed class DiscoveryMetrics : IDiscoveryMetrics, IDisposable
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
    public void OrganisationStarted(MetricsTagList tags)
    {
        _organisationsQueued.Add(1, ToTagList(tags));
        Interlocked.Increment(ref _activeJobs);
    }

    public void OrganisationCompleted(MetricsTagList tags)
    {
        _organisationsQueued.Add(-1, ToTagList(tags));
        _organisationsCompleted.Add(1, ToTagList(tags));
    }

    public void OrganisationFailed(MetricsTagList tags)
    {
        _organisationsQueued.Add(-1, ToTagList(tags));
        _organisationsFailed.Add(1, ToTagList(tags));
    }

    public void RecordOrganisationDuration(double milliseconds, MetricsTagList tags)
        => _organisationDuration.Record(milliseconds, ToTagList(tags));

    public void SetProjectCount(int count, MetricsTagList tags)
        => Volatile.Write(ref _lastProjectCount, count);

    // --- Project ---
    public void ProjectStarted(MetricsTagList tags)
        => _projectsQueued.Add(1, ToTagList(tags));

    public void ProjectCompleted(MetricsTagList tags)
    {
        _projectsQueued.Add(-1, ToTagList(tags));
        _projectsCompleted.Add(1, ToTagList(tags));
    }

    public void ProjectFailed(MetricsTagList tags)
    {
        _projectsQueued.Add(-1, ToTagList(tags));
        _projectsFailed.Add(1, ToTagList(tags));
    }

    public void RecordProjectDuration(double milliseconds, MetricsTagList tags)
        => _projectDuration.Record(milliseconds, ToTagList(tags));

    // --- Inventory ---
    public void RecordWorkItemsCounted(int count, MetricsTagList tags)
        => _inventoryWorkItems.Add(count, ToTagList(tags));

    public void RecordRevisionsCounted(int count, MetricsTagList tags)
        => _inventoryRevisions.Add(count, ToTagList(tags));

    public void RecordReposCounted(int count, MetricsTagList tags)
        => _inventoryRepos.Add(count, ToTagList(tags));

    // --- Dependencies ---
    public void RecordLinksFound(int count, MetricsTagList tags)
        => _dependencyLinks.Add(count, ToTagList(tags));

    public void RecordWorkItemsAnalysed(int count, MetricsTagList tags)
        => _dependencyWorkItemsAnalysed.Add(count, ToTagList(tags));

    // --- Operational ---
    public void RecordCheckpointSaved(MetricsTagList tags)
        => _checkpointsSaved.Add(1, ToTagList(tags));

    public void RecordJobDuration(double milliseconds, MetricsTagList tags)
    {
        _jobDuration.Record(milliseconds, ToTagList(tags));
        Interlocked.Decrement(ref _activeJobs);
    }

    private static TagList ToTagList(MetricsTagList tags)
    {
        var tagList = new TagList();
        for (var i = 0; i < tags.Count; i++)
            tagList.Add(tags[i].Key, tags[i].Value);
        return tagList;
    }

    public void Dispose() => _meter.Dispose();
}

