#if !NET481
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.DependencyInjection;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Connectors;

/// <summary>
/// Dispatches all <see cref="ITeamTarget"/> calls to the concrete implementation
/// registered for the endpoint's <c>Type</c> discriminator.
/// </summary>
public sealed class CompositeTeamTarget : ITeamTarget
{
    private readonly IReadOnlyDictionary<string, ITeamTarget> _targets;

    public CompositeTeamTarget(IEnumerable<KeyedTeamTarget> registrations)
    {
        var dict = new Dictionary<string, ITeamTarget>(StringComparer.OrdinalIgnoreCase);
        foreach (var reg in registrations)
            dict[reg.Key] = reg.Target;
        _targets = dict;
    }

    private ITeamTarget Resolve(MigrationEndpointOptions endpoint)
    {
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var typeKey = endpoint.Type;
        if (string.IsNullOrWhiteSpace(typeKey))
            throw new ArgumentException("Endpoint has no Type discriminator.", nameof(endpoint));

        if (!_targets.TryGetValue(typeKey, out var target))
            throw new InvalidOperationException(
                $"No ITeamTarget is registered for endpoint type '{typeKey}'. " +
                "Register one with AddTeamTarget(key, implementation).");

        return target;
    }

    /// <inheritdoc/>
    public Task<string> CreateOrUpdateTeamAsync(
        MigrationEndpointOptions endpoint, string projectName, TeamDefinition team, CancellationToken ct)
        => Resolve(endpoint).CreateOrUpdateTeamAsync(endpoint, projectName, team, ct);

    /// <inheritdoc/>
    public Task SetTeamSettingsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamSettings settings, CancellationToken ct)
        => Resolve(endpoint).SetTeamSettingsAsync(endpoint, projectName, teamId, settings, ct);

    /// <inheritdoc/>
    public Task AssignIterationAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamIteration iteration, CancellationToken ct)
        => Resolve(endpoint).AssignIterationAsync(endpoint, projectName, teamId, iteration, ct);

    /// <inheritdoc/>
    public Task AddMemberAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamMember member, CancellationToken ct)
        => Resolve(endpoint).AddMemberAsync(endpoint, projectName, teamId, member, ct);

    /// <inheritdoc/>
    public Task SetCapacityAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, string iterationId, TeamCapacityEntry[] capacity, CancellationToken ct)
        => Resolve(endpoint).SetCapacityAsync(endpoint, projectName, teamId, iterationId, capacity, ct);

    /// <inheritdoc/>
    public Task SetAreaPathsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamAreaPaths areaPaths, CancellationToken ct)
        => Resolve(endpoint).SetAreaPathsAsync(endpoint, projectName, teamId, areaPaths, ct);
}

/// <summary>Registration descriptor for a keyed <see cref="ITeamTarget"/>.</summary>
public sealed record KeyedTeamTarget(string Key, ITeamTarget Target);
#endif
