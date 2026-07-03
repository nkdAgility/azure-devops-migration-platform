// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

namespace DevOpsMigrationPlatform.ControlPlane.Jobs;

/// <summary>
/// In-memory store for the most recently pushed <see cref="JobTaskList"/> per job.
/// Updated by <c>WorkerEventsController.POST /workers/{workerId}/events</c>.
/// Individual task states are updated as <see cref="ProgressEvent"/> events arrive
/// via the unified worker events channel.
/// Read by <c>TelemetryController.GET /jobs/{jobId}/tasks</c>
/// and included in the bootstrap response.
/// </summary>
public sealed class InMemoryJobTaskStore
{
    private readonly ConcurrentDictionary<Guid, JobTaskList> _lists = new();

    /// <summary>
    /// Stores (or replaces) the task list for the given job.
    /// Called once at job start when the agent pushes its execution plan.
    /// </summary>
    public void Store(Guid jobId, JobTaskList taskList) =>
        _lists[jobId] = taskList;

    /// <summary>
    /// Returns the current <see cref="JobTaskList"/> for <paramref name="jobId"/>,
    /// or <c>null</c> if the agent has not yet pushed a plan.
    /// </summary>
    public JobTaskList? GetLatest(Guid jobId) =>
        _lists.TryGetValue(jobId, out var list) ? list : null;

    /// <summary>
    /// Applies a partial update to the task identified by <paramref name="taskId"/>.
    /// Merges the supplied status and optional completed count into the stored record.
    /// No-ops if no task list exists for <paramref name="jobId"/> or the task is not found.
    /// </summary>
    public void UpdateTask(
        Guid jobId,
        string taskId,
        JobTaskStatus newStatus,
        long? completedCount,
        long? knownTotal,
        DateTimeOffset timestamp)
    {
        _lists.AddOrUpdate(
            jobId,
            _ => new JobTaskList(), // guard: should not happen in practice
            (_, existing) =>
            {
                var updated = new System.Collections.Generic.List<JobTask>();
                foreach (var task in existing.Tasks)
                {
                    if (task.Id != taskId)
                    {
                        updated.Add(task);
                        continue;
                    }

                    var startedAt = newStatus == JobTaskStatus.Running
                        ? timestamp
                        : task.StartedAt;

                    var completedAt = (newStatus == JobTaskStatus.Completed ||
                                       newStatus == JobTaskStatus.Failed ||
                                       newStatus == JobTaskStatus.Skipped)
                        ? timestamp
                        : task.CompletedAt;

                    updated.Add(task with
                    {
                        Status = newStatus,
                        CompletedCount = completedCount ?? task.CompletedCount,
                        KnownTotal = knownTotal ?? task.KnownTotal,
                        StartedAt = startedAt,
                        CompletedAt = completedAt
                    });
                }

                return existing with
                {
                    Tasks = updated.AsReadOnly()
                };
            });
    }

    /// <summary>Removes the task list when a job is no longer active.</summary>
    public void Remove(Guid jobId) => _lists.TryRemove(jobId, out _);
}
