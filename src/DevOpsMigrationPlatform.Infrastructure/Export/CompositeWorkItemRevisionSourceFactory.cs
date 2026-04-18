#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Export;

/// <summary>
/// Dispatches <see cref="IWorkItemRevisionSourceFactory.CreateAsync"/> to the concrete
/// factory registered for the endpoint's <c>Type</c> discriminator.
/// Inner factories are resolved lazily from <see cref="IServiceProvider"/> so that
/// scoped factories (e.g. Azure DevOps) receive the correct per-scope dependencies.
/// </summary>
public sealed class CompositeWorkItemRevisionSourceFactory : IWorkItemRevisionSourceFactory
{
    private readonly IReadOnlyDictionary<string, Type> _factoryTypes;
    private readonly IServiceProvider _serviceProvider;

    public CompositeWorkItemRevisionSourceFactory(
        IEnumerable<KeyedWorkItemRevisionSourceFactory> registrations,
        IServiceProvider serviceProvider)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.FactoryType;
        _factoryTypes = dict;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public Task<IWorkItemRevisionSource> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken cancellationToken)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var typeKey = endpoint is JobEndpointMigrationOptions je
            ? je.JobEndpoint.Type
            : endpoint.Type;

        if (string.IsNullOrWhiteSpace(typeKey))
            throw new ArgumentException("Endpoint has no Type discriminator.", nameof(endpoint));

        if (!_factoryTypes.TryGetValue(typeKey, out var factoryType))
            throw new InvalidOperationException(
                $"No IWorkItemRevisionSourceFactory is registered for endpoint type '{typeKey}'. " +
                "Register one with AddRevisionSourceFactory(key, factory).");

        var factory = (IWorkItemRevisionSourceFactory)_serviceProvider.GetRequiredService(factoryType);
        return factory.CreateAsync(endpoint, cancellationToken);
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IWorkItemRevisionSourceFactory"/>.</summary>
public sealed record KeyedWorkItemRevisionSourceFactory(string Key, Type FactoryType);
#endif
