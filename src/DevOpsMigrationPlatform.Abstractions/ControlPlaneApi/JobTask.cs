// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Represents a single executable step in a <see cref="JobTaskList"/>.
/// Immutable after creation; status updates produce a new instance.
/// </summary>
public sealed record JobTask
{
    /// <summary>
    /// Unique task identifier within the job.
    /// Format: <c>{taskkind}.{module}.{orgSlug}.{projectSlug}</c>,
    /// e.g. <c>"capture.workitems.myorg.projecta"</c>.
    /// </summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Human-readable task name, e.g. "WorkItems Export".</summary>
    public string Name { get; init; } = string.Empty;

    /// <summary>
    /// The type of work this task performs. The executor dispatches on this value.
    /// </summary>
    public TaskKind TaskKind { get; init; }

    /// <summary>
    /// Display hint — the migration lifecycle segment this task belongs to (e.g. "export", "import").
    /// <para><strong>Not used for execution dispatch.</strong> The executor dispatches on <see cref="TaskKind"/> only.</para>
    /// Null for tasks that do not belong to a migration lifecycle phase (e.g. Capture, Analyse).
    /// </summary>
    public string? Phase { get; init; }

    /// <summary>
    /// The organisation URL this task is scoped to, e.g. "https://dev.azure.com/myorg".
    /// Set by the plan builder; null for tasks that are not org-scoped.
    /// </summary>
    public string? OrganisationUrl { get; init; }

    /// <summary>
    /// The project name this task is scoped to, e.g. "MyProject".
    /// Set by the plan builder; null for fan-in tasks such as <see cref="TaskKind.Analyse"/>.
    /// </summary>
    public string? ProjectName { get; init; }

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

    /// <summary>
    /// Task IDs (e.g. "import.identities") that must complete successfully before this task may execute.
    /// Null or empty = no dependencies. Dependencies are evaluated per-phase; Export tasks have no dependencies.
    /// </summary>
    public IReadOnlyList<string>? DependsOn { get; init; }
}
