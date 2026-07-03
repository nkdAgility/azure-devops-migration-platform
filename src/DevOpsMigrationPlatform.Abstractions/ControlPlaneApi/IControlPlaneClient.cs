// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Streaming;

namespace DevOpsMigrationPlatform.Abstractions.ControlPlaneApi;

/// <summary>
/// Abstraction over the control-plane HTTP client.
/// Declared in Abstractions so TUI view unit tests can inject a fake implementation.
/// </summary>
public interface IControlPlaneClient
{
    /// <summary>Returns all jobs visible to the caller via <c>GET /jobs</c>.</summary>
    Task<IReadOnlyList<JobSummary>> GetAllJobsAsync(CancellationToken ct);

    /// <summary>
    /// Opens the unified SSE stream at <c>GET /jobs/{jobId}/stream?from={fromSeq}</c>
    /// and yields <see cref="JobStreamEvent"/> records until the stream closes.
    /// Handles progress, diagnostic, and terminal events.
    /// </summary>
    IAsyncEnumerable<JobStreamEvent> StreamJobAsync(Guid jobId, CancellationToken ct, long fromSeq = 0);

    /// <summary>
    /// Returns the bootstrap payload for a job (snapshot + metrics + task list + last event sequence),
    /// or <c>null</c> when the job has not yet emitted any telemetry.
    /// Calls <c>GET /jobs/{jobId}/bootstrap</c>. Use for one-shot initial state only.
    /// </summary>
    Task<JobBootstrap?> GetBootstrapAsync(Guid jobId, CancellationToken ct);

    /// <summary>
    /// Returns the current <see cref="JobTaskList"/> for a job, or <c>null</c> when the
    /// agent has not yet pushed an execution plan.
    /// Calls <c>GET /jobs/{jobId}/tasks</c>.
    /// </summary>
    Task<JobTaskList?> GetTasksAsync(Guid jobId, CancellationToken ct);
}
