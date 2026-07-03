// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using DevOpsMigrationPlatform.Abstractions.ProjectLifecycle;
using DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.ProjectLifecycle;

public sealed class SimulatedProjectProcessProvider : IProjectProcessProvider
{
    public Task<string> ResolveProcessTypeIdAsync(ProjectLifecycleContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(context.ProcessName))
            return Task.FromResult(KnownProcessIds.Agile);

        if (KnownProcessIds.TryResolve(context.ProcessName, out var processTypeId))
            return Task.FromResult(processTypeId);

        throw new InvalidOperationException(
            $"Simulated connector does not recognise process '{context.ProcessName}'.");
    }
}
