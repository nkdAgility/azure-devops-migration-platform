// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.ProjectLifecycle;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.ProjectLifecycle;

public sealed class ProjectProcessService : IProjectProcessService
{
    private readonly IReadOnlyDictionary<string, Type> _providerTypes;
    private readonly IServiceProvider _serviceProvider;

    public ProjectProcessService(
        IEnumerable<KeyedProjectProcessProvider> registrations,
        IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var registration in registrations)
            dict[registration.Key] = registration.ServiceType;
        _providerTypes = dict;
    }

    public Task<string> ResolveProcessTypeIdAsync(ProjectLifecycleContext context, CancellationToken cancellationToken)
    {
        if (context is null)
            throw new ArgumentNullException(nameof(context));
        if (string.IsNullOrWhiteSpace(context.ConnectorType))
            throw new InvalidOperationException("Connector type is required for process resolution.");

        if (!_providerTypes.TryGetValue(context.ConnectorType, out var serviceType))
            throw new InvalidOperationException(
                $"No project process provider is registered for connector '{context.ConnectorType}'.");

        var provider = (IProjectProcessProvider)_serviceProvider.GetRequiredService(serviceType);
        return provider.ResolveProcessTypeIdAsync(context, cancellationToken);
    }
}

public sealed record KeyedProjectProcessProvider(string Key, Type ServiceType);
