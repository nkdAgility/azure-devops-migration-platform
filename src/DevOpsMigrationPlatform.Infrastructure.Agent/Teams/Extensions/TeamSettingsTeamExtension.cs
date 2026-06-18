// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;

/// <summary>
/// Teams module extension: exports and imports team settings (backlog levels, bug behaviour,
/// working days) as a separate <c>Teams/{slug}/settings.json</c> artifact.
/// </summary>
public sealed class TeamSettingsTeamExtension : IModuleExtension
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

    private readonly TeamSettingsExtensionOptions _options;
    private readonly ITeamSource? _teamSource;
    private readonly ITeamTarget? _teamTarget;
    private readonly ILogger<TeamSettingsTeamExtension>? _logger;

    public TeamSettingsTeamExtension(
        IOptions<TeamSettingsExtensionOptions> options,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        ILogger<TeamSettingsTeamExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _logger = logger;
    }

    public string Module => "Teams";
    public string Name => "TeamSettings";
    public int Order => 10;
    public bool SupportsExport => _teamSource is not null;
    public bool SupportsImport => _teamTarget is not null;
    public bool IsEnabled => _options.Enabled;

    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));

        if (_teamSource is null)
        {
            _logger?.LogDebug("[TeamSettings] No ITeamSource registered — skipping settings export for team '{TeamName}'.", ctx.Team.Name);
            return;
        }

        TeamSettings? settings;
        try
        {
            settings = await _teamSource.GetTeamSettingsAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TeamSettings] Failed to fetch settings for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        if (settings is null)
        {
            _logger?.LogDebug("[TeamSettings] No settings returned for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        var json = JsonSerializer.Serialize(settings, s_writeOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ctx.Package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "settings.json")),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger?.LogInformation("[TeamSettings] Exported settings for team '{TeamName}' → Teams/{Slug}/settings.json.", ctx.Team.Name, ctx.Slug);
    }

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));

        if (_teamTarget is null)
        {
            _logger?.LogDebug("[TeamSettings] No ITeamTarget registered — skipping settings import for team '{TeamName}'.", ctx.Team.Name);
            return;
        }

        if (string.IsNullOrEmpty(ctx.TargetEntityId))
        {
            _logger?.LogWarning("[TeamSettings] TargetEntityId not set for team '{TeamName}' — skipping settings import.", ctx.Team.Name);
            return;
        }

        var payload = await ctx.Package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "settings.json")),
            ct).ConfigureAwait(false);

        if (payload is null)
        {
            _logger?.LogDebug("[TeamSettings] No settings.json found for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        string json;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        json = await reader.ReadToEndAsync().ConfigureAwait(false);

        TeamSettings? settings;
        try
        {
            settings = JsonSerializer.Deserialize<TeamSettings>(json, s_readOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[TeamSettings] Malformed settings.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        if (settings is null)
        {
            _logger?.LogWarning("[TeamSettings] Null settings in settings.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        try
        {
            await _teamTarget.SetTeamSettingsAsync(null!, ctx.ProjectName, ctx.TargetEntityId!, settings, ct).ConfigureAwait(false);
            _logger?.LogInformation("[TeamSettings] Imported settings for team '{TeamName}'.", ctx.Team.Name);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[TeamSettings] Failed to import settings for team '{TeamName}' — skipping.", ctx.Team.Name);
        }
    }
}
