// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
    private readonly IFieldTransformTool? _fieldTransformTool;
    private readonly IPackageAccess _package;
    private readonly NodeTranslationOptions? _nodeStructureOptions;

    public RevisionFolderProcessorFactory(
        ILoggerFactory loggerFactory,
        IPackageAccess package,
        IPlatformMetrics? metrics = null,
        IFieldTransformTool? fieldTransformTool = null,
        INodeTranslationTool? nodeStructureTool = null,
        IOptions<NodeTranslationOptions>? nodeStructureOptions = null)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _package = package ?? throw new System.ArgumentNullException(nameof(package));
        _metrics = metrics;
        _fieldTransformTool = fieldTransformTool;
        _nodeStructureTool = nodeStructureTool;
        _nodeStructureOptions = nodeStructureOptions?.Value;
    }

    /// <inheritdoc/>
    public IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        string organisation,
        string project)
        => Create(target, idMapStore, checkpointing, identityLookupTool, organisation, project, nodeStructureContext: null);

    /// <inheritdoc/>
    public IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        string organisation,
        string project,
        ProjectMapping? nodeStructureContext)
        => new RevisionFolderProcessor(
            target,
            idMapStore,
            checkpointing,
            identityLookupTool,
            _loggerFactory.CreateLogger<RevisionFolderProcessor>(),
            organisation,
            project,
            _metrics,
            fieldTransformTool: _fieldTransformTool,
            nodeStructureTool: _nodeStructureTool,
            nodeStructureContext: nodeStructureContext,
            nodeStructureOptions: _nodeStructureOptions,
            package: _package);
}
#endif
