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
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;

/// <summary>
/// Teams module extension: exports and imports per-iteration team capacity as a separate
/// <c>Teams/{slug}/capacity.json</c> artifact.
/// </summary>
public sealed class TeamCapacityTeamExtension : IModuleExtension
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

    private readonly TeamCapacityExtensionOptions _options;
    private readonly ITeamSource? _teamSource;
    private readonly ITeamTarget? _teamTarget;
    private readonly ILogger<TeamCapacityTeamExtension>? _logger;

    public TeamCapacityTeamExtension(
        IOptions<TeamCapacityExtensionOptions> options,
        ITeamSource? teamSource = null,
        ITeamTarget? teamTarget = null,
        ILogger<TeamCapacityTeamExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _teamSource = teamSource;
        _teamTarget = teamTarget;
        _logger = logger;
    }

    public string Module => "Teams";
    public string Name => "TeamCapacity";
    public int Order => 40;
    public bool SupportsExport => _teamSource is not null;
    public bool SupportsImport => _teamTarget is not null;
    public bool IsEnabled => _options.Enabled;

    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));

        if (_teamSource is null)
        {
            _logger?.LogDebug("[TeamCapacity] No ITeamSource registered — skipping capacity export for team '{TeamName}'.", ctx.Team.Name);
            return;
        }

        // Capacity requires iteration IDs — read iterations.json from the package (already written by TeamIterationsTeamExtension)
        var iterationsPayload = await ctx.Package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "iterations.json")),
            ct).ConfigureAwait(false);

        if (iterationsPayload is null)
        {
            _logger?.LogDebug("[TeamCapacity] No iterations.json found for team '{TeamName}' — skipping capacity export.", ctx.Team.Name);
            return;
        }

        List<TeamIteration>? iterations;
        string iterJson;
        using (var iterReader = new StreamReader(iterationsPayload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false))
            iterJson = await iterReader.ReadToEndAsync().ConfigureAwait(false);

        try
        {
            iterations = JsonSerializer.Deserialize<List<TeamIteration>>(iterJson, s_readOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[TeamCapacity] Malformed iterations.json for team '{TeamName}' — skipping capacity.", ctx.Team.Name);
            return;
        }

        if (iterations is null || iterations.Count == 0)
        {
            _logger?.LogDebug("[TeamCapacity] No iterations in iterations.json for team '{TeamName}' — skipping capacity.", ctx.Team.Name);
            return;
        }

        var capacityByIteration = new Dictionary<string, TeamCapacityEntry[]>();

        foreach (var iteration in iterations)
        {
            try
            {
                var capacity = await _teamSource.GetTeamCapacityAsync(
                    ctx.ProjectName, ctx.EntityId, iteration.Id, ct).ConfigureAwait(false);
                if (capacity.Length > 0)
                    capacityByIteration[iteration.Id] = capacity;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (IsCapacityNotSupportedException(ex))
            {
                _logger?.LogInformation("[TeamCapacity] Capacity not supported for iteration '{IterationId}' — skipping.", iteration.Id);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "[TeamCapacity] Failed to fetch capacity for iteration '{IterationId}' — skipping.", iteration.Id);
            }
        }

        var json = JsonSerializer.Serialize(capacityByIteration, s_writeOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);
        await ctx.Package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "capacity.json")),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger?.LogInformation("[TeamCapacity] Exported capacity for {Count} iterations for team '{TeamName}' → Teams/{Slug}/capacity.json.",
            capacityByIteration.Count, ctx.Team.Name, ctx.Slug);
    }

    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));

        if (_teamTarget is null)
        {
            _logger?.LogDebug("[TeamCapacity] No ITeamTarget registered — skipping capacity import for team '{TeamName}'.", ctx.Team.Name);
            return;
        }

        if (string.IsNullOrEmpty(ctx.TargetEntityId))
        {
            _logger?.LogWarning("[TeamCapacity] TargetEntityId not set for team '{TeamName}' — skipping capacity import.", ctx.Team.Name);
            return;
        }

        var payload = await ctx.Package.RequestContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "capacity.json")),
            ct).ConfigureAwait(false);

        if (payload is null)
        {
            _logger?.LogDebug("[TeamCapacity] No capacity.json found for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        string json;
        using var reader = new StreamReader(payload.Content, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: false);
        json = await reader.ReadToEndAsync().ConfigureAwait(false);

        Dictionary<string, TeamCapacityEntry[]>? capacityByIteration;
        try
        {
            capacityByIteration = JsonSerializer.Deserialize<Dictionary<string, TeamCapacityEntry[]>>(json, s_readOptions);
        }
        catch (JsonException ex)
        {
            _logger?.LogWarning(ex, "[TeamCapacity] Malformed capacity.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        if (capacityByIteration is null || capacityByIteration.Count == 0)
        {
            _logger?.LogDebug("[TeamCapacity] No capacity in capacity.json for team '{TeamName}' — skipping.", ctx.Team.Name);
            return;
        }

        foreach (var kvp in capacityByIteration)
        {
            var iterationId = kvp.Key;
            var capacity = kvp.Value;
            try
            {
                await _teamTarget.SetCapacityAsync(null!, ctx.ProjectName, ctx.TargetEntityId!, iterationId, capacity, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (ex.Message.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0
                || ex.Message.IndexOf("NotImplemented", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                _logger?.LogInformation(
                    "[TeamCapacity] Capacity not supported on target for iteration '{IterationId}' — skipping.", iterationId);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "[TeamCapacity] Failed to set capacity for iteration '{IterationId}' in team '{Team}' — skipping.",
                    iterationId, ctx.Team.Name);
            }
        }

        _logger?.LogInformation("[TeamCapacity] Imported capacity for team '{TeamName}'.", ctx.Team.Name);
    }

    private static bool IsCapacityNotSupportedException(Exception ex)
        => ex.Message.IndexOf("capacity", StringComparison.OrdinalIgnoreCase) >= 0
        || ex.Message.IndexOf("not supported", StringComparison.OrdinalIgnoreCase) >= 0
        || ex.Message.IndexOf("NotImplemented", StringComparison.OrdinalIgnoreCase) >= 0;
}
