# WorkItems Format

## 4. WorkItems Module (Aggregate Root)

WorkItems is the primary high-fidelity module. Its on-disk layout is canonical and must not be changed.

### On-Disk Layout

```
WorkItems/
  yyyy-MM-dd/
    <ticks>-<workItemId>-<revisionIndex>/
      revision.json
      [comment.json]          ← written when a comment edit/delete is detected (controlled by the Comments extension, enabled by default)
      <attachment files>
      <embedded image files>
    <ticks>-<workItemId>-c<commentId>/
      comment.json
      <embedded image files>
```

Each date folder contains a mix of **revision sub-folders** and **comment sub-folders**, all sorted chronologically by their ticks-based prefix:

- **Revision sub-folder**: `<ticks>-<workItemId>-<revisionIndex>/` — contains `revision.json` and optionally attachment binaries and embedded image files.
- **Comment sub-folder**: `<ticks>-<workItemId>-c<commentId>/` — contains `comment.json` and optionally embedded image files. The `c` prefix in `c<commentId>` distinguishes comment folders from revision folders in the same date folder. The date folder used is the comment's `createdDate` for the original comment, and the comment's `modifiedDate` for each subsequent edit version. Multiple folders may exist for the same `commentId` (one per version).

Streaming import processes revision and comment sub-folders together in lexicographic (chronological) order within each date folder.

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
  ],
  "embeddedImages": [
    {
      "originalUrl": "https://dev.azure.com/org/_apis/wit/attachments/uuid",
      "relativePath": "image-abc123def456.png",
      "extension": "png",
      "sha256": "abc123...",
      "size": 51200
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
| `embeddedImages` | array | Embedded images discovered and rewritten in field values |

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

Attachments are scoped to their revision. There is no `Attachments/` root folder in the package. This is a hard rule; see [.agents/guardrails/architecture-boundaries.md](../.agents/guardrails/architecture-boundaries.md).

### Comments and Discussions

Each comment is stored in its own sub-folder within the date folder that corresponds to the comment's `createdDate` (original) or `modifiedDate` (each edit version). This places comments chronologically alongside revision sub-folders in the same date folder, enabling streaming import to process all entries in the correct order.

**Comment sub-folder naming**: `<ticks>-<workItemId>-c<commentId>/`

- `ticks` is the .NET `DateTime.Ticks` of the comment's `createdDate` (for the original) or `modifiedDate` (for each edit version), in UTC.
- `workItemId` is the integer work item ID.
- `c<commentId>` is the comment ID prefixed with `c` to distinguish it from revision folders in the same date folder.

Each comment sub-folder contains a single file named `comment.json`.

**Multiple versions of a comment**: When a comment is edited, the original is stored at its `createdDate` ticks and each edit is a separate sub-folder at its `modifiedDate` ticks. Both folders use the same `commentId` in the name. The `version` field inside `comment.json` identifies which version the folder represents.

**Deleted comments**: Excluded from the export by default. The configuration flag `modules.workItems.extensions[Comments].parameters.includeDeleted` (boolean, default `false`) enables inclusion.

### Inline Comment Fetching (Feature-Gated)

When a work item revision represents a **comment edit or delete** — detected by the presence of
`System.CommentCount` in the revision's changed fields AND the absence of `System.History` — the
export orchestrator can optionally fetch the matching comment versions from the Azure DevOps
Comments API and write them as `comment.json` **inside the revision folder** (alongside `revision.json`).

This behaviour is **enabled by default** when the `Comments` extension is enabled (the default).
Disable it by setting `{ "Type": "Comments", "Enabled": false }` in the module's `Extensions` list.

**Detection logic (`IsCommentEditOrDeleteRevision`):**

- `RevisionIndex == 0` → always excluded (creation revision; all fields appear as changed).
- `System.History` present and non-empty → comment addition; no API call needed (text is already in `revision.json`).
- `System.CommentCount` present AND `System.History` absent or empty → comment edit or delete; fetch from API.

**Matching strategy:** comments are fetched for the work item, then filtered to those whose
`ModifiedDate` is within ±1 second of the revision's `ChangedDate`.

**File format:** `comment.json` inside the revision folder is a JSON array of matching comment versions. Multiple comments can match the same revision timestamp.

**Error handling:** Comment API failures during inline fetching are **non-fatal**. A `ProgressEvent`
warning is emitted and the export continues. The revision folder will exist without a `comment.json`
if the API call fails.

**Known limitation:** `AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` has an upstream SDK bug
(`$top` parameter out of range). Errors are non-fatal and handled as above. Full comment data will
be correctly persisted once the SDK is fixed.

**Relationship to `c<commentId>` sub-folders:** The two `comment.json` placements serve different
purposes and both may be present in the same package:

| Placement | Format | Contents | When written |
|-----------|--------|----------|--------------|
| `<rev-folder>/comment.json` | JSON array | Comments matching this revision's timestamp | Only when the Comments extension is enabled and a comment edit/delete is detected |
| `<commentId-folder>/comment.json` | JSON object | One version of one comment | Comment sub-module export (future) |

---

### Embedded Images

Embedded images are images referenced inline within HTML or Markdown field values (e.g. `<img src="...">` in HTML or `![](...)` in Markdown). These are a different category from attachments: they are discovered during export, downloaded from the source system, and stored locally.

Embedded images are stored beside the revision or comment that references them, and parent JSON content is rewritten to local relative paths. Detailed metadata shape, fetch behavior, and source-interface contracts live in [docs/work-item-iteration-guide.md](../../docs/work-item-iteration-guide.md) and [docs/package-format-reference.md](../../docs/package-format-reference.md).
