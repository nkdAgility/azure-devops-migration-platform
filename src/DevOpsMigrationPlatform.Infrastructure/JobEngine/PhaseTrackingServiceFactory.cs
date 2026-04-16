using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.JobEngine;

/// <summary>
/// Creates <see cref="PhaseTrackingService"/> instances bound to a per-operation <see cref="IStateStore"/>.
/// </summary>
public sealed class PhaseTrackingServiceFactory : IPhaseTrackingServiceFactory
{
    /// <inheritdoc/>
    public IPhaseTrackingService Create(IStateStore stateStore)
        => new PhaseTrackingService(stateStore);
}
