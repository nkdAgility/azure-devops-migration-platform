#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

/// <summary>
/// Dispatches <see cref="IWorkItemRevisionSourceFactory.CreateAsync"/> to the concrete
/// factory registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// Inner factories are resolved lazily from <see cref="IServiceProvider"/> so that
/// scoped factories (e.g. Azure DevOps) receive the correct per-scope dependencies.
/// </summary>
public sealed class CompositeWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly IReadOnlyDictionary<string, Type> _factoryTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISourceEndpointInfo _endpointInfo;

    public CompositeWorkItemRevisionSourceFactory(
        IEnumerable<KeyedWorkItemRevisionSourceFactory> registrations,
        IServiceProvider serviceProvider,
        ISourceEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.FactoryType;
        _factoryTypes = dict;
        _serviceProvider = serviceProvider;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(CancellationToken cancellationToken)
    {
        var typeKey = _endpointInfo.ConnectorType;

        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ISourceEndpointInfo has no ConnectorType.");

        if (!_factoryTypes.TryGetValue(typeKey, out var factoryType))
            throw new InvalidOperationException(
                $"No IWorkItemRevisionSourceFactory is registered for endpoint type '{typeKey}'. " +
                "Register one with AddRevisionSourceFactory(key, factory).");

        var factory = (IWorkItemRevisionSourceFactory)_serviceProvider.GetRequiredService(factoryType);
        return factory.CreateAsync(cancellationToken);
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IWorkItemRevisionSourceFactory"/>.</summary>
public sealed record KeyedWorkItemRevisionSourceFactory(string Key, Type FactoryType);
#endif
