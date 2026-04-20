# Streaming Import

## 5. Streaming Import Model

Import operates as a forward-only streaming read of the package. No database, no in-memory sorting, no large dataset loading. The filesystem is the ordered log.

### Enumeration Order

1. Enumerate `WorkItems/` date folders in lexicographic (ascending) order — `yyyy-MM-dd` sorts naturally.
2. Within each date folder, enumerate revision folders in lexicographic (ascending) order — `<ticks>-<workItemId>-<revisionIndex>` sorts by ticks (time), then work item, then revision index.

This guarantees chronological processing without any global index.

### Staged Import Semantics

For each revision folder, processing proceeds through four stages in order:

| Stage | Label | Action |
|---|---|---|
| A | `CreatedOrUpdated` | Create the work item if it does not exist, or identify it by mapped ID |
| B | `AppliedFields` | Apply the revision's field values |
| C | `AppliedLinks` | Apply related links, external links, and hyperlinks |
| D | `UploadedAttachments` | Upload binary files and attach them to the work item |

The cursor is updated to `Completed` after all four stages succeed.

Stage label values are canonical and shared with the cursor schema. See [.agents/context/checkpointing.md](checkpointing.md) for the full enum.

### Failure Behaviour

- A failure in any stage halts processing of that revision folder.
- The cursor records which stage failed so resume begins from that stage, not from the start of the folder.
- Stages A–D are individually idempotent: repeating a stage that already succeeded must not produce duplicate data.

### Idempotency Notes

- **Stage A (`CreatedOrUpdated`):** Check `Checkpoints/idmap.db` for an existing `sourceId → targetId` mapping. If found, use the existing target ID and skip creation.
- **Stage B (`AppliedFields`):** Applying the same fields again must be a no-op or result in the same state.
- **Stage C (`AppliedLinks`):** Query the target for existing links before creation. Do not add a link that already exists.
- **Stage D (`UploadedAttachments`):** The Azure DevOps REST API does not expose SHA256 in attachment list responses. Idempotency is therefore tracked locally: after a successful upload, record `(workItemId, revisionIndex, relativePath) → targetAttachmentId` in `Checkpoints/idmap.db`. On resume, if an entry exists for the attachment, skip re-upload. The SHA256 stored in `revision.json` is used for local file integrity verification only (export-time guarantee), not for target-side deduplication.

### Non-Negotiables

- Import **must** be streaming. Loading all revisions into memory before processing is forbidden.
- Enumeration order **must** follow the lexicographic rule above. Do not sort in memory.
- See [.agents/guardrails/system-architecture.md](../guardrails/system-architecture.md) for the hard guardrails.

---

## Rerun / Sync Import

Import is designed for multi-pass execution. The same package can be exported and imported incrementally without duplicating work items.

### How delta export works

The export cursor (`Checkpoints/workitems.cursor.json`) records the last revision folder written. On a re-export run, the orchestrator skips all folders at or before `lastProcessed` and writes only new revision folders. The package therefore grows forward-only.

### How delta import works

The import cursor (`Checkpoints/workitems.cursor.json`) records the last revision folder fully processed. On a re-import run, the orchestrator skips all folders at or before `lastProcessed` without re-importing them. This means only newly exported revision folders are imported.

### Per-work-item revision skip logic

In addition to the folder-level cursor, `Checkpoints/idmap.db` stores `last_revision_index` per source work item. On each import pass the orchestrator checks whether the current revision folder's revision index is ≤ `last_revision_index` for the mapped work item. If it is, the revision is skipped even if the folder-level cursor was reset (e.g. after `--force-fresh` re-export followed by a partial re-import). This prevents re-applying revisions that were already applied to the target.

### `--force-fresh` semantics

`--force-fresh` deletes the folder-level cursor but preserves `idmap.db`. The next import pass therefore re-enumerates all revision folders from scratch but the `last_revision_index` guard ensures already-applied revisions are skipped per work item. This enables safe "sync" imports: export new revisions, then `--force-fresh` import processes only the delta.
