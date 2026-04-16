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
    /// <summary>Creates the database tables if they do not exist and prepares the connection.</summary>
    Task InitializeAsync(CancellationToken ct);

    /// <summary>Returns the target work item ID for <paramref name="sourceId"/>, or <see langword="null"/> if not mapped.</summary>
    Task<int?> GetTargetWorkItemIdAsync(int sourceId, CancellationToken ct);

    /// <summary>Records a source-to-target work item ID mapping.</summary>
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
}
