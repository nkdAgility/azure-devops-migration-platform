#if !NET481
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Dispatches all <see cref="IClassificationTreeReader"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// </summary>
public sealed class CompositeClassificationTreeReader : IClassificationTreeReader
{
    private readonly IReadOnlyDictionary<string, IClassificationTreeReader> _readers;

    public CompositeClassificationTreeReader(
        IEnumerable<KeyedClassificationTreeReader> registrations,
        ISourceEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, IClassificationTreeReader>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Reader;
        _readers = dict;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private readonly ISourceEndpointInfo _endpointInfo;

    private IClassificationTreeReader Resolve()
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ISourceEndpointInfo has no ConnectorType.");

        if (!_readers.TryGetValue(typeKey, out var reader))
            throw new InvalidOperationException(
                $"No IClassificationTreeReader is registered for endpoint type '{typeKey}'. " +
                "Register one with AddClassificationTreeReader(key, implementation).");

        return reader;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<string> EnumerateAreaNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var node in Resolve().EnumerateAreaNodesAsync(ct))
            yield return node;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<IterationNodeEntry> EnumerateIterationNodesAsync(
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var node in Resolve().EnumerateIterationNodesAsync(ct))
            yield return node;
    }
}

/// <summary>Registration descriptor for a keyed <see cref="IClassificationTreeReader"/>.</summary>
public sealed record KeyedClassificationTreeReader(string Key, IClassificationTreeReader Reader);
#endif
