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
/// Orchestrates per-team migration — both export and import — behind a single
/// phase-symmetric seam dispatched by <see cref="Modules.TeamsOrchestrator"/>.
/// <para>
/// Export writes the team definition to <c>Teams/{slug}/team.json</c>; import creates or
/// updates the team on the target system and returns the resolved target team ID. All
/// capability sub-concerns (settings, iterations, members, capacity, area paths) are
/// delegated to registered <see cref="IModuleExtension"/> implementations and written to
/// separate artifact files under <c>Teams/{slug}/</c>.
/// </para>
/// </summary>
/// <remarks>
/// The <see cref="NodeTranslation"/> area-path recording on the export side is also handled by
/// <c>TeamIterationsTeamExtension</c>, which calls <see cref="IReferencedPathLifecycle"/>
/// directly. This orchestrator only writes the team definition artifact.
/// </remarks>
public sealed class TeamMigrationOrchestrator
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_jsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    // Export-side dependencies.
    private readonly ITeamSource? _teamSource;
    private readonly object? _referencedPathTracker;
    private readonly ISourceEndpointInfo? _sourceEndpointInfo;

    // Import-side dependencies.
    private readonly ITeamTarget? _teamTarget;
    private readonly IIdentityTranslationTool? _identityTranslationTool;
    private readonly INodeTranslationTool? _nodeTranslationTool;
    private readonly ITargetEndpointInfo? _targetEndpointInfo;

    private readonly ILogger<TeamMigrationOrchestrator> _logger;

    /// <summary>
    /// Constructs the unified per-team migration orchestrator. Export requires
    /// <paramref name="teamSource"/>/<paramref name="sourceEndpointInfo"/>; import requires
    /// <paramref name="teamTarget"/>/<paramref name="targetEndpointInfo"/>. Both halves are
    /// registered together in production DI (all four seams are available on both target
    /// frameworks); the phase-specific halves are nullable so that phase-scoped tests and the
    /// net481 source-only agent — where import is guarded out at the module level — can supply
    /// only the half they exercise.
    /// </summary>
    public TeamMigrationOrchestrator(
        ITeamSource? teamSource,
        ITeamTarget? teamTarget,
        ILogger<TeamMigrationOrchestrator> logger,
        ISourceEndpointInfo? sourceEndpointInfo,
        ITargetEndpointInfo? targetEndpointInfo,
        object? referencedPathTracker = null,
        INodeTranslationTool? nodeTranslationTool = null,
        IIdentityTranslationTool? identityTranslationTool = null)
    {
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _sourceEndpointInfo = sourceEndpointInfo;
        _targetEndpointInfo = targetEndpointInfo;
        _referencedPathTracker = referencedPathTracker;
        _nodeTranslationTool = nodeTranslationTool;
        _identityTranslationTool = identityTranslationTool;
    }

    // ── Export ──────────────────────────────────────────────────────────────

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
        TeamsDataOptions data,
        TeamsProcessingOptions processing,
        CancellationToken ct)
    {
        if (_teamSource is null)
            throw new InvalidOperationException($"{nameof(ITeamSource)} is required for team export operations.");

        using var activity = s_activitySource.StartActivity("teams.export.team");
        activity?.SetTag("team.name", team.Name);
        activity?.SetTag("team.slug", slug);

        // Record area paths for NodeTranslation (still handled here for backward compat
        // when no TeamIterationsTeamExtension is registered)
        if (processing.NodeTranslation && _referencedPathTracker is not null)
        {
            try
            {
                var areaPaths = await _teamSource!.GetTeamAreaPathsAsync(projectName, team.Id, ct).ConfigureAwait(false);
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
        if (data.TeamSettings)
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
            settings = await _teamSource!.GetTeamSettingsAsync(projectName, team.Id, ct).ConfigureAwait(false);
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
        if (_referencedPathTracker is DevOpsMigrationPlatform.Abstractions.Agent.Tools.IReferencedPathLifecycle tracker)
            return tracker.RecordAreaPathAsync(path, package, organisation, project, ct);
#endif
        return Task.CompletedTask;
    }

    // ── Import ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Creates or updates a single team on the target system and returns the target team ID.
    /// Capability imports (settings, iterations, members, capacity, area paths) are handled
    /// by the registered <see cref="Abstractions.Agent.IModuleExtension"/> implementations
    /// which are dispatched by <see cref="Modules.TeamsOrchestrator"/> after this call.
    /// </summary>
    /// <param name="projectName">Target project name.</param>
    /// <param name="sourceProjectName">Source project name — used for path translation.</param>
    /// <param name="teamPackage">The team package to import.</param>
    /// <param name="data">Extension toggles (governs core capability imports such as team settings).</param>
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
        if (_teamTarget is null)
            throw new InvalidOperationException($"{nameof(ITeamTarget)} is required for team import operations.");

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
        var targetTeamId = await _teamTarget!.CreateOrUpdateTeamAsync(
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
            await _teamTarget!.SetTeamSettingsAsync(projectName, targetTeamId, settings, ct).ConfigureAwait(false);
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
