// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Import;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Dispatches <see cref="IWorkItemTypeReadinessTargetFactory.CreateAsync"/> to the concrete
/// factory registered for the endpoint's <c>Type</c> discriminator.
/// </summary>
public sealed class CompositeWorkItemTypeReadinessTargetFactory : IWorkItemTypeReadinessTargetFactory
{
    private readonly IReadOnlyDictionary<string, IWorkItemTypeReadinessTargetFactory> _factories;
    private readonly ITargetEndpointInfo _endpointInfo;

    public CompositeWorkItemTypeReadinessTargetFactory(
        IEnumerable<KeyedWorkItemTypeReadinessTargetFactory> registrations,
        ITargetEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, IWorkItemTypeReadinessTargetFactory>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
        {
            dict[reg.Key] = reg.Factory;
        }

        _factories = dict;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc />
    public Task<IWorkItemTypeReadinessTarget> CreateAsync(CancellationToken ct)
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
        {
            throw new InvalidOperationException("ITargetEndpointInfo has no ConnectorType.");
        }

        if (!_factories.TryGetValue(typeKey, out var factory))
        {
            throw new InvalidOperationException(
                $"No IWorkItemTypeReadinessTargetFactory is registered for endpoint type '{typeKey}'. " +
                "Register one with AddWorkItemTypeReadinessTargetFactory(typeKey, factory).");
        }

        return factory.CreateAsync(ct);
    }
}

/// <summary>Registration wrapper for a keyed <see cref="IWorkItemTypeReadinessTargetFactory"/>.</summary>
public sealed record KeyedWorkItemTypeReadinessTargetFactory(
    string Key,
    IWorkItemTypeReadinessTargetFactory Factory);
#endif
