# Checkpointing

## 6. Cursor-Based Checkpointing

Instead of per-work-item watermark tables, the system uses a forward-only cursor stored as a JSON file. This requires no database and makes resume O(1).

### Cursor File Location

```
Checkpoints/workitems.cursor.json
```

### Schema

```json
{
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "stage": "Completed",
  "updatedAt": "2026-02-25T18:12:34Z"
}
```

### Fields

| Field | Type | Description |
|---|---|---|
| `lastProcessed` | string | Relative path to the last successfully processed revision folder |
| `stage` | string | Last completed stage — must be one of the canonical values below |
| `updatedAt` | ISO 8601 string | UTC timestamp of the last cursor update |

### Canonical Stage Values

All modules must use these exact string values. Deviation is a schema violation.

| Value | Meaning |
|---|---|
| `CreatedOrUpdated` | Target work item was created or identified |
| `AppliedFields` | Revision field values were written to the target |
| `AppliedLinks` | Related, external, and hyperlinks were applied |
| `UploadedAttachments` | Binary files were uploaded and attached |
| `Completed` | All stages for this revision folder succeeded |

### Cursor is a Folder Path

The cursor value is the relative path of the last processed revision folder — the folder itself, not a row ID or sequence number. This makes it directly usable as a filesystem seek position.

### Resume Logic

1. Begin enumerating `WorkItems/` as normal.
2. Skip all folders whose path is lexicographically less than or equal to `lastProcessed`.
3. If `stage` is not `Completed`, resume processing within the `lastProcessed` folder starting from the next stage.
4. Continue forward.

### Stage Progression

```
CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments → Completed
```

The cursor is written after each stage completes. A crash between stages leaves the cursor at the last completed stage, enabling fine-grained resume.

### Per-Module Cursors

Each module maintains its own cursor file under `Checkpoints/`:

```
Checkpoints/
  workitems.cursor.json
  teams.cursor.json
  permissions.cursor.json
  builds.cursor.json
  git.cursor.json
  idmap.db          (SQLite — source workItemId → target workItemId mapping; package-local indexed storage)
  idmap.json        (fallback for small packages or tooling)
  agent.lock        (exclusive package lock — JSON file; created atomically by PackageLockFileService on job start, deleted on job completion)
```

The convention is `<moduleName-lowercase>.cursor.json`. Modules must not share cursor files.

### ID Map

The `Checkpoints/idmap.db` (SQLite) file tracks source-to-target work item ID mappings, uploaded attachment records, and per-work-item revision progress. It is written during Stage `CreatedOrUpdated` (work item ID) and Stage `UploadedAttachments` (attachment ID per revision). The `work_item_map` table also stores `last_revision_index` per work item — this is updated after each revision is fully applied and is used for revision-level skip logic during rerun/sync imports. `idmap.db` is the sole mechanism for idempotency checks during resume. See [.agents/context/identity-and-mapping.md](identity-and-mapping.md) for the identity mapping counterpart.

### Package Lock

Each job acquires an exclusive file-system lock on the package before running any module. The lock file is `Checkpoints/agent.lock` and is created atomically using `FileMode.CreateNew`. If another agent instance holds the lock and is confirmed alive via the control plane, the job throws `PackageLockConflictException` (hard bounce). If the lock is stale (owning agent is no longer active), it is deleted and replaced. The lock is released (file deleted) when the job completes or faults.

---

## Export Cursor Behaviour

Export modules use the same cursor schema as import. The key difference is that export has no intra-item stages — a revision folder is either fully written or not written at all.

- Export modules write `stage: "Completed"` after each revision folder is successfully written to the package.
- The `lastProcessed` field holds the relative path of the last revision folder written (e.g. `WorkItems/2026-04-10/638760123456789012-42-17/`).
- The cursor is updated after every individual revision folder so that an interruption results in at most one revision folder of re-work on resume.
- On resume, the orchestrator skips all folders lexicographically less than or equal to `lastProcessed` in a single O(1) comparison per folder — no full scan.

---

## Both-Mode Phase Tracking

When a job runs in `Both` mode (export then import), a top-level phase record tracks whether each phase has completed. This allows a re-run to skip the export phase entirely if it already completed.

### Phase Record Location

```
Checkpoints/job.phase.json
```

### Schema

```json
{
  "exportCompleted": true,
  "importCompleted": false,
  "updatedAt": "2026-04-10T12:34:56Z"
}
```

### Resume Logic (Both Mode)

1. Read `Checkpoints/job.phase.json` before running any module.
2. If `exportCompleted: true` → skip all export-phase modules; jump directly to import phase.
3. If `importCompleted: true` → skip import-phase modules too; job is already complete.
4. Otherwise run from the first incomplete phase, with each module resuming from its own cursor.

The phase record is absent for `Export`-only or `Import`-only jobs. `PhaseTrackingService` returns a default record (both flags `false`) when the file is missing.
