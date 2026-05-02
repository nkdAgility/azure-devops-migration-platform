// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tools.NodeTranslation;

/// <summary>
/// Dispatches all <see cref="INodeCreator"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// </summary>
public sealed class CompositeNodeCreator : INodeCreator
{
    private readonly IReadOnlyDictionary<string, INodeCreator> _creators;
    private readonly ITargetEndpointInfo _endpointInfo;

    public CompositeNodeCreator(
        IEnumerable<KeyedNodeCreator> registrations,
        ITargetEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, INodeCreator>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Creator;
        _creators = dict;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private INodeCreator Resolve()
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ITargetEndpointInfo has no ConnectorType.");

        if (!_creators.TryGetValue(typeKey, out var creator))
            throw new InvalidOperationException(
                $"No INodeCreator is registered for endpoint type '{typeKey}'. " +
                "Register one with AddNodeCreator(key, implementation).");

        return creator;
    }

    public Task<bool> NodeExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
        => Resolve().NodeExistsAsync(nodeType, path, ct);

    public Task EnsureExistsAsync(ClassificationNodeType nodeType, string path, CancellationToken ct)
        => Resolve().EnsureExistsAsync(nodeType, path, ct);

    public Task SetIterationDatesAsync(string path, DateTimeOffset? startDate, DateTimeOffset? finishDate, CancellationToken ct)
        => Resolve().SetIterationDatesAsync(path, startDate, finishDate, ct);
}

/// <summary>Registration descriptor for a keyed <see cref="INodeCreator"/>.</summary>
public sealed record KeyedNodeCreator(string Key, INodeCreator Creator);
#endif
