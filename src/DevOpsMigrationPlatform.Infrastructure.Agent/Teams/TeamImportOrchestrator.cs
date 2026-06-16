// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Orchestrates per-team import: creates or updates the team on the target system and
/// returns the resolved target team ID. All capability sub-concerns (settings, iterations,
/// members, capacity, area paths) are now delegated to registered
/// <see cref="Abstractions.Agent.IModuleExtension"/> implementations.
/// </summary>
public sealed class TeamImportOrchestrator
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly ITeamTarget _teamTarget;
    private readonly IIdentityTranslationTool? _identityTranslationTool;
    private readonly INodeTranslationTool? _nodeTranslationTool;
    private readonly ILogger<TeamImportOrchestrator> _logger;
    private readonly ITargetEndpointInfo _endpointInfo;

    public TeamImportOrchestrator(
        ITeamTarget teamTarget,
        ILogger<TeamImportOrchestrator> logger,
        ITargetEndpointInfo endpointInfo,
        INodeTranslationTool? nodeTranslationTool = null,
        IIdentityTranslationTool? identityTranslationTool = null)
    {
        _teamTarget = teamTarget ?? throw new ArgumentNullException(nameof(teamTarget));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
        _nodeTranslationTool = nodeTranslationTool;
        _identityTranslationTool = identityTranslationTool;
    }

    /// <summary>
    /// Creates or updates a single team on the target system and returns the target team ID.
    /// Capability imports (settings, iterations, members, capacity, area paths) are handled
    /// by the registered <see cref="Abstractions.Agent.IModuleExtension"/> implementations
    /// which are dispatched by <see cref="Modules.TeamsOrchestrator"/> after this call.
    /// </summary>
    /// <param name="projectName">Target project name.</param>
    /// <param name="sourceProjectName">Source project name — used for path translation.</param>
    /// <param name="teamPackage">The team package to import.</param>
    /// <param name="extensions">Extension toggles (retained for backward compatibility; not used for capability dispatch).</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<string> ImportTeamAsync(
        string projectName,
        string sourceProjectName,
        TeamPackage teamPackage,
        TeamsModuleExtensionsOptions extensions,
        CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("teams.import.team");
        activity?.SetTag("team.name", teamPackage.Definition.Name);

        // Log a warning if this is the default team — ITeamTarget has no explicit default team assignment API.
        if (teamPackage.Definition.IsDefault)
        {
            _logger.LogWarning(
                "[Teams] Default team '{Name}' detected — target API does not support explicit default team assignment. " +
                "Ensure the target project's default team matches the source.",
                teamPackage.Definition.Name);
        }

        // Create or update team — returns the target team ID that extensions will use.
        // TODO T051+: ITeamTarget still requires MigrationEndpointOptions parameter
        var targetTeamId = await _teamTarget.CreateOrUpdateTeamAsync(
            null!, projectName, teamPackage.Definition, ct).ConfigureAwait(false);

        return targetTeamId;
    }
}
