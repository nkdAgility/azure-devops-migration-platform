// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

public interface IWorkItemExportOrchestratorFactory
{
    IWorkItemExportOrchestrator Create(
        IPackageAccess package,
        string organisation,
        string project,
        ICheckpointingService checkpointingService,
        IAttachmentBinarySource? attachmentBinarySource,
        IProgressSink? progressSink,
        IWorkItemCommentSourceFactory? inlineCommentSourceFactory,
        IWorkItemFetchService? fetchService,
        IReadOnlyList<WorkItemFieldFilterOptions>? filterOptions,
        IPlatformMetrics? metrics,
        string? jobId,
        string? taskId,
        Microsoft.Extensions.Logging.ILogger? logger,
        string? wiqlQuery,
        IWorkItemDiscoveryService? discoveryService,
        IExportProgressStoreFactory? exportProgressStoreFactory,
        string? packageUri,
        IReferencedPathTracker? referencedPathTracker = null);
}
