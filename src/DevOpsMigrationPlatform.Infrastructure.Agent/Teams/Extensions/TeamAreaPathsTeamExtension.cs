// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;

/// <summary>
/// Teams module extension: exports and imports team area path assignments as a separate
/// <c>Teams/{slug}/area-paths.json</c> artifact in the Azure DevOps teamfieldvalues REST
/// shape (<c>field.referenceName</c> + <c>defaultValue</c> + <c>values[{value, includeChildren}]</c>).
/// Export captures the per-entry <c>includeChildren</c> flag verbatim; import replays it
/// verbatim with NodeTranslation-based path mapping. Values of custom (non
/// <c>System.AreaPath</c>) team fields are never pushed through the area-path map.
/// </summary>
public sealed class TeamAreaPathsTeamExtension : IModuleExtension
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConnectorCapabilityProvider _capProvider;
    private readonly ITeamSource _teamSource;
    private readonly ITeamTarget _teamTarget;
    private readonly INodeTranslationTool? _nodeTranslationTool;
    private readonly ILogger<TeamAreaPathsTeamExtension>? _logger;

    public TeamAreaPathsTeamExtension(
        IConnectorCapabilityProvider capProvider,
        ITeamSource teamSource,
        ITeamTarget teamTarget,
        INodeTranslationTool? nodeTranslationTool = null,
        ILogger<TeamAreaPathsTeamExtension>? logger = null)
    {
        _capProvider = capProvider ?? throw new ArgumentNullException(nameof(capProvider));
        _teamSource = teamSource ?? throw new ArgumentNullException(nameof(teamSource));
        _teamTarget = teamTarget ?? throw new ArgumentNullException(nameof(teamTarget));
        _nodeTranslationTool = nodeTranslationTool;
        _logger = logger;
    }

    public string Module => "Teams";
    public string Name => "TeamAreaPaths";
    public int Order => 50;
    public bool SupportsExport => _capProvider.Has(Cap.TeamAreaPaths);
    public bool SupportsImport => _capProvider.Has(Cap.TeamAreaPaths);
    // Always enabled — gating is the connector's TeamAreaPaths capability; path translation
    // is governed by the NodeTranslation Processing seam (ConfigVersion 2.0 anatomy, ADR-0028).
    public bool IsEnabled => true;

    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));

        TeamAreaPaths? areaPaths;
        try
        {
            areaPaths = await _teamSource.GetTeamAreaPathsAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TeamAreaPaths] Failed to fetch area paths for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        if (areaPaths is null)
        {
            _logger?.LogDebug("[TeamAreaPaths] No area paths returned for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        var json = JsonSerializer.Serialize(areaPaths, s_writeOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ctx.Package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "area-paths.json")),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger?.LogInformation(
            "[TeamAreaPaths] Exported area paths for team '{TeamName}' → Teams/{Slug}/area-paths.json.",
            ctx.Team.Name, ctx.Slug);
    }

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

        // Custom (non System.AreaPath) team field values are not area paths — replay them
        // verbatim rather than corrupting them through the area-path map.
        var translate = areaPaths.IsAreaPathField;

        // Translate default path — if untranslatable, skip the whole area paths assignment
        var defaultValue = translate
            ? TranslatePath(TeamAreaPaths.AreaPathFieldReferenceName, areaPaths.DefaultValue, projectMapping)
            : areaPaths.DefaultValue;
        if (string.IsNullOrWhiteSpace(defaultValue))
        {
            _logger?.LogWarning(
                "[TeamAreaPaths] Default area path '{Path}' could not be translated for team '{TeamName}' — skipping area paths import.",
                areaPaths.DefaultValue, ctx.Team.Name);
            return;
        }

        // Translate entries — skip individual entries that cannot be translated; the
        // includeChildren flag is replayed verbatim on every surviving entry.
        var translatedValues = new List<TeamFieldValueEntry>();
        foreach (var entry in areaPaths.Values)
        {
            var value = translate
                ? TranslatePath(TeamAreaPaths.AreaPathFieldReferenceName, entry.Value, projectMapping)
                : entry.Value;
            if (!string.IsNullOrWhiteSpace(value))
                translatedValues.Add(entry with { Value = value! });
            else
                _logger?.LogWarning(
                    "[TeamAreaPaths] Could not translate included area path '{Path}' for team '{TeamName}' — skipping this path.",
                    entry.Value, ctx.Team.Name);
        }

        var translatedAreaPaths = new TeamAreaPaths(defaultValue!, translatedValues, areaPaths.FieldReferenceName);

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
