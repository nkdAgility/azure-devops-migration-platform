// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Inventory;

/// <summary>
/// Azure DevOps implementation of <see cref="IRepoDiscoveryService"/>.
/// Counts Git repositories in a team project via the Git REST API.
/// </summary>
internal sealed class AzureDevOpsRepoDiscoveryService : IRepoDiscoveryService
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsRepoDiscoveryService(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task<int> CountReposAsync(
        OrganisationEndpoint endpoint,
        string project,
        CancellationToken cancellationToken = default)
    {
        var gitClient = await _clientFactory.CreateGitClientAsync(endpoint, cancellationToken);
        var repos = await gitClient.GetRepositoriesAsync(project, cancellationToken: cancellationToken);
        return repos?.Count ?? 0;
    }
}
