// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using Microsoft.Extensions.Configuration;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Context;

/// <summary>
/// Default implementation of <see cref="IJobConfiguration"/>.
/// Registered as a singleton so the same instance is shared across all consumers
/// within a lease/job lifecycle. Thread-safe via <c>volatile</c> reads/writes.
/// </summary>
public sealed class JobConfiguration : IJobConfiguration
{
    private volatile IConfiguration? _packageConfig;

    /// <inheritdoc/>
    public IConfiguration? PackageConfig
    {
        get => _packageConfig;
        set => _packageConfig = value;
    }

    /// <inheritdoc/>
    public void Clear()
    {
        _packageConfig = null;
    }
}
