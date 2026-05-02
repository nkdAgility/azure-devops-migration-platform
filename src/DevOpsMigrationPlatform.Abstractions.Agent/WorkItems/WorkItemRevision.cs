// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using DevOpsMigrationPlatform.Abstractions.Agent.Attachments;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// The full data for one revision of one work item.
/// This is the type serialised to revision.json in the package.
/// See .agents/context/workitems-format.md for the canonical JSON schema.
/// </summary>
public record WorkItemRevision
{
    public int WorkItemId { get; init; }
    public int RevisionIndex { get; init; }
    public DateTimeOffset ChangedDate { get; init; }

    public IReadOnlyList<WorkItemField> Fields { get; init; } = Array.Empty<WorkItemField>();
    public IReadOnlyList<ExternalWorkItemLink> ExternalLinks { get; init; } = Array.Empty<ExternalWorkItemLink>();
    public IReadOnlyList<RelatedWorkItemLink> RelatedLinks { get; init; } = Array.Empty<RelatedWorkItemLink>();
    public IReadOnlyList<HyperlinkWorkItemLink> Hyperlinks { get; init; } = Array.Empty<HyperlinkWorkItemLink>();
    public IReadOnlyList<AttachmentMetadata> Attachments { get; init; } = Array.Empty<AttachmentMetadata>();

    /// <summary>
    /// Embedded images referenced in field values or comments.
    /// Each entry maps the original source URL to a local file in this revision folder.
    /// See .agents/context/workitems-format.md for the canonical JSON schema.
    /// </summary>
    public IReadOnlyList<EmbeddedImageMetadata> EmbeddedImages { get; init; } = Array.Empty<EmbeddedImageMetadata>();
}
