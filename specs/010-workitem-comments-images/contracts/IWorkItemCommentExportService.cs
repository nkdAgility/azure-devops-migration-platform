// ============================================================
// DevOpsMigrationPlatform.Abstractions
// Namespace: DevOpsMigrationPlatform.Abstractions.Services
// ============================================================

namespace DevOpsMigrationPlatform.Abstractions.Services;

/// <summary>
/// Exports all comment versions for a single work item by calling
/// <see cref="IWorkItemCommentSource"/> and writing the resulting
/// <c>comment.json</c> files via <c>IArtefactStore</c>.
/// This service is called by <c>WorkItemExportOrchestrator</c>
/// after all revision folders for a given work item have been written.
/// </summary>
public interface IWorkItemCommentExportService
{
    /// <summary>
    /// Exports all comment versions for <paramref name="workItemId"/>.
    /// Writes one <c>comment.json</c> per comment version into
    /// <c>WorkItems/yyyy-MM-dd/&lt;ticks&gt;-&lt;workItemId&gt;-c&lt;commentId&gt;/</c>.
    /// </summary>
    /// <param name="workItemId">The source work item identifier.</param>
    /// <param name="cancellationToken">Propagated cancellation token.</param>
    Task ExportAsync(int workItemId, CancellationToken cancellationToken);
}
