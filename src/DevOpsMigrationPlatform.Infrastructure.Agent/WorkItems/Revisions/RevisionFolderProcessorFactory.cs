// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Revisions;

/// <summary>
/// Creates <see cref="WorkItemResolutionProcessor"/> instances for the given import-time collaborators.
/// Hides the <see cref="ILoggerFactory"/> dependency from the interface contract.
/// </summary>
public sealed class RevisionFolderProcessorFactory : IWorkItemResolutionProcessorFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly IPlatformMetrics? _metrics;
    private readonly INodeTranslationTool? _nodeStructureTool;
    private readonly IFieldTransformTool? _fieldTransformTool;
    private readonly IPackageAccess _package;
    private readonly NodeTranslationOptions? _nodeStructureOptions;
    private readonly IEnumerable<IModuleExtension> _moduleExtensions;
    private readonly Abstractions.Agent.Modules.IIdentitiesOrchestrator? _identitiesOrchestrator;

    public RevisionFolderProcessorFactory(
        ILoggerFactory loggerFactory,
        IPackageAccess package,
        IEnumerable<IModuleExtension> moduleExtensions,
        IPlatformMetrics? metrics = null,
        IFieldTransformTool? fieldTransformTool = null,
        INodeTranslationTool? nodeStructureTool = null,
        IOptions<NodeTranslationOptions>? nodeStructureOptions = null,
        Abstractions.Agent.Modules.IIdentitiesOrchestrator? identitiesOrchestrator = null)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _package = package ?? throw new System.ArgumentNullException(nameof(package));
        _moduleExtensions = moduleExtensions ?? throw new System.ArgumentNullException(nameof(moduleExtensions));
        _metrics = metrics;
        _fieldTransformTool = fieldTransformTool;
        _nodeStructureTool = nodeStructureTool;
        _nodeStructureOptions = nodeStructureOptions?.Value;
        _identitiesOrchestrator = identitiesOrchestrator;
    }

    /// <inheritdoc/>
    public IWorkItemResolutionProcessor Create(
        IWorkItemTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityTranslationTool? identityTranslationTool,
        string organisation,
        string project)
        => Create(target, idMapStore, checkpointing, identityTranslationTool, organisation, project, nodeStructureContext: null);

    /// <inheritdoc/>
    public IWorkItemResolutionProcessor Create(
        IWorkItemTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityTranslationTool? identityTranslationTool,
        string organisation,
        string project,
        ProjectMapping? nodeStructureContext,
        bool embeddedImagesEnabledByLever = true)
    {
        var embeddedImagesOptions = embeddedImagesEnabledByLever
            ? (EmbeddedImagesExtensionOptionsConfig?)null
            : new EmbeddedImagesExtensionOptionsConfig { Enabled = false };

        return new WorkItemResolutionProcessor(
            target,
            idMapStore,
            checkpointing,
            identityTranslationTool,
            _loggerFactory.CreateLogger<WorkItemResolutionProcessor>(),
            organisation,
            project,
            _moduleExtensions,
            metrics: _metrics,
            fieldTransformTool: _fieldTransformTool,
            nodeStructureTool: _nodeStructureTool,
            nodeStructureContext: nodeStructureContext,
            nodeStructureOptions: _nodeStructureOptions,
            package: _package,
            embeddedImagesOptions: embeddedImagesOptions,
            // ADR-0026 (TC-M1): the pure translation tool receives the orchestrator-owned
            // resolved map as data at translate time.
            identityTranslationMapProvider: _identitiesOrchestrator is { } identities
                ? () => identities.TranslationMap
                : null);
    }
}
