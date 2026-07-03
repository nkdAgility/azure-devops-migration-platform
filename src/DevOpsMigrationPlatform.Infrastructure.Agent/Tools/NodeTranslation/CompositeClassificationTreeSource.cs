// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Dispatches all <see cref="IClassificationTreeSource"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// </summary>
public sealed class CompositeClassificationTreeSource : IClassificationTreeSource
{
    private readonly IReadOnlyDictionary<string, IClassificationTreeSource> _readers;

    public CompositeClassificationTreeSource(
        IEnumerable<KeyedClassificationTreeSource> registrations,
        ISourceEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, IClassificationTreeSource>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Reader;
        _readers = dict;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private readonly ISourceEndpointInfo _endpointInfo;

    private IClassificationTreeSource Resolve()
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ISourceEndpointInfo has no ConnectorType.");

        if (!_readers.TryGetValue(typeKey, out var reader))
            throw new InvalidOperationException(
                $"No IClassificationTreeSource is registered for endpoint type '{typeKey}'. " +
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

    /// <inheritdoc/>
    public Task<int> CountNodesAsync(string project, CancellationToken ct)
        => Resolve().CountNodesAsync(project, ct);
}

/// <summary>Registration descriptor for a keyed <see cref="IClassificationTreeSource"/>.</summary>
public sealed record KeyedClassificationTreeSource(string Key, IClassificationTreeSource Reader);
