// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches all <see cref="ITeamTarget"/> calls to the concrete implementation
/// registered for the endpoint's connector type, resolved via <see cref="ITargetEndpointInfo"/>.
/// </summary>
public sealed class CompositeTeamTarget : ITeamTarget
{
    private readonly IReadOnlyDictionary<string, ITeamTarget> _targets;
    private readonly ITargetEndpointInfo _endpointInfo;

    public CompositeTeamTarget(IEnumerable<KeyedTeamTarget> registrations, ITargetEndpointInfo endpointInfo)
    {
        var dict = new Dictionary<string, ITeamTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Target;
        _targets = dict;
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
    }

    private ITeamTarget Resolve()
    {
        var typeKey = _endpointInfo.ConnectorType;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new InvalidOperationException("ITargetEndpointInfo.ConnectorType is null or empty.");

        if (!_targets.TryGetValue(typeKey, out var target))
            throw new InvalidOperationException(
                $"No ITeamTarget is registered for connector type '{typeKey}'. " +
                "Register one with AddTeamTarget(key, implementation).");

        return target;
    }

    /// <inheritdoc/>
    public Task<string> CreateOrUpdateTeamAsync(
        MigrationEndpointOptions endpoint, string projectName, TeamDefinition team, CancellationToken ct)
        => Resolve().CreateOrUpdateTeamAsync(endpoint, projectName, team, ct);

    /// <inheritdoc/>
    public Task SetTeamSettingsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamSettings settings, CancellationToken ct)
        => Resolve().SetTeamSettingsAsync(endpoint, projectName, teamId, settings, ct);

    /// <inheritdoc/>
    public Task AssignIterationAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamIteration iteration, CancellationToken ct)
        => Resolve().AssignIterationAsync(endpoint, projectName, teamId, iteration, ct);

    /// <inheritdoc/>
    public Task AddMemberAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamMember member, CancellationToken ct)
        => Resolve().AddMemberAsync(endpoint, projectName, teamId, member, ct);

    /// <inheritdoc/>
    public Task SetCapacityAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, string iterationId, TeamCapacityEntry[] capacity, CancellationToken ct)
        => Resolve().SetCapacityAsync(endpoint, projectName, teamId, iterationId, capacity, ct);

    /// <inheritdoc/>
    public Task SetAreaPathsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamAreaPaths areaPaths, CancellationToken ct)
        => Resolve().SetAreaPathsAsync(endpoint, projectName, teamId, areaPaths, ct);
}

/// <summary>Registration descriptor for a keyed <see cref="ITeamTarget"/>.</summary>
public sealed record KeyedTeamTarget(string Key, ITeamTarget Target);
