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
    /// <param name="extensions">Extension toggles (governs core capability imports such as team settings).</param>
    /// <param name="ct">Cancellation token.</param>
    public Task<string> ImportTeamAsync(
        string projectName,
        string sourceProjectName,
        TeamPackage teamPackage,
        TeamsDataOptions data,
        CancellationToken ct)
        => ImportTeamAsync(projectName, sourceProjectName, teamPackage, data,
            organisation: null, slug: null, package: null, ct);

    /// <summary>
    /// Creates or updates a single team and applies core capability imports. When
    /// <paramref name="package"/>, <paramref name="organisation"/> and <paramref name="slug"/>
    /// are supplied, team settings are imported from <c>Teams/{slug}/settings.json</c> as part
    /// of the core Teams pipeline (folded from the former TeamSettingsTeamExtension seam —
    /// EC-M3 / ADR-0024).
    /// </summary>
    public async Task<string> ImportTeamAsync(
        string projectName,
        string sourceProjectName,
        TeamPackage teamPackage,
        TeamsDataOptions data,
        string? organisation,
        string? slug,
        DevOpsMigrationPlatform.Abstractions.Storage.IPackageAccess? package,
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
        // The connector resolves its own target endpoint (EC-L1 / ADR-0024).
        var targetTeamId = await _teamTarget.CreateOrUpdateTeamAsync(
            projectName, teamPackage.Definition, ct).ConfigureAwait(false);

        // Core pipeline: import team settings from Teams/{slug}/settings.json (EC-M3).
        if (data.TeamSettings && package is not null && organisation is not null && slug is not null)
        {
            await ImportTeamSettingsAsync(
                projectName, teamPackage.Definition.Name, organisation, sourceProjectName, slug,
                targetTeamId, package, ct).ConfigureAwait(false);
        }

        return targetTeamId;
    }

    private static readonly System.Text.Json.JsonSerializerOptions s_settingsReadOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private async Task ImportTeamSettingsAsync(
        string projectName,
        string teamName,
        string organisation,
        string sourceProjectName,
        string slug,
        string targetTeamId,
        DevOpsMigrationPlatform.Abstractions.Storage.IPackageAccess package,
        CancellationToken ct)
    {
        if (string.IsNullOrEmpty(targetTeamId))
        {
            _logger.LogWarning("[Teams] Target team ID not set for team '{TeamName}' — skipping settings import.", teamName);
            return;
        }

        var payload = await package.RequestContentAsync(
            new DevOpsMigrationPlatform.Abstractions.Storage.PackageContentContext(
                DevOpsMigrationPlatform.Abstractions.Storage.PackageContentKind.Artefact,
                Organisation: organisation,
                Project: projectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(slug, "settings.json")),
            ct).ConfigureAwait(false);

        if (payload is null)
        {
            _logger.LogDebug("[Teams] No settings.json found for team '{TeamName}' — skipping.", teamName);
            return;
        }

        string json;
        using (var reader = new System.IO.StreamReader(payload.Content, System.Text.Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false))
        {
            json = await reader.ReadToEndAsync().ConfigureAwait(false);
        }

        TeamSettings? settings;
        try
        {
            settings = System.Text.Json.JsonSerializer.Deserialize<TeamSettings>(json, s_settingsReadOptions);
        }
        catch (System.Text.Json.JsonException ex)
        {
            _logger.LogWarning(ex, "[Teams] Malformed settings.json for team '{TeamName}' — skipping.", teamName);
            return;
        }

        if (settings is null)
        {
            _logger.LogWarning("[Teams] Null settings in settings.json for team '{TeamName}' — skipping.", teamName);
            return;
        }

        try
        {
            await _teamTarget.SetTeamSettingsAsync(projectName, targetTeamId, settings, ct).ConfigureAwait(false);
            _logger.LogInformation("[Teams] Imported settings for team '{TeamName}'.", teamName);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams] Failed to import settings for team '{TeamName}' — skipping.", teamName);
        }
    }
}
