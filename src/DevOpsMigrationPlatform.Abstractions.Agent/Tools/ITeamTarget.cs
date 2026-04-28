#if !NET481
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Connector abstraction for writing team data to a target system.
/// </summary>
public interface ITeamTarget
{
    /// <summary>Creates or updates a team and returns the target team ID.</summary>
    Task<string> CreateOrUpdateTeamAsync(string projectName, TeamDefinition team, CancellationToken ct);

    /// <summary>Applies team board configuration settings.</summary>
    Task SetTeamSettingsAsync(string projectName, string teamId, TeamSettings settings, CancellationToken ct);

    /// <summary>Assigns an iteration to a team.</summary>
    Task AssignIterationAsync(string projectName, string teamId, TeamIteration iteration, CancellationToken ct);

    /// <summary>Adds a member to a team.</summary>
    Task AddMemberAsync(string projectName, string teamId, TeamMember member, CancellationToken ct);

    /// <summary>Sets per-member capacity for an iteration.</summary>
    Task SetCapacityAsync(string projectName, string teamId, string iterationId, TeamCapacityEntry[] capacity, CancellationToken ct);

    /// <summary>Assigns area paths to a team.</summary>
    Task SetAreaPathsAsync(string projectName, string teamId, TeamAreaPaths areaPaths, CancellationToken ct);
}
#endif
