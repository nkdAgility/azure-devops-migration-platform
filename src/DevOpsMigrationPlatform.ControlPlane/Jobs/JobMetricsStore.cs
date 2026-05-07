// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// In-memory store for the most recent <see cref="JobMetrics"/> per job.
/// Updated by <c>TelemetryController.POST /agents/lease/{leaseId}/metrics</c>.
/// Read by <c>TelemetryController.GET /jobs/{jobId}/telemetry</c>.
/// </summary>
public sealed class JobMetricsStore
{
    private readonly ConcurrentDictionary<Guid, JobMetrics> _snapshots = new();

    /// <summary>
    /// Merges <paramref name="metrics"/> into the stored record for <paramref name="jobId"/>.
    /// Sub-counters (Teams, Nodes, Identities, WorkItems) are preserved from the previous record
    /// when the incoming value is null, so module-level events do not erase each other.
    /// </summary>
    public void Store(Guid jobId, JobMetrics metrics)
    {
        _snapshots.AddOrUpdate(
            jobId,
            metrics,
            (_, existing) => Merge(existing, metrics));
    }

    /// <summary>Returns the latest metrics for <paramref name="jobId"/>, or <c>null</c> if none.</summary>
    public JobMetrics? GetLatest(Guid jobId) =>
        _snapshots.TryGetValue(jobId, out var s) ? s : null;

    /// <summary>Removes the metrics when a job is no longer active.</summary>
    public void Remove(Guid jobId) =>
        _snapshots.TryRemove(jobId, out _);

    private static JobMetrics Merge(JobMetrics existing, JobMetrics incoming)
    {
        var inMig = incoming.Migration;
        var exMig = existing.Migration;

        if (inMig is null && exMig is null)
            return incoming;

        var mergedMig = new MigrationCounters
        {
            WorkItems = inMig?.WorkItems ?? exMig?.WorkItems ?? new WorkItemCounters(),
            Teams = inMig?.Teams ?? exMig?.Teams,
            Nodes = inMig?.Nodes ?? exMig?.Nodes,
            Identities = inMig?.Identities ?? exMig?.Identities,
            DependencyCapture = inMig?.DependencyCapture ?? exMig?.DependencyCapture,
            Diagnostics = inMig?.Diagnostics ?? exMig?.Diagnostics,
        };

        return incoming with
        {
            Migration = mergedMig,
            Scope = incoming.Scope ?? existing.Scope,
        };
    }
}
