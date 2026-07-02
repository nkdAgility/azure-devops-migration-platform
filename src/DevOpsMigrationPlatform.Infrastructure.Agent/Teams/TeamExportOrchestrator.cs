// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Orchestrates per-team export: writes the team definition to
/// <c>Teams/{slug}/team.json</c>. All capability sub-concerns (settings, iterations,
/// members, capacity, area paths) are now delegated to registered
/// <see cref="IModuleExtension"/> implementations and written to separate artifact
/// files under <c>Teams/{slug}/</c>.
/// </summary>
/// <remarks>
/// The <see cref="NodeTranslation"/> area-path recording is also now handled by
/// <c>TeamIterationsTeamExtension</c>, which calls <see cref="IReferencedPathTracker"/>
/// directly. This orchestrator only writes the team definition artifact.
/// </remarks>
public sealed class TeamExportOrchestrator
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private readonly ITeamSource _teamSource;
    private readonly object? _referencedPathTracker;
    private readonly ILogger<TeamExportOrchestrator> _logger;
    private readonly ISourceEndpointInfo _endpointInfo;

    public TeamExportOrchestrator(
        ITeamSource teamSource,
        ILogger<TeamExportOrchestrator> logger,
        ISourceEndpointInfo endpointInfo,
        object? referencedPathTracker = null)
    {
        _teamSource = teamSource ?? throw new ArgumentNullException(nameof(teamSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
        _referencedPathTracker = referencedPathTracker;
    }

    /// <summary>
    /// Exports a single team's definition to <c>Teams/{slug}/team.json</c>.
    /// Capability sub-concerns (settings, iterations, members, capacity) are written
    /// by the registered <see cref="IModuleExtension"/> implementations.
    /// </summary>
    public async Task ExportTeamAsync(
        string organisation,
        string projectName,
        TeamDefinition team,
        string slug,
        IPackageAccess package,
        TeamsModuleExtensionsOptions extensions,
        CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("teams.export.team");
        activity?.SetTag("team.name", team.Name);
        activity?.SetTag("team.slug", slug);

        // Record area paths for NodeTranslation (still handled here for backward compat
        // when no TeamIterationsTeamExtension is registered)
        if (extensions.NodeTranslation && _referencedPathTracker is not null)
        {
            try
            {
                var areaPaths = await _teamSource.GetTeamAreaPathsAsync(projectName, team.Id, ct).ConfigureAwait(false);
                if (areaPaths is not null)
                {
                    if (!string.IsNullOrEmpty(areaPaths.DefaultAreaPath))
                        await RecordAreaPathAsync(areaPaths.DefaultAreaPath, package, organisation, projectName, ct).ConfigureAwait(false);

                    foreach (var path in areaPaths.IncludedAreaPaths)
                    {
                        if (!string.IsNullOrEmpty(path))
                            await RecordAreaPathAsync(path, package, organisation, projectName, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[Teams] Failed to record area paths for team '{Name}' — continuing.", team.Name);
            }
        }

        // Core pipeline: export team settings (backlog levels, bug behaviour, working days)
        // to Teams/{slug}/settings.json. Folded from the former TeamSettingsTeamExtension
        // seam (EC-M3 / ADR-0024); artefact content is byte-for-byte identical.
        if (extensions.TeamSettings)
        {
            await ExportTeamSettingsAsync(organisation, projectName, team, slug, package, ct).ConfigureAwait(false);
        }

        // Write definition-only team.json (capabilities are in split artifact files)
        var teamPackage = new TeamPackage
        {
            Definition = team
        };

        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json), writable: false);
        await package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: projectName,
                Module: "Teams",
                Address: new TeamDefinitionAddress(slug)),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[Teams] Exported team definition '{Name}' → Teams/{Slug}/team.json.",
            team.Name, slug);
    }

    private async Task ExportTeamSettingsAsync(
        string organisation,
        string projectName,
        TeamDefinition team,
        string slug,
        IPackageAccess package,
        CancellationToken ct)
    {
        TeamSettings? settings;
        try
        {
            settings = await _teamSource.GetTeamSettingsAsync(projectName, team.Id, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[Teams] Failed to fetch settings for team '{TeamName}' — skipping.", team.Name);
            return;
        }

        if (settings is null)
        {
            _logger.LogDebug("[Teams] No settings returned for team '{TeamName}' — skipping.", team.Name);
            return;
        }

        var json = JsonSerializer.Serialize(settings, s_jsonOptions);
        using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(json), writable: false);
        await package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: organisation,
                Project: projectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(slug, "settings.json")),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger.LogInformation("[Teams] Exported settings for team '{TeamName}' → Teams/{Slug}/settings.json.", team.Name, slug);
    }

    private Task RecordAreaPathAsync(
        string path,
        IPackageAccess package,
        string organisation,
        string project,
        CancellationToken ct)
    {
#if !NET481
        if (_referencedPathTracker is DevOpsMigrationPlatform.Abstractions.Agent.Tools.IReferencedPathTracker tracker)
            return tracker.RecordAreaPathAsync(path, package, organisation, project, ct);
#endif
        return Task.CompletedTask;
    }
}

/// <summary>Package model for a single team — serialised to Teams/{slug}/team.json.</summary>
public sealed class TeamPackage
{
    public TeamDefinition Definition { get; init; } = null!;
    public TeamSettings? Settings { get; init; }
    public List<TeamIteration> Iterations { get; init; } = new();
    public List<TeamMember> Members { get; init; } = new();
    public TeamAreaPaths? AreaPaths { get; init; }
    public Dictionary<string, TeamCapacityEntry[]> CapacityByIteration { get; init; } = new();
}
