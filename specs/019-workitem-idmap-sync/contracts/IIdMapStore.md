# Contract: IIdMapStore (updated)

**Namespace**: `DevOpsMigrationPlatform.Abstractions`  
**Project**: `DevOpsMigrationPlatform.Abstractions`  
**Backed by**: `SqliteIdMapStore` (Infrastructure)

## Full interface

```csharp
public interface IIdMapStore : IAsyncDisposable
{
    // Existing — unchanged
    Task InitializeAsync(CancellationToken ct);
    Task<int?> GetTargetWorkItemIdAsync(int sourceId, CancellationToken ct);
    Task SetWorkItemMappingAsync(int sourceId, int targetId, CancellationToken ct);    // SQL changed — see below
    Task<string?> GetAttachmentIdAsync(int sourceWorkItemId, int revisionIndex, string relativePath, CancellationToken ct);
    Task SetAttachmentMappingAsync(int sourceWorkItemId, int revisionIndex, string relativePath, string targetAttachmentId, CancellationToken ct);
    Task SeedWorkItemMappingsAsync(IAsyncEnumerable<IdMapEntry> entries, CancellationToken ct);

    // New — this feature
    Task<int?> GetLastRevisionIndexAsync(int sourceId, CancellationToken ct);
    Task UpdateLastRevisionIndexAsync(int sourceId, int revisionIndex, CancellationToken ct);
    IAsyncEnumerable<IdMapEntry> EnumerateWorkItemMappingsAsync(CancellationToken ct);
    Task RecordSkippedRevisionAsync(string folderPath, int sourceId, int targetId, string reason, CancellationToken ct);
}
```

## Behaviour rules

| Method | Behaviour |
|---|---|
| `InitializeAsync` | Creates tables if absent; validates `work_item_map` schema has `last_revision_index` column. Throws `InvalidOperationException` if old schema detected. |
| `SetWorkItemMappingAsync` | `INSERT INTO ... ON CONFLICT DO UPDATE SET target_id = excluded.target_id` — preserves `last_revision_index`. |
| `SeedWorkItemMappingsAsync` | `INSERT OR IGNORE` — preserves existing mappings and `last_revision_index`. |
| `GetLastRevisionIndexAsync` | Returns `NULL` if source ID not mapped or column is NULL. |
| `UpdateLastRevisionIndexAsync` | Monotonic: `MAX(COALESCE(last_revision_index, -1), @rev)`. Never decrements. |
| `EnumerateWorkItemMappingsAsync` | Streams `work_item_map` in ascending `source_id` order. Returns `IdMapEntry` with `LastRevisionIndex`. Never materialises into memory. |
| `RecordSkippedRevisionAsync` | `INSERT OR REPLACE INTO skipped_revisions ...`. Idempotent. |
