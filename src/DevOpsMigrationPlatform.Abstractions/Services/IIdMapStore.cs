using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions;

/// <summary>
/// Abstraction for source-to-target work item ID and attachment ID mapping storage.
/// Backed by SQLite (<c>Checkpoints/idmap.db</c>) in production.
/// The SQLite file is package-local indexed storage — not a control-plane database.
/// </summary>
public interface IIdMapStore : System.IAsyncDisposable
{
    /// <summary>
    /// Creates the database tables if they do not exist and prepares the connection.
    /// Throws <see cref="System.InvalidOperationException"/> if the existing schema is outdated
    /// (e.g., missing the <c>last_revision_index</c> column).
    /// </summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Returns the target work item ID for <paramref name="sourceId"/>, or <see langword="null"/> if not mapped.</summary>
    Task<int?> GetTargetWorkItemIdAsync(int sourceId, CancellationToken ct);

    /// <summary>
    /// Records a source-to-target work item ID mapping.
    /// Uses <c>ON CONFLICT DO UPDATE SET target_id = excluded.target_id</c> — preserves
    /// <c>last_revision_index</c> on re-seed.
    /// </summary>
    Task SetWorkItemMappingAsync(int sourceId, int targetId, CancellationToken ct);

    /// <summary>Returns the target attachment identifier for a previously uploaded attachment, or <see langword="null"/> if not recorded.</summary>
    Task<string?> GetAttachmentIdAsync(int sourceWorkItemId, int revisionIndex, string relativePath, CancellationToken ct);

    /// <summary>Records an uploaded attachment for idempotency during resume.</summary>
    Task SetAttachmentMappingAsync(int sourceWorkItemId, int revisionIndex, string relativePath, string targetAttachmentId, CancellationToken ct);

    /// <summary>
    /// Bulk-seeds work item mappings from an async sequence, used by resolution strategies at import startup.
    /// Existing mappings are not overwritten (INSERT OR IGNORE semantics).
    /// </summary>
    Task SeedWorkItemMappingsAsync(IAsyncEnumerable<IdMapEntry> entries, CancellationToken ct);

    /// <summary>
    /// Returns the highest revision index that has been successfully imported for <paramref name="sourceId"/>,
    /// or <see langword="null"/> if the source ID is not mapped or revision tracking has not been recorded.
    /// </summary>
    Task<int?> GetLastRevisionIndexAsync(int sourceId, CancellationToken ct);

    /// <summary>
    /// Monotonically updates the <c>last_revision_index</c> for <paramref name="sourceId"/>.
    /// Uses <c>MAX(COALESCE(last_revision_index, -1), @rev)</c> — never decrements the stored value.
    /// </summary>
    Task UpdateLastRevisionIndexAsync(int sourceId, int revisionIndex, CancellationToken ct);

    /// <summary>
    /// Streams all work item mappings in ascending <c>source_id</c> order.
    /// Never materialises all rows into memory — uses deferred yield return.
    /// Each <see cref="IdMapEntry"/> includes the <c>LastRevisionIndex</c> if tracked.
    /// </summary>
    IAsyncEnumerable<IdMapEntry> EnumerateWorkItemMappingsAsync(CancellationToken ct);

    /// <summary>
    /// Records a skipped revision folder in the <c>skipped_revisions</c> table.
    /// Uses <c>INSERT OR REPLACE</c> semantics — idempotent on repeated calls for the same folder.
    /// </summary>
    /// <param name="folderPath">Relative path of the revision folder, e.g. <c>WorkItems/2026-01-15/638760000000000001-42-3</c>.</param>
    /// <param name="sourceId">Source work item ID.</param>
    /// <param name="targetId">Target work item ID (from the now-invalid mapping).</param>
    /// <param name="reason">Machine-readable reason code, e.g. <c>"TargetWorkItemDeleted"</c>.</param>
    /// <param name="ct">Cancellation token.</param>
    Task RecordSkippedRevisionAsync(string folderPath, int sourceId, int targetId, string reason, CancellationToken ct);
}
