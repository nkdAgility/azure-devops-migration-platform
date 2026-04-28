#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Identity;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

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

    /// <summary>
    /// Creates a new <see cref="IRevisionFolderProcessor"/> with NodeStructure context
    /// for area/iteration path translation.
    /// </summary>
    IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityMappingService identityMapping,
        IArtefactStore artefactStore,
        DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping? nodeStructureContext);
}
#endif

