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
| A | `Created` | Create the work item if it does not exist, or identify it by mapped ID |
| B | `Fields` | Apply the revision's field values |
| C | `Links` | Apply related links, external links, and hyperlinks |
| D | `Attachments` | Upload binary files and attach them to the work item |

The cursor is updated to `Completed` after all four stages succeed.

### Failure Behaviour

- A failure in any stage halts processing of that revision folder.
- The cursor records which stage failed so resume begins from that stage, not from the start of the folder.
- Stages A–D are individually idempotent: repeating a stage that already succeeded must not produce duplicate data.

### Idempotency Notes

- **Stage A:** Use the ID map (`Checkpoints/idmap.db`) to detect an already-created work item. Do not create a duplicate.
- **Stage B:** Applying the same fields again must be a no-op or result in the same state.
- **Stage C:** Links must be checked for existence before creation.
- **Stage D:** Attachments must be checked by SHA256 before upload.

### Non-Negotiables

- Import **must** be streaming. Loading all revisions into memory before processing is forbidden.
- Enumeration order **must** follow the lexicographic rule above. Do not sort in memory.
- See [agents/system-architecture.md](../agents/system-architecture.md) for the hard guardrails.
