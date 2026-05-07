// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Holds the explicit per-job <see cref="IAgentJobContext"/> for the currently executing agent job.
/// Set once at job start and cleared when the job finishes.
/// </summary>
public interface ICurrentAgentJobContextAccessor
{
    /// <summary>
    /// The current immutable per-job context, or <see langword="null"/> when no job is active.
    /// </summary>
    IAgentJobContext? Current { get; }

    /// <summary>Sets the current job context at job start.</summary>
    void Set(IAgentJobContext context);

    /// <summary>Clears the current job context when the job completes.</summary>
    void Clear();
}