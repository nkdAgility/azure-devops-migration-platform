// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Storage;
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
    private readonly IPlatformMetrics? _metrics;
    private readonly INodeTranslationTool? _nodeStructureTool;
    private readonly IPackageAccess _package;

    public RevisionFolderProcessorFactory(
        ILoggerFactory loggerFactory,
        IPackageAccess package,
        IPlatformMetrics? metrics = null,
        INodeTranslationTool? nodeStructureTool = null)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _package = package ?? throw new System.ArgumentNullException(nameof(package));
        _metrics = metrics;
        _nodeStructureTool = nodeStructureTool;
    }

    /// <inheritdoc/>
    public IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        IArtefactStore artefactStore)
        => Create(target, idMapStore, checkpointing, identityLookupTool, artefactStore, nodeStructureContext: null);

    /// <inheritdoc/>
    public IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        IArtefactStore artefactStore,
        ProjectMapping? nodeStructureContext)
        => new RevisionFolderProcessor(
            target,
            idMapStore,
            checkpointing,
            identityLookupTool,
            artefactStore,
            _loggerFactory.CreateLogger<RevisionFolderProcessor>(),
            _metrics,
            nodeStructureTool: _nodeStructureTool,
            nodeStructureContext: nodeStructureContext,
            package: _package);
}
#endif
