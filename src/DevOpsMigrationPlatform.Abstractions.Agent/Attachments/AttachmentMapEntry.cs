// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

namespace DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

/// <summary>
/// Tracks an uploaded attachment for idempotency during import resume.
/// Stored in the <c>attachment_map</c> table of <c>Checkpoints/idmap.db</c>.
/// </summary>
public record AttachmentMapEntry
{
    /// <summary>Source work item ID.</summary>
    public int SourceWorkItemId { get; init; }

    /// <summary>Zero-based revision index where the attachment appeared.</summary>
    public int RevisionIndex { get; init; }

    /// <summary>Relative path of the attachment file in the revision folder.</summary>
    public string RelativePath { get; init; } = string.Empty;

    /// <summary>Target attachment URL or GUID returned by the target system after upload.</summary>
    public string TargetAttachmentId { get; init; } = string.Empty;
}
