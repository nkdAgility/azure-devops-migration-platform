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
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;

/// <summary>
/// Teams module extension: exports and imports team iteration assignments as a separate
/// <c>Teams/{slug}/iterations.json</c> artifact. Records iteration paths via
/// <see cref="IReferencedPathTracker"/> (when available) during export. Translates
/// paths via <see cref="INodeTranslationTool"/> (when available) during import.
/// </summary>
public sealed class TeamIterationsTeamExtension : IModuleExtension
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly TeamIterationsExtensionOptions _options;
    private readonly IConnectorCapabilityProvider _capProvider;
    private readonly ITeamSource _teamSource;
    private readonly ITeamTarget _teamTarget;
    private readonly INodeTranslationTool? _nodeTranslationTool;
    private readonly IReferencedPathTracker? _referencedPathTracker;
    private readonly ILogger<TeamIterationsTeamExtension>? _logger;

    public TeamIterationsTeamExtension(
        IOptions<TeamIterationsExtensionOptions> options,
        IConnectorCapabilityProvider capProvider,
        ITeamSource teamSource,
        ITeamTarget teamTarget,
        INodeTranslationTool? nodeTranslationTool = null,
        IReferencedPathTracker? referencedPathTracker = null,
        ILogger<TeamIterationsTeamExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _capProvider = capProvider ?? throw new ArgumentNullException(nameof(capProvider));
        _teamSource = teamSource ?? throw new ArgumentNullException(nameof(teamSource));
        _teamTarget = teamTarget ?? throw new ArgumentNullException(nameof(teamTarget));
        _nodeTranslationTool = nodeTranslationTool;
        _referencedPathTracker = referencedPathTracker;
        _logger = logger;
    }

    public string Module => "Teams";
    public string Name => "TeamIterations";
    public int Order => 20;
    public bool SupportsExport => _capProvider.Has(Cap.TeamIterations);
    public bool SupportsImport => _capProvider.Has(Cap.TeamIterations);
    public bool IsEnabled => _options.Enabled;

    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));


        var iterations = new List<TeamIteration>();
        try
        {
            await foreach (var iteration in _teamSource.GetTeamIterationsAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false))
            {
                iterations.Add(iteration);

                // Record iteration paths for node translation
                if (_referencedPathTracker is not null)
                {
                    try
                    {
                        await _referencedPathTracker.RecordIterationPathAsync(
                            iteration.Path, ctx.Package, ctx.Organisation, ctx.ProjectName, ct).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        _logger?.LogWarning(ex, "[TeamIterations] Failed to record iteration path '{Path}' — continuing.", iteration.Path);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TeamIterations] Failed to fetch iterations for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        var json = JsonSerializer.Serialize(iterations, s_writeOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ctx.Package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "iterations.json")),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger?.LogInformation("[TeamIterations] Exported {Count} iterations for team '{TeamName}' → Teams/{Slug}/iterations.json.",
            iterations.Count, ctx.Team.Name, ctx.Slug);
    }

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));


        if (string.IsNullOrEmpty(ctx.TargetEntityId))
        {
            _logger?.LogWarning("[TeamIterations] TargetEntityId not set for team '{TeamName}' — skipping iterations import.", ctx.Team.Name);
            return;
        }

        var payload = await ctx.Package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "iterations.json")),
            ct).ConfigureAwait(false);

        if (payload is null)
        {
            _logger?.LogDebug("[TeamIterations] No iterations.json found for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        string json;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        json = await reader.ReadToEndAsync().ConfigureAwait(false);

        List<TeamIteration>? iterations;
        try
        {
            iterations = JsonSerializer.Deserialize<List<TeamIteration>>(json, s_readOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[TeamIterations] Malformed iterations.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        if (iterations is null || iterations.Count == 0)
        {
            _logger?.LogDebug("[TeamIterations] No iterations in iterations.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        var projectMapping = new ProjectMapping(ctx.SourceProjectName, ctx.ProjectName);

        foreach (var iteration in iterations)
        {
            try
            {
                var translatedPath = TranslatePath("System.IterationPath", iteration.Path, projectMapping);
                if (translatedPath is null)
                {
                    _logger?.LogWarning(
                        "[TeamIterations] Could not translate iteration path '{Path}' for team '{Team}' — skipping.",
                        iteration.Path, ctx.Team.Name);
                    continue;
                }

                var translatedIteration = iteration with { Path = translatedPath };
                await _teamTarget.AssignIterationAsync(ctx.ProjectName, ctx.TargetEntityId!, translatedIteration, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "[TeamIterations] Failed to assign iteration '{Path}' for team '{Team}' — skipping.",
                    iteration.Path, ctx.Team.Name);
            }
        }

        _logger?.LogInformation("[TeamIterations] Imported iterations for team '{TeamName}'.", ctx.Team.Name);
    }

    private string? TranslatePath(string fieldName, string? sourcePath, ProjectMapping projectMapping)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            return null;

        if (_nodeTranslationTool is null || !_nodeTranslationTool.IsEnabled)
            return sourcePath;

        var result = _nodeTranslationTool.TranslatePath(fieldName, sourcePath!, projectMapping);
        return result.TargetPath;
    }
}
