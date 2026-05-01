#if !NET481
using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// DEPRECATED: This composite dispatcher is incompatible with the new IOptions-based DI model.
/// Connector-specific implementations should be registered directly via their Add*Services() extensions.
/// </summary>
[Obsolete("Register concrete implementations directly in connector service extensions.")]
public sealed class CompositeNodeCreator : INodeCreator
{
    public CompositeNodeCreator()
    {
    }

    public Task<bool> NodeExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
    {
        throw new NotSupportedException(
            "CompositeNodeCreator is deprecated. Register concrete INodeCreator implementations directly in connector service extensions.");
    }

    public Task EnsureExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
    {
        throw new NotSupportedException(
            "CompositeNodeCreator is deprecated. Register concrete INodeCreator implementations directly in connector service extensions.");
    }

    public Task SetIterationDatesAsync(string path, DateTimeOffset? startDate, DateTimeOffset? finishDate, CancellationToken ct)
    {
        throw new NotSupportedException(
            "CompositeNodeCreator is deprecated. Register concrete INodeCreator implementations directly in connector service extensions.");
    }
}

/// <summary>Registration descriptor for a keyed <see cref="INodeCreator"/>.</summary>
public sealed record KeyedNodeCreator(string Key, INodeCreator Creator);
#endif
