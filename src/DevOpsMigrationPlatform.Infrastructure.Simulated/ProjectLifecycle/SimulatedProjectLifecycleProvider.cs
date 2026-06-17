// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;

/// <summary>
/// Deterministic in-memory lifecycle actions for simulated runs.
/// </summary>
public sealed class SimulatedProjectLifecycleProvider : IProjectLifecycleProvider
{
    private static readonly ConcurrentDictionary<string, byte> ActiveProjects = new(StringComparer.OrdinalIgnoreCase);

    public Task CreateActionAsync(ProjectLifecycleContext context, string projectName, CancellationToken cancellationToken)
    {
        if (!ActiveProjects.TryAdd(projectName, 1))
            throw new InvalidOperationException($"Project '{projectName}' already exists for a concurrent run.");

        return Task.CompletedTask;
    }

    public Task TeardownActionAsync(ProjectLifecycleRecord record, CancellationToken cancellationToken)
    {
        if (!ActiveProjects.TryRemove(record.ProjectName, out _))
            throw new InvalidOperationException($"Project '{record.ProjectName}' does not exist in simulated state.");

        return Task.CompletedTask;
    }
}
