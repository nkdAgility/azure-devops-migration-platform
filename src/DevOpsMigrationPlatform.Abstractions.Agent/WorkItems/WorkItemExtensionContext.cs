// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Storage;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Per-revision context passed to each work-item <see cref="IModuleExtension"/> during import/export.
/// </summary>
/// <remarks>
/// The domain port for a single work-item revision: it carries the revision being processed and the
/// resolved target identity, decoupled from the checkpoint/resume delivery mechanism that drives it.
/// Extensions cast the incoming <see cref="IExtensionContext"/> to this type.
/// </remarks>
public sealed record WorkItemExtensionContext : IExtensionContext
{
    /// <inheritdoc/>
    public required string Organisation { get; init; }

    /// <inheritdoc/>
    public required string ProjectName { get; init; }

    /// <summary>Source work item id (as string) — the <see cref="IExtensionContext"/> entity key.</summary>
    public required string EntityId { get; init; }

    /// <summary>Target work item id (as string). Set before import; null on export.</summary>
    public string? TargetEntityId { get; init; }

    /// <inheritdoc/>
    public required IPackageAccess Package { get; init; }

    /// <summary>The revision being processed (fields, links, attachments, comments).</summary>
    public required WorkItemRevision Revision { get; init; }

    /// <summary>Resolved target work item id (numeric) for this revision during import.</summary>
    public required int TargetWorkItemId { get; init; }

    /// <summary>Relative package folder for this revision (e.g. <c>WorkItems/2026-01-15/&lt;ticks&gt;-42-3</c>).</summary>
    public required string FolderPath { get; init; }

    /// <summary>
    /// The per-job target connector for this import. Carried on the context (not ctor-injected) because
    /// the target is resolved per job, while extensions are run-wide. Null on export.
    /// </summary>
    public IWorkItemTarget? Target { get; init; }
}
