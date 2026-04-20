# Data Model: Work Item ID Map — Integrity, Rebuild, and Sync Support

## SQLite Schema — `Checkpoints/idmap.db`

### Table: `work_item_map` (modified)

```sql
CREATE TABLE IF NOT EXISTS work_item_map (
    source_id           INTEGER PRIMARY KEY,
    target_id           INTEGER NOT NULL,
    last_revision_index INTEGER             -- NULL until first revision is successfully imported
);
```

| Column | Type | Constraints | Description |
|---|---|---|---|
| `source_id` | INTEGER | PRIMARY KEY | Source system work item ID |
| `target_id` | INTEGER | NOT NULL | Target system work item ID |
| `last_revision_index` | INTEGER | nullable | Highest revision index successfully imported for this work item. NULL = no revision imported yet (fallback to cursor-only logic). |

**Change from previous schema**: added `last_revision_index` column. No migration required (fresh schema).  
**Schema guard**: `InitializeAsync` checks for the column via `PRAGMA table_info(work_item_map)`. If missing → throws `InvalidOperationException("idmap.db has an outdated schema. Delete Checkpoints/idmap.db and rerun.")`.

---

### Table: `attachment_map` (unchanged)

```sql
CREATE TABLE IF NOT EXISTS attachment_map (
    source_work_item_id INTEGER NOT NULL,
    revision_index       INTEGER NOT NULL,
    relative_path        TEXT    NOT NULL,
    target_attachment_id TEXT    NOT NULL,
    PRIMARY KEY (source_work_item_id, revision_index, relative_path)
);
```

---

### Table: `skipped_revisions` (new)

```sql
CREATE TABLE IF NOT EXISTS skipped_revisions (
    folder_path  TEXT    PRIMARY KEY,
    source_id    INTEGER NOT NULL,
    target_id    INTEGER NOT NULL,
    reason       TEXT    NOT NULL,
    skipped_at   TEXT    NOT NULL   -- ISO 8601 UTC
);
```

| Column | Type | Description |
|---|---|---|
| `folder_path` | TEXT (PK) | Relative revision folder path (e.g. `WorkItems/2026-04-01/638760123456789012-42-3`) |
| `source_id` | INTEGER | Source work item ID |
| `target_id` | INTEGER | Target work item ID that was expected but not found |
| `reason` | TEXT | Human-readable reason (e.g. `"TargetWorkItemDeleted"`) |
| `skipped_at` | TEXT | ISO 8601 UTC timestamp |

---

## File: `Checkpoints/agent.lock` (new)

JSON file. Created atomically via `FileStream(path, FileMode.CreateNew)`.

```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "pid": 12345,
  "acquiredAt": "2026-04-20T13:00:00.000Z"
}
```

| Field | Type | Description |
|---|---|---|
| `jobId` | string (GUID) | Job ID from `MigrationJob.JobId` |
| `pid` | integer | Process ID of the agent that acquired the lock |
| `acquiredAt` | ISO 8601 UTC | When the lock was acquired |

**Lifecycle**: Created before module execution; deleted in the `IAsyncDisposable.DisposeAsync` of the lock handle returned by `AcquireAsync`.

---

## Record: `IdMapEntry` (modified)

```csharp
public record IdMapEntry
{
    public int SourceId { get; init; }
    public int TargetId { get; init; }
    public int? LastRevisionIndex { get; init; }  // NEW — null if not yet tracked
}
```

---

## Interface Changes

### `IIdMapStore` — new members

```csharp
/// <summary>Returns the last successfully imported revision index, or null if not tracked.</summary>
Task<int?> GetLastRevisionIndexAsync(int sourceId, CancellationToken ct);

/// <summary>
/// Monotonically updates the last revision index for a source work item.
/// Uses MAX(existing, revisionIndex) — never decrements.
/// </summary>
Task UpdateLastRevisionIndexAsync(int sourceId, int revisionIndex, CancellationToken ct);

/// <summary>
/// Streams all work item mappings from the store in ascending source_id order.
/// Used by the integrity check. Does not stream attachment or skipped-revision records.
/// </summary>
IAsyncEnumerable<IdMapEntry> EnumerateWorkItemMappingsAsync(CancellationToken ct);

/// <summary>Records a durable skipped-revision entry when a target work item no longer exists.</summary>
Task RecordSkippedRevisionAsync(string folderPath, int sourceId, int targetId, string reason, CancellationToken ct);
```

**`SetWorkItemMappingAsync` SQL change**: from `INSERT OR REPLACE` to:
```sql
INSERT INTO work_item_map (source_id, target_id)
VALUES (@sid, @tid)
ON CONFLICT(source_id) DO UPDATE SET target_id = excluded.target_id
```
This preserves `last_revision_index` on re-seed. The previous `INSERT OR REPLACE` would have reset it to NULL.

---

### `IWorkItemImportTarget` — new member

```csharp
/// <summary>Returns true if the work item with the given ID exists in the target project.</summary>
Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct);
```

---

### `IPackageLockService` (new — in `DevOpsMigrationPlatform.Abstractions`)

```csharp
/// <summary>
/// Acquires an exclusive lock on the migration package at <paramref name="packagePath"/>.
/// If a live lock already exists, throws <see cref="PackageLockConflictException"/>.
/// Stale locks (owning process dead) are replaced silently.
/// </summary>
public interface IPackageLockService
{
    Task<IAsyncDisposable> AcquireAsync(string packagePath, string jobId, CancellationToken ct);
}
```

### `PackageLockConflictException` (new — in `DevOpsMigrationPlatform.Abstractions`)

```csharp
public sealed class PackageLockConflictException : Exception
{
    public string PackagePath { get; }
    public string OwnerJobId { get; }
    public int OwnerPid { get; }
    public DateTimeOffset AcquiredAt { get; }
}
```

---

## Integrity Check — behavioral contract

Invoked from `WorkItemImportOrchestrator.ImportAsync` after `InitializeAsync` and `SeedAsync`, before the streaming folder loop.

```
for each entry in EnumerateWorkItemMappingsAsync():
    exists = await target.WorkItemExistsAsync(entry.TargetId, ct)
    if not exists:
        logger.LogWarning(
            "[WorkItems][IntegrityCheck] Mapping {SourceId}→{TargetId} points to a non-existent target work item.",
            entry.SourceId, entry.TargetId)
        // emits OpenTelemetry log; does NOT abort the job
```

No file output. No return value. Results surface via `ILogger` (forwarded to `Logs/agent.jsonl` via `PackageDiagnosticSink`).

---

## Stage A — deleted target handling

In `RevisionFolderProcessor.ProcessStageAAsync`:

```
targetId = await idMapStore.GetTargetWorkItemIdAsync(sourceId, ct)
if targetId is not null:
    exists = await target.WorkItemExistsAsync(targetId.Value, ct)
    if not exists:
        logger.LogError(
            "[WorkItems][StageA] Target work item {TargetId} (mapped from source {SourceId}) " +
            "no longer exists. Folder: {FolderPath}. Recording skip and advancing cursor.",
            targetId, sourceId, folderPath)
        await idMapStore.RecordSkippedRevisionAsync(folderPath, sourceId, targetId.Value, "TargetWorkItemDeleted", ct)
        // caller writes cursor as Completed; processing continues with next folder
        throw new SkippedRevisionException("TargetWorkItemDeleted");
```

`SkippedRevisionException` is a module-private exception type used by the processor to signal the orchestrator to write cursor and continue rather than failing the job.
