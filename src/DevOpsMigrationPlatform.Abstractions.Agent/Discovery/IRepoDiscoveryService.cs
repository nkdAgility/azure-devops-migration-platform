// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Counts the Git repositories in a single team project.
/// </summary>
public interface IRepoDiscoveryService
{
    /// <summary>
    /// Returns the number of Git repositories in <paramref name="project"/>.
    /// </summary>
    Task<int> CountReposAsync(
        OrganisationEndpoint endpoint,
        string project,
        CancellationToken cancellationToken = default);
}
