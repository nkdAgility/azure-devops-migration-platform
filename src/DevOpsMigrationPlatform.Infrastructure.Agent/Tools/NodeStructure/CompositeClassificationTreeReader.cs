#if !NET481
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeStructure;

/// <summary>
/// Dispatches <see cref="IClassificationTreeReader"/> calls to the concrete reader
/// registered for the endpoint's <c>Type</c> discriminator.
/// </summary>
public sealed class CompositeClassificationTreeReader : IClassificationTreeReader
{
    private readonly IReadOnlyDictionary<string, IClassificationTreeReader> _readers;

    public CompositeClassificationTreeReader(IEnumerable<KeyedClassificationTreeReader> registrations)
    {
        var dict = new Dictionary<string, IClassificationTreeReader>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Reader;
        _readers = dict;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
        MigrationEndpointOptions endpoint,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = GetReader(endpoint);
        await foreach (var node in reader.EnumerateAreaNodesAsync(endpoint, ct).ConfigureAwait(false))
            yield return node;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        MigrationEndpointOptions endpoint,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var reader = GetReader(endpoint);
        await foreach (var entry in reader.EnumerateIterationNodesAsync(endpoint, ct).ConfigureAwait(false))
            yield return entry;
    }

    private IClassificationTreeReader GetReader(MigrationEndpointOptions endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var typeKey = endpoint.Type;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new ArgumentException("Endpoint has no Type discriminator.", nameof(endpoint));

        if (!_readers.TryGetValue(typeKey, out var reader))
            throw new InvalidOperationException(
                $"No IClassificationTreeReader is registered for endpoint type '{typeKey}'. " +
                "Register one with AddClassificationTreeReader(key, reader).");

        return reader;
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IClassificationTreeReader"/>.</summary>
public sealed record KeyedClassificationTreeReader(string Key, IClassificationTreeReader Reader);
#endif
