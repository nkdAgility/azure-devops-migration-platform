// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Import;

/// <summary>
/// Backward-compatible alias for the renamed <see cref="WorkItemResolutionProcessor"/>.
/// </summary>
public sealed class RevisionFolderProcessor : WorkItemResolutionProcessor
{
    public RevisionFolderProcessor(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        ILogger<RevisionFolderProcessor> logger,
        string organisation,
        string project,
        IPlatformMetrics? metrics = null,
        string? jobId = null,
        IFieldTransformTool? fieldTransformTool = null,
        INodeTranslationTool? nodeStructureTool = null,
        ProjectMapping? nodeStructureContext = null,
        NodeTranslationOptions? nodeStructureOptions = null,
        IPackageAccess? package = null,
        AttachmentReplayService? attachmentReplayService = null,
        EmbeddedImageReplayService? embeddedImageReplayService = null)
        : base(
            target,
            idMapStore,
            checkpointing,
            identityLookupTool,
            logger,
            organisation,
            project,
            metrics,
            jobId,
            fieldTransformTool,
            nodeStructureTool,
            nodeStructureContext,
            nodeStructureOptions,
            package,
            attachmentReplayService,
            embeddedImageReplayService)
    {
    }
}
