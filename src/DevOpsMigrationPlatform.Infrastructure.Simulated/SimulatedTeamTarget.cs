using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated;

/// <summary>
/// Simulated <see cref="ITeamTarget"/> that stores teams in memory for testing.
/// </summary>
public sealed class SimulatedTeamTarget : ITeamTarget
{
    /// <summary>All created/updated teams: slug → definition.</summary>
    public Dictionary<string, TeamDefinition> Teams { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Team settings applied: teamId → settings.</summary>
    public Dictionary<string, TeamSettings> TeamSettings { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Iteration assignments: teamId → list of iterations.</summary>
    public Dictionary<string, List<TeamIteration>> Iterations { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Members added: teamId → list of members.</summary>
    public Dictionary<string, List<TeamMember>> Members { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Capacity set: teamId/iterationId → entries.</summary>
    public Dictionary<string, TeamCapacityEntry[]> Capacity { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Area paths set: teamId → area paths.</summary>
    public Dictionary<string, TeamAreaPaths> AreaPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

    /// <inheritdoc/>
    public Task<string> CreateOrUpdateTeamAsync(
        MigrationEndpointOptions endpoint, string projectName, TeamDefinition team, CancellationToken ct)
    {
        var targetId = $"target-{team.Name.ToLowerInvariant().Replace(' ', '-')}";
        Teams[targetId] = team;
        return Task.FromResult(targetId);
    }

    /// <inheritdoc/>
    public Task SetTeamSettingsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamSettings settings, CancellationToken ct)
    {
        TeamSettings[teamId] = settings;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AssignIterationAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamIteration iteration, CancellationToken ct)
    {
        if (!Iterations.ContainsKey(teamId))
            Iterations[teamId] = new List<TeamIteration>();
        Iterations[teamId].Add(iteration);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task AddMemberAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamMember member, CancellationToken ct)
    {
        if (!Members.ContainsKey(teamId))
            Members[teamId] = new List<TeamMember>();
        Members[teamId].Add(member);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetCapacityAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, string iterationId, TeamCapacityEntry[] capacity, CancellationToken ct)
    {
        Capacity[$"{teamId}/{iterationId}"] = capacity;
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task SetAreaPathsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, TeamAreaPaths areaPaths, CancellationToken ct)
    {
        AreaPaths[teamId] = areaPaths;
        return Task.CompletedTask;
    }
}
