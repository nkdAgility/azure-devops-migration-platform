// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent;
using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.WorkItems.Extensions;

/// <summary>
/// Extension context passed to <see cref="IModuleExtension.ExportAsync"/> for each
/// work item revision during the export phase.
/// </summary>
public sealed class WorkItemRevisionExportContext : IExtensionContext
{
    /// <inheritdoc/>
    public string Organisation { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string ProjectName { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string EntityId { get; init; } = string.Empty;

    /// <inheritdoc/>
    public string? TargetEntityId => null;

    /// <inheritdoc/>
    public IPackageAccess Package { get; init; } = null!;

    /// <summary>Source work item identifier.</summary>
    public int WorkItemId { get; init; }

    /// <summary>Zero-based index of this revision in the work item's history.</summary>
    public int RevisionIndex { get; init; }

    /// <summary>The full revision payload being exported.</summary>
    public WorkItemRevision Revision { get; init; } = null!;

    /// <summary>
    /// Package-relative folder path for this revision (e.g.
    /// <c>2024-01-15/00638412345678901234-42-3/</c>). Attachment binaries and
    /// supplementary JSON files (comment.json) are written into this folder.
    /// </summary>
    public string FolderPath { get; init; } = string.Empty;

    /// <summary>
    /// Source endpoint options, if available. Used by extensions that need to contact the
    /// source system (e.g. <see cref="CommentsWorkItemExtension"/> fetching comment versions).
    /// Null when not applicable or not provided.
    /// </summary>
    public MigrationEndpointOptions? SourceEndpoint { get; init; }
}
