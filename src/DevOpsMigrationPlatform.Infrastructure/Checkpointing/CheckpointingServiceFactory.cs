using DevOpsMigrationPlatform.Abstractions;

namespace DevOpsMigrationPlatform.Infrastructure.Checkpointing;

/// <summary>
/// Creates <see cref="CheckpointingService"/> instances bound to a per-operation <see cref="IStateStore"/>.
/// </summary>
public sealed class CheckpointingServiceFactory : ICheckpointingServiceFactory
{
    /// <inheritdoc/>
    public ICheckpointingService Create(IStateStore stateStore)
        => new CheckpointingService(stateStore);
}
