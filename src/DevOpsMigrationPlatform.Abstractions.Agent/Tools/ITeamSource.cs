using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Connector abstraction for reading team data from a source system.
/// Endpoint info is resolved from DI.
/// </summary>
public interface ITeamSource
{
    /// <summary>Enumerates all teams in the given project.</summary>
    IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(string projectName, CancellationToken ct);

    /// <summary>Returns team settings or null when unavailable.</summary>
    Task<TeamSettings?> GetTeamSettingsAsync(string projectName, string teamId, CancellationToken ct);

    /// <summary>Enumerates iteration assignments for a team.</summary>
    IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(string projectName, string teamId, CancellationToken ct);

    /// <summary>Enumerates members of a team.</summary>
    IAsyncEnumerable<TeamMember> GetTeamMembersAsync(string projectName, string teamId, CancellationToken ct);

    /// <summary>Returns per-member capacity for a team/iteration pair.</summary>
    Task<TeamCapacityEntry[]> GetTeamCapacityAsync(string projectName, string teamId, string iterationId, CancellationToken ct);

    /// <summary>Returns area path assignments for a team.</summary>
    Task<TeamAreaPaths?> GetTeamAreaPathsAsync(string projectName, string teamId, CancellationToken ct);
}
