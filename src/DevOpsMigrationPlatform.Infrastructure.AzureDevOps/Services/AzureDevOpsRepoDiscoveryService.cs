using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Services;

/// <summary>
/// Azure DevOps implementation of <see cref="IRepoDiscoveryService"/>.
/// Counts Git repositories in a team project via the Git REST API.
/// </summary>
public sealed class AzureDevOpsRepoDiscoveryService : IRepoDiscoveryService
{
    private readonly IAzureDevOpsClientFactory _clientFactory;

    public AzureDevOpsRepoDiscoveryService(IAzureDevOpsClientFactory clientFactory)
    {
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
    }

    public async Task<int> CountReposAsync(
        string orgOrCollection,
        string project,
        string pat,
        CancellationToken cancellationToken = default)
    {
        var gitClient = await _clientFactory.CreateGitClientAsync(orgOrCollection, pat, cancellationToken);
        var repos = await gitClient.GetRepositoriesAsync(project, cancellationToken: cancellationToken);
        return repos?.Count ?? 0;
    }
}
