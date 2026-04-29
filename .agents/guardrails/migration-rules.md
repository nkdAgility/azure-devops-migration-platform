# Migration Rules

Enforceable migration behaviour rules. If code conflicts with this file, reject it.

---

## Core Pattern

Source → Files → Target. Migrations are: deterministic, resumable, portable, auditable, memory-safe (streaming).

---

## Non-Negotiable Invariants

1. **No direct Source → Target.** All operations go via the on-disk package.
2. **WorkItems layout is canonical.** `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/revision.json`. Import order = lexicographic enumeration.
3. **Attachments owned by WorkItems.** Stored beside the revision that introduced them. No global attachments module or top-level folder.
4. **Import must be streaming.** One revision folder at a time. No materializing all revisions in memory. No global sorting beyond directory enumeration.
5. **Resume is cursor-based.** Forward-only cursor under `.migration/Checkpoints/`. No hidden state elsewhere.
6. **Determinism mandatory.** Re-run Export = stable ordering. Re-run Import = idempotent via checkpoints + mapping.

---

## Package Layout

Root MUST contain: `manifest.json`, `WorkItems/`, `.migration/Checkpoints/`, `.migration/Logs/`. Optional: `Teams/`, `Permissions/`, `Builds/`, `Git/`, `Identities/`.

## Manifest

`manifest.json` MUST include: `packageVersion`, `toolVersion`, `runId`, `configHash`, source metadata (`type`, `org/collection`, `project`, `apiVersion`), `includedTypes`, `schemaVersions`. Written before import begins.

## Checkpointing

- Cursor: `.migration/Checkpoints/<module>.cursor.json`
- Fields: `lastProcessed` (relative package path), `stage` (one of: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`), `updatedAt` (UTC).
- Resume: skip folders ≤ `lastProcessed` lexicographically. Non-Completed stage → resume within that folder's stage.
- No per-work-item watermark databases.

---

## Module Contract

Each module MUST implement: `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`, `DependsOn`.

MUST: write only through `IArtefactStore`, persist state only through `IStateStore`, declare dependencies explicitly.

MUST NOT: access another module's folder, persist state outside Checkpoints, perform ad-hoc file IO, perform live migrations.

---

## Identity & Mapping

- `Identities/`: `descriptors.jsonl`, `mapping.json` (user-editable overrides), `unresolved.json`.
- ID mappings: `.migration/Checkpoints/idmap.db` or `idmap.json`. Deterministic, append-only or transactionally safe.
- Modules MUST use `IIdentityMappingService` — never implement own identity resolution.

---

## Source Types

### AzureDevOpsServices (REST)
- Export: respect per-module extensions (WIQL at module level), produce package output only, record source metadata in manifest.
- Import: consume package only, use mapping/identity services, resumable via cursor.

### TeamFoundationServer
- Export by `TfsMigrationAgent` (net481 polling agent, same lease protocol as MigrationAgent).
- Dispatches via `IModule` (`TfsJobAgentWorker`). `ExportAsync` fully implemented; `PrepareAsync`/`ImportAsync`/`ValidateAsync` return `Task.CompletedTask` until TFS import supported.
- .NET 10 host MUST NOT link against .NET Framework or TfsMigrationAgent project.
- Credentials via job contract only. Uses `IArtefactStore` (`FileSystemArtefactStore`) and `IStateStore`.
- `source.type: TeamFoundationServer` with non-`file:///` package URI → reject at Tier 0.

### Simulated
- Testing/dev only. Same `IModule`/`IArtefactStore` architecture — no bypass.
- Deterministic from `seed` + `workItemCount`. Same inputs = same package byte-for-byte.
- Field values prefixed `[SIMULATED]`. Target accepts all items without external writes.
- Both source and target emit `ProgressEvent` at same granularity as real implementations.
- Identity mapping still runs. `discovery inventory` with Simulated returns counts from config without WIQL.

---

## WorkItems Export

- Scopeable by WIQL. Revisions in increasing `revisionIndex` order.
- Folder: `yyyy-MM-dd` from `changedDate` (UTC); `<ticks>-<workItemId>-<revisionIndex>`.
- `revision.json`: `workItemId`, `revisionIndex`, `changedDate`, `fields` (delta), `links`, `attachments`, `embeddedImages`.
- Attachments: beside `revision.json`. Each has `originalName`, `relativePath`, `sha256`, `size`.
- Comments: per-comment sub-folders `<ticks>-<workItemId>-c<commentId>/comment.json`. Extension inside `WorkItemsModule` — NOT a separate `IModule`. Deleted excluded by default.
- Embedded Images: SHA-256 named files beside parent document. Non-ADO URLs preserved as-is with warning. Inaccessible images non-fatal.

---

## WorkItems Import (Staged)

Per revision folder, in order:

| Stage | Action |
|-------|--------|
| A: CreatedOrUpdated | Create/identify target work item; record ID mapping |
| B: AppliedFields | Apply field changes |
| C: AppliedLinks | Apply links (using ID mapping for related items) |
| D: UploadedAttachments | Upload binaries, verify hash, add attachment relations |
| Completed | Update cursor, advance `lastProcessed` |

Write cursor after each stage. Never skip/reorder. On crash, resume from next incomplete stage.

---

## Prepare Rules

- Import checks `.migration/Checkpoints/prepare.complete.json`. Absent → auto-run Prepare.
- `PrepareAsync`: reads package via `IArtefactStore`, connects to target via injected services, writes `<Module>/prepare-report.json`. Idempotent. Does NOT connect to source. Does NOT modify operator-edited mapping files.
- Blocking issue → Import aborts. `Migrate` mode aborts after Prepare.

---

## Validation

**Pre-flight:** manifest readable, required folders exist, cursor files valid, `revision.json` schema-compatible, attachments exist with matching sha256/size, identity mapping completeness assessed.

**Post-flight:** work item counts match within configured bounds, sample verification of revisions/links/attachments, unresolved identities recorded, failures detailed.

Fail-fast unless configured otherwise.

---

## Prohibited

- Attachments outside revision folder or in global store.
- Comments in flat `<workItemId>-comments.json` at date-folder level.
- Embedded images in global shared directory.
- Comment/image export as separate top-level `IModule`.
- Loading all WorkItems/revisions into memory.
- Full graph before processing.
- Hidden progress state outside `.migration/Checkpoints/`.
- Direct source-to-target migration.
- Breaking deterministic folder naming.
- Bypassing identity/ID mapping services.
