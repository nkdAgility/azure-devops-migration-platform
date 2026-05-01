using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Options;

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
        MigrationEndpointOptions endpoint,
        string project,
        CancellationToken cancellationToken = default)
        => Task.FromResult(SimulatedReposPerProject);
}
