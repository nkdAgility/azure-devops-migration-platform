// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Attachments;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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
    private readonly LinksWorkItemExtension? _linksExtension;
    private readonly AttachmentsWorkItemExtension? _attachmentsExtension;
    private readonly CommentsWorkItemExtension? _commentsExtension;

    public RevisionFolderProcessorFactory(
        ILoggerFactory loggerFactory,
        IPackageAccess package,
        IPlatformMetrics? metrics = null,
        IFieldTransformTool? fieldTransformTool = null,
        INodeTranslationTool? nodeStructureTool = null,
        IOptions<NodeTranslationOptions>? nodeStructureOptions = null,
        LinksWorkItemExtension? linksExtension = null,
        AttachmentsWorkItemExtension? attachmentsExtension = null,
        CommentsWorkItemExtension? commentsExtension = null)
    {
        _loggerFactory = loggerFactory ?? throw new System.ArgumentNullException(nameof(loggerFactory));
        _package = package ?? throw new System.ArgumentNullException(nameof(package));
        _metrics = metrics;
        _fieldTransformTool = fieldTransformTool;
        _nodeStructureTool = nodeStructureTool;
        _nodeStructureOptions = nodeStructureOptions?.Value;
        _linksExtension = linksExtension;
        _attachmentsExtension = attachmentsExtension;
        _commentsExtension = commentsExtension;
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
        bool attachmentsEnabledByLever = true,
        bool linksEnabledByLever = true,
        bool embeddedImagesEnabledByLever = true)
    {
        var linksExtension = linksEnabledByLever
            ? _linksExtension
            : DisabledLinks();
        var attachmentsExtension = attachmentsEnabledByLever
            ? _attachmentsExtension
            : DisabledAttachments();
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
            _metrics,
            fieldTransformTool: _fieldTransformTool,
            nodeStructureTool: _nodeStructureTool,
            nodeStructureContext: nodeStructureContext,
            nodeStructureOptions: _nodeStructureOptions,
            package: _package,
            linksExtension: linksExtension,
            attachmentsExtension: attachmentsExtension,
            commentsExtension: _commentsExtension,
            embeddedImagesOptions: embeddedImagesOptions);
    }

    private static LinksWorkItemExtension DisabledLinks()
        => new(Options.Create(new LinksExtensionOptions { Enabled = false }));

    private AttachmentsWorkItemExtension DisabledAttachments()
        => new(Options.Create(new AttachmentsExtensionOptions { Enabled = false }),
               NullLogger<AttachmentReplayTool>.Instance);
}
