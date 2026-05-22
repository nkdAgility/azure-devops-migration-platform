// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.Import;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// <see cref="IWorkItemResolutionStrategyFactory"/> for the Simulated connector.
/// Always returns a <see cref="NullResolutionStrategy"/> — no external service is needed.
/// </summary>
public sealed class SimulatedResolutionStrategyFactory : IWorkItemResolutionStrategyFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemImportTarget target,
        ITargetEndpointInfo endpoint,
        CancellationToken ct)
    {
        return Task.FromResult<IWorkItemResolutionStrategy>(new NullResolutionStrategy());
    }
}

