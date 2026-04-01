# WorkItems Format

## 4. WorkItems Module (Aggregate Root)

WorkItems is the primary high-fidelity module. Its on-disk layout is canonical and must not be changed.

### On-Disk Layout

```
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      <attachment files>
```

Each revision folder corresponds to exactly one revision of one work item. Attachment files belonging to that revision are stored physically beside `revision.json`.

### revision.json Required Fields

```json
{
  "workItemId": 12345,
  "revisionIndex": 17,
  "changedDate": "2026-02-25T18:12:34Z",
  "fields": [...],
  "externalLinks": [...],
  "relatedLinks": [...],
  "hyperlinks": [...],
  "attachments": [
    {
      "originalName": "screenshot.png",
      "relativePath": "638760123456789012-screenshot.png",
      "sha256": "abc123...",
      "size": 102400
    }
  ]
}
```

### Required Fields

| Field | Type | Description |
|---|---|---|
| `workItemId` | integer | Work item ID in the source system |
| `revisionIndex` | integer | Zero-based revision index |
| `changedDate` | ISO 8601 string | UTC timestamp of the revision |
| `fields` | array | All field values as of this revision |
| `externalLinks` | array | External links attached to this revision |
| `relatedLinks` | array | Related work item links |
| `hyperlinks` | array | Hyperlinks |
| `attachments` | array | Attachment metadata for files in this folder |

### Attachment Rules

- Attachment files are stored **beside** `revision.json` in the same folder.
- There is **no global attachments module** and no mandatory blob store.
- Each attachment entry in `revision.json` includes:
  - `originalName` — the filename as it appeared in the source system
  - `relativePath` — the actual filename on disk (may be prefixed with attachment ID or SHA)
  - `sha256` — integrity hash
  - `size` — byte count

**Optional integrity improvement:** store files as `<attachmentId>-<sha256>` on disk, preserving the original filename only in metadata.

### No Global Attachments Module

Attachments are scoped to their revision. There is no `Attachments/` root folder in the package. This is a hard rule; see [.agents/guardrails/system-architecture.md](../.agents/guardrails/system-architecture.md).
