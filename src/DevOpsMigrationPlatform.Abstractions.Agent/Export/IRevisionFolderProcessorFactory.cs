// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

#if !NET481
using DevOpsMigrationPlatform.Abstractions.Agent.Import;
using DevOpsMigrationPlatform.Abstractions.Storage;
using DevOpsMigrationPlatform.Abstractions.Agent.Checkpointing;
using DevOpsMigrationPlatform.Abstractions.Agent.Tools;
namespace DevOpsMigrationPlatform.Abstractions.Agent.Export;

/// <summary>
/// Creates <see cref="IRevisionFolderProcessor"/> instances wired to a specific import target,
/// id-map store, checkpointing service, identity lookup tool, and artefact store.
/// Injected into module classes to avoid constructing infrastructure types directly.
/// </summary>
public interface IRevisionFolderProcessorFactory
{
    /// <summary>
    /// Creates a new <see cref="IRevisionFolderProcessor"/> for the given import context.
    /// </summary>
    IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        string organisation,
        string project);

    /// <summary>
    /// Creates a new <see cref="IRevisionFolderProcessor"/> with NodeTranslation context
    /// for area/iteration path translation.
    /// </summary>
    IRevisionFolderProcessor Create(
        IWorkItemImportTarget target,
        IIdMapStore idMapStore,
        ICheckpointingService checkpointing,
        IIdentityLookupTool? identityLookupTool,
        string organisation,
        string project,
        DevOpsMigrationPlatform.Abstractions.Agent.Tools.ProjectMapping? nodeStructureContext);
}
#endif

