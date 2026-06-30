# Migration Rules

Enforceable migration behaviour rules. If code conflicts with this file, reject it.

---

## Core Pattern

Source → Files → Target. Migrations are: deterministic, resumable, portable, auditable, memory-safe (streaming).

**Pipeline phases** (each runnable independently or chained via `Migrate`):

**Inventory → Export → Prepare → Import → Validate**

Phase gates ensure prerequisites are met automatically:
- Export auto-runs Inventory if root `.migration/inventory.complete.json` is absent.
- Import auto-runs Prepare if root `.migration/prepare.complete.json` is absent.

---

## Non-Negotiable Invariants

1. **No direct Source → Target.** All operations go via the on-disk package.
2. **WorkItems layout is canonical.** `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/revision.json`. Import order = lexicographic enumeration.
3. **Attachments owned by WorkItems.** Stored beside the revision that introduced them. No global attachments module or top-level folder.
4. **Import must be streaming.** One revision folder at a time. No materializing all revisions in memory. No global sorting beyond directory enumeration.
5. **Resume is cursor-based.** Forward-only project cursor under `/{org}/{project}/.migration/`. No hidden state elsewhere.
6. **Determinism mandatory.** Re-run Export = stable ordering. Re-run Import = idempotent via checkpoints + mapping.

---

## Package Layout

Root MUST contain: `.migration/`, plus one or more `/{org}/{project}/` project subtrees. Each project subtree contains `manifest.json`, `WorkItems/`, and optional module folders such as `Teams/`, `Permissions/`, `Builds/`, `Git/`, `Identities/`.

Within `.migration/`:
- root files are authoritative package state shared across runs
- `runs/<runId>/` contains run-scoped audit copies and logs only
- subsequent runs MUST NOT depend on files under `runs/<runId>/` for resume or orchestration

## Manifest

`manifest.json` MUST include: `packageVersion`, `toolVersion`, `runId`, `configHash`, source metadata (`type`, `org/collection`, `project`, `apiVersion`), `includedTypes`, `schemaVersions`. Written before import begins.

## Checkpointing

- Cursor: `/{org}/{project}/.migration/{action}.{module}.cursor.json`
- Fields: `lastProcessed` (relative project path), `stage` (one of: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`), `updatedAt` (UTC).
- Resume: skip folders ≤ `lastProcessed` lexicographically. Non-Completed stage → resume within that folder's stage.
- No per-work-item watermark databases.
- Run folders (`.migration/runs/<runId>/`) are audit-only and are not part of resume semantics.

---

## Module Contract

Each module MUST implement: `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`, `DependsOn`.

Modules MAY declare `SupportsExport` and `SupportsImport` flags. Inventory-only modules (e.g. `InventoryModule`) set `SupportsImport = false` and perform inventory work inside `ExportAsync`.

MUST: write only through `IArtefactStore`, persist state only through `IStateStore`, declare dependencies explicitly.

MUST NOT: access another module's folder, persist state outside root `.migration/`, project `/{org}/{project}/.migration/`, or run-scoped audit output, perform ad-hoc file IO, perform live migrations.

---

## Identity & Mapping

- `Identities/`: `descriptors.jsonl`, `mapping.json` (user-editable overrides), `unresolved.json`.
- ID mappings: root `.migration/idmap.db` or `idmap.json`. Deterministic, append-only or transactionally safe.
- Modules MUST use `IIdentityMappingService` — never implement own identity resolution.

---

## Source Types

### AzureDevOpsServices (REST)
- Export: respect per-module extensions (WIQL at module level), produce package output only, record source metadata in manifest.
- Import: consume package only, use mapping/identity services, resumable via cursor.

### TeamFoundationServer
- Export by `TfsMigrationAgent` (net481 polling agent, same lease protocol as MigrationAgent).
- Dispatches via `IModule` (`TfsJobAgentWorker`). Full feature parity is required on `net481`. Guard clauses that skip functionality are not permitted.
- .NET 10 host MUST NOT link against .NET Framework or TfsMigrationAgent project.
- Credentials via job contract only. Uses `IArtefactStore` (`FileSystemArtefactStore`) and `IStateStore`.
- `source.type: TeamFoundationServer` with non-`file:///` package URI → reject at Tier 0.

### Simulated
- Testing/dev only. Same `IModule`/`IArtefactStore` architecture — no bypass.
- Deterministic from `seed` + `workItemCount`. Same inputs = same package byte-for-byte.
- Field values prefixed `[SIMULATED]`. Target accepts all items without external writes.
- Both source and target emit `ProgressEvent` at same granularity as real implementations.
- Identity mapping still runs. `queue` with `Mode: Inventory` and `source.type: Simulated` returns counts from config without WIQL.

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

- Import checks root `.migration/prepare.complete.json`. Absent → auto-run Prepare.
- `PrepareAsync`: reads package via `IArtefactStore`, connects to target via injected services, writes `<Module>/prepare-report.json`. Idempotent. Does NOT connect to source. Does NOT modify operator-edited mapping files.
- Blocking issue → Import aborts. `Migrate` mode aborts after Prepare.

### Import Failure Pattern Checks (Prepare)

- Import-capable modules that perform failure-pattern readiness checks MUST use a composable `IImportFailurePattern` list evaluated during `PrepareAsync`.
- Each `IImportFailurePattern` checks one failure class and MAY emit multiple findings from a single assessment.
- Findings MUST be structured with stable machine identifiers and evidence keys so reruns can be diffed without text matching.
- Aggregate readiness MUST be computed from all findings with exactly two outcomes: `Ready` or `ChangesRequired`.
- Any blocking finding MUST set readiness to `ChangesRequired` and MUST keep existing import gate semantics (Import aborts until corrected and Prepare is rerun).
- Warning findings MUST remain visible in reports and telemetry but MUST NOT block import by default.
- New failure classes MUST be added by introducing a new `IImportFailurePattern` implementation, not by changing orchestrator phase-gate flow.
- Failure-pattern checks are package-and-target only: source-system calls in these checks are forbidden.

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
- Hidden progress state outside root `.migration/`, project `/{org}/{project}/.migration/`, or run-scoped audit output under `.migration/runs/<runId>/`.
- Direct source-to-target migration.
- Breaking deterministic folder naming.
- Bypassing identity/ID mapping services.




