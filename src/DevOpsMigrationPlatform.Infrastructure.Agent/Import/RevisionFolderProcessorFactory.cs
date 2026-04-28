#if !NET481
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Creates <see cref="RevisionFolderProcessor"/> instances for the given import-time collaborators.
/// Hides the <see cref="ILoggerFactory"/> dependency from the interface contract.
/// </summary>
public sealed class RevisionFolderProcessorFactory : IRevisionFolderProcessorFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IMigrationMetrics? _metrics;
    private readonly INodeTranslationTool? _nodeStructureTool;

    public RevisionFolderProcessorFactory(
        ILoggerFactory loggerFactory,
        IMigrationMetrics? metrics = null,
        INodeTranslationTool? nodeStructureTool = null)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _metrics = metrics;
        _nodeStructureTool = nodeStructureTool;
    }

    /// <inheritdoc/>
    public IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityMappingService identityMapping,
        IArtefactStore artefactStore)
        => Create(target, idMapStore, checkpointing, identityMapping, artefactStore, nodeStructureContext: null);

    /// <inheritdoc/>
    public IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityMappingService identityMapping,
        IArtefactStore artefactStore,
        ProjectMapping? nodeStructureContext)
        => new RevisionFolderProcessor(
            target,
            idMapStore,
            checkpointing,
            identityMapping,
            artefactStore,
            _loggerFactory.CreateLogger<RevisionFolderProcessor>(),
            _metrics,
            nodeStructureTool: _nodeStructureTool,
            nodeStructureContext: nodeStructureContext);
}
#endif
