// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Platform.AzureDevOpsAccess;
using Microsoft.Extensions.Logging;
using Polly;
using Adob = Microsoft.TeamFoundation.Work.WebApi;
using AdoTask = Microsoft.TeamFoundation.Work.WebApi.Contracts.Taskboard;
using WorkContext = Microsoft.TeamFoundation.Core.WebApi.Types.TeamContext;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Teams;

/// <summary>
/// Azure DevOps REST API implementation of <see cref="ITeamBoardAdapter"/>.
/// Read (export) methods use the source endpoint; write and merge-read methods use the target endpoint.
/// </summary>
internal sealed class AzureDevOpsBoardAdapter : ITeamBoardAdapter
{
    private readonly IAzureDevOpsClientFactory _clientFactory;
    private readonly ISourceEndpointInfo _source;
    private readonly ITargetEndpointInfo _target;
    private readonly ILogger<AzureDevOpsBoardAdapter>? _logger;
    private readonly IAsyncPolicy _retryPipeline;

    public AzureDevOpsBoardAdapter(
        IAzureDevOpsClientFactory clientFactory,
        ISourceEndpointInfo source,
        ITargetEndpointInfo target,
        ILogger<AzureDevOpsBoardAdapter>? logger = null)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _source = source ?? throw new ArgumentNullException(nameof(source));
        _target = target ?? throw new ArgumentNullException(nameof(target));
        _logger = logger;
        _retryPipeline = AzureDevOpsRetryPolicy.GetSdkRetryPolicy(logger);
    }

    // -------------------------------------------------------------------------
    // Export — read from source
    // -------------------------------------------------------------------------

    public async IAsyncEnumerable<BoardConfig> GetBoardsAsync(
        string project,
        string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var org = _source.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);

        // Transient failures (429/timeout/503) are retried via the shared resilience pipeline;
        // non-transient failures propagate to the caller instead of being swallowed as an empty result.
        var boardRefs = await _retryPipeline.ExecuteAsync(
                token => workClient.GetBoardsAsync(teamContext, cancellationToken: token), ct)
            .ConfigureAwait(false);

        foreach (var boardRef in boardRefs)
        {
            ct.ThrowIfCancellationRequested();
            Adob.Board board;
            try
            {
                board = await workClient.GetBoardAsync(teamContext, boardRef.Name, cancellationToken: ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex,
                    "[BoardAdapter/ADO] Failed to get board '{Board}' — skipping.", boardRef.Name);
                continue;
            }

            var columns = (board.Columns ?? []).Select(MapColumn).ToList();
            var lanes = (board.Rows ?? [])
                .Select(r => new BoardSwimLane(r.Id?.ToString(), r.Name ?? string.Empty))
                .ToList();
            yield return new BoardConfig(board.Name, columns, lanes);
        }
    }

    public async Task<CardRuleSettings?> GetCardRuleSettingsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
    {
        var org = _source.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);

        // Transient failures (429/timeout/503) are retried via the shared resilience pipeline;
        // non-transient failures propagate to the caller instead of being swallowed as null.
        var adoSettings = await _retryPipeline.ExecuteAsync(
                token => workClient.GetBoardCardRuleSettingsAsync(
                    teamContext, boardName, cancellationToken: token), ct)
            .ConfigureAwait(false);

        if (adoSettings?.rules is null || adoSettings.rules.Count == 0)
            return null;

        var rules = new List<CardRule>();
        if (adoSettings.rules.TryGetValue("fill", out var fillRules))
        {
            foreach (var r in fillRules ?? [])
            {
                var color = r.settings?.TryGetValue("background-color", out var c) == true ? c : null;
                rules.Add(new CardRule(
                    Name: r.name ?? string.Empty,
                    Color: color,
                    IsEnabled: string.Equals(r.isEnabled, "true", StringComparison.OrdinalIgnoreCase),
                    Filter: r.filter ?? string.Empty));
            }
        }
        return rules.Count == 0 ? null : new CardRuleSettings(rules);
    }

    public async IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(
        string project,
        string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var org = _source.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);

        List<Adob.BacklogLevelConfiguration> backlogs;
        try
        {
            backlogs = await workClient.GetBacklogsAsync(teamContext, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[BoardAdapter/ADO] Failed to get backlogs for team '{TeamId}'.", teamId);
            yield break;
        }

        foreach (var b in backlogs)
        {
            ct.ThrowIfCancellationRequested();
            yield return new BacklogMetadata(
                Name: b.Name ?? string.Empty,
                WitCategory: b.Id ?? string.Empty,
                LevelType: MapBacklogType(b.Type),
                Rank: b.Rank);
        }
    }

    public async IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(
        string project,
        string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var org = _source.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);

        AdoTask.TaskboardColumns? taskboard;
        try
        {
            taskboard = await workClient.GetColumnsAsync(teamContext, cancellationToken: ct)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[BoardAdapter/ADO] Failed to get taskboard columns for team '{TeamId}'.", teamId);
            yield break;
        }

        var allCols = taskboard?.Columns?.ToList() ?? [];
        var count = allCols.Count;
        for (var i = 0; i < count; i++)
        {
            ct.ThrowIfCancellationRequested();
            yield return MapTaskboardColumn(allCols[i], i, count);
        }
    }

    // -------------------------------------------------------------------------
    // Import-side reads — read from target (used by Merge / Skip modes)
    // -------------------------------------------------------------------------

    public async Task<IReadOnlyList<BoardColumn>> GetBoardColumnsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
    {
        var org = _target.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);
        try
        {
            var cols = await workClient.GetBoardColumnsAsync(
                teamContext, boardName, cancellationToken: ct).ConfigureAwait(false);
            return (cols ?? []).Select(MapColumn).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[BoardAdapter/ADO] Failed to get board columns for '{Board}' — returning empty.", boardName);
            return [];
        }
    }

    public async Task<IReadOnlyList<BoardSwimLane>> GetBoardSwimLanesAsync(
        string project, string teamId, string boardName, CancellationToken ct)
    {
        var org = _target.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);
        try
        {
            var rows = await workClient.GetBoardRowsAsync(
                teamContext, boardName, cancellationToken: ct).ConfigureAwait(false);
            return (rows ?? [])
                .Select(r => new BoardSwimLane(r.Id?.ToString(), r.Name ?? string.Empty))
                .ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[BoardAdapter/ADO] Failed to get swimlanes for '{Board}' — returning empty.", boardName);
            return [];
        }
    }

    public async Task<IReadOnlyList<TaskboardColumn>> GetCurrentTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
    {
        var org = _target.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);
        try
        {
            var taskboard = await workClient.GetColumnsAsync(
                teamContext, cancellationToken: ct).ConfigureAwait(false);
            var cols = taskboard?.Columns ?? [];
            var count = cols.Count;
            return cols.Select((c, i) => MapTaskboardColumn(c, i, count)).ToList();
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex,
                "[BoardAdapter/ADO] Failed to get current taskboard columns — returning empty.");
            return [];
        }
    }

    public async Task<TargetBoardSnapshot> GetBoardConfigSnapshotAsync(
        string project, string teamId, CancellationToken ct)
    {
        ISet<string> boardNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await foreach (var b in GetBoardsAsync(project, teamId, ct).ConfigureAwait(false))
            boardNames.Add(b.BoardName);

        var boardColumns = new Dictionary<string, IReadOnlyList<BoardColumn>>(StringComparer.OrdinalIgnoreCase);
        var boardSwimLanes = new Dictionary<string, IReadOnlyList<BoardSwimLane>>(StringComparer.OrdinalIgnoreCase);
        foreach (var name in boardNames)
        {
            boardColumns[name] = await GetBoardColumnsAsync(project, teamId, name, ct).ConfigureAwait(false);
            boardSwimLanes[name] = await GetBoardSwimLanesAsync(project, teamId, name, ct).ConfigureAwait(false);
        }

        var taskboardColumns = await GetCurrentTaskboardColumnsAsync(project, teamId, ct).ConfigureAwait(false);

        return new TargetBoardSnapshot
        {
            BoardNames = boardNames,
            BoardColumns = boardColumns,
            BoardSwimLanes = boardSwimLanes,
            TaskboardColumns = taskboardColumns
        };
    }

    // -------------------------------------------------------------------------
    // Import — write to target
    // -------------------------------------------------------------------------

    public async Task UpdateBoardColumnsAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardColumn> columns, CancellationToken ct)
    {
        var org = _target.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);
        var adoCols = columns.Select(MapColumnToAdo).ToList();
        await workClient.UpdateBoardColumnsAsync(adoCols, teamContext, boardName, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateSwimLanesAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardSwimLane> swimLanes, CancellationToken ct)
    {
        var org = _target.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);
        var adoRows = swimLanes
            .Select(l => new Adob.BoardRow { Name = l.Name })
            .ToList();
        await workClient.UpdateBoardRowsAsync(adoRows, teamContext, boardName, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    public async Task UpdateCardRuleSettingsAsync(
        string project, string teamId, string boardName,
        CardRuleSettings? rules, CancellationToken ct)
    {
        if (rules is null) return;

        var org = _target.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);
        var adoSettings = new Adob.BoardCardRuleSettings
        {
            rules = new Dictionary<string, List<Adob.Rule>>
            {
                ["fill"] = rules.Rules
                    .Select(r => new Adob.Rule
                    {
                        name = r.Name,
                        isEnabled = r.IsEnabled ? "true" : "false",
                        filter = r.Filter,
                        settings = r.Color is not null
                            ? new Adob.attribute { ["background-color"] = r.Color }
                            : new Adob.attribute()
                    })
                    .ToList()
            }
        };
        await workClient.UpdateBoardCardRuleSettingsAsync(
            adoSettings, teamContext, boardName, cancellationToken: ct).ConfigureAwait(false);
    }

    public async Task UpdateTaskboardColumnsAsync(
        string project, string teamId,
        IReadOnlyList<TaskboardColumn> columns, CancellationToken ct)
    {
        var org = _target.ToOrganisationEndpoint();
        var workClient = await _clientFactory.CreateWorkClientAsync(org, ct).ConfigureAwait(false);
        var teamContext = new WorkContext(project, teamId);
        var updates = columns
            .Select(col => new AdoTask.UpdateTaskboardColumn
            {
                Id = null,
                Name = col.Name,
                Order = col.Order,
                Mappings = col.StateMappings
                    .Select(m => new AdoTask.TaskboardColumnMapping
                    {
                        WorkItemType = m.WorkItemType,
                        State = m.State,
                    })
                    .ToList()
            })
            .ToList();
        await workClient.UpdateColumnsAsync(updates, teamContext, cancellationToken: ct)
            .ConfigureAwait(false);
    }

    // -------------------------------------------------------------------------
    // Mapping helpers
    // -------------------------------------------------------------------------

    private static BoardColumn MapColumn(Adob.BoardColumn c) =>
        new(
            Name: c.Name ?? string.Empty,
            ColumnType: (BoardColumnType)(int)c.ColumnType,
            ItemLimit: c.ItemLimit,
            IsSplit: c.IsSplit ?? false,
            Description: c.Description,
            StateMappings: (c.StateMappings ?? new Dictionary<string, string>())
                .Select(kv => new BoardColumnStateMapping(kv.Key, kv.Value))
                .ToList());

    private static Adob.BoardColumn MapColumnToAdo(BoardColumn c) =>
        new()
        {
            Name = c.Name,
            ColumnType = (Adob.BoardColumnType)(int)c.ColumnType,
            IsSplit = c.IsSplit,
            ItemLimit = c.ItemLimit,
            Description = c.Description,
            StateMappings = c.StateMappings
                .ToDictionary(m => m.WorkItemType, m => m.State)
        };

    // ADO SDK taskboard columns have no explicit ColumnType — infer from position within the collection.
    private static TaskboardColumn MapTaskboardColumn(AdoTask.TaskboardColumn col, int index, int total)
    {
        var columnType = index == 0 ? BoardColumnType.Incoming
            : (index == total - 1 ? BoardColumnType.Outgoing : BoardColumnType.InProgress);
        return new TaskboardColumn(
            Name: col.Name ?? string.Empty,
            ColumnType: columnType,
            Order: col.Order,
            StateMappings: (col.Mappings ?? [])
                .Select(m => new BoardColumnStateMapping(
                    m.WorkItemType ?? string.Empty,
                    m.State ?? string.Empty))
                .ToList());
    }

    private static BacklogLevelType MapBacklogType(Adob.BacklogType type) =>
        type switch
        {
            Adob.BacklogType.Portfolio => BacklogLevelType.Portfolio,
            Adob.BacklogType.Requirement => BacklogLevelType.Requirement,
            Adob.BacklogType.Task => BacklogLevelType.Task,
            _ => BacklogLevelType.Requirement
        };
}
