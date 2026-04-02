using System;
using System.Collections.Concurrent;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Services;

/// <summary>
/// In-memory store for the most recent <see cref="MetricSnapshot"/> per job.
/// Updated by <c>TelemetryController.POST /agents/lease/{leaseId}/telemetry</c>.
/// Read by <c>TelemetryController.GET /jobs/{jobId}/telemetry</c>.
/// </summary>
public sealed class JobTelemetryStore
{
    private readonly ConcurrentDictionary<Guid, MetricSnapshot> _snapshots = new();

    /// <summary>Stores or replaces the latest snapshot for <paramref name="jobId"/>.</summary>
    public void Store(Guid jobId, MetricSnapshot snapshot) =>
        _snapshots[jobId] = snapshot;

    /// <summary>Returns the latest snapshot for <paramref name="jobId"/>, or <c>null</c> if none.</summary>
    public MetricSnapshot? GetLatest(Guid jobId) =>
        _snapshots.TryGetValue(jobId, out var s) ? s : null;

    /// <summary>Removes the snapshot when a job is no longer active.</summary>
    public void Remove(Guid jobId) =>
        _snapshots.TryRemove(jobId, out _);
}
