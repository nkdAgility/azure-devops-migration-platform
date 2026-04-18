using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Infrastructure.Import;

namespace DevOpsMigrationPlatform.Infrastructure.Simulated.Import;

/// <summary>
/// <see cref="IWorkItemResolutionStrategyFactory"/> for the Simulated connector.
/// Always returns a <see cref="NullResolutionStrategy"/> — no external service is needed.
/// </summary>
public sealed class SimulatedResolutionStrategyFactory : IWorkItemResolutionStrategyFactory
{
    /// <inheritdoc/>
    public Task<IWorkItemResolutionStrategy> CreateAsync(
        WorkItemResolutionStrategyOptions options,
        IWorkItemImportTarget target,
        string project,
        string accessToken,
        CancellationToken ct)
    {
        return Task.FromResult<IWorkItemResolutionStrategy>(new NullResolutionStrategy());
    }
}

