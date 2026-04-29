# WorkItems Rules

Canonical rules for the WorkItems package folder. Reject code that violates these structures.

---

## Folder Naming

```
WorkItems/
  yyyy-MM-dd/                           ← changedDate (UTC)
    <ticks>-<workItemId>-<revisionIndex>/  ← revision folder
      revision.json
      <attachment-filename>             ← binary, beside revision
    <ticks>-<workItemId>-c<commentId>/     ← comment folder
      comment.json
```

- `ticks`: `changedDate.Ticks` (ensures uniqueness + chronological sort).
- Import order = lexicographic enumeration of all revision folders across all date folders.
- Date folder groups for human readability; never skip/reorder within or across.

---

## revision.json — Required Fields

| Field | Type | Notes |
|-------|------|-------|
| `workItemId` | int | Source work item ID |
| `revisionIndex` | int | 0-based, monotonically increasing per work item |
| `changedDate` | string (ISO 8601 UTC) | When this revision occurred |
| `fields` | object | Delta fields for this revision (only changed fields) |
| `links` | array | Link state at this revision (full snapshot) |
| `attachments` | array | Attachment metadata added in this revision |
| `embeddedImages` | array | (optional) Image references extracted from HTML fields |

Each attachment entry: `{ originalName, relativePath, sha256, size }`.

---

## comment.json — Required Fields

| Field | Type | Notes |
|-------|------|-------|
| `workItemId` | int | Parent work item ID |
| `commentId` | int | ADO comment ID |
| `text` | string | Rendered text (HTML) |
| `createdBy` | object | Identity descriptor |
| `createdDate` | string (ISO 8601 UTC) | — |
| `modifiedBy` | object | Identity descriptor (if edited) |
| `modifiedDate` | string (ISO 8601 UTC) | (if edited) |

---

## Comment Fetching Rules

- Comments are fetched inline during WorkItems export — NOT a separate module.
- Deleted comments excluded by default (configurable).
- Comment folders use same date grouping as parent revision.
- Comments sorted by `createdDate` ascending.

---

## Import Stages (per revision folder)

| Stage | Cursor Value | Action |
|-------|-------------|--------|
| A | `CreatedOrUpdated` | Create/update target work item, record ID mapping |
| B | `AppliedFields` | Apply field delta |
| C | `AppliedLinks` | Resolve + apply links (using ID mapping) |
| D | `UploadedAttachments` | Upload binaries, verify sha256, attach |
| Completed | `Completed` | Advance `lastProcessed` |

Write cursor after each stage. Crash-safe: resume from last incomplete stage.

---

## Idempotency Rules

- Create: if target ID already in mapping → skip creation, proceed to fields.
- Fields: apply delta unconditionally (last-writer-wins within same revision).
- Links: add if not present; remove if absent from snapshot and was previously added.
- Attachments: skip if sha256+size match existing attachment on target.

---

## Immutable Contracts

Once a `packageVersion` is released:
- `revision.json` schema fields MUST NOT be removed or renamed.
- New optional fields MAY be added.
- Folder naming convention MUST NOT change.
- Breaking changes require new `packageVersion` + versioned upgrader.

---

## Prohibited

- Flat `<workItemId>-comments.json` at date-folder level.
- Global attachments directory.
- Embedded images in shared directory.
- Loading all revisions into memory.
- Comments as separate `IModule`.
- Sorting enumerated results in memory.
- Skipping stages or reordering stages.
- Modifying `revision.json` during import.
