using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// The ordered list of tasks that the agent will execute for a given job.
/// Pushed by the agent at job start via <c>POST /agents/lease/{leaseId}/tasks</c>
/// and returned as part of <see cref="JobBootstrap"/> for late-joining clients.
/// </summary>
public sealed record JobTaskList
{
    /// <summary>Ordered list of tasks in execution sequence.</summary>
    public IReadOnlyList<JobTask> Tasks { get; init; } = Array.Empty<JobTask>();

    /// <summary>UTC timestamp when the agent pushed this plan.</summary>
    public DateTimeOffset PushedAt { get; init; } = DateTimeOffset.UtcNow;
}
