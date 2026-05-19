// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace DevOpsMigrationPlatform.Abstractions.Storage;

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

    /// <summary>
    /// Returns the last revision index that was successfully applied for <paramref name="sourceId"/>,
    /// or <see langword="null"/> if no revision has been recorded yet.
    /// Used by the revision-index watermark to skip already-applied revisions on re-run.
    /// </summary>
    Task<int?> GetLastRevisionIndexAsync(int sourceId, CancellationToken ct);

    /// <summary>
    /// Updates the last revision index for <paramref name="sourceId"/> using MAX semantics:
    /// the value is only updated when <paramref name="revisionIndex"/> is greater than the
    /// currently stored value (monotonic, never decremented).
    /// </summary>
    Task UpdateLastRevisionIndexAsync(int sourceId, int revisionIndex, CancellationToken ct);

    /// <summary>
    /// Checks all work item mappings against the target system.
    /// Returns the list of stale mappings whose target work item no longer exists.
    /// The caller is responsible for logging warnings per stale mapping.
    /// </summary>
    Task<IReadOnlyList<IdMapEntry>> CheckIntegrityAsync(
        Func<int, CancellationToken, Task<bool>> targetExistsAsync,
        CancellationToken ct);

    /// <summary>
    /// Records a skip reason for a source work item when its mapped target no longer exists.
    /// Used by Stage A duplicate prevention when a mapped target has been deleted.
    /// </summary>
    Task RecordSkippedRevisionAsync(int sourceId, string reason, CancellationToken ct);
}
