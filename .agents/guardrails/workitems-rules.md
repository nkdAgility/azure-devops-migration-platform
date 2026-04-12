# WorkItems Rules — Practical Guardrails

These rules govern all work on the WorkItems module. See [.agents/context/workitems-format.md](../context/workitems-format.md) and [.agents/context/import-streaming.md](../context/import-streaming.md) for the full reference.

## Folder Naming Rules

- Date folder: `yyyy-MM-dd` — zero-padded, ISO 8601 date only, UTC.
- Revision folder: `<ticks>-<workItemId>-<revisionIndex>` — all three segments, hyphen-separated.
  - `ticks` is the .NET `DateTime.Ticks` value of `changedDate` in UTC.
  - `workItemId` is the integer work item ID with no padding.
  - `revisionIndex` is the zero-based integer revision index with no padding.
  - Do not use any other format. Do not add prefixes, suffixes, or additional segments.
- Comment folder: `<ticks>-<workItemId>-c<commentId>` — the `c` prefix before `commentId` distinguishes comment folders from revision folders in the same date folder.
  - `ticks` is the .NET `DateTime.Ticks` of the comment's `createdDate` (original) or `modifiedDate` (each edit), in UTC.
  - `workItemId` is the integer work item ID with no padding.
  - `commentId` is the integer comment ID with no padding, prefixed with `c`.
  - Each comment version (original + each edit) is a separate folder at its respective date ticks.

**Valid revision folder example:** `WorkItems/2026-02-25/638760123456789012-12345-17/`
**Valid comment folder example:** `WorkItems/2026-02-25/638760123456789013-12345-c42/`

Revision folders and comment folders sort chronologically together in the same date folder by their ticks prefix. This is the canonical interleaved chronological order for streaming import.

## revision.json Required Fields

Every `revision.json` must contain all of the following. Missing any field is a validation error:

- `workItemId` (integer)
- `revisionIndex` (integer, zero-based)
- `changedDate` (ISO 8601 UTC string)
- `fields` (array, may be empty but must be present)
- `externalLinks` (array, may be empty)
- `relatedLinks` (array, may be empty)
- `hyperlinks` (array, may be empty)
- `attachments` (array, may be empty; each entry must include `originalName`, `relativePath`, `sha256`, `size`)
- `embeddedImages` (array, may be empty; each entry must include `originalUrl`, `relativePath`, `extension`, `sha256`, `size`)

## comment.json Required Fields

`comment.json` appears in two placements (see [.agents/context/workitems-format.md](../context/workitems-format.md) — Inline Comment Fetching):

1. **Inside a revision folder** (`<rev-folder>/comment.json`) — a **JSON array** of `WorkItemComment` objects
   whose `ModifiedDate` is within ±1 second of the revision's `ChangedDate`. Written by the inline
   comment fetching path. Enabled by default; disable per-scope with `inlineComments.enabled: false`.
2. **Inside a comment sub-folder** (`<commentId-folder>/comment.json`) — a single **JSON object**
   representing one version of one comment.

Every `WorkItemComment` object (in either placement) must contain all of the following. Missing any field is a validation error:

- `commentId` (integer)
- `version` (integer, 1-based version of this comment text)
- `text` (string — the raw comment text, HTML or Markdown)
- `format` (string — `"html"` or `"markdown"`)
- `createdBy` (object with `id`, `name`, `email`)
- `createdDate` (ISO 8601 UTC string)
- `modifiedBy` (object with `id`, `name`, `email`)
- `modifiedDate` (ISO 8601 UTC string)
- `isDeleted` (boolean)
- `embeddedImages` (array, may be empty; same schema as `revision.json` embedded images)

## Inline Comment Fetching Rules

- Inline comment fetching is **enabled by default**. Set `inlineComments.enabled: false` in scope parameters to disable it.
- `RevisionIndex == 0` must **never** be treated as a comment edit/delete revision, regardless of field content.
- A revision is a comment edit/delete if and only if `System.CommentCount` is present in its changed fields AND `System.History` is absent or empty.
- Comment API failures during inline fetching are **non-fatal**: emit a `ProgressEvent` warning and continue. Do not fail the export.
- Always propagate `CancellationToken` on all async comment fetch operations.



Process each revision folder through stages in this exact order:

| Stage | Label | Description |
|---|---|---|
| A | `CreatedOrUpdated` | Create or identify the target work item; record source→target ID in `Checkpoints/idmap.db` |
| B | `AppliedFields` | Apply all field values from `revision.json` |
| C | `AppliedLinks` | Apply `relatedLinks`, `externalLinks`, `hyperlinks` |
| D | `UploadedAttachments` | Upload files and record `(workItemId, revisionIndex, relativePath) → targetAttachmentId` in `Checkpoints/idmap.db` |

Write the cursor after each stage. Never skip a stage. Never reorder stages.

Stage label values are canonical. Use the exact string values above in the cursor `stage` field. Do not abbreviate, rename, or invent variants.

## Idempotency and Resume Behaviour

- **Before Stage A (`CreatedOrUpdated`):** Check `Checkpoints/idmap.db` for a `sourceWorkItemId → targetWorkItemId` entry. If found, skip creation and use the existing target ID.
- **Before Stage C (`AppliedLinks`):** Query the target for existing links before adding. Do not add a link that already exists.
- **Before Stage D (`UploadedAttachments`):** Check `Checkpoints/idmap.db` for an entry keyed by `(workItemId, revisionIndex, relativePath)`. If found, skip re-upload. The target API does not expose SHA256; idempotency is local-record-based only. SHA256 in `revision.json` is for local file integrity verification only.
- **On resume:** The cursor's `stage` field identifies the last completed stage. Begin from the next stage, not from Stage A.

## What Must Not Change

- The revision folder naming format — it is the streaming import contract.
- The comment folder naming format — `<ticks>-<workItemId>-c<commentId>/` is canonical and must not be altered or replaced with a per-work-item flat file.
- The stage order — it reflects dependency order (item must exist before fields, fields before links, links before attachments).
- The cursor file path — `Checkpoints/workitems.cursor.json`.
- The `lastProcessed` value format — it must be the relative path of the revision or comment folder, not an ID or timestamp.
- The stage label strings — they must be one of: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`. No other values are valid.
