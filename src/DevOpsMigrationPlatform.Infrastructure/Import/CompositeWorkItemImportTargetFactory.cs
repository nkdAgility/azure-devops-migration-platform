#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Infrastructure.Modules;

namespace DevOpsMigrationPlatform.Infrastructure.Import;

/// <summary>
/// Dispatches <see cref="IWorkItemImportTargetFactory.CreateAsync"/> to the concrete
/// factory registered for the endpoint's <c>Type</c> discriminator.
/// Connector assemblies register their factories via
/// <see cref="IServiceCollection.AddKeyedSingleton{TService}"/> or by calling
/// <c>AddImportTargetFactory</c>.
/// </summary>
public sealed class CompositeWorkItemImportTargetFactory : IWorkItemImportTargetFactory
{
    private readonly IReadOnlyDictionary<string, IWorkItemImportTargetFactory> _factories;

    public CompositeWorkItemImportTargetFactory(
        IEnumerable<KeyedWorkItemImportTargetFactory> registrations)
    {
        var dict = new Dictionary<string, IWorkItemImportTargetFactory>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Factory;
        _factories = dict;
    }

    /// <inheritdoc/>
    public Task<IWorkItemImportTarget> CreateAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken ct)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var typeKey = endpoint is JobEndpointMigrationOptions je
            ? je.JobEndpoint.Type
            : endpoint.Type;

        if (string.IsNullOrWhiteSpace(typeKey))
            throw new ArgumentException("Endpoint has no Type discriminator.", nameof(endpoint));

        if (!_factories.TryGetValue(typeKey, out var factory))
            throw new InvalidOperationException(
                $"No IWorkItemImportTargetFactory is registered for endpoint type '{typeKey}'. " +
                "Register one with AddImportTargetFactory(key, factory).");

        return factory.CreateAsync(endpoint, ct);
    }
}

/// <summary>Registration wrapper for a keyed <see cref="IWorkItemImportTargetFactory"/>.</summary>
public sealed record KeyedWorkItemImportTargetFactory(string Key, IWorkItemImportTargetFactory Factory);
#endif
