#if !NET481
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
/// Orchestrates per-team import in fixed order:
/// settings → NodeTranslation (iterations/areas) → iterations → members → capacity.
/// </summary>
public sealed class TeamImportOrchestrator
{
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private readonly ITeamTarget _teamTarget;
    private readonly IIdentityLookupTool? _identityLookupTool;
    private readonly INodeTranslationTool? _NodeTransformTool;
    private readonly ILogger<TeamImportOrchestrator> _logger;
    private readonly ITargetEndpointInfo _endpointInfo;

    public TeamImportOrchestrator(
        ITeamTarget teamTarget,
        ILogger<TeamImportOrchestrator> logger,
        ITargetEndpointInfo endpointInfo,
        INodeTranslationTool? NodeTransformTool = null,
        IIdentityLookupTool? identityLookupTool = null)
    {
        _teamTarget = teamTarget ?? throw new ArgumentNullException(nameof(teamTarget));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _endpointInfo = endpointInfo ?? throw new ArgumentNullException(nameof(endpointInfo));
        _NodeTransformTool = NodeTransformTool;
        _identityLookupTool = identityLookupTool;
    }

    /// <summary>
    /// Imports a single team from the package into the target system.
    /// </summary>
    /// <param name="projectName">Target project name.</param>
    /// <param name="sourceProjectName">Source project name — used for path translation.</param>
    /// <param name="teamPackage">The team package to import.</param>
    /// <param name="extensions">Extension toggles.</param>
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

        // 1. Create or update team
        // TODO T051+: ITeamTarget still requires MigrationEndpointOptions parameter
        // This interface needs IOptions<TargetEndpointOptions> injection or to be split into Info + Options
        var targetTeamId = await _teamTarget.CreateOrUpdateTeamAsync(
            null!, projectName, teamPackage.Definition, ct).ConfigureAwait(false);

        // 2. Settings
        if (extensions.TeamSettings && teamPackage.Settings is not null)
        {
            await _teamTarget.SetTeamSettingsAsync(
                null!, projectName, targetTeamId, teamPackage.Settings, ct).ConfigureAwait(false);
        }

        // 3. Iterations (with path translation)
        if (extensions.TeamIterations)
        {
            var projectMapping = new ProjectMapping(sourceProjectName, projectName);
            foreach (var iteration in teamPackage.Iterations)
            {
                try
                {
                    var translatedPath = TranslatePath("System.IterationPath", iteration.Path, projectMapping);
                    if (translatedPath is null)
                    {
                        _logger.LogWarning(
                            "[Teams] Could not translate iteration path '{Path}' for team '{Team}' — skipping.",
                            iteration.Path, teamPackage.Definition.Name);
                        continue;
                    }

                    var translatedIteration = iteration with { Path = translatedPath };
                    await _teamTarget.AssignIterationAsync(
                        null!, projectName, targetTeamId, translatedIteration, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Teams] Failed to assign iteration '{Path}' for team '{Team}' — skipping.",
                        iteration.Path, teamPackage.Definition.Name);
                }
            }
        }

        // 4. Members (with identity mapping)
        if (extensions.TeamMembers)
        {
            foreach (var member in teamPackage.Members)
            {
                try
                {
                    var resolvedDescriptor = extensions.IdentityLookup && _identityLookupTool?.IsEnabled == true ? _identityLookupTool.Resolve(member.Descriptor) : member.Descriptor;
                    var resolvedMember = member with { Descriptor = resolvedDescriptor };
                    await _teamTarget.AddMemberAsync(
                        null!, projectName, targetTeamId, resolvedMember, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Teams] Failed to add member '{Member}' to team '{Team}' — skipping.",
                        member.DisplayName, teamPackage.Definition.Name);
                }
            }
        }

        // 5. Area paths (with path translation)
        if (extensions.NodeTranslation && teamPackage.AreaPaths is not null)
        {
            var projectMapping = new ProjectMapping(sourceProjectName, projectName);
            var defaultPath = TranslatePath("System.AreaPath", teamPackage.AreaPaths.DefaultAreaPath, projectMapping);
            if (defaultPath is not null)
            {
                var translatedPaths = new System.Collections.Generic.List<string>();
                foreach (var path in teamPackage.AreaPaths.IncludedAreaPaths)
                {
                    var translated = TranslatePath("System.AreaPath", path, projectMapping);
                    if (translated is not null)
                        translatedPaths.Add(translated);
                    else
                        _logger.LogWarning("[Teams] Could not translate area path '{Path}' — skipping.", path);
                }

                var translatedAreaPaths = new TeamAreaPaths(defaultPath, translatedPaths);
                await _teamTarget.SetAreaPathsAsync(
                    null!, projectName, targetTeamId, translatedAreaPaths, ct).ConfigureAwait(false);
            }
        }

        // 6. Capacity
        if (extensions.TeamCapacity && teamPackage.CapacityByIteration.Count > 0)
        {
            foreach (var (iterationId, capacity) in teamPackage.CapacityByIteration)
            {
                try
                {
                    await _teamTarget.SetCapacityAsync(
                        null!, projectName, targetTeamId, iterationId, capacity, ct).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex.Message.Contains("not supported", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("NotImplemented", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "[Teams] Capacity not supported on target for iteration '{IterationId}' — skipping.", iterationId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[Teams] Failed to set capacity for iteration '{IterationId}' in team '{Team}' — skipping.",
                        iterationId, teamPackage.Definition.Name);
                }
            }
        }

        return targetTeamId;
    }

    private string? TranslatePath(string fieldName, string? sourcePath, ProjectMapping projectMapping)
    {
        if (string.IsNullOrEmpty(sourcePath))
            return sourcePath;

        if (_NodeTransformTool is null || !_NodeTransformTool.IsEnabled)
            return sourcePath; // pass through if tool disabled

        var result = _NodeTransformTool.TranslatePath(fieldName, sourcePath, projectMapping);
        return result.TargetPath ?? sourcePath;
    }
}
#endif
