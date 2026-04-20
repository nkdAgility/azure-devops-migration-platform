# Research: Work Item ID Map — Integrity, Rebuild, and Sync Support

## Resolved Design Decisions

### 1. SQLite schema — `work_item_map` column additions

**Decision**: Add `last_revision_index INTEGER` directly to the `work_item_map` table schema. No migration path needed (no existing production users).

**Rationale**: Single column addition; SQLite supports `ALTER TABLE ADD COLUMN` but it is simpler and safer to just define the correct schema up front. Any stale local dev DB is detected at `InitializeAsync` time via a column probe and a clear error is thrown instructing the developer to delete `idmap.db`.

**Alternatives considered**: Separate `revision_progress` table — rejected because it adds a join on every revision-skip check and no benefit over a nullable column.

---

### 2. Package lock mechanism — `Checkpoints/agent.lock`

**Decision**: Acquire an OS-level exclusive file lock using `FileStream` with `FileMode.CreateNew` in `PackageLockFileService`. The lock file contains JSON: `{ jobId, pid, acquiredAt }`. On acquire: if file does not exist → create atomically. If file exists and owning PID is still alive → throw `PackageLockConflictException` (hard bounce). If owning PID is dead (stale) → delete and re-create.

**Rationale**: `FileMode.CreateNew` is an atomic OS primitive — it fails with `IOException` if the file already exists. This eliminates the TOCTOU race of a read-then-write approach. PID liveness check is valid for `file://` topology (same machine guaranteed). For cloud topology, the control plane's lease system prevents duplicate job assignment; the lock file is defence-in-depth.

**Alternatives considered**:
- `IStateStore.WriteAsync` — not conditional/atomic; cannot guarantee exclusivity without TOCTOU race.
- SQLite `BEGIN EXCLUSIVE` — does not prevent a second process from opening a _different_ handle before the first transaction.
- Cloud blob lease — overkill for local topology; control plane already handles cloud conflict prevention.

---

### 3. `IPackageLockService` placement — `JobAgentWorker`, not `WorkItemsModule`

**Decision**: Inject `IPackageLockService` into `JobAgentWorker` and acquire the lock inside `ExecuteMigrationAsync` before any module is called. Release when the method exits (via `IAsyncDisposable`).

**Rationale**: The lock protects the entire package (all modules), not just WorkItems. Placing it inside `WorkItemsModule` would allow other modules (Teams, Permissions, Git) to race. `JobAgentWorker` is the single point of module pipeline execution.

**Rule 21 compliance note**: `IPackageLockService` is defined in `DevOpsMigrationPlatform.Abstractions` and its scope is intentionally job-engine-wide. Although `JobAgentWorker` is the only current consumer, export jobs and discovery jobs may acquire it in future. The abstraction is justified as cross-cutting, not module-specific.

---

### 4. Deleted target work item — skip record design

**Decision**: When Stage A finds an existing `idmap.db` mapping but `WorkItemExistsAsync` returns false for the target ID: (a) emit a structured error log via `ILogger` (OTel-forwarded to `Logs/agent.jsonl`); (b) insert a row into a new `skipped_revisions` table in `idmap.db`; (c) write the cursor as Completed so the folder is not reprocessed.

**Rationale**: Writing cursor as Completed prevents infinite retry loops. The `skipped_revisions` table makes the skip durable, queryable, and visible to operators without requiring them to parse log files. Recovery path: operator deletes the orphaned entry from `work_item_map` (or from `skipped_revisions` after fixing the target), then reruns — the cursor will skip already-processed folders but the previously-skipped folder can be reprocessed by resetting the cursor to before that folder.

**Schema for `skipped_revisions`**:
```sql
CREATE TABLE IF NOT EXISTS skipped_revisions (
    folder_path   TEXT    PRIMARY KEY,
    source_id     INTEGER NOT NULL,
    target_id     INTEGER NOT NULL,
    reason        TEXT    NOT NULL,
    skipped_at    TEXT    NOT NULL   -- ISO 8601 UTC
);
```

**Alternatives considered**:
- Job failure on any deleted-target detection — too aggressive; one corrupt mapping should not abort a 10,000-item migration.
- Log only (no durable record) — insufficient; operator cannot discover what was skipped without grepping `Logs/agent.jsonl`.

---

### 5. Integrity check — no new interface abstraction

**Decision**: Add `CheckIntegrityAsync` as a private method on `WorkItemImportOrchestrator` (or an internal helper). Uses `IIdMapStore.EnumerateWorkItemMappingsAsync` (streaming) and `IWorkItemImportTarget.WorkItemExistsAsync`. Results emitted via `ILogger<WorkItemImportOrchestrator>` (structured OTel).

**Rationale**: The integrity check is a startup phase of the import pipeline, not a general-purpose service. Creating a separate `IIdMapIntegrityService` would be a single-use abstraction (rule 21 violation). Keeping it inside the orchestrator uses existing injected dependencies with zero new abstractions.

**Alternatives considered**:
- Dedicated `IIdMapIntegrityService` in Abstractions — violates rule 21 (single consumer).
- Module-private `IdMapIntegrityChecker` class in Infrastructure — valid but adds a class with no reuse; inlining in the orchestrator is simpler.

---

### 6. Revision-level skip logic — monotonic update and centralized parser

**Decision**: Parse folder name using a shared static `WorkItemFolderParser.TryParse(folderName, out ticks, out workItemId, out revisionIndex)` utility (returns false for comment folders). Revision skip check: `if (revisionIndex <= lastRevisionIndex) → skip`. `UpdateLastRevisionIndexAsync` uses `MAX` semantics: `UPDATE work_item_map SET last_revision_index = MAX(COALESCE(last_revision_index, -1), @rev)`. Called after `ProcessAsync` returns with all stages Completed. Comment folders are never subject to revision-index skip logic.

**Rationale**: Monotonic update prevents a reordered or duplicate revision folder from rolling back progress. Centralized parser eliminates scattered `Split('-')` calls. `COALESCE(last_revision_index, -1)` handles the null (first-run) case correctly.

**Alternatives considered**:
- Blind overwrite `UPDATE SET last_revision_index = @rev` — would allow a retried out-of-order revision to lower the watermark.
- Per-query `MAX` in calling code — moves logic out of SQL and into C#, less reliable.

---

### 7. `IWorkItemImportTarget.WorkItemExistsAsync` — breaking interface change

**Decision**: Add `Task<bool> WorkItemExistsAsync(int targetWorkItemId, CancellationToken ct)` to `IWorkItemImportTarget`. Update all implementations: `AzureDevOpsWorkItemImportTarget` (ADO GET work item, handle 404 as `false`), `SimulatedWorkItemImportTarget` (returns `true` always for now — simulated target never deletes).

**Rationale**: The integrity check and Stage A deleted-target guard both need a lightweight "does this ID exist?" check. `ResolveSingleAsync` was rejected because it resolves by provenance (source→target), not by known target ID.

**Alternatives considered**:
- Reuse `ResolveSingleAsync` — wrong semantics; looks up by source provenance, not known target ID.
- Batched existence check API — cleaner for integrity scan but no batch-by-ID ADO REST endpoint exists; batching done via `GetWorkItemsBatchAsync` which has a 200-ID limit and requires extra pagination code. Acceptable future optimization; not required for correctness.
