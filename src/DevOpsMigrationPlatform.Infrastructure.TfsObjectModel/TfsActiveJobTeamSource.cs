// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Teams;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;

namespace DevOpsMigrationPlatform.Infrastructure.TfsObjectModel;

/// <summary>
/// <see cref="ITeamSource"/> adapter for the TFS agent.
/// Delegates to the <see cref="ITeamSource"/> from the currently active
/// <see cref="TfsJobServices"/> held in <see cref="ActiveTfsJobServices"/>.
/// </summary>
public sealed class TfsActiveJobTeamSource : ITeamSource
{
    private readonly ActiveTfsJobServices _activeServices;

    public TfsActiveJobTeamSource(ActiveTfsJobServices activeServices)
    {
        _activeServices = activeServices ?? throw new ArgumentNullException(nameof(activeServices));
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(
        string projectName,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var source = _activeServices.Require().TeamSource;
        await foreach (var team in source.EnumerateTeamsAsync(projectName, ct).ConfigureAwait(false))
            yield return team;
    }

    /// <inheritdoc/>
    public Task<TeamSettings?> GetTeamSettingsAsync(
        string projectName, string teamId, CancellationToken ct)
        => _activeServices.Require().TeamSource.GetTeamSettingsAsync(projectName, teamId, ct);

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamIteration> GetTeamIterationsAsync(
        string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var source = _activeServices.Require().TeamSource;
        await foreach (var item in source.GetTeamIterationsAsync(projectName, teamId, ct).ConfigureAwait(false))
            yield return item;
    }

    /// <inheritdoc/>
    public async IAsyncEnumerable<TeamMember> GetTeamMembersAsync(
        string projectName, string teamId,
        [EnumeratorCancellation] CancellationToken ct)
    {
        var source = _activeServices.Require().TeamSource;
        await foreach (var member in source.GetTeamMembersAsync(projectName, teamId, ct).ConfigureAwait(false))
            yield return member;
    }

    /// <inheritdoc/>
    public Task<TeamCapacityEntry[]> GetTeamCapacityAsync(
        string projectName, string teamId, string iterationId, CancellationToken ct)
        => _activeServices.Require().TeamSource.GetTeamCapacityAsync(projectName, teamId, iterationId, ct);

    /// <inheritdoc/>
    public Task<TeamAreaPaths?> GetTeamAreaPathsAsync(
        string projectName, string teamId, CancellationToken ct)
        => _activeServices.Require().TeamSource.GetTeamAreaPathsAsync(projectName, teamId, ct);
}
