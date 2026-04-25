namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Creates <see cref="IPhaseTrackingService"/> instances bound to a specific <see cref="IStateStore"/>.
/// Injected into worker classes that receive a per-operation state store at runtime.
/// </summary>
public interface IPhaseTrackingServiceFactory
{
    /// <summary>
    /// Creates a new <see cref="IPhaseTrackingService"/> backed by the given <paramref name="stateStore"/>.
    /// </summary>
    IPhaseTrackingService Create(IStateStore stateStore);
}
