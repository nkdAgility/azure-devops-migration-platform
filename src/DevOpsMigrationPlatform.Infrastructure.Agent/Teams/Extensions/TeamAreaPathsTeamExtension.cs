// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;

/// <summary>
/// Teams module extension: imports team area path assignments from
/// <c>Teams/{slug}/area-paths.json</c> with NodeTranslation-based path mapping.
/// Area paths are export-only via <see cref="TeamExportOrchestrator"/> (which records
/// them via <see cref="IReferencedPathTracker"/>) — this extension handles import only.
/// </summary>
public sealed class TeamAreaPathsTeamExtension : IModuleExtension
{
    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TeamAreaPathsExtensionOptions _options;
    private readonly IConnectorCapabilityProvider _capProvider;
    private readonly ITeamTarget _teamTarget;
    private readonly INodeTranslationTool? _nodeTranslationTool;
    private readonly ILogger<TeamAreaPathsTeamExtension>? _logger;

    public TeamAreaPathsTeamExtension(
        IOptions<TeamAreaPathsExtensionOptions> options,
        IConnectorCapabilityProvider capProvider,
        ITeamTarget teamTarget,
        INodeTranslationTool? nodeTranslationTool = null,
        ILogger<TeamAreaPathsTeamExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _capProvider = capProvider ?? throw new ArgumentNullException(nameof(capProvider));
        _teamTarget = teamTarget ?? throw new ArgumentNullException(nameof(teamTarget));
        _nodeTranslationTool = nodeTranslationTool;
        _logger = logger;
    }

    public string Module => "Teams";
    public string Name => "TeamAreaPaths";
    public int Order => 50;
    public bool SupportsExport => false;   // Area path recording is handled by TeamExportOrchestrator
    public bool SupportsImport => _capProvider.Has(Cap.TeamAreaPaths);
    public bool IsEnabled => _options.Enabled;

    public Task ExportAsync(IExtensionContext context, CancellationToken ct)
        => Task.CompletedTask; // No export — area paths are recorded via IReferencedPathTracker

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));


        if (string.IsNullOrEmpty(ctx.TargetEntityId))
        {
            _logger?.LogWarning("[TeamAreaPaths] TargetEntityId not set for team '{TeamName}' — skipping area paths import.", ctx.Team.Name);
            return;
        }

        var payload = await ctx.Package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "area-paths.json")),
            ct).ConfigureAwait(false);

        if (payload is null)
        {
            _logger?.LogDebug("[TeamAreaPaths] No area-paths.json found for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        string json;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        json = await reader.ReadToEndAsync().ConfigureAwait(false);

        TeamAreaPaths? areaPaths;
        try
        {
            areaPaths = JsonSerializer.Deserialize<TeamAreaPaths>(json, s_readOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[TeamAreaPaths] Malformed area-paths.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        if (areaPaths is null)
        {
            _logger?.LogDebug("[TeamAreaPaths] No area paths in area-paths.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        var projectMapping = new ProjectMapping(ctx.SourceProjectName, ctx.ProjectName);

        // Translate default path — if untranslatable, skip the whole area paths assignment
        var defaultPath = TranslatePath("System.AreaPath", areaPaths.DefaultAreaPath, projectMapping);
        if (defaultPath is null)
        {
            _logger?.LogWarning(
                "[TeamAreaPaths] Default area path '{Path}' could not be translated for team '{TeamName}' — skipping area paths import.",
                areaPaths.DefaultAreaPath, ctx.Team.Name);
            return;
        }

        // Translate included paths — skip individual paths that cannot be translated
        var translatedIncluded = new List<string>();
        foreach (var path in areaPaths.IncludedAreaPaths)
        {
            var translated = TranslatePath("System.AreaPath", path, projectMapping);
            if (translated is not null)
                translatedIncluded.Add(translated);
            else
                _logger?.LogWarning(
                    "[TeamAreaPaths] Could not translate included area path '{Path}' for team '{TeamName}' — skipping this path.",
                    path, ctx.Team.Name);
        }

        var translatedAreaPaths = new TeamAreaPaths(defaultPath, translatedIncluded);

        try
        {
            await _teamTarget.SetAreaPathsAsync(ctx.ProjectName, ctx.TargetEntityId!, translatedAreaPaths, ct).ConfigureAwait(false);
            _logger?.LogInformation("[TeamAreaPaths] Imported area paths for team '{TeamName}'.", ctx.Team.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TeamAreaPaths] Failed to set area paths for team '{TeamName}' — skipping.", ctx.Team.Name);
        }
    }

    private string? TranslatePath(string fieldName, string? sourcePath, ProjectMapping projectMapping)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        if (_nodeTranslationTool is null || !_nodeTranslationTool.IsEnabled)
            return sourcePath; // translation tool inactive — pass through unchanged

        var result = _nodeTranslationTool.TranslatePath(fieldName, sourcePath!, projectMapping);

        // FR-009/GAP-005: null TargetPath means untranslatable — return null, caller skips
        return result.TargetPath;
    }
}
