using System;
using System.Collections.Concurrent;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// In-memory store for the most recent <see cref="JobMetrics"/> per job.
/// Updated by <c>TelemetryController.POST /agents/lease/{leaseId}/metrics</c>.
/// Read by <c>TelemetryController.GET /jobs/{jobId}/telemetry</c>.
/// </summary>
public sealed class JobMetricsStore
{
    private readonly ConcurrentDictionary<Guid, JobMetrics> _snapshots = new();

    /// <summary>Stores or replaces the latest metrics for <paramref name="jobId"/>.</summary>
    public void Store(Guid jobId, JobMetrics metrics) =>
        _snapshots[jobId] = metrics;

    /// <summary>Returns the latest metrics for <paramref name="jobId"/>, or <c>null</c> if none.</summary>
    public JobMetrics? GetLatest(Guid jobId) =>
        _snapshots.TryGetValue(jobId, out var s) ? s : null;

    /// <summary>Removes the metrics when a job is no longer active.</summary>
    public void Remove(Guid jobId) =>
        _snapshots.TryRemove(jobId, out _);
}
