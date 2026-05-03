// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;

/// <summary>
/// Simulated implementation of <see cref="IRepoDiscoveryService"/>.
/// Returns a small deterministic count per project — no network calls are made.
/// </summary>
public sealed class SimulatedRepoDiscoveryService : IRepoDiscoveryService
{
    private const int SimulatedReposPerProject = 2;

    /// <inheritdoc/>
    public Task<int> CountReposAsync(
        OrganisationEndpoint endpoint,
        string project,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SimulatedReposPerProject);
}
