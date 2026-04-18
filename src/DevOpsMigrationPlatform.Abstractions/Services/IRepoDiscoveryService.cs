using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Services;

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
