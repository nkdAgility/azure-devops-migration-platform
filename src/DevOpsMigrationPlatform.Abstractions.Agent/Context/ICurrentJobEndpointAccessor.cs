// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Context;

/// <summary>
/// Holds the explicit source and target endpoint views for the currently executing job.
/// Set once when the job starts and cleared when the job completes.
/// </summary>
public interface ICurrentJobEndpointAccessor
{
    /// <summary>The current source endpoint view, or <see langword="null"/> when unavailable.</summary>
    ISourceEndpointInfo? Source { get; }

    /// <summary>The current target endpoint view, or <see langword="null"/> when unavailable.</summary>
    ITargetEndpointInfo? Target { get; }

    /// <summary>Sets the current source endpoint view.</summary>
    void SetSource(ISourceEndpointInfo endpoint);

    /// <summary>Sets the current target endpoint view.</summary>
    void SetTarget(ITargetEndpointInfo endpoint);

    /// <summary>Clears the current source endpoint view.</summary>
    void ClearSource();

    /// <summary>Clears the current target endpoint view.</summary>
    void ClearTarget();

    /// <summary>Clears both endpoint views.</summary>
    void Clear();
}