// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Organisations;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches all <see cref="IWorkItemDiscoveryService"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// </summary>
public sealed class CompositeWorkItemDiscoveryService : IWorkItemDiscoveryService
{
    private readonly IReadOnlyDictionary<string, Type> _serviceTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISourceEndpointInfo _endpointInfo;

    public CompositeWorkItemDiscoveryService(
        IEnumerable<KeyedWorkItemDiscoveryService> registrations,
        IServiceProvider serviceProvider,
        ISourceEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.ServiceType;
        _serviceTypes = dict;
        _serviceProvider = serviceProvider;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private IWorkItemDiscoveryService Resolve(string? typeOverride = null)
    {
        // Prefer the explicit type from the endpoint (e.g. when called from Dependencies job which
        // has no Source section in config). Fall back to ISourceEndpointInfo for Export/Import jobs.
        var typeKey = !string.IsNullOrWhiteSpace(typeOverride) ? typeOverride : _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ISourceEndpointInfo has no ConnectorType.");

        var resolvedTypeKey = typeKey!;
        if (!_serviceTypes.TryGetValue(resolvedTypeKey, out var serviceType))
            throw new InvalidOperationException(
                $"No IWorkItemDiscoveryService is registered for endpoint type '{resolvedTypeKey}'. " +
                "Register one with AddWorkItemDiscoveryService(key, implementation).");

        return (IWorkItemDiscoveryService)_serviceProvider.GetRequiredService(serviceType);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ProjectDiscoverySummary> DiscoverWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        WorkItemFetchScope? scope = null,
        IProgress<int>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var summary in Resolve(endpoint.Type).DiscoverWorkItemsAsync(endpoint, project, scope, progress, cancellationToken))
            yield return summary;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<ProjectDiscoverySummary> CountWorkItemsAsync(
        OrganisationEndpoint endpoint,
        string project,
        string? baseQuery = null,
        IProgress<int>? progress = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var summary in Resolve(endpoint.Type).CountWorkItemsAsync(endpoint, project, baseQuery, progress, cancellationToken))
            yield return summary;
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IWorkItemDiscoveryService"/>.</summary>
public sealed record KeyedWorkItemDiscoveryService(string Key, Type ServiceType);
