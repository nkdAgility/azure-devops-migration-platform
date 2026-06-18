// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Agent.WorkItems;

/// <summary>
/// Abstraction for writing work items to a target system during import.
/// All target system SDK calls are wrapped behind this interface.
/// Mirrors <see cref="IWorkItemRevisionSource"/> on the export side.
/// Implementations: <c>AzureDevOpsWorkItemTarget</c>, <c>SimulatedWorkItemTarget</c>.
/// </summary>
public interface IWorkItemTarget
{
    /// <summary>
    /// Create a new work item of the given type with the supplied initial field values.
    /// </summary>
    Task<ImportedWorkItemResult> CreateWorkItemAsync(
        string workItemType,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct);

    /// <summary>
    /// Apply field values to an existing target work item.
    /// </summary>
    Task UpdateFieldsAsync(
        int targetWorkItemId,
        IReadOnlyList<WorkItemField> fields,
        CancellationToken ct);

    /// <summary>
    /// Add links to a target work item, skipping any that already exist.
    /// Related link target IDs must already be resolved to target IDs by the caller.
    /// </summary>
    Task AddLinksAsync(
        int targetWorkItemId,
        IReadOnlyList<RelatedWorkItemLink> relatedLinks,
        IReadOnlyList<ExternalWorkItemLink> externalLinks,
        IReadOnlyList<HyperlinkWorkItemLink> hyperlinks,
        CancellationToken ct);

    /// <summary>
    /// Upload an attachment binary to the target storage.
    /// Returns the URL (or identifier) of the uploaded binary.
    /// The relation between the work item and the attachment is added later
    /// via <see cref="ApplyRevisionAsync"/>.
    /// </summary>
    Task<string> UploadAttachmentAsync(
        int targetWorkItemId,
        string fileName,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Apply all revision data to an existing target work item in a single call:
    /// fields, link relations, and attachment relations.
    /// Attachment binaries must already be uploaded via <see cref="UploadAttachmentAsync"/>
    /// before calling this method; <paramref name="attachmentResults"/> carries the resulting URLs.
    /// </summary>
    Task ApplyRevisionAsync(
        int targetWorkItemId,
        IReadOnlyList<WorkItemField> fields,
        IReadOnlyList<RelatedWorkItemLink> relatedLinks,
        IReadOnlyList<ExternalWorkItemLink> externalLinks,
        IReadOnlyList<HyperlinkWorkItemLink> hyperlinks,
        IReadOnlyList<AttachmentUploadResult> attachmentResults,
        CancellationToken ct);

    /// <summary>
    /// Upload an embedded image binary to the target.
    /// Returns the new target URL where the image is accessible.
    /// </summary>
    Task<string> UploadEmbeddedImageAsync(
        string fileName,
        Stream content,
        CancellationToken ct);

    /// <summary>
    /// Create a comment on a target work item.
    /// </summary>
    Task CreateCommentAsync(
        int targetWorkItemId,
        string text,
        CancellationToken ct);

    /// <summary>
    /// Query the current set of relations on a target work item for idempotency checks (Stage C).
    /// </summary>
    Task<WorkItemRelations> GetExistingRelationsAsync(
        int targetWorkItemId,
        CancellationToken ct);

    /// <summary>
    /// Returns <see langword="true"/> when the named work item type exists on the target project.
    /// </summary>
    Task<bool> WorkItemTypeExistsAsync(string workItemType, CancellationToken ct);

    /// <summary>
    /// Returns <see langword="true"/> if the target work item with <paramref name="targetWorkItemId"/> exists;
    /// <see langword="false"/> if it has been deleted or never existed.
    /// Used by Stage A duplicate prevention and the integrity check pass.
    /// </summary>
    Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct);
}
