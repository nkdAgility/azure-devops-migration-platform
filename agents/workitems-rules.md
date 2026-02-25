# WorkItems Rules — Practical Guardrails

These rules govern all work on the WorkItems module. See [docs/workitems-format.md](../docs/workitems-format.md) and [docs/import-streaming.md](../docs/import-streaming.md) for the full reference.

## Folder Naming Rules

- Date folder: `yyyy-MM-dd` — zero-padded, ISO 8601 date only, UTC.
- Revision folder: `<ticks>-<workItemId>-<revisionIndex>` — all three segments, hyphen-separated.
- `ticks` is the .NET `DateTime.Ticks` value of `changedDate` in UTC.
- `workItemId` is the integer work item ID with no padding.
- `revisionIndex` is the zero-based integer revision index with no padding.
- Do not use any other format. Do not add prefixes, suffixes, or additional segments.

**Valid example:** `WorkItems/2026-02-25/638760123456789012-12345-17/`

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

## Staged Import Semantics

Process each revision folder through stages in this exact order:

| Stage | Label | Description |
|---|---|---|
| A | `Created` | Create or identify the target work item; record in ID map |
| B | `Fields` | Apply all field values from `revision.json` |
| C | `Links` | Apply `relatedLinks`, `externalLinks`, `hyperlinks` |
| D | `Attachments` | Upload files and attach via API |

Write the cursor after each stage. Never skip a stage. Never reorder stages.

## Idempotency and Resume Behaviour

- **Before Stage A:** Check `Checkpoints/idmap.db` (or `idmap.json`). If a mapping already exists for this `workItemId`, skip creation and use the existing target ID.
- **Before Stage C:** Query the target for existing links before adding. Do not add a link that already exists.
- **Before Stage D:** Compute SHA256 of each attachment on disk and compare to the target attachment list. Do not re-upload an attachment that already exists with the same hash.
- **On resume:** The cursor's `stage` field identifies the last completed stage. Begin from the next stage, not from Stage A.

## What Must Not Change

- The folder naming format — it is the streaming import contract.
- The stage order — it reflects dependency order (item must exist before fields, fields before links, links before attachments).
- The cursor file path — `Checkpoints/workitems.cursor.json`.
- The `lastProcessed` value format — it must be the relative path of the revision folder, not an ID or timestamp.
