// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Ambient singleton that carries the identity of the currently executing job.
/// Set by the agent worker immediately before job execution begins; cleared in the
/// finally block after the job ends or the lease is released.
///
/// Inject this into any service that needs the current job ID or kind without
/// coupling to the full <see cref="DevOpsMigrationPlatform.Abstractions.Jobs.Job"/>
/// dispatch token (which contains credentials and should not be passed around).
///
/// Thread-safe: implementations use <c>volatile</c> semantics.
/// <c>null</c> when no job is active.
/// </summary>
public interface IActiveJobState
{
    /// <summary>
    /// The JobId (UUID v4) of the currently executing job,
    /// or <see langword="null"/> when no job is active.
    /// </summary>
    string? JobId { get; }

    /// <summary>
    /// The kind of the currently executing job (e.g. <c>"Export"</c>, <c>"Import"</c>),
    /// or <see langword="null"/> when no job is active.
    /// </summary>
    string? Kind { get; }

    /// <summary>Sets the job identity at job start. Called once per lease acquisition.</summary>
    void Set(string jobId, string kind);

    /// <summary>Clears all state when the job completes or the lease is released.</summary>
    void Clear();
}
