// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Teams;

/// <summary>
/// Null adapter for TFS — implements <see cref="ITeamBoardAdapter"/> by throwing
/// <see cref="NotSupportedException"/> on every method. Registered so that
/// <c>BoardConfigTeamExtension</c> can be constructed via DI; the capability check
/// (<see cref="TfsConnectorCapabilityProvider"/>) fires before any adapter call is made.
/// </summary>
public sealed class TfsNullBoardAdapter : ITeamBoardAdapter
{
    private static NotSupportedException NotSupported([CallerMemberName] string? member = null)
        => new($"TFS Object Model does not support board configuration. Called: {member}");

    public IAsyncEnumerable<BoardConfig> GetBoardsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotSupported();

    public Task<CardRuleSettings?> GetCardRuleSettingsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => throw NotSupported();

    public IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotSupported();

    public IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotSupported();

    public Task UpdateBoardColumnsAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardColumn> columns, CancellationToken ct)
        => throw NotSupported();

    public Task UpdateSwimLanesAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardSwimLane> swimLanes, CancellationToken ct)
        => throw NotSupported();

    public Task UpdateCardRuleSettingsAsync(
        string project, string teamId, string boardName,
        CardRuleSettings? rules, CancellationToken ct)
        => throw NotSupported();

    public Task UpdateTaskboardColumnsAsync(
        string project, string teamId,
        IReadOnlyList<TaskboardColumn> columns, CancellationToken ct)
        => throw NotSupported();

    public Task<IReadOnlyList<BoardColumn>> GetBoardColumnsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => throw NotSupported();

    public Task<IReadOnlyList<BoardSwimLane>> GetBoardSwimLanesAsync(
        string project, string teamId, string boardName, CancellationToken ct)
        => throw NotSupported();

    public Task<IReadOnlyList<TaskboardColumn>> GetCurrentTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotSupported();

    public Task<TargetBoardSnapshot> GetBoardConfigSnapshotAsync(
        string project, string teamId, CancellationToken ct)
        => throw NotSupported();
}
