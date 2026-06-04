// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches all <see cref="IIdentityAdapter"/> calls to the concrete implementation
/// registered for the <b>target</b> endpoint's connector type (resolved from DI). The
/// identity adapter queries the live target tenant during the Prepare phase, so dispatch
/// is keyed on <see cref="ITargetEndpointInfo.ConnectorType"/>.
/// </summary>
public sealed class CompositeIdentityAdapter : IIdentityAdapter
{
    private readonly IReadOnlyDictionary<string, Type> _adapterTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly ITargetEndpointInfo _endpointInfo;

    public CompositeIdentityAdapter(
        IEnumerable<KeyedIdentityAdapter> registrations,
        IServiceProvider serviceProvider,
        ITargetEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.AdapterType;
        _adapterTypes = dict;
        _serviceProvider = serviceProvider;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private IIdentityAdapter Resolve()
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ITargetEndpointInfo has no ConnectorType.");

        if (!_adapterTypes.TryGetValue(typeKey, out var adapterType))
            throw new InvalidOperationException(
                $"No IIdentityAdapter is registered for target endpoint type '{typeKey}'. " +
                "Register one with AddIdentityAdapter<T>(key).");

        return (IIdentityAdapter)_serviceProvider.GetRequiredService(adapterType);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByUpnAsync(string upn, string projectName, CancellationToken ct)
        => Resolve().FindByUpnAsync(upn, projectName, ct);

    /// <inheritdoc/>
    public Task<IReadOnlyList<IdentityCandidate>> FindByDisplayNameAsync(string displayName, string projectName, CancellationToken ct)
        => Resolve().FindByDisplayNameAsync(displayName, projectName, ct);
}

/// <summary>Registration descriptor for a keyed <see cref="IIdentityAdapter"/>.</summary>
public sealed record KeyedIdentityAdapter(string Key, Type AdapterType);
