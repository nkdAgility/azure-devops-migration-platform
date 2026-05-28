// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// <see cref="IWorkItemResolutionStrategyFactory"/> for the Simulated connector.
/// Uses <see cref="NullResolutionStrategy"/> when no explicit strategy is configured.
/// Simulated targets currently do not support explicit provenance lookup strategies.
/// </summary>
public sealed class SimulatedResolutionStrategyFactory : IWorkItemResolutionStrategyFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemTarget target,
        ITargetEndpointInfo endpoint,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.Strategy))
            return Task.FromResult<IWorkItemResolutionStrategy>(new NullResolutionStrategy());

        throw new InvalidOperationException(
            $"WorkItemResolutionStrategy.strategy '{options.Strategy}' is not supported for Simulated targets. " +
            "Leave strategy empty to use idmap-only resolution.");
    }
}
