using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches <see cref="IIdentitySource.EnumerateIdentitiesAsync"/> to the concrete
/// implementation registered for the endpoint's <c>Type</c> discriminator.
/// </summary>
public sealed class CompositeIdentitySource : IIdentitySource
{
    private readonly IReadOnlyDictionary<string, Type> _sourceTypes;
    private readonly IServiceProvider _serviceProvider;

    public CompositeIdentitySource(
        IEnumerable<KeyedIdentitySource> registrations,
        IServiceProvider serviceProvider)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.SourceType;
        _sourceTypes = dict;
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        MigrationEndpointOptions endpoint,
        string projectName,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var typeKey = endpoint.Type;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new ArgumentException("Endpoint has no Type discriminator.", nameof(endpoint));

        if (!_sourceTypes.TryGetValue(typeKey, out var sourceType))
            throw new InvalidOperationException(
                $"No IIdentitySource is registered for endpoint type '{typeKey}'. " +
                "Register one with AddIdentitySource(key, implementation).");

        var source = (IIdentitySource)_serviceProvider.GetRequiredService(sourceType);
        await foreach (var identity in source.EnumerateIdentitiesAsync(endpoint, projectName, cancellationToken))
            yield return identity;
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IIdentitySource"/>.</summary>
public sealed record KeyedIdentitySource(string Key, Type SourceType);
