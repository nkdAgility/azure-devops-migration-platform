// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Teams;

/// <summary>
/// Simulated <see cref="ITeamBoardAdapter"/> that returns deterministic boards for testing.
/// Update methods capture calls in-memory for assertion in tests.
/// </summary>
public sealed class SimulatedBoardAdapter : ITeamBoardAdapter
{
    // -------------------------------------------------------------------------
    // Captured import calls — for test assertion
    // -------------------------------------------------------------------------

    public List<(string Project, string TeamId, string BoardName, IReadOnlyList<BoardColumn> Columns)>
        UpdateBoardColumnsCalls { get; } = [];

    public List<(string Project, string TeamId, string BoardName, IReadOnlyList<BoardSwimLane> SwimLanes)>
        UpdateSwimLanesCalls { get; } = [];

    public List<(string Project, string TeamId, string BoardName, CardRuleSettings? Rules)>
        UpdateCardRuleSettingsCalls { get; } = [];

    public List<(string Project, string TeamId, IReadOnlyList<TaskboardColumn> Columns)>
        UpdateTaskboardColumnsCalls { get; } = [];

    // -------------------------------------------------------------------------
    // Deterministic seed data
    // -------------------------------------------------------------------------

    private static readonly BoardColumn[] s_storiesColumns =
    [
        new BoardColumn("Proposed",  BoardColumnType.Incoming,   null, false, null, []),
        new BoardColumn("Active",    BoardColumnType.InProgress, 5,    false, null, []),
        new BoardColumn("Resolved",  BoardColumnType.Outgoing,   null, false, null, []),
    ];

    private static readonly BoardColumn[] s_epicsColumns =
    [
        new BoardColumn("New",         BoardColumnType.Incoming,   null, false, null, []),
        new BoardColumn("In Progress", BoardColumnType.InProgress, null, false, null, []),
        new BoardColumn("Done",        BoardColumnType.Outgoing,   null, false, null, []),
    ];

    private static readonly BoardSwimLane[] s_storiesSwimLanes =
    [
        new BoardSwimLane("swimlane-1", "Expedite"),
        new BoardSwimLane("swimlane-2", "Normal"),
    ];

    private static readonly BoardConfig[] s_boards =
    [
        new BoardConfig("Stories", s_storiesColumns, s_storiesSwimLanes),
        new BoardConfig("Epics",   s_epicsColumns,   []),
    ];

    private static readonly CardRuleSettings s_storiesCardRules = new(
    [
        new CardRule("High Priority", "#ff0000", true, "[Priority] = 1"),
    ]);

    private static readonly BacklogMetadata[] s_backlogs =
    [
        new BacklogMetadata("Epics",   "Microsoft.EpicCategory",        BacklogLevelType.Portfolio,   1),
        new BacklogMetadata("Stories", "Microsoft.RequirementCategory",  BacklogLevelType.Requirement, 2),
    ];

    private static readonly TaskboardColumn[] s_taskboardColumns =
    [
        new TaskboardColumn("To Do",       BoardColumnType.Incoming,   0, []),
        new TaskboardColumn("In Progress", BoardColumnType.InProgress, 1, []),
        new TaskboardColumn("Done",        BoardColumnType.Outgoing,   2, []),
    ];

    // -------------------------------------------------------------------------
    // Export (read) methods
    // -------------------------------------------------------------------------

    public async IAsyncEnumerable<BoardConfig> GetBoardsAsync(
        string project,
        string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var board in s_boards)
        {
            ct.ThrowIfCancellationRequested();
            yield return board;
            await Task.CompletedTask;
        }
    }

    public Task<CardRuleSettings?> GetCardRuleSettingsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => Task.FromResult<CardRuleSettings?>(boardName == "Stories" ? s_storiesCardRules : null);

    public async IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(
        string project, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var b in s_backlogs)
        {
            ct.ThrowIfCancellationRequested();
            yield return b;
            await Task.CompletedTask;
        }
    }

    public async IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(
        string project, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        foreach (var col in s_taskboardColumns)
        {
            ct.ThrowIfCancellationRequested();
            yield return col;
            await Task.CompletedTask;
        }
    }

    public Task<IReadOnlyList<BoardColumn>> GetBoardColumnsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
    {
        IReadOnlyList<BoardColumn> result = boardName == "Stories" ? s_storiesColumns : s_epicsColumns;
        return Task.FromResult(result);
    }

    public Task<IReadOnlyList<BoardSwimLane>> GetBoardSwimLanesAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<BoardSwimLane>>([]);

    public Task<IReadOnlyList<TaskboardColumn>> GetCurrentTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
        => Task.FromResult<IReadOnlyList<TaskboardColumn>>([]);

    public Task<TargetBoardSnapshot> GetBoardConfigSnapshotAsync(
        string project, string teamId, CancellationToken ct)
        => Task.FromResult(TargetBoardSnapshot.Empty);

    // -------------------------------------------------------------------------
    // Import (write) methods — capture for test assertion
    // -------------------------------------------------------------------------

    public Task UpdateBoardColumnsAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardColumn> columns, CancellationToken ct)
    {
        UpdateBoardColumnsCalls.Add((project, teamId, boardName, columns));
        return Task.CompletedTask;
    }

    public Task UpdateSwimLanesAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardSwimLane> swimLanes, CancellationToken ct)
    {
        UpdateSwimLanesCalls.Add((project, teamId, boardName, swimLanes));
        return Task.CompletedTask;
    }

    public Task UpdateCardRuleSettingsAsync(
        string project, string teamId, string boardName,
        CardRuleSettings? rules, CancellationToken ct)
    {
        UpdateCardRuleSettingsCalls.Add((project, teamId, boardName, rules));
        return Task.CompletedTask;
    }

    public Task UpdateTaskboardColumnsAsync(
        string project, string teamId,
        IReadOnlyList<TaskboardColumn> columns, CancellationToken ct)
    {
        UpdateTaskboardColumnsCalls.Add((project, teamId, columns));
        return Task.CompletedTask;
    }
}
