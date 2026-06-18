// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Discovery;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Streaming;
using Microsoft.Extensions.Logging;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

public interface IWorkItemExportOrchestratorFactory
{
    IWorkItemExportOrchestrator Create(
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
        ILogger? logger,
        string? wiqlQuery,
        IWorkItemDiscoveryService? discoveryService,
        IExportProgressStoreFactory? exportProgressStoreFactory,
        string? packageUri,
        IReferencedPathTracker? referencedPathTracker = null,
        IReadOnlyList<IModuleExtension>? exportExtensions = null,
        MigrationEndpointOptions? endpoint = null);
}
