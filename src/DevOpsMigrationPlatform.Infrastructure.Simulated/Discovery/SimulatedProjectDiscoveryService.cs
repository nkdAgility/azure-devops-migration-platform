using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Organisations;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Discovery;

/// <summary>
/// Simulated implementation of <see cref="IProjectDiscoveryService"/>.
/// Returns project names from the injected <see cref="SimulatedGeneratorConfig"/>,
/// or a single <c>"SimulatedProject"</c> placeholder when no projects are configured.
/// No network calls are made.
/// </summary>
public sealed class SimulatedProjectDiscoveryService : IProjectDiscoveryService
{
    private readonly SimulatedGeneratorConfig? _generatorConfig;

    public SimulatedProjectDiscoveryService(SimulatedGeneratorConfig? generatorConfig = null)
    {
        _generatorConfig = generatorConfig;
    }

    /// <inheritdoc/>
    public Task<List<string>> DiscoverProjectsAsync(
        OrganisationEndpoint endpoint,
        CancellationToken cancellationToken = default)
    {
        if (_generatorConfig?.Projects is { Count: > 0 } projects)
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
