using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated;

/// <summary>
/// Simulated <see cref="ITeamSource"/> that returns deterministic teams for testing.
/// </summary>
public sealed class SimulatedTeamSource : ITeamSource
{
    private static readonly TeamDefinition[] s_teams = new[]
    {
        new TeamDefinition("team-alpha-id", "Alpha Team", "Alpha development team", true),
        new TeamDefinition("team-beta-id", "Beta Team", "Beta development team", false),
    };

    private static readonly TeamMember[] s_alphaMembers = new[]
    {
        new TeamMember("desc-alice", "Alice Smith", "alice@simulated.example.com", true),
        new TeamMember("desc-bob", "Bob Jones", "bob@simulated.example.com", false),
    };

    private static readonly TeamMember[] s_betaMembers = new[]
    {
        new TeamMember("desc-carol", "Carol Taylor", "carol@simulated.example.com", true),
    };

    private static readonly TeamIteration s_sprint1 = new(
        "iteration-1", "ProjectA\\Sprint 1", "Sprint 1",
        new System.DateTimeOffset(2024, 1, 1, 0, 0, 0, System.TimeSpan.Zero),
        new System.DateTimeOffset(2024, 1, 14, 0, 0, 0, System.TimeSpan.Zero),
        false, false);

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(
        MigrationEndpointOptions endpoint, string projectName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var team in s_teams)
        {
            ct.ThrowIfCancellationRequested();
            yield return team;
            await Task.Yield();
        }
    }

    /// <inheritdoc/>
    public Task<TeamSettings?> GetTeamSettingsAsync(MigrationEndpointOptions endpoint, string projectName, string teamId, CancellationToken ct)
    {
        TeamSettings? settings = new TeamSettings("Backlog", false, new[] { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday" });
        return Task.FromResult<TeamSettings?>(settings);
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        yield return s_sprint1;
        await Task.Yield();
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var members = teamId == "team-alpha-id" ? s_alphaMembers : s_betaMembers;
        foreach (var member in members)
        {
            ct.ThrowIfCancellationRequested();
            yield return member;
            await Task.Yield();
        }
    }

    /// <inheritdoc/>
    public Task<TeamCapacityEntry[]> GetTeamCapacityAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, string iterationId, CancellationToken ct)
    {
        TeamCapacityEntry[] capacity = new[]
        {
            new TeamCapacityEntry("desc-alice", "Alice Smith",
                new[] { new ActivityEntry("Development", 6.0) }, 0)
        };
        return Task.FromResult(capacity);
    }

    /// <inheritdoc/>
    public Task<TeamAreaPaths?> GetTeamAreaPathsAsync(
        MigrationEndpointOptions endpoint, string projectName, string teamId, CancellationToken ct)
    {
        TeamAreaPaths? areaPaths = new TeamAreaPaths(
            projectName,
            new[] { projectName, $"{projectName}\\Sub" });
        return Task.FromResult<TeamAreaPaths?>(areaPaths);
    }
}
