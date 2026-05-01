#if !NET481
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// DEPRECATED: This composite dispatcher is incompatible with the new IOptions-based DI model.
/// Connector-specific implementations should be registered directly via their Add*Services() extensions.
/// </summary>
[Obsolete("Register concrete implementations directly in connector service extensions.")]
public sealed class CompositeClassificationTreeReader : IClassificationTreeReader
{
    public CompositeClassificationTreeReader()
    {
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<string> EnumerateAreaNodesAsync(
        CancellationToken ct)
    {
        throw new NotSupportedException(
            "CompositeClassificationTreeReader is deprecated. Register concrete IClassificationTreeReader implementations directly in connector service extensions.");
    }

    /// <inheritdoc/>
    public IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        CancellationToken ct)
    {
        throw new NotSupportedException(
            "CompositeClassificationTreeReader is deprecated. Register concrete IClassificationTreeReader implementations directly in connector service extensions.");
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IClassificationTreeReader"/>.</summary>
public sealed record KeyedClassificationTreeReader(string Key, IClassificationTreeReader Reader);
#endif
