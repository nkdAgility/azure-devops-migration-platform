// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel.Teams;

/// <summary>
/// Null adapter for TFS — implements <see cref="ITeamBoardAdapter"/> per the
/// reduced-capability contract result pattern (see docs/connector-model.md and
/// <c>TfsIdentityAdapter</c>): reads return empty sequences/results, writes are
/// no-ops, and every call logs a structured <see cref="LogLevel.Warning"/>.
/// It MUST NOT throw. Registered so that <c>BoardConfigTeamExtension</c> can be
/// constructed via DI; the capability check (<see cref="TfsConnectorCapabilityProvider"/>)
/// fires before any adapter call is made.
/// </summary>
public sealed class TfsNullBoardAdapter : ITeamBoardAdapter
{
    private readonly ILogger<TfsNullBoardAdapter> _logger;

    public TfsNullBoardAdapter(ILogger<TfsNullBoardAdapter> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private void WarnNotSupported([CallerMemberName] string? member = null)
        => _logger.LogWarning(
            "[Teams/TFS] Board configuration is not supported by the TFS Object Model; " +
            "{Operation} returns an empty result — board settings will be skipped for this connector.",
            member);

    private static async IAsyncEnumerable<T> EmptyAsync<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    public IAsyncEnumerable<BoardConfig> GetBoardsAsync(
        string project, string teamId, CancellationToken ct)
    {
        WarnNotSupported();
        return EmptyAsync<BoardConfig>();
    }

    public Task<CardRuleSettings?> GetCardRuleSettingsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.FromResult<CardRuleSettings?>(null);
    }

    public IAsyncEnumerable<BacklogMetadata> GetBacklogsAsync(
        string project, string teamId, CancellationToken ct)
    {
        WarnNotSupported();
        return EmptyAsync<BacklogMetadata>();
    }

    public IAsyncEnumerable<TaskboardColumn> GetTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
    {
        WarnNotSupported();
        return EmptyAsync<TaskboardColumn>();
    }

    public Task UpdateBoardColumnsAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardColumn> columns, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.CompletedTask;
    }

    public Task UpdateSwimLanesAsync(
        string project, string teamId, string boardName,
        IReadOnlyList<BoardSwimLane> swimLanes, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.CompletedTask;
    }

    public Task UpdateCardRuleSettingsAsync(
        string project, string teamId, string boardName,
        CardRuleSettings? rules, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.CompletedTask;
    }

    public Task UpdateTaskboardColumnsAsync(
        string project, string teamId,
        IReadOnlyList<TaskboardColumn> columns, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<BoardColumn>> GetBoardColumnsAsync(
        string project, string teamId, string boardName, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.FromResult<IReadOnlyList<BoardColumn>>(Array.Empty<BoardColumn>());
    }

    public Task<IReadOnlyList<BoardSwimLane>> GetBoardSwimLanesAsync(
        string project, string teamId, string boardName, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.FromResult<IReadOnlyList<BoardSwimLane>>(Array.Empty<BoardSwimLane>());
    }

    public Task<IReadOnlyList<TaskboardColumn>> GetCurrentTaskboardColumnsAsync(
        string project, string teamId, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.FromResult<IReadOnlyList<TaskboardColumn>>(Array.Empty<TaskboardColumn>());
    }

    public Task<TargetBoardSnapshot> GetBoardConfigSnapshotAsync(
        string project, string teamId, CancellationToken ct)
    {
        WarnNotSupported();
        return Task.FromResult(new TargetBoardSnapshot());
    }
}
