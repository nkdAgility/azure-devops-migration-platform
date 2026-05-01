using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// DEPRECATED: This composite dispatcher is incompatible with the new IOptions-based DI model.
/// Connector-specific implementations should be registered directly via their Add*Services() extensions.
/// </summary>
[Obsolete("Register concrete implementations directly in connector service extensions.")]
public sealed class CompositeIdentitySource : IIdentitySource
{
    private readonly IServiceProvider _serviceProvider;

    public CompositeIdentitySource(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(
        string projectName,
        CancellationToken cancellationToken)
    {
        throw new NotSupportedException(
            "CompositeIdentitySource is deprecated. Register concrete IIdentitySource implementations directly in connector service extensions.");
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IIdentitySource"/>.</summary>
public sealed record KeyedIdentitySource(string Key, Type SourceType);
