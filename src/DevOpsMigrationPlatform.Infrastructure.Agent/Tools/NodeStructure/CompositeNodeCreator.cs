#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// Dispatches <see cref="INodeCreator"/> calls to the concrete creator
/// registered for the endpoint's <c>Type</c> discriminator.
/// </summary>
public sealed class CompositeNodeCreator : INodeCreator
{
    private readonly IReadOnlyDictionary<string, INodeCreator> _creators;

    public CompositeNodeCreator(IEnumerable<KeyedNodeCreator> registrations)
    {
        var dict = new Dictionary<string, INodeCreator>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Creator;
        _creators = dict;
    }

    /// <inheritdoc/>
    public Task<bool> NodeExistsAsync(ClassificationNodeType nodeType, string path, MigrationEndpointOptions endpoint, CancellationToken ct)
        => GetCreator(endpoint).NodeExistsAsync(nodeType, path, endpoint, ct);

    /// <inheritdoc/>
    public Task EnsureExistsAsync(ClassificationNodeType nodeType, string path, MigrationEndpointOptions endpoint, CancellationToken ct)
        => GetCreator(endpoint).EnsureExistsAsync(nodeType, path, endpoint, ct);

    /// <inheritdoc/>
    public Task SetIterationDatesAsync(string path, DateTimeOffset? startDate, DateTimeOffset? finishDate, MigrationEndpointOptions endpoint, CancellationToken ct)
        => GetCreator(endpoint).SetIterationDatesAsync(path, startDate, finishDate, endpoint, ct);

    private INodeCreator GetCreator(MigrationEndpointOptions endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var typeKey = endpoint.Type;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new ArgumentException("Endpoint has no Type discriminator.", nameof(endpoint));

        if (!_creators.TryGetValue(typeKey, out var creator))
            throw new InvalidOperationException(
                $"No INodeCreator is registered for endpoint type '{typeKey}'. " +
                "Register one with AddNodeCreator(key, creator).");

        return creator;
    }
}

/// <summary>Registration descriptor for a keyed <see cref="INodeCreator"/>.</summary>
public sealed record KeyedNodeCreator(string Key, INodeCreator Creator);
#endif
