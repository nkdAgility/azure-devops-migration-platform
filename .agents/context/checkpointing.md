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
  idmap.db          (ID map — source workItemId → target workItemId; backed by PostgreSQL Portable binary in Local/Dedicated Server topology or PostgreSQL Flexible Server in Cloud topologies)
  idmap.json        (fallback for small packages or tooling)
```

The convention is `<moduleName-lowercase>.cursor.json`. Modules must not share cursor files.

### ID Map

The `Checkpoints/idmap.db` (or `idmap.json`) file tracks source-to-target work item ID mappings and uploaded attachment records. It is written during Stage `CreatedOrUpdated` (work item ID) and Stage `UploadedAttachments` (attachment ID per revision). It is the sole mechanism for idempotency checks during resume. See [.agents/context/identity-and-mapping.md](identity-and-mapping.md) for the identity mapping counterpart.
