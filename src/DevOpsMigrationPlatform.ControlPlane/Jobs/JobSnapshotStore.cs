// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// In-memory store for the most recent <see cref="JobSnapshot"/> per job.
/// Updated by <c>WorkerEventsController.POST /workers/{workerId}/events</c>.
/// Read by <c>TelemetryController.GET /jobs/{jobId}/snapshot</c> and the bootstrap endpoint.
/// </summary>
public sealed class JobSnapshotStore
{
    private readonly ConcurrentDictionary<Guid, JobSnapshot> _snapshots = new();

    /// <summary>Stores or replaces the latest snapshot for <paramref name="jobId"/>.</summary>
    public void Store(Guid jobId, JobSnapshot snapshot) =>
        _snapshots[jobId] = snapshot;

    /// <summary>Returns the latest snapshot for <paramref name="jobId"/>, or <c>null</c> if none.</summary>
    public JobSnapshot? GetLatest(Guid jobId) =>
        _snapshots.TryGetValue(jobId, out var s) ? s : null;

    /// <summary>Removes the snapshot when a job is no longer active.</summary>
    public void Remove(Guid jobId) =>
        _snapshots.TryRemove(jobId, out _);
}
