// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Result returned by <see cref="IWorkItemTarget"/> after creating or updating a work item.
/// </summary>
public record ImportedWorkItemResult
{
    /// <summary>The ID of the work item in the target system.</summary>
    public int TargetWorkItemId { get; init; }

    /// <summary>
    /// <see langword="true"/> if the work item was newly created;
    /// <see langword="false"/> if an existing target work item was updated.
    /// </summary>
    public bool IsNewlyCreated { get; init; }

    /// <summary>
    /// The attachments stored by <see cref="IWorkItemTarget.CreateFromRevisionAsync"/>, one per
    /// pending attachment that had a <see cref="PendingAttachment.RelativePath"/> and a binary.
    /// </summary>
    public System.Collections.Generic.IReadOnlyList<CreatedAttachment> CreatedAttachments { get; init; } =
        System.Array.Empty<CreatedAttachment>();
}
