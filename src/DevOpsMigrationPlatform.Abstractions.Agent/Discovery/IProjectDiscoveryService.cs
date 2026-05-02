using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Discovery;

/// <summary>
/// Enumerates team projects in an Azure DevOps organisation or TFS collection.
/// </summary>
public interface IProjectDiscoveryService
{
    /// <summary>
    /// Returns the names of all team projects accessible with the given endpoint.
    /// </summary>
    Task<List<string>> DiscoverProjectsAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default);
}
