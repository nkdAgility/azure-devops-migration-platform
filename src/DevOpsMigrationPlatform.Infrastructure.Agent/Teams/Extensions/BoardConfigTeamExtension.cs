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
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Teams.Extensions;

/// <summary>
/// Teams module extension: exports (and in Phase 9 imports) the full board
/// configuration — columns, swimlanes, card rules, backlogs and taskboard columns —
/// to <c>Teams/{slug}/board-config.json</c>.
/// </summary>
public sealed class BoardConfigTeamExtension : IModuleExtension
{
    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly BoardConfigExtensionOptions _options;
    private readonly ITeamBoardAdapter _adapter;
    private readonly IConnectorCapabilityProvider _capProvider;
    private readonly ILogger<BoardConfigTeamExtension>? _logger;

    /// <inheritdoc/>
    public string Module => "Teams";

    /// <inheritdoc/>
    public string Name => "BoardConfig";

    /// <inheritdoc/>
    public int Order => 100;

    /// <inheritdoc/>
    public bool SupportsExport => true;

    /// <inheritdoc/>
    public bool SupportsImport => true;

    /// <inheritdoc/>
    public bool IsEnabled => _options.Enabled;

    public BoardConfigTeamExtension(
        IOptions<BoardConfigExtensionOptions> options,
        ITeamBoardAdapter adapter,
        IConnectorCapabilityProvider capProvider,
        ILogger<BoardConfigTeamExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _capProvider = capProvider ?? throw new ArgumentNullException(nameof(capProvider));
        _logger = logger;
    }

    /// <inheritdoc/>
    public async Task ExportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));

        // Capability gate — TFS returns ConnectorCapability.None; no null-guards needed.
        if (!_capProvider.Has(Cap.BoardConfig))
        {
            _logger?.LogInformation(
                "[BoardConfig] Connector does not support board config — skipping team '{Team}'",
                ctx.Team.Name);
            return;
        }

        _logger?.LogInformation(
            "[BoardConfig] Exporting board config for team '{Team}'...",
            ctx.Team.Name);

        var boards = new List<BoardConfig>();

        await foreach (var board in _adapter.GetBoardsAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false))
        {
            var columns = _options.Columns
                ? board.Columns
                : (IReadOnlyList<BoardColumn>)[];

            var swimLanes = _options.SwimLanes
                ? board.SwimLanes
                : (IReadOnlyList<BoardSwimLane>)[];

            boards.Add(new BoardConfig(board.BoardName, columns, swimLanes));
        }

        var teamBoardConfig = new TeamBoardConfig
        {
            TeamName = ctx.Team.Name,
            ExportedAt = DateTimeOffset.UtcNow,
            Boards = boards,
            CardRules = null,       // US3 — not yet implemented
            Backlogs = [],           // US4 — not yet implemented
            TaskboardColumns = [],   // US5 — not yet implemented
        };

        var json = JsonSerializer.Serialize(teamBoardConfig, s_writeOptions);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json), writable: false);

        await ctx.Package.PersistContentAsync(
            new PackageContentContext(
                PackageContentKind.Artefact,
                Organisation: ctx.Organisation,
                Project: ctx.ProjectName,
                Module: "Teams",
                Address: new TeamArtifactAddress(ctx.Slug, "board-config.json")),
            new PackagePayload(stream, "application/json"),
            ct).ConfigureAwait(false);

        _logger?.LogInformation(
            "[BoardConfig] Exported {Count} boards for team '{Team}' → Teams/{Slug}/board-config.json",
            boards.Count, ctx.Team.Name, ctx.Slug);
    }

    /// <inheritdoc/>
    public Task ImportAsync(IExtensionContext context, CancellationToken ct)
        => Task.CompletedTask; // Phase 9
}
