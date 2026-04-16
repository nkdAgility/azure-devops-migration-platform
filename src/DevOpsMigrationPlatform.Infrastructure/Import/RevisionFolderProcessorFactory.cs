#if !NET481
using DevOpsMigrationPlatform.Abstractions;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Import;

/// <summary>
/// Creates <see cref="RevisionFolderProcessor"/> instances for the given import-time collaborators.
/// Hides the <see cref="ILoggerFactory"/> dependency from the interface contract.
/// </summary>
public sealed class RevisionFolderProcessorFactory : IRevisionFolderProcessorFactory
{
    private readonly ILoggerFactory _loggerFactory;

    public RevisionFolderProcessorFactory(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
    }

    /// <inheritdoc/>
    public IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityMappingService identityMapping,
        IArtefactStore artefactStore)
        => new RevisionFolderProcessor(
            target,
            idMapStore,
            checkpointing,
            identityMapping,
            artefactStore,
            _loggerFactory.CreateLogger<RevisionFolderProcessor>());
}
#endif
