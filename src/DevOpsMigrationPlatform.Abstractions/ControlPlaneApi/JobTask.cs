using System;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Represents a single executable step in a <see cref="JobTaskList"/>.
/// Immutable after creation; status updates produce a new instance.
/// </summary>
public sealed record JobTask
{
    /// <summary>Unique task identifier within the job, e.g. "export.workitems".</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable task name, e.g. "WorkItems Export".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>Logical phase grouping, e.g. "Export" or "Import". Null for standalone tasks.</summary>
    public string? Phase { get; init; }

    /// <summary>Execution order within the plan (0-based ascending).</summary>
    public int Order { get; init; }

    /// <summary>Current execution status of this task.</summary>
    public JobTaskStatus Status { get; init; } = JobTaskStatus.Pending;

    /// <summary>
    /// Known total item count if available at plan time (e.g. from inventory.json).
    /// Null when unknown.
    /// </summary>
    public long? KnownTotal { get; init; }

    /// <summary>Number of items completed so far. Updated via <see cref="ProgressEvent"/> emissions.</summary>
    public long? CompletedCount { get; init; }

    /// <summary>UTC timestamp when this task transitioned to <see cref="JobTaskStatus.Running"/>.</summary>
    public DateTimeOffset? StartedAt { get; init; }

    /// <summary>UTC timestamp when this task reached a terminal state.</summary>
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Human-readable reason this task was skipped. Non-null only when <see cref="Status"/> is <see cref="JobTaskStatus.Skipped"/>.</summary>
    public string? SkipReason { get; init; }
}
