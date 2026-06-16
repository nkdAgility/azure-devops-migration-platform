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

    private static readonly BoardConfig[] s_boards =
    [
        new BoardConfig("Stories", s_storiesColumns, []),
        new BoardConfig("Epics",   s_epicsColumns,   []),
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
        => Task.FromResult<CardRuleSettings?>(null); // US3

    public async IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(
        string project, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // US4 — return empty sequence
        await Task.CompletedTask;
        yield break;
    }

    public async IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(
        string project, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        // US5 — return empty sequence
        await Task.CompletedTask;
        yield break;
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
