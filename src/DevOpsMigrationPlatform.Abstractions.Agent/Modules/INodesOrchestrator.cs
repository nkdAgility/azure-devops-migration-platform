// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Context;
using DevOpsMigrationPlatform.Abstractions.Agent.Export;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Telemetry;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
using DevOpsMigrationPlatform.Abstractions.Agent.Validation;
using DevOpsMigrationPlatform.Abstractions.Validation;

namespace DevOpsMigrationPlatform.Abstractions.Agent.Modules;

/// <summary>
/// Orchestrates classification-tree (node) export, import, and validation operations.
/// </summary>
public interface INodesOrchestrator
{
    Task ExportAsync(
        IClassificationTreeCapture capture,
        ExportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        CancellationToken ct);

#if !NET481
    Task ImportAsync(
        ImportContext context,
        ISourceEndpointInfo sourceEndpointInfo,
        ITargetEndpointInfo targetEndpointInfo,
        ICheckpointingServiceFactory? checkpointingFactory,
        bool replicateSourceTree,
        CancellationToken ct);

    /// <summary>
    /// Reads Nodes/referenced-paths.json and ensures all translated paths exist in the target.
    /// Called by WorkItemsModule before work item import. No-op when AutoCreateNodes is false.
    /// </summary>
    Task EnsureReferencedPathsAsync(
        ProjectMapping context,
        IPackageAccess package,
        string organisation,
        string project,
        CancellationToken ct,
        IPlatformMetrics? metrics = null,
        string? jobId = null);
#endif

    Task ValidateAsync(
        IPackageAccess package,
        string organisation,
        string project,
        ValidationContext context,
        CancellationToken ct);
}
