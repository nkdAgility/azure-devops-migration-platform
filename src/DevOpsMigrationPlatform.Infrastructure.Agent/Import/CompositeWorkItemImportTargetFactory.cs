// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Dispatches <see cref="IWorkItemImportTargetFactory.CreateAsync"/> to the concrete
/// factory registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// Connector assemblies register their factories via
/// <see cref="IServiceCollection.AddKeyedSingleton{TService}"/> or by calling
/// <c>AddImportTargetFactory</c>.
/// </summary>
public sealed class CompositeWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly IReadOnlyDictionary<string, IWorkItemImportTargetFactory> _factories;
    private readonly ITargetEndpointInfo _endpointInfo;

    public CompositeWorkItemImportTargetFactory(
        IEnumerable<KeyedWorkItemImportTargetFactory> registrations,
        ITargetEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, IWorkItemImportTargetFactory>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Factory;
        _factories = dict;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public Task<IWorkItemImportTarget> CreateAsync(CancellationToken ct)
    {
        var typeKey = _endpointInfo.ConnectorType;

        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ITargetEndpointInfo has no ConnectorType.");

        if (!_factories.TryGetValue(typeKey, out var factory))
            throw new InvalidOperationException(
                $"No IWorkItemImportTargetFactory is registered for endpoint type '{typeKey}'. " +
                "Register one with AddImportTargetFactory(key, factory).");

        return factory.CreateAsync(ct);
    }
}

/// <summary>Registration wrapper for a keyed <see cref="IWorkItemImportTargetFactory"/>.</summary>
public sealed record KeyedWorkItemImportTargetFactory(string Key, IWorkItemImportTargetFactory Factory);
#endif
