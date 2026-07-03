// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Tools;

/// <summary>
/// Connector abstraction for writing team data to a target system.
/// Implementations resolve their own target endpoint (e.g. via
/// <c>ITargetEndpointInfo</c>); callers address teams by project and team ID only
/// (ADR-0024, EC-L1 — the seam no longer takes an endpoint parameter).
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
