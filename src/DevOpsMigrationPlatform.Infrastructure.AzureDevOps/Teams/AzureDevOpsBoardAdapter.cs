// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Teams;

/// <summary>
/// Azure DevOps implementation of <see cref="ITeamBoardAdapter"/>.
/// Phase 4 stub — all methods throw <see cref="NotImplementedException"/>.
/// The real implementation is delivered in later stories (US1 ADO integration).
/// </summary>
public sealed class AzureDevOpsBoardAdapter : ITeamBoardAdapter
{
    private static NotImplementedException NotYetImplemented([CallerMemberName] string? member = null)
        => new($"AzureDevOps board adapter not yet implemented — Phase 4 stub. Called: {member}");

    public IAsyncEnumerable<BoardConfig> GetBoardsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotYetImplemented();

    public Task<CardRuleSettings?> GetCardRuleSettingsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => throw NotYetImplemented();

    public IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotYetImplemented();

    public IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotYetImplemented();

    public Task UpdateBoardColumnsAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardColumn> columns, CancellationToken ct)
        => throw NotYetImplemented();

    public Task UpdateSwimLanesAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardSwimLane> swimLanes, CancellationToken ct)
        => throw NotYetImplemented();

    public Task UpdateCardRuleSettingsAsync(
        string project, string teamId, string boardName,
        CardRuleSettings? rules, CancellationToken ct)
        => throw NotYetImplemented();

    public Task UpdateTaskboardColumnsAsync(
        string project, string teamId,
        IReadOnlyList<TaskboardColumn> columns, CancellationToken ct)
        => throw NotYetImplemented();

    public Task<IReadOnlyList<BoardColumn>> GetBoardColumnsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => throw NotYetImplemented();

    public Task<IReadOnlyList<BoardSwimLane>> GetBoardSwimLanesAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => throw NotYetImplemented();

    public Task<IReadOnlyList<TaskboardColumn>> GetCurrentTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotYetImplemented();
}
