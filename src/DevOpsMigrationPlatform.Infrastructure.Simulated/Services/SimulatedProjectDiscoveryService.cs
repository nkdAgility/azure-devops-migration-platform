using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Services;
using DevOpsMigrationPlatform.Abstractions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Services;

/// <summary>
/// Simulated implementation of <see cref="IProjectDiscoveryService"/>.
/// Returns project names from the <see cref="SimulatedEndpointOptions.Generator"/> configuration,
/// or a single <c>"SimulatedProject"</c> placeholder when no projects are configured.
/// No network calls are made.
/// </summary>
public sealed class SimulatedProjectDiscoveryService : IProjectDiscoveryService
{
    /// <inheritdoc/>
    public Task<List<string>> DiscoverProjectsAsync(
        MigrationEndpointOptions endpoint,
        CancellationToken cancellationToken = default)
    {
        if (endpoint is SimulatedEndpointOptions simulated
            && simulated.Generator?.Projects is { Count: > 0 } projects)
        {
            var projectNames = projects
                .Select(p => p.Name)
                .Where(n => !string.IsNullOrEmpty(n))
                .ToList();

            if (projectNames.Count > 0)
                return Task.FromResult(projectNames);
        }

        return Task.FromResult(new List<string> { "SimulatedProject" });
    }
}
