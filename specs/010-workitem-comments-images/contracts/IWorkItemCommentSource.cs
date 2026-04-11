// ============================================================
// DevOpsMigrationPlatform.Abstractions
// Namespace: DevOpsMigrationPlatform.Abstractions.Services
// ============================================================

using DevOpsMigrationPlatform.Abstractions.Models;

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Retrieves all comment versions for a single work item from the source system.
/// Each invocation returns an ordered async sequence of <see cref="WorkItemComment"/>
/// records in ascending chronological order (original createdDate first, then edit versions
/// by modifiedDate). The source emits one record per comment version — an original comment
/// with two edits produces three records.
/// </summary>
public interface IWorkItemCommentSource
{
    /// <summary>
    /// Streams all comment versions for <paramref name="workItemId"/> in ascending
    /// chronological order.
    /// </summary>
    /// <param name="workItemId">The source work item identifier.</param>
    /// <param name="includeDeleted">
    /// When <c>true</c>, deleted comments are included in the sequence.
    /// </param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId,
        bool includeDeleted,
        CancellationToken cancellationToken);
}
