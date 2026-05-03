// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches all <see cref="IIdentitySource"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// </summary>
public sealed class CompositeIdentitySource : IIdentitySource
{
    private readonly IReadOnlyDictionary<string, Type> _sourceTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISourceEndpointInfo _endpointInfo;

    public CompositeIdentitySource(
        IEnumerable<KeyedIdentitySource> registrations,
        IServiceProvider serviceProvider,
        ISourceEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.SourceType;
        _sourceTypes = dict;
        _serviceProvider = serviceProvider;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private IIdentitySource Resolve()
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ISourceEndpointInfo has no ConnectorType.");

        if (!_sourceTypes.TryGetValue(typeKey, out var sourceType))
            throw new InvalidOperationException(
                $"No IIdentitySource is registered for endpoint type '{typeKey}'. " +
                "Register one with AddIdentitySource(key, implementation).");

        return (IIdentitySource)_serviceProvider.GetRequiredService(sourceType);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var identity in Resolve().EnumerateIdentitiesAsync(projectName, cancellationToken))
            yield return identity;
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IIdentitySource"/>.</summary>
public sealed record KeyedIdentitySource(string Key, Type SourceType);
