using System;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Discovery;

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
        MigrationEndpointOptions endpoint,
        string project,
        CancellationToken cancellationToken = default)
    {
        var orgEndpoint = endpoint.ToOrganisationEndpoint();
        var gitClient = await _clientFactory.CreateGitClientAsync(orgEndpoint, cancellationToken);
        var repos = await gitClient.GetRepositoriesAsync(project, cancellationToken: cancellationToken);
        return repos?.Count ?? 0;
    }
}
