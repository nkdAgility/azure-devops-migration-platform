// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches all <see cref="ITeamSource"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator (resolved from DI).
/// </summary>
public sealed class CompositeTeamSource : ITeamSource
{
    private readonly IReadOnlyDictionary<string, Type> _sourceTypes;
    private readonly IServiceProvider _serviceProvider;
    private readonly ISourceEndpointInfo _endpointInfo;

    public CompositeTeamSource(
        IEnumerable<KeyedTeamSource> registrations,
        IServiceProvider serviceProvider,
        ISourceEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.SourceType;
        _sourceTypes = dict;
        _serviceProvider = serviceProvider;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private ITeamSource Resolve()
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ISourceEndpointInfo has no ConnectorType.");

        if (!_sourceTypes.TryGetValue(typeKey, out var sourceType))
            throw new InvalidOperationException(
                $"No ITeamSource is registered for endpoint type '{typeKey}'. " +
                "Register one with AddTeamSource(key, implementation).");

        return (ITeamSource)_serviceProvider.GetRequiredService(sourceType);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var team in Resolve().EnumerateTeamsAsync(projectName, ct))
            yield return team;
    }

    /// <inheritdoc/>
    public Task<TeamSettings?> GetTeamSettingsAsync(
        string projectName, string teamId, CancellationToken ct)
        => Resolve().GetTeamSettingsAsync(projectName, teamId, ct);

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var iteration in Resolve().GetTeamIterationsAsync(projectName, teamId, ct))
            yield return iteration;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (var member in Resolve().GetTeamMembersAsync(projectName, teamId, ct))
            yield return member;
    }

    /// <inheritdoc/>
    public Task<TeamCapacityEntry[]> GetTeamCapacityAsync(
        string projectName, string teamId, string iterationId, CancellationToken ct)
        => Resolve().GetTeamCapacityAsync(projectName, teamId, iterationId, ct);

    /// <inheritdoc/>
    public Task<TeamAreaPaths?> GetTeamAreaPathsAsync(
        string projectName, string teamId, CancellationToken ct)
        => Resolve().GetTeamAreaPathsAsync(projectName, teamId, ct);
}

/// <summary>Registration descriptor for a keyed <see cref="ITeamSource"/>.</summary>
public sealed record KeyedTeamSource(string Key, Type SourceType);
