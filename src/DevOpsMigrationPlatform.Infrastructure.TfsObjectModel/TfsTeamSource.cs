// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;
using Microsoft.TeamFoundation.Client;
using Microsoft.TeamFoundation.Framework.Client;
using Microsoft.TeamFoundation.Framework.Common;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// TFS Object Model implementation of <see cref="ITeamSource"/>.
/// Uses <see cref="TfsTeamService"/> to enumerate team definitions, settings,
/// iterations, members, capacity, and area paths. Export-only.
/// </summary>
public sealed class TfsTeamSource : ITeamSource
{
    private readonly TfsTeamProjectCollection _collection;
    private readonly ILogger<TfsTeamSource> _logger;

    public TfsTeamSource(
        TfsTeamProjectCollection collection,
        ILogger<TfsTeamSource> logger)
    {
        _collection = collection ?? throw new ArgumentNullException(nameof(collection));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        _logger.LogInformation("[Teams][TFS] Enumerating teams for project '{Project}'.", projectName);

        var teamService = _collection.GetService<TfsTeamService>();
        TeamFoundationTeam[] teams;
        try
        {
            teams = teamService.QueryTeams(projectName).ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[Teams][TFS] Failed to query teams for project '{Project}'.", projectName);
            yield break;
        }

        foreach (var team in teams)
        {
            ct.ThrowIfCancellationRequested();
            yield return new TeamDefinition(
                Id: team.Identity.TeamFoundationId.ToString(),
                Name: team.Name,
                Description: team.Description ?? string.Empty,
                IsDefault: false); // TFS doesn't expose a default flag via this API
        }
    }

    /// <inheritdoc/>
    public async Task<TeamSettings?> GetTeamSettingsAsync(
        string projectName,
        string teamId,
        CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // TFS Object Model doesn't expose team settings (working days / bugs behaviour)
        // via the TeamService directly. Return a sensible default.
        _logger.LogDebug(
            "[Teams][TFS] Team settings not available via TFS Object Model for team '{TeamId}' — returning defaults.",
            teamId);

        return new TeamSettings(
            BacklogNavigationLevel: "Requirements",
            BugsBehavior: false,
            WorkingDays: new[] { "monday", "tuesday", "wednesday", "thursday", "friday" });
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        string projectName,
        string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        _logger.LogDebug(
            "[Teams][TFS] Iteration enumeration not available via TFS Object Model for team '{TeamId}'.", teamId);

        // TFS iteration access requires Work REST API or WIT client; the Object Model
        // exposes iteration nodes via ICommonStructureService but not team-specific assignments.
        // Return empty — NodesModule exports the iteration tree separately.
        yield break;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        string projectName,
        string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        _logger.LogDebug(
            "[Teams][TFS] Enumerating members for team '{TeamId}' in project '{Project}'.",
            teamId, projectName);

        var teamService = _collection.GetService<TfsTeamService>();
        TeamFoundationTeam? team = null;
        try
        {
            // QueryTeams returns all teams; find the one matching the requested ID.
            if (Guid.TryParse(teamId, out var teamGuid))
            {
                team = teamService.QueryTeams(projectName)
                    .FirstOrDefault(t => t.Identity.TeamFoundationId == teamGuid);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams][TFS] Could not query teams for project '{Project}' — no members yielded.", projectName);
            yield break;
        }

        if (team is null)
        {
            _logger.LogWarning("[Teams][TFS] Team '{TeamId}' not found — no members yielded.", teamId);
            yield break;
        }

        // ReadTeam's second parameter is a List<string> of property names to load, or null for all.
        TeamFoundationTeam? expandedTeam = null;
        try
        {
            expandedTeam = teamService.ReadTeam(team.Identity.Descriptor, (List<string>?)null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams][TFS] Could not expand members for team '{TeamId}' — no members yielded.", teamId);
            yield break;
        }

        if (expandedTeam is null)
            yield break;

        // GetMembers enumerates direct members of the team.
        foreach (var member in expandedTeam.GetMembers(_collection, MembershipQuery.Direct) ?? Enumerable.Empty<TeamFoundationIdentity>())
        {
            ct.ThrowIfCancellationRequested();
            yield return new TeamMember(
                Descriptor: member.Descriptor.Identifier,
                DisplayName: member.DisplayName ?? member.UniqueName ?? string.Empty,
                UniqueName: member.UniqueName ?? string.Empty,
                IsAdmin: false);
        }
    }

    /// <inheritdoc/>
    public async Task<TeamCapacityEntry[]> GetTeamCapacityAsync(
        string projectName,
        string teamId,
        string iterationId,
        CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // Capacity data requires the Work REST API which is not available through
        // the TFS Object Model alone.
        _logger.LogDebug(
            "[Teams][TFS] Capacity data not available via TFS Object Model for team '{TeamId}'/iteration '{IterationId}'.",
            teamId, iterationId);

        return Array.Empty<TeamCapacityEntry>();
    }

    /// <inheritdoc/>
    public async Task<TeamAreaPaths?> GetTeamAreaPathsAsync(
        string projectName,
        string teamId,
        CancellationToken ct)
    {
        await Task.CompletedTask.ConfigureAwait(false);

        // Area path assignments require the Work REST API. Return empty so NodeTranslation
        // export (which captures the full tree) remains the authoritative source.
        _logger.LogDebug(
            "[Teams][TFS] Area path assignments not available via TFS Object Model for team '{TeamId}'.", teamId);

        return null;
    }
}
