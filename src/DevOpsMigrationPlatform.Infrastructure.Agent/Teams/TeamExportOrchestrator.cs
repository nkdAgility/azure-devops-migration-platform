// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams;

/// <summary>
/// Orchestrates per-team export: captures team definition, settings, iterations,
/// members, capacity, and area paths into a <c>Teams/{slug}/team.json</c> file.
/// </summary>
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
    private readonly IReferencedPathTracker? _referencedPathTracker;
    private readonly ILogger<TeamExportOrchestrator> _logger;
    private readonly ISourceEndpointInfo _endpointInfo;

    public TeamExportOrchestrator(
        ITeamSource teamSource,
        ILogger<TeamExportOrchestrator> logger,
        ISourceEndpointInfo endpointInfo,
        IReferencedPathTracker? referencedPathTracker = null)
    {
        _teamSource = teamSource ?? throw new ArgumentNullException(nameof(teamSource));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
        _referencedPathTracker = referencedPathTracker;
    }

    /// <summary>
    /// Exports a single team (settings, iterations, members, capacity, area paths) to
    /// <c>Teams/{slug}/team.json</c>.
    /// </summary>
    public async Task ExportTeamAsync(
        string projectName,
        TeamDefinition team,
        string slug,
        IArtefactStore artefactStore,
        TeamsModuleExtensionsOptions extensions,
        CancellationToken ct)
    {
        using var activity = s_activitySource.StartActivity("teams.export.team");
        activity?.SetTag("team.name", team.Name);
        activity?.SetTag("team.slug", slug);

        var teamSettings = extensions.TeamSettings
            ? await _teamSource.GetTeamSettingsAsync(projectName, team.Id, ct).ConfigureAwait(false)
            : null;

        var iterations = new List<TeamIteration>();
        if (extensions.TeamIterations)
        {
            await foreach (var iteration in _teamSource.GetTeamIterationsAsync(projectName, team.Id, ct).ConfigureAwait(false))
            {
                iterations.Add(iteration);

                // Record iteration paths for NodeTranslation extension
                if (extensions.NodeTranslation && _referencedPathTracker is not null)
                {
                    await _referencedPathTracker.RecordIterationPathAsync(
                        iteration.Path, artefactStore, ct).ConfigureAwait(false);
                }
            }
        }

        var members = new List<TeamMember>();
        if (extensions.TeamMembers)
        {
            await foreach (var member in _teamSource.GetTeamMembersAsync(projectName, team.Id, ct).ConfigureAwait(false))
                members.Add(member);
        }

        TeamAreaPaths? areaPaths = null;
        if (extensions.NodeTranslation)
        {
            areaPaths = await _teamSource.GetTeamAreaPathsAsync(projectName, team.Id, ct).ConfigureAwait(false);

            // Record area paths for NodeTranslation extension
            if (_referencedPathTracker is not null && areaPaths is not null)
            {
                if (!string.IsNullOrEmpty(areaPaths.DefaultAreaPath))
                    await _referencedPathTracker.RecordAreaPathAsync(areaPaths.DefaultAreaPath, artefactStore, ct).ConfigureAwait(false);

                foreach (var path in areaPaths.IncludedAreaPaths)
                {
                    if (string.IsNullOrEmpty(path)) continue;
                    await _referencedPathTracker.RecordAreaPathAsync(path, artefactStore, ct).ConfigureAwait(false);
                }
            }
        }

        // Build capacity per iteration
        var capacityByIteration = new Dictionary<string, TeamCapacityEntry[]>();
        if (extensions.TeamCapacity && iterations.Count > 0)
        {
            foreach (var iteration in iterations)
            {
                try
                {
                    var capacity = await _teamSource.GetTeamCapacityAsync(
                        projectName, team.Id, iteration.Id, ct).ConfigureAwait(false);
                    if (capacity.Length > 0)
                        capacityByIteration[iteration.Id] = capacity;
                }
                catch (Exception ex) when (IsCapacityNotSupportedException(ex))
                {
                    _logger.LogInformation("[Teams] Capacity not supported for TFS pre-2017.2 — skipping capacity for iteration '{IterationId}'.", iteration.Id);
                }
            }
        }

        // Serialize to team.json
        var teamPackage = new TeamPackage
        {
            Definition = team,
            Settings = teamSettings,
            Iterations = iterations,
            Members = members,
            AreaPaths = areaPaths,
            CapacityByIteration = capacityByIteration
        };

        var json = JsonSerializer.Serialize(teamPackage, s_jsonOptions);
        var artifactPath = $"Teams/{slug}/team.json";
        await artefactStore.WriteAsync(artifactPath, json, ct).ConfigureAwait(false);

        _logger.LogInformation(
            "[Teams] Exported team '{Name}' → {Path} ({IterCount} iterations, {MemberCount} members).",
            team.Name, artifactPath, iterations.Count, members.Count);
    }

    private static bool IsCapacityNotSupportedException(Exception ex)
        => ex.Message.Contains("capacity", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
        || ex.Message.Contains("NotImplemented", StringComparison.OrdinalIgnoreCase);
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
#endif
