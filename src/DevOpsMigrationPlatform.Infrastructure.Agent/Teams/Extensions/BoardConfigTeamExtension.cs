// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using Cap = DevOpsMigrationPlatform.Abstractions.Agent.ConnectorCapability;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using DevOpsMigrationPlatform.Abstractions.Telemetry;
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
    private static readonly ActivitySource s_activitySource = new(WellKnownActivitySourceNames.Migration);

    private static readonly JsonSerializerOptions s_writeOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false,
    };

    private readonly BoardConfigExtensionOptions _options;
    private readonly ITeamBoardAdapter _adapter;
    private readonly IConnectorCapabilityProvider _capProvider;
    private readonly IBoardConfigMergeTool _mergeTool;
    private readonly IPlatformMetrics? _metrics;
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
        IBoardConfigMergeTool mergeTool,
        IPlatformMetrics? metrics = null,
        ILogger<BoardConfigTeamExtension>? logger = null)
    {
        _options = (options ?? throw new ArgumentNullException(nameof(options))).Value;
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        _capProvider = capProvider ?? throw new ArgumentNullException(nameof(capProvider));
        _mergeTool = mergeTool ?? throw new ArgumentNullException(nameof(mergeTool));
        _metrics = metrics;
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
            ctx.ProgressSink?.Emit(new ProgressEvent
            {
                Module = "Teams",
                Stage = "BoardConfigSkipped",
                Message = $"Board config skipped for team '{ctx.Slug}': connector capability absent."
            });
            return;
        }

        _logger?.LogInformation(
            "[BoardConfig] Exporting board config for team '{Team}'...",
            ctx.Team.Name);

        ctx.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "Teams",
            Stage = "BoardConfigExporting",
            Message = $"Exporting board config for team '{ctx.Slug}'."
        });
        var sw = Stopwatch.StartNew();
        var plan = BoardConfigExportPlan.From(_options, _capProvider);

        var boards = new List<BoardConfig>();
        var cardRulesPerBoard = new Dictionary<string, CardRuleSettings?>();

        await foreach (var board in _adapter.GetBoardsAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false))
        {
            var columns = plan.ExportColumns ? board.Columns : (IReadOnlyList<BoardColumn>)[];
            var swimLanes = plan.ExportSwimLanes ? board.SwimLanes : (IReadOnlyList<BoardSwimLane>)[];
            boards.Add(new BoardConfig(board.BoardName, columns, swimLanes));

            if (plan.ExportCardRules)
            {
                var rules = await _adapter.GetCardRuleSettingsAsync(
                    ctx.ProjectName, ctx.EntityId, board.BoardName, ct).ConfigureAwait(false);
                cardRulesPerBoard[board.BoardName] = rules;
            }
        }

        CardRuleSettings? aggregatedCardRules = null;
        if (plan.ExportCardRules && cardRulesPerBoard.Count > 0)
        {
            var allRules = cardRulesPerBoard.Values
                .Where(r => r is not null)
                .SelectMany(r => r!.Rules)
                .ToList();
            if (allRules.Count > 0)
                aggregatedCardRules = new CardRuleSettings(allRules);
        }

        var backlogs = new List<BacklogMetadata>();
        if (plan.ExportBacklogs)
        {
            await foreach (var b in _adapter.GetBacklogsAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false))
                backlogs.Add(b);
        }

        var taskboardColumns = new List<TaskboardColumn>();
        if (plan.ExportTaskboardColumns)
        {
            await foreach (var col in _adapter.GetTaskboardColumnsAsync(ctx.ProjectName, ctx.EntityId, ct).ConfigureAwait(false))
                taskboardColumns.Add(col);
        }

        var teamBoardConfig = new TeamBoardConfig
        {
            TeamName = ctx.Team.Name,
            ExportedAt = DateTimeOffset.UtcNow,
            Boards = boards,
            CardRules = aggregatedCardRules,
            Backlogs = backlogs,
            TaskboardColumns = taskboardColumns,
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

        ctx.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "Teams",
            Stage = "BoardConfigExported",
            Message = $"Exported board config for team '{ctx.Slug}': {boards.Count} board(s) in {sw.ElapsedMilliseconds}ms."
        });
    }

    private static readonly JsonSerializerOptions s_readOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc/>
    public async Task ImportAsync(IExtensionContext context, CancellationToken ct)
    {
        if (context is not TeamExtensionContext ctx)
            throw new ArgumentException($"Expected {nameof(TeamExtensionContext)}.", nameof(context));

        using var activity = s_activitySource.StartActivity("teams.boardconfig.import");
        activity?.SetTag("team.slug", ctx.Slug);
        activity?.SetTag("module", "Teams");

        var importMetricsTags = new MetricsTagList { { "module", "Teams" } };
        _metrics?.IncrementBoardConfigImportInFlight(importMetricsTags);
        try
        {
            await ImportCoreAsync(ctx, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _metrics?.RecordBoardConfigImportError(importMetricsTags);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            throw;
        }
        finally
        {
            _metrics?.DecrementBoardConfigImportInFlight(importMetricsTags);
        }
    }

    private async Task ImportCoreAsync(TeamExtensionContext ctx, CancellationToken ct)
    {
        if (!_capProvider.Has(Cap.BoardConfig))
        {
            _logger?.LogInformation(
                "[BoardConfig] Connector does not support board config — skipping import for '{Team}'",
                ctx.Team.Name);
            ctx.ProgressSink?.Emit(new ProgressEvent
            {
                Module = "Teams",
                Stage = "BoardConfigImportSkipped",
                Message = $"Board config import skipped for team '{ctx.Slug}': connector capability absent."
            });
            _metrics?.RecordBoardConfigImportSkipped(new MetricsTagList { { "module", "Teams" } });
            return;
        }

        if (ctx.TargetEntityId is null)
        {
            _logger?.LogWarning(
                "[BoardConfig] Target team ID not resolved for '{Team}' — skipping board config import",
                ctx.Team.Name);
            ctx.ProgressSink?.Emit(new ProgressEvent
            {
                Module = "Teams",
                Stage = "BoardConfigImportSkipped",
                Message = $"Board config import skipped for team '{ctx.Slug}': target team ID not resolved."
            });
            _metrics?.RecordBoardConfigImportSkipped(new MetricsTagList { { "module", "Teams" } });
            return;
        }

        var contentCtx = new PackageContentContext(
            PackageContentKind.Artefact,
            Organisation: ctx.Organisation,
            Project: ctx.ProjectName,
            Module: "Teams",
            Address: new TeamArtifactAddress(ctx.Slug, "board-config.json"));

        var payload = await ctx.Package.RequestContentAsync(contentCtx, ct).ConfigureAwait(false);
        if (payload is null)
        {
            _logger?.LogWarning(
                "[BoardConfig] No board-config.json found for team '{Team}' — skipping import",
                ctx.Team.Name);
            ctx.ProgressSink?.Emit(new ProgressEvent
            {
                Module = "Teams",
                Stage = "BoardConfigImportSkipped",
                Message = $"Board config import skipped for team '{ctx.Slug}': board-config.json not found in package."
            });
            _metrics?.RecordBoardConfigImportSkipped(new MetricsTagList { { "module", "Teams" } });
            return;
        }

        ctx.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "Teams",
            Stage = "BoardConfigImporting",
            Message = $"Importing board config for team '{ctx.Slug}' (mode={_options.ImportMode})."
        });
        var importSw = Stopwatch.StartNew();

        TeamBoardConfig teamBoardConfig;
        using (var reader = new StreamReader(payload.Content, Encoding.UTF8))
        {
#if NET7_0_OR_GREATER
            var json = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
#else
            var json = await reader.ReadToEndAsync().ConfigureAwait(false);
#endif
            teamBoardConfig = JsonSerializer.Deserialize<TeamBoardConfig>(json, s_readOptions)
                ?? throw new InvalidOperationException("board-config.json deserialised to null.");
        }

        var targetId = ctx.TargetEntityId!;

        // Batch all target reads before writing.
        // Even in Replace mode we need target column data to validate state mappings (FR-013).
        var snapshot = await _adapter.GetBoardConfigSnapshotAsync(ctx.ProjectName, targetId, ct).ConfigureAwait(false);

        foreach (var board in teamBoardConfig.Boards)
        {
            if (_options.ImportMode == BoardConfigImportMode.Skip &&
                snapshot.BoardNames.Contains(board.BoardName))
                continue;

            try
            {
                if (_options.Columns)
                {
                    snapshot.BoardColumns.TryGetValue(board.BoardName, out var targetColumns);
                    targetColumns ??= [];

                    var validStates = _mergeTool.BuildValidStatesMap(targetColumns);

                    var columns = _options.ImportMode == BoardConfigImportMode.Merge
                        ? _mergeTool.MergeByName(board.Columns, targetColumns, c => c.Name)
                        : board.Columns;

                    // Engine validates (EC-M4); extension keeps the warning policy.
                    var validation = _mergeTool.FilterInvalidStateMappings(columns, validStates);
                    foreach (var omittedMapping in validation.OmittedMappings)
                    {
                        _logger?.LogWarning(
                            "[BoardConfig] Board '{Board}' column '{Column}': omitting state mapping for " +
                            "WIT '{Wit}' → state '{State}' — state absent in target process.",
                            board.BoardName, omittedMapping.ColumnName, omittedMapping.WorkItemType, omittedMapping.State);
                    }
                    columns = validation.Columns;

                    await _adapter.UpdateBoardColumnsAsync(
                        ctx.ProjectName, targetId, board.BoardName, columns, ct).ConfigureAwait(false);
                }

                if (_options.SwimLanes)
                {
                    snapshot.BoardSwimLanes.TryGetValue(board.BoardName, out var targetLanes);
                    targetLanes ??= [];

                    var lanes = _options.ImportMode == BoardConfigImportMode.Merge
                        ? _mergeTool.MergeByName(board.SwimLanes, targetLanes, l => l.Name)
                        : board.SwimLanes;
                    await _adapter.UpdateSwimLanesAsync(
                        ctx.ProjectName, targetId, board.BoardName, lanes, ct).ConfigureAwait(false);
                }

                if (_options.CardRules && teamBoardConfig.CardRules is not null)
                    await _adapter.UpdateCardRuleSettingsAsync(
                        ctx.ProjectName, targetId, board.BoardName, teamBoardConfig.CardRules, ct).ConfigureAwait(false);
            }
            catch (UnauthorizedAccessException ex)
            {
                _logger?.LogWarning(ex,
                    "[BoardConfig] Permission denied updating board '{Board}' for team '{Team}' — skipping board",
                    board.BoardName, ctx.Team.Name);
            }
        }

        if (_options.TaskboardColumns &&
            _capProvider.Has(Cap.TaskboardColumns) &&
            teamBoardConfig.TaskboardColumns.Count > 0)
        {
            var taskboardAlreadyExists = snapshot.TaskboardColumns.Count > 0;
            if (_options.ImportMode != BoardConfigImportMode.Skip || !taskboardAlreadyExists)
            {
                var taskCols = _options.ImportMode == BoardConfigImportMode.Merge
                    ? _mergeTool.MergeByName(teamBoardConfig.TaskboardColumns, snapshot.TaskboardColumns, c => c.Name)
                    : teamBoardConfig.TaskboardColumns;
                await _adapter.UpdateTaskboardColumnsAsync(
                    ctx.ProjectName, targetId, taskCols, ct).ConfigureAwait(false);
            }
        }

        _logger?.LogInformation(
            "[BoardConfig] Imported board config for team '{Team}' ({Boards} boards, mode={Mode})",
            ctx.Team.Name, teamBoardConfig.Boards.Count, _options.ImportMode);

        ctx.ProgressSink?.Emit(new ProgressEvent
        {
            Module = "Teams",
            Stage = "BoardConfigImported",
            Message = $"Imported board config for team '{ctx.Slug}': {teamBoardConfig.Boards.Count} board(s) in {importSw.ElapsedMilliseconds}ms (mode={_options.ImportMode})."
        });

        var completedTags = new MetricsTagList { { "module", "Teams" } };
        _metrics?.RecordBoardConfigImportCount(completedTags);
        _metrics?.RecordBoardConfigImportDuration(importSw.Elapsed.TotalMilliseconds, completedTags);
    }

    // Aggregates all capability-gate checks for the export path into one place.
    private sealed record BoardConfigExportPlan(
        bool ExportColumns,
        bool ExportSwimLanes,
        bool ExportCardRules,
        bool ExportBacklogs,
        bool ExportTaskboardColumns)
    {
        public static BoardConfigExportPlan From(BoardConfigExtensionOptions options, IConnectorCapabilityProvider caps) => new(
            ExportColumns:          options.Columns,
            ExportSwimLanes:        options.SwimLanes,
            ExportCardRules:        options.CardRules        && caps.Has(Cap.BoardConfig),
            ExportBacklogs:         options.Backlogs         && caps.Has(Cap.Backlogs),
            ExportTaskboardColumns: options.TaskboardColumns && caps.Has(Cap.TaskboardColumns));
    }

}
