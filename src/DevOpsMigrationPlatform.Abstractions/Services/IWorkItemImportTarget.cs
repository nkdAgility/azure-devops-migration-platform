using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Abstraction for writing work items to a target system during import.
/// All target system SDK calls are wrapped behind this interface.
/// Mirrors <see cref="IWorkItemRevisionSource"/> on the export side.
/// Implementations: <c>AzureDevOpsWorkItemImportTarget</c>, <c>SimulatedWorkItemImportTarget</c>.
/// </summary>
public interface IWorkItemImportTarget
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
    /// Upload an attachment binary and attach it to the target work item.
    /// Returns the target attachment identifier (URL or GUID).
    /// </summary>
    Task<string> UploadAttachmentAsync(
        int targetWorkItemId,
        string fileName,
        Stream content,
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
    /// Returns <see langword="true"/> if a work item with <paramref name="targetWorkItemId"/> exists
    /// in the target project.
    /// Used by the integrity check and Stage A deleted-target guard.
    /// Must NOT throw for 404 responses — return <see langword="false"/> instead.
    /// </summary>
    Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct);
}
