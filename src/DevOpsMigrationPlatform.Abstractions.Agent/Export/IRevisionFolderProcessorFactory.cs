#if !NET481
namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Creates <see cref="IRevisionFolderProcessor"/> instances wired to a specific import target,
/// id-map store, checkpointing service, identity mapping service, and artefact store.
/// Injected into module classes to avoid constructing infrastructure types directly.
/// </summary>
public interface IRevisionFolderProcessorFactory
{
    /// <summary>
    /// Creates a new <see cref="IRevisionFolderProcessor"/> for the given import context.
    /// </summary>
    IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityMappingService identityMapping,
        IArtefactStore artefactStore);
}
#endif
