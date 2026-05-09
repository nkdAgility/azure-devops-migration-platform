// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Jobs;

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

    /// <summary>
    /// Ordered phase summaries for presentation and plan inspection.
    /// This metadata groups the flat <see cref="Tasks"/> list without changing execution semantics.
    /// </summary>
    public IReadOnlyList<JobPhaseSummary> Phases { get; init; } = Array.Empty<JobPhaseSummary>();

    /// <summary>UTC timestamp when the agent pushed this plan.</summary>
    public DateTimeOffset PushedAt { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// The <see cref="Jobs.JobKind"/> this plan was built for.
    /// Used to detect mode switches (e.g. a previous Export plan being resumed as an Import).
    /// <c>null</c> for plans persisted before this property was introduced (treated as unknown).
    /// </summary>
    public JobKind? ForKind { get; init; }
}
