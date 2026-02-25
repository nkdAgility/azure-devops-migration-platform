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
| `stage` | string | Last completed stage: `Created`, `Fields`, `Links`, `Attachments`, or `Completed` |
| `updatedAt` | ISO 8601 string | UTC timestamp of the last cursor update |

### Cursor is a Folder Path

The cursor value is the relative path of the last processed revision folder — the folder itself, not a row ID or sequence number. This makes it directly usable as a filesystem seek position.

### Resume Logic

1. Begin enumerating `WorkItems/` as normal.
2. Skip all folders whose path is lexicographically less than or equal to `lastProcessed`.
3. If `stage` is not `Completed`, resume processing within the `lastProcessed` folder starting from the next stage.
4. Continue forward.

### Stage Progression

```
Created → Fields → Links → Attachments → Completed
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
```

The convention is `<moduleName-lowercase>.cursor.json`. Modules must not share cursor files.
