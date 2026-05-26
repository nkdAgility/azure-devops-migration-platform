// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Creates WorkItem resolution processors wired to a specific import target,
/// id-map store, checkpointing service, identity lookup tool, and artefact store.
/// Injected into module classes to avoid constructing infrastructure types directly.
/// </summary>
public interface IWorkItemResolutionProcessorFactory
{
    /// <summary>
    /// Creates a new <see cref="IWorkItemResolutionProcessor"/> for the given import context.
    /// </summary>
    IWorkItemResolutionProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        string organisation,
        string project);

    /// <summary>
    /// Creates a new <see cref="IWorkItemResolutionProcessor"/> with NodeTranslation context
    /// for area/iteration path translation.
    /// </summary>
    IWorkItemResolutionProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        string organisation,
        string project,
        DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping? nodeStructureContext);
}
