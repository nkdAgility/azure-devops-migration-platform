using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Services;

/// <summary>
/// Simulated implementation of <see cref="IProjectDiscoveryService"/>.
/// Returns project names encoded in the endpoint URL using the pattern
/// <c>simulated://projects/ProjectA,ProjectB</c>, or a single <c>"SimulatedProject"</c>
/// placeholder when no projects are encoded.
/// No network calls are made.
/// </summary>
public sealed class SimulatedProjectDiscoveryService : IProjectDiscoveryService
{
    /// <inheritdoc/>
    public Task<List<string>> DiscoverProjectsAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        // Projects may be encoded in the URL: simulated://projects/ProjectA,ProjectB
        if (endpoint?.ResolvedUrl?.StartsWith("simulated://projects/", System.StringComparison.OrdinalIgnoreCase) == true)
        {
            var projectList = endpoint.ResolvedUrl["simulated://projects/".Length..]
                .Split(',', System.StringSplitOptions.RemoveEmptyEntries)
                .Select(p => p.Trim())
                .Where(p => !string.IsNullOrEmpty(p))
                .ToList();

            if (projectList.Count > 0)
                return Task.FromResult(projectList);
        }

        // Default: return a single placeholder project.
        return Task.FromResult(new List<string> { "SimulatedProject" });
    }
}
