// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Dispatches <see cref="IWorkItemResolutionStrategyFactory.CreateAsync"/> to the concrete
/// factory whose <see cref="KeyedWorkItemResolutionStrategyFactory.CanHandle"/> delegate returns <c>true</c>
/// for the given <see cref="IWorkItemImportTarget"/> instance.
/// </summary>
public sealed class CompositeWorkItemResolutionStrategyFactory : IWorkItemResolutionStrategyFactory
{
    private readonly IReadOnlyList<KeyedWorkItemResolutionStrategyFactory> _factories;

    public CompositeWorkItemResolutionStrategyFactory(
        IEnumerable<KeyedWorkItemResolutionStrategyFactory> registrations)
    {
        _factories = new List<KeyedWorkItemResolutionStrategyFactory>(registrations);
    }

    /// <inheritdoc/>
    public Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemImportTarget target,
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        if (target is null)
            throw new ArgumentNullException(nameof(target));

        foreach (var reg in _factories)
        {
            if (reg.CanHandle(target))
                return reg.Factory.CreateAsync(options, target, endpoint, ct);
        }

        throw new InvalidOperationException(
            $"No IWorkItemResolutionStrategyFactory is registered that can handle target type '{target.GetType().Name}'.");
    }
}

/// <summary>Registration wrapper for a <see cref="IWorkItemResolutionStrategyFactory"/> with a target-type predicate.</summary>
public sealed record KeyedWorkItemResolutionStrategyFactory(
    Func<IWorkItemImportTarget, bool> CanHandle,
    IWorkItemResolutionStrategyFactory Factory);
