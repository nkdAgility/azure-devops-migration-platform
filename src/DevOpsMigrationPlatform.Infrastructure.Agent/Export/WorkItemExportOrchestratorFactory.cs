// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Configuration;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Export;

public sealed class WorkItemExportOrchestratorFactory : IWorkItemExportOrchestratorFactory
{
    public IWorkItemExportOrchestrator Create(
        IPackageAccess package,
        string organisation,
        string project,
        ICheckpointingService checkpointingService,
        IAttachmentBinarySource? attachmentBinarySource,
        IProgressSink? progressSink,
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
        IReferencedPathTracker? referencedPathTracker = null,
        IReadOnlyList<IModuleExtension>? exportExtensions = null,
        MigrationEndpointOptions? endpoint = null)
    {
        return new WorkItemExportOrchestrator(
            package,
            organisation,
            project,
            checkpointingService,
            attachmentBinarySource,
            progressSink,
            endpoint: endpoint,
            fetchService: fetchService,
            filterOptions: filterOptions,
            metrics: metrics,
            jobId: jobId,
            taskId: taskId,
            logger: logger,
            wiqlQuery: wiqlQuery,
            discoveryService: discoveryService,
            exportProgressStoreFactory: exportProgressStoreFactory,
            packageUri: packageUri
#if !NET481
            ,
            referencedPathTracker: referencedPathTracker,
            exportExtensions: exportExtensions
#endif
            );
    }
}
