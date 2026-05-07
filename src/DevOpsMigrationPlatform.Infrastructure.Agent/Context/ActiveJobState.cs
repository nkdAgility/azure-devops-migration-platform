// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default implementation of <see cref="IActiveJobState"/>.
/// Registered as a singleton so the same instance is shared across all consumers
/// within a lease/job lifecycle. Thread-safe via <c>volatile</c> reads/writes.
/// </summary>
public sealed class ActiveJobState : IActiveJobState
{
    private volatile string? _jobId;
    private volatile string? _kind;

    /// <inheritdoc/>
    public string? JobId => _jobId;

    /// <inheritdoc/>
    public string? Kind => _kind;

    /// <inheritdoc/>
    public void Set(string jobId, string kind)
    {
        _jobId = jobId;
        _kind = kind;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _jobId = null;
        _kind = null;
    }
}
