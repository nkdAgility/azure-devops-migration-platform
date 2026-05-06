# Feature Specification: Import — WorkItems, Attachments, and Nodes

**Feature Branch**: `029-import-workitems-attachments-nodes`  
**Created**: 2026-05-02  
**Status**: Draft  
**Input**: Ensure the import of WorkItems, Attachments, and Nodes

## Architecture References

| Document | Status |
|---|---|
| `docs/module-development-guide.md` | Confirmed accurate — Module/Orchestrator/Service pattern, `IModule` contract, dependency graph rules |
| `docs/architecture.md` | Confirmed accurate — Source → Package → Target model, no direct migration |
| `.agents/context/import-streaming.md` | Confirmed accurate — four-stage streaming import model (A: CreatedOrUpdated, B: AppliedFields, C: AppliedLinks, D: UploadedAttachments) |
| `.agents/context/migration-package-concept.md` | Confirmed accurate — `WorkItems/`, `Nodes/` layout, `idmap.db`, cursor schema |
| `.agents/context/checkpointing-summary.md` | Confirmed accurate — cursor-based resume per revision folder |
| `.agents/guardrails/architecture-boundaries.md` | Confirms: streaming is mandatory (rule 2), no in-memory sort (rule 3), cursors required (rule 4), attachments beside revision.json (rule 5), modules through `IArtefactStore`/`IStateStore` only (rule 7), Nodes before WorkItems (rule 8/dependency graph) |
| `analysis/proposed-features.md` | Reference for M2 (NodeTranslationTool — referred to as `NodeStructureTool` in proposed-features.md; canonical code name is `NodeTranslationTool`), M4 (WorkItemsModule missing options including `Attachments.maxSizeBytes`), T7 (WorkItemResolutionTool), P1 (Checkpoint Reconciliation) |

> **No conflicts found** between user intent and documented architecture. The implementation patterns (`WorkItemImportOrchestrator`, `RevisionFolderProcessor`, `NodesModule`) already exist. This spec formalises the acceptance criteria, connector coverage requirements, and observable test assertions that are currently absent or incomplete.

## Clarifications

### Session 2026-05-02

- Q: What is the canonical code name for the tool that translates area/iteration paths? → A: `NodeTranslationTool` (`INodeTranslationTool`, config section `MigrationPlatform:Tools:NodeTranslation`). The name `NodeStructureTool` appears in `proposed-features.md` but is not the implemented name; all references in this spec use `NodeTranslationTool`.
- Q: Are embedded images re-fetched from the source system during import, or read from the package? → A: Read from the package only — binaries are stored beside `revision.json` during export. The source system is never contacted during import. For TFS-originated packages, HTML fields will contain TFS server URLs; these MUST be rewritten to target URLs using the package binaries. If the binary is absent from the package (e.g. EmbeddedImages extension was disabled at export time), the URL cannot be rewritten and a `Warning` is logged.
- Q: Does the export phase currently download embedded images and store the URL-to-file map in `revision.json`? → A: **Partially implemented — there is a known gap.** The architecture is fully designed: `EmbeddedImageMetadata` (with `OriginalUrl` + `RelativePath`) is stored in `WorkItemRevision.EmbeddedImages` in `revision.json`, and `EmbeddedImageExportService` can download/rewrite HTML and Markdown. However, `WorkItemExportOrchestrator` does **not** yet call `EmbeddedImageExportService` — so `revision.json.EmbeddedImages` is always an empty list in the current implementation. The import side (`RevisionFolderProcessor.RewriteEmbeddedImageUrlsAsync`) is correctly implemented and consumes that list. The export gap must be closed for end-to-end embedded image migration to function. This discrepancy is tracked in `discrepancies.md`.
- Q: What capabilities from the legacy `azure-devops-migration-tools` are not yet in this spec? → A: Legacy gap analysis was performed against `TfsWorkItemMigrationProcessor`, `TfsNodeStructureTool`, `TfsEmbededImagesTool`, `TfsRevisionManagerTool`, `TfsAttachmentTool`, and `TfsWorkItemEmbededLinkTool`. 15 gaps (G-01 to G-15) were identified. All high/medium priority gaps have been addressed with new FRs in this spec (FR-030 to FR-040 and FR-041). One item — Git link rewrite (G-10) — is deferred to a separate spec. Field transformation (G-03) is addressed by FR-041 in this spec. See the `## Legacy Parity Gap Analysis` section for full details.

### Session 2026-05-03

- C: `IEmbeddedImageExportService` (implemented as `EmbeddedImageExportService` in `DevOpsMigrationPlatform.Infrastructure.Agent.Export`) is fully implemented and available in the current codebase. The clarification in Session 2026-05-02 correctly identified the gap as orchestrator wiring only — the service itself is NOT missing. The discrepancy in `discrepancies.md` (Discrepancy #4) remains accurate: `WorkItemExportOrchestrator` does not yet call the service, which is the only remaining gap for end-to-end embedded image migration. No implementation work is needed on the service class itself.
- C: `FieldTransformTool` (`IFieldTransformTool`, `FieldTransformOptions`) is fully implemented in `DevOpsMigrationPlatform.Infrastructure.Agent.Tools.FieldTransform` and is 100% in scope for this spec. A `FieldTransform` extension for `WorkItemsModule` that invokes `IFieldTransformTool.ApplyTransforms` during Stage B (`AppliedFields`) is required. See FR-041. The previous G-03 disposition of "Separate spec" is superseded by this clarification; G-03 is now addressed by FR-041 in this spec.
- Q: When `FieldTransformTool.ApplyTransforms` throws during Stage B, how should the failure be handled? → A: Always halt immediately — field transforms are a critical configuration error. The failure does NOT count against `MaxGracefulFailures`; the import always halts regardless of the budget. See FR-041a.
- Q: Can the `FieldTransform` job contract extension carry inline `TransformGroups` configuration, or does it only toggle the globally-configured `FieldTransformOptions`? → A: Toggle only — the extension only enables/disables the globally-configured `FieldTransformOptions`. No per-job override of transform groups is supported. See FR-043.
- Q: What assertion is required in the Simulated system test for FR-041? → A: Assert that the target connector received a field value matching the post-transform result (e.g. transformed `System.AreaPath` value equals the expected mapped path). See SC-008.
- Q: How should the Polly retry policy in FR-040 handle ADO rate limiting? → A: The Polly policy MUST handle `HTTP 429 Too Many Requests` in addition to transient failures (5xx / network errors). When a 429 response includes a `Retry-After` header, the policy MUST wait the specified number of seconds before retrying. `X-RateLimit-Remaining` SHOULD be monitored and logged at `Debug` level when it approaches zero. See updated FR-040.
- Q: What is the default value for `extensions[Attachments].maxSizeBytes`? → A: Per ADO documentation — ADO Services enforces a hard limit of **60 MB** (62,914,560 bytes) per attachment; ADO Server defaults to **4 MB** (4,194,304 bytes) but is configurable up to 2 GB. The platform default for `maxSizeBytes` MUST be `0` (meaning no platform-side cap — the target API enforces its own limits). Operators targeting large attachment workloads should set this explicitly. See updated Edge Case.
- Q: When WorkItemsModule.PrepareAsync detects problems (missing WI types, missing node paths, unresolved identities), what's the operator override model? → A: Always blocking — Import refuses to start if any Prepare finding is Error-level. No `--force` or `IgnorePrepareFailures` override is supported; the operator must fix the underlying issue and re-run Prepare. Warnings (unmapped fields, unresolved identities, oversized attachments) do not block Import. See FR-P01 through FR-P09.
- Q: Should spec 029 own PrepareAsync FRs for both NodesModule and WorkItemsModule? → A: Yes — both modules get PrepareAsync. FR-P01 (IModule interface extension) through FR-P09 (WorkItemsModule prepare report output) are added to this spec. The foundational `IModule` gap (interface currently has no `PrepareAsync`) is noted in the infrastructure preamble of the Prepare Phase section.

## User Scenarios & Testing *(mandatory)*

### User Story 0 — Prepare Phase Pre-flight (Priority: P1)

A migration operator has completed export and has a package ready. Before running Import, they run Prepare to surface any issues — missing node paths, unknown work item types, unresolved identities, unmapped fields — so they can fix configuration before any write operations occur on the target. Prepare is re-runnable; the operator can fix an issue and re-run until `prepare.complete.json` is written.

**Why this priority**: Import can fail mid-run if node paths or work item types are missing. Prepare surfaces these failures upfront so the operator can correct config without having to clean up a partial import. The goal is the fewest import failures possible.

**Independent Test**: Can be fully tested using a Simulated connector. The Simulated source returns a `referenced-paths.json` listing a path that does not exist on the Simulated target, and a `revision.json` containing a work item type unknown to the target. Prepare MUST write `Nodes/prepare-report.json` with that path in `Missing`, `WorkItems/prepare-report.json` with that type in `WorkItemTypeErrors`, and MUST NOT write `prepare.complete.json`.

**Acceptance Scenarios**:

1. **Given** a package where `Nodes/referenced-paths.json` lists a path absent from the Simulated target and `AutoCreateNodes = false`, **When** `NodesModule.PrepareAsync` runs, **Then** `Nodes/prepare-report.json` contains the path in `Missing`, an `Error` log is emitted, and `prepare.complete.json` is NOT written.
2. **Given** a package containing a `revision.json` with `WorkItemType = "Epic"` and the Simulated target does not have `"Epic"`, **When** `WorkItemsModule.PrepareAsync` runs, **Then** `WorkItems/prepare-report.json` has `"Epic"` in `WorkItemTypeErrors`, an `Error` log is emitted, and `prepare.complete.json` is NOT written.
3. **Given** all Prepare checks pass (no Error-level findings), **When** both `NodesModule.PrepareAsync` and `WorkItemsModule.PrepareAsync` run, **Then** `prepare.complete.json` is written to `.migration/Checkpoints/`.
4. **Given** `prepare.complete.json` already exists, **When** Import is started, **Then** Prepare is NOT re-run (the marker is honoured and import proceeds directly).
5. **Given** `prepare.complete.json` is absent, **When** Import is started, **Then** Prepare is automatically executed first; if Prepare produces Error-level findings, Import halts with a report before making any writes to the target.
6. **Given** `Identities/prepare-report.json` reports 3 unresolved identities, **When** `WorkItemsModule.PrepareAsync` runs, **Then** `WorkItems/prepare-report.json` has `UnresolvedIdentityCount = 3`, a `Warning` is logged, and `prepare.complete.json` IS still written (unresolved identities are a warning, not a blocker).
7. **Given** Prepare is re-run after the operator fixes a missing node path (now present on the target), **When** `NodesModule.PrepareAsync` runs again, **Then** `Nodes/prepare-report.json` is overwritten showing the path in `Present`, no Error is logged, and `prepare.complete.json` is written.

---

### User Story 1 — Node Structure Import (Priority: P1)

A migration operator has exported a source project's area and iteration tree to the package. Before importing work items, the operator runs the import in a mode that creates all missing classification nodes (area paths and iteration paths) on the target project, so that subsequent work item import does not fail on unresolvable paths.

**Why this priority**: Nodes must be present before any work item is written. If a work item references `Project\Team A\Sprint 1` and that path does not exist, the import fails or silently drops the path. Node import is the prerequisite for all other import operations.

**Independent Test**: Can be fully tested by running `NodesModule ImportAsync` against a package containing `Nodes/source-tree.json` and `Nodes/referenced-paths.json` with a Simulated connector, then asserting that the node ensurer was called with the expected paths and the cursor transitions to `Completed`.

**Acceptance Scenarios**:

1. **Given** a package with `Nodes/referenced-paths.json` listing `Project\Area1` and `Project\Sprint 1`, **When** `NodesModule ImportAsync` runs with `AutoCreateNodes = true`, **Then** both paths are created on the target and a `Completed` cursor is written.
2. **Given** a package with `Nodes/source-tree.json` listing a full area tree, **When** `NodesModule ImportAsync` runs with `ReplicateSourceTree = true`, **Then** all nodes from the source tree are replicated to the target.
3. **Given** `ReplicateSourceTree = false` and `AutoCreateNodes = false`, **When** `NodesModule ImportAsync` runs, **Then** no node creation calls are made and the cursor is still written as `Completed`.
4. **Given** a package that is missing `Nodes/source-tree.json`, **When** `NodesModule ValidateAsync` runs, **Then** validation returns a blocking error identifying the missing file.
5. **Given** a previous node import run was interrupted mid-way, **When** `NodesModule ImportAsync` runs again, **Then** it resumes from the cursor position and does not duplicate any node creation calls.

---

### User Story 2 — Work Item Revision Import (Priority: P1)

A migration operator has a package containing work item revision history. On running import, every revision is applied to the target project in chronological order: the work item is created or identified on first revision, then fields, links, and attachments are applied in subsequent stages. At the end, the target project contains work items that faithfully reflect the source history.

**Why this priority**: Work item import is the primary deliverable of the platform. Without correct revision replay, the migration is incomplete regardless of what else succeeds.

**Independent Test**: Can be fully tested by running `WorkItemsModule ImportAsync` against a Simulated package containing two work items with three revisions each, then asserting that the simulated target received exactly three `CreateWorkItem` or `UpdateFields` calls per item, in the correct order, with the correct field values.

**Acceptance Scenarios**:

1. **Given** a package with work item revision folders in canonical lexicographic order, **When** `WorkItemsModule ImportAsync` runs, **Then** revisions are applied to the target in ascending lexicographic folder order without any in-memory reordering.
2. **Given** revision folders for work item 101 at revisions 1, 2, and 3, **When** the import processes revision 1, **Then** a new work item is created on the target and a `sourceId → targetId` mapping is recorded in `idmap.db`.
3. **Given** revision folders for work item 101 at revisions 2 and 3, **When** the import processes revision 2 and 3, **Then** the existing target work item is updated (not a new item created) because `idmap.db` already contains the mapping.
4. **Given** a revision folder is partially processed (Stage B cursor written, Stage C not yet written), **When** the import resumes, **Then** Stage A and B are skipped and Stage C resumes from where it left off, without any duplicate field updates.
5. **Given** a package with 10,000 revision folders, **When** the import processes all folders, **Then** the memory footprint does not grow proportionally with the number of revisions (one folder processed at a time).
6. **Given** a revision contains an identity field value from the source, **When** the import applies that revision, **Then** the identity is resolved via `IIdentityLookupTool` before being written to the target.
7. **Given** a revision contains a `System.AreaPath` value, **When** the import applies that revision and `NodeTranslationTool` is configured, **Then** the path is translated according to the mapping rules before being written.

---

### User Story 3 — Attachment Import (Priority: P2)

A migration operator's package contains binary attachment files stored beside each `revision.json`. On running import, every attachment is uploaded to the corresponding target work item revision. Already-uploaded attachments are tracked in `idmap.db` so that re-running import after an interruption does not create duplicate attachments.

**Why this priority**: Attachments represent critical project artefacts (screenshots, design files, test evidence). Their absence after migration is a visible, user-reported defect. Idempotency is required because attachment upload can fail mid-run.

**Independent Test**: Can be fully tested by running `WorkItemsModule ImportAsync` against a Simulated package where a revision folder contains a `revision.json` referencing one attachment and the binary file beside it, then asserting the simulated target received exactly one attachment upload call and `idmap.db` contains the resulting target attachment ID.

**Acceptance Scenarios**:

1. **Given** a revision folder contains `revision.json` referencing `screenshot.png` and the binary file is present beside `revision.json`, **When** Stage D runs, **Then** `screenshot.png` is streamed to the target and the resulting target attachment ID is stored in `idmap.db`.
2. **Given** `idmap.db` already contains an entry for `(workItemId=101, revisionIndex=2, relativePath="screenshot.png")`, **When** Stage D runs for that revision, **Then** no upload call is made for `screenshot.png` (idempotent resume).
3. **Given** `revision.json` references an attachment but the binary file is absent from the package, **When** Stage D runs, **Then** a structured `Warning` log is emitted identifying the missing file and processing continues with the next attachment.
4. **Given** `extensions[Attachments].enabled = false`, **When** Stage D runs, **Then** no attachment upload calls are made but the cursor is still advanced to `UploadedAttachments`.
5. **Given** a package containing embedded images in `revision.json` HTML fields, **When** Stage B processes the revision, **Then** embedded image binaries are read from the package (not re-fetched from the source system), uploaded to the target, and the HTML field references are rewritten to the target URLs before the field update call.
6. **Given** a revision attachment upload fails midway through a set of 5 attachments, **When** the import resumes, **Then** only the remaining unrecorded attachments are uploaded, not the ones already in `idmap.db`.

---

### User Story 4 — Import Ordering and Module Dependencies (Priority: P1)

A migration operator runs a full import job. The platform ensures that `NodesModule` completes before `WorkItemsModule` starts the import phase, and that `IdentitiesModule` completes before any work item identity resolution occurs. The operator does not need to manually sequence modules; the dependency graph enforces the correct order.

**Why this priority**: Running work item import before nodes or identities are ready produces incorrect or failed results. This must be a platform-enforced guarantee, not a documentation recommendation.

**Independent Test**: Can be tested by configuring an import job with all three modules enabled and verifying via log output that `NodesModule` ImportAsync completes before `WorkItemsModule` ImportAsync starts, and `IdentitiesModule` completes before either.

**Acceptance Scenarios**:

1. **Given** a job with `NodesModule`, `IdentitiesModule`, and `WorkItemsModule` all enabled, **When** the import phase runs, **Then** `IdentitiesModule` completes before any other module, `NodesModule` completes before `WorkItemsModule`, and `WorkItemsModule` runs last.
2. **Given** `NodesModule` import fails with an error, **When** the job attempts to run `WorkItemsModule` import, **Then** `WorkItemsModule` is skipped and the job fails with a dependency error, not a partial import.
3. **Given** `prepare.complete.json` is absent from the package, **When** the import job starts, **Then** prepare is automatically run before import begins, in accordance with Guardrail 10.

---

### Edge Cases

- What happens when a work item in the package references an area path that was not included in `Nodes/referenced-paths.json`?  
  → The `NodeTranslationTool` must return a resolution failure; if `SkipOnUnresolvableArea = true` (via `NodeTranslationOptions`) the revision is skipped with a `Warning` log; if `false` the import halts at that revision.
- What happens when the target already contains a work item with the same `ReflectedWorkItemId`?  
  → The `IWorkItemResolutionStrategy` detects the existing item, records the mapping in `idmap.db`, and proceeds to update fields rather than create a duplicate.
- What happens when an attachment binary is larger than `extensions[Attachments].maxSizeBytes`?  
  → The attachment is skipped with a `Warning` log; the cursor still advances to `UploadedAttachments` for that revision. The default value of `maxSizeBytes` is `0` (no platform-side cap). ADO Services enforces a hard upload limit of **60 MB** per attachment; ADO Server defaults to **4 MB** (configurable up to 2 GB). Operators should set `maxSizeBytes` explicitly if they want to skip oversized attachments before attempting the API call — particularly useful when migrating to an ADO Server instance with a low default limit.
- What happens if `idmap.db` is deleted after a partial import run?  
  → All work items that were previously created on the target will be duplicated unless the operator runs checkpoint reconciliation (`devopsmigration reconcile`) to rebuild the mapping from the target.
- What happens when a revision folder name does not match the canonical `<ticks>-<workItemId>-<revisionIndex>` format?  
  → The folder is skipped with a structured `Warning` log; the cursor does not advance past it until the next valid folder.
- What happens when an embedded image binary is absent from the package during import?  
  → A structured `Warning` log is emitted for the missing file; the original source URL is left unrewritten in the HTML field. For TFS-sourced packages this means the image reference will remain a dead TFS server URL in the target — the operator must be aware this is a data loss scenario.
- What happens when embedded images are from a TFS source and the TFS server is no longer reachable?  
  → Not applicable — embedded images are read from the package during import, never from the live source system. If they were not downloaded during export (e.g. `extensions[EmbeddedImages].enabled = false` at export time), their binaries will not be in the package and the missing-file warning path above applies.
- What happens when a work item changed type mid-history (e.g. was "Task" in revision 1, "Bug" in revision 5)?  
  → The import must detect the type change between consecutive revisions and issue a type-change update (`System.WorkItemType` patch) before applying the new revision's fields. Without this, fields specific to the new type may be silently ignored. (FR-033)
- What happens when a package contains a work item type (e.g. `"Epic"`) that does not exist on the target?  
  → If `StrictTypeValidation = true`, import halts at pre-flight with a clear error. If `false`, a `Warning` is emitted and the revision is skipped (contributing to `MaxGracefulFailures`). (FR-034)
- What happens when `ClosedDate` is null but the work item's state in the revision is `Closed` or `Done`?  
  → A structured `Warning` log is emitted noting the field is null and will revert to the current date on save. This is an unavoidable ADO API behaviour; the operator should use a field mapping to set an explicit close date if this matters. (See legacy: `CheckClosedDateIsValid`)
- What happens when a revision folder contains more than 100 attachments?  
  → Attachments above position 100 are skipped with a `Warning` log per skipped file. This matches the ADO REST API hard limit of 100 attachments per work item. (G-08)
- What happens when an embedded work item mention URL in a field (e.g. `/_workitems/edit/42`) has no matching entry in `idmap.db`?  
  → The URL is left unchanged and a `Debug` entry is emitted. A second-pass resolution loop is out of scope; the operator should re-run the import once all referenced items have been imported to resolve remaining links. (FR-030)

## Requirements *(mandatory)*

### Functional Requirements

**Prepare Phase (IModule.PrepareAsync)**

> **Infrastructure note**: As of the investigation date (2026-05-03), `IModule` does NOT contain a `PrepareAsync` method — the interface currently declares only `ExportAsync`, `ImportAsync`, and `ValidateAsync`. The FRs below require that `PrepareAsync(PrepareContext context, CancellationToken ct)` be added to `IModule` and that `PrepareContext` be created as a new context type in `DevOpsMigrationPlatform.Abstractions.Agent.Context`. This is a foundational interface extension; it must be addressed before any of the Prepare FRs below can be implemented. A `prepare.complete.json` marker is written to `.migration/Checkpoints/` only after all module `PrepareAsync` calls complete without Error-level findings. Import refuses to start if any Prepare finding is Error-level — no operator override or `--force` flag is supported; the operator must fix the underlying issue and re-run Prepare.

- **FR-P01**: `IModule` MUST be extended with `Task PrepareAsync(PrepareContext context, CancellationToken ct)`. All existing module implementations MUST provide a non-throwing default implementation (returning `Task.CompletedTask`) so existing modules are not broken. `PrepareAsync` MUST be called by the Job Engine before `ImportAsync` in Import mode (or Migrate mode); if `prepare.complete.json` already exists, `PrepareAsync` is skipped unless explicitly re-run. `PrepareAsync` MUST be idempotent — re-running it overwrites its output artefacts but MUST NOT modify operator-edited mapping files (e.g. `Identities/mapping.json`).

- **FR-P02** *(NodesModule PrepareAsync — path existence check)*: `NodesModule.PrepareAsync` MUST read `Nodes/referenced-paths.json` and, for each listed area/iteration path, call the target connector to check whether the path already exists (e.g. `INodeCreator.NodeExistsAsync`). The result MUST be written to `Nodes/prepare-report.json` containing: a `Present` list (paths already on the target), a `Missing` list (paths absent from the target), and a `WouldAutoCreate` boolean indicating whether `AutoCreateNodes = true` would resolve all missing paths. `PrepareAsync` MUST NOT create any nodes — that is the responsibility of `ImportAsync`.

- **FR-P03** *(NodesModule PrepareAsync — blocking rule)*: If any paths are in the `Missing` list AND `AutoCreateNodes = false`, `NodesModule.PrepareAsync` MUST log each missing path at `Error` level and mark the prepare result as failed. This MUST prevent `prepare.complete.json` from being written and MUST block Import from starting.

- **FR-P04** *(WorkItemsModule PrepareAsync — work item type check)*: `WorkItemsModule.PrepareAsync` MUST enumerate all `revision.json` files in the package, collect the unique set of `WorkItemType` values, and query the target for the list of valid work item types. Any type in the package that does not exist on the target MUST be logged at `Error` level. If any unknown types are found, the prepare result MUST be marked as failed (blocking import). This check corresponds to and pre-empts the import-time check in FR-034; if Prepare was run and passed, FR-034's `ValidateAsync` will not find new type errors at import time.

- **FR-P05** *(WorkItemsModule PrepareAsync — field translation completeness)*: `WorkItemsModule.PrepareAsync` MUST enumerate all field names across all `revision.json` files and cross-check against the configured `FieldTranslations` dictionary. Any field name present in the package that has no entry in `FieldTranslations` MUST be logged at `Warning` level (not `Error`). Unmapped fields do not block import — they are written to the target as-is, using the source field name. The warning is surfaced so the operator can decide whether to add a translation rule or accept the default.

- **FR-P06** *(WorkItemsModule PrepareAsync — node path completeness)*: `WorkItemsModule.PrepareAsync` MUST collect all unique `System.AreaPath` and `System.IterationPath` values from all `revision.json` files and check each against the target (via `INodeCreator.NodeExistsAsync`). Paths already confirmed present in `Nodes/prepare-report.json` (from FR-P02) SHOULD be skipped to avoid duplicate calls. Any path that is missing on the target AND would not be auto-created (`AutoCreateNodes = false`) MUST be logged at `Error` level and mark the prepare result as failed. When `AutoCreateNodes = true`, missing paths MUST be logged at `Warning` level only (they will be created by `NodesModule.ImportAsync`).

- **FR-P07** *(WorkItemsModule PrepareAsync — identity gap report)*: If `Identities/prepare-report.json` exists in the package, `WorkItemsModule.PrepareAsync` MUST read it and log the count of unresolved identities at `Warning` level. Unresolved identities do not block import — they will cause identity fields to be written with the source descriptor value. The warning is surfaced so the operator can resolve them in `Identities/mapping.json` before import if required.

- **FR-P08** *(WorkItemsModule PrepareAsync — attachment size pre-scan)*: If `extensions[Attachments].maxSizeBytes > 0`, `WorkItemsModule.PrepareAsync` MUST scan attachment metadata in all `revision.json` files and log at `Warning` level any attachment whose declared size (from `revision.json`) exceeds `maxSizeBytes`. These attachments will be skipped at import time (FR-020 edge case). This check does not block import; it gives the operator advance notice of which attachments will be skipped.

- **FR-P09** *(WorkItemsModule PrepareAsync — prepare report output)*: On completion, `WorkItemsModule.PrepareAsync` MUST write `WorkItems/prepare-report.json` containing: `WorkItemTypeErrors` (list of unknown types), `UnmappedFields` (list of unmapped field names), `MissingNodePaths` (list of missing area/iteration paths), `UnresolvedIdentityCount` (integer), `OversizedAttachments` (list of `{WorkItemId, RevisionIndex, FileName, SizeBytes}` objects for attachments exceeding `maxSizeBytes`). If any `WorkItemTypeErrors` or `MissingNodePaths` (where `AutoCreateNodes = false`) are non-empty, the prepare run MUST be marked failed and `prepare.complete.json` MUST NOT be written.

**Nodes Import**

- **FR-001**: `NodesModule ImportAsync` MUST read `Nodes/referenced-paths.json` and create all listed area and iteration paths on the target when `AutoCreateNodes = true`.
- **FR-002**: `NodesModule ImportAsync` MUST read `Nodes/source-tree.json` and replicate the full classification tree on the target when `ReplicateSourceTree = true`.
- **FR-003**: Node creation calls MUST be idempotent — creating a node that already exists MUST NOT produce an error or duplicate.
- **FR-004**: `NodesModule ValidateAsync` MUST report a blocking error if `Nodes/source-tree.json` is absent from the package.
- **FR-005**: The `NodesModule` MUST write a cursor to `.migration/Checkpoints/nodes.cursor.json` on completion so that import can resume after interruption.

**Work Item Revision Import**

- **FR-006**: `WorkItemsModule ImportAsync` MUST enumerate `WorkItems/` date folders in strict lexicographic ascending order.
- **FR-007**: Within each date folder, revision sub-folders MUST be enumerated in strict lexicographic ascending order — no in-memory sorting.
- **FR-008**: For each revision folder, the import MUST execute four sequential stages: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`.
- **FR-009**: Stage A (`CreatedOrUpdated`) MUST check `idmap.db` for an existing `sourceId → targetId` mapping. If found, the existing target item MUST be used; no duplicate creation is permitted.
- **FR-010**: Stage B (`AppliedFields`) MUST resolve identity field values via `IIdentityLookupTool` before writing to the target.
- **FR-011**: Stage B MUST apply area path and iteration path translations via `NodeTranslationTool` (configured under `MigrationPlatform:Tools:NodeTranslation`) when the tool is enabled. Translation rules use ordered regex `AreaPathMappings` and `IterationPathMappings`; language override (e.g. localised root segment) is applied first.
- **FR-012**: Stage C (`AppliedLinks`) MUST query the target for existing links before adding new ones; duplicate links MUST NOT be created.
- **FR-013**: The cursor MUST be written after each successfully completed stage so that a resume restarts from the next uncompleted stage, not from the beginning of the revision folder.
- **FR-014**: `WorkItemsModule ImportAsync` MUST emit a structured `Warning` log if it completes with zero items processed while the module is enabled.

**Attachment Import**

- **FR-015**: Stage D (`UploadedAttachments`) MUST check `idmap.db` for an existing `(workItemId, revisionIndex, relativePath) → targetAttachmentId` entry before uploading; if found the upload MUST be skipped.
- **FR-016**: Attachment binaries MUST be streamed directly from `IArtefactStore` to the target upload call; the binary content MUST NOT be buffered entirely in memory.
- **FR-017**: If an attachment binary is absent from the package, the system MUST emit a `Warning` log and continue processing remaining attachments in the revision; it MUST NOT halt the import.
- **FR-018**: After a successful attachment upload, the target attachment ID MUST be persisted in `idmap.db` before moving to the next attachment.
- **FR-019**: Embedded image binaries referenced in HTML fields MUST be read from the package (beside `revision.json`) and uploaded to the target in Stage B, **before** the field update call. The HTML field content MUST be rewritten to replace the original source URLs with the new target URLs. The source system is NOT contacted during import — all binaries come from the package. This applies equally to packages exported from AzureDevOps and from TFS; TFS-sourced HTML will contain TFS server URLs which are unreachable on the target and MUST be rewritten.
- **FR-020**: If `extensions[Attachments].enabled = false`, Stage D MUST be skipped but the cursor MUST still advance to `UploadedAttachments`.

**Embedded Image Export (prerequisite for FR-019)**

- **FR-028** *(Export prerequisite)*: During work item export, `WorkItemExportOrchestrator` MUST call `IEmbeddedImageExportService.ProcessHtmlAsync` (and `ProcessMarkdownAsync` for Markdown fields) for every HTML/Markdown field in each revision when `extensions[EmbeddedImages].enabled = true`. The returned rewritten HTML MUST replace the original field value before `revision.json` is written to the package.
- **FR-029** *(Export prerequisite)*: For each image downloaded during export, a corresponding `EmbeddedImageMetadata` entry (with `OriginalUrl`, `RelativePath`, `Extension`, `Sha256`, `Size`) MUST be appended to `WorkItemRevision.EmbeddedImages` and serialised into `revision.json`. This is the URL-to-file map that the import side consumes. **Note**: As of the investigation date (2026-05-02), `WorkItemExportOrchestrator` does not yet wire `IEmbeddedImageExportService` — `EmbeddedImages` is always `[]` in the current implementation. This gap is tracked in `discrepancies.md` (Discrepancy #4) and must be resolved before FR-019 can function end-to-end.

**Embedded Work Item Mention Link Rewrite**

- **FR-030**: Stage B (`AppliedFields`) MUST inspect every HTML and plain-text field value in the revision and rewrite any embedded hyperlinks that match the pattern `/_workitems/edit/{sourceId}` (TFS or AzureDevOps) by looking up `sourceId` in `idmap.db`. If a target ID is found, the URL MUST be rewritten to the equivalent target work item URL. If no mapping exists yet (the referenced item has not been imported), the link MUST remain unchanged and a `Debug` log entry MUST be emitted; a second-pass resolution loop is out of scope.
- **FR-031**: The link rewrite in FR-030 MUST apply only to hyperlinks in `<a href="...">` tags and plain-text URL strings; the work item field text content MUST NOT be otherwise altered.

**Work Item Type Mapping**

- **FR-032**: `WorkItemsModule` MUST support an optional `WorkItemTypeMappings` dictionary (`source type name → target type name`). When a revision specifies a type that is present in the dictionary, the mapped type MUST be used when creating or updating the target work item. When no mapping is defined, the source type is used unchanged.
- **FR-033**: If a work item changed its type across revisions (i.e. the type in revision N differs from revision N-1), the import MUST execute a type-change update on the target before applying revision N's field values. This requires a REST PATCH to `System.WorkItemType` on AzureDevOps; for the Simulated connector the in-memory representation MUST be updated.

**Pre-Flight Validation**

- **FR-034**: Before starting `WorkItemsModule ImportAsync`, the module MUST perform a `ValidateAsync` step that checks whether every work item type name present in the package exists on the target. Any missing types MUST be logged as `Error`; if `StrictTypeValidation = true` (default `false`) the import MUST halt.
- **FR-035**: `ValidateAsync` MUST also log a `Warning` if any identity field value in the package's identity mapping file has no target principal (unresolved identities). The warning MUST not halt the import.

**Migration Audit Trail**

- **FR-036**: If `GenerateMigrationComment` is enabled (`false` by default), after all revisions for a work item have been applied, `WorkItemsModule` MUST append a single HTML comment to the work item's `History` field: `"This work item was migrated from <source URL>. Source ID: {sourceId}."` The format MUST be stable across platform versions.
- **FR-037**: If `AttachRevisionHistory` is enabled (`false` by default), after all revisions are applied, `WorkItemsModule` MUST attach a JSON file (`_revision-history.json`) to the target work item containing the full array of source revisions from `revision.json`. The attach MUST be idempotent (guarded by `idmap.db`).

**Graceful Failure Tolerance**

- **FR-038**: `WorkItemsModule ImportAsync` MUST support a `MaxGracefulFailures` option (default `0` = halt on first error). When `MaxGracefulFailures > 0`, per-work-item errors MUST be counted; if the count does not exceed `MaxGracefulFailures`, processing continues with the next work item and the error is written to a structured log entry at `Error` level. Once the threshold is exceeded, the import MUST halt and surface all accumulated errors.
- **FR-039**: Errors counted against `MaxGracefulFailures` MUST include target API failures, WI type validation failures (FR-034 when `StrictTypeValidation = false`), and attachment upload failures. Node path resolution failures governed by `SkipOnUnresolvableArea/Iteration` are handled separately and do not count.

**Save Retry**

- **FR-040**: All calls to `IWorkItemImportTarget.CreateOrUpdateAsync` and `IWorkItemImportTarget.UploadAttachmentAsync` MUST be wrapped in a Polly retry policy with exponential back-off. The retry limit MUST be configurable via `WorkItemCreateRetryLimit` (default `5`); on exhaustion the failure MUST be escalated to the `MaxGracefulFailures` counter. The Polly policy MUST handle `HTTP 429 Too Many Requests` (ADO rate-limit response) in addition to transient network/5xx failures. When a 429 response includes a `Retry-After` header, the retry delay MUST be the value specified in that header (in seconds); the `Retry-After` wait does NOT consume one of the `WorkItemCreateRetryLimit` attempts — it is a mandatory back-off pause. `X-RateLimit-Remaining` SHOULD be logged at `Debug` level when present to aid operator visibility. When a 429 response arrives without a `Retry-After` header, the standard exponential back-off applies.

**Field Transform Extension**

- **FR-041**: `WorkItemsModule` MUST support an optional `FieldTransform` extension. When the `FieldTransform` extension is enabled (`extensions[FieldTransform].enabled = true`), Stage B (`AppliedFields`) MUST invoke `IFieldTransformTool.ApplyTransforms` on each revision's field dictionary, using `FieldTransformPhase.Import`, **after** identity resolution (FR-010) and node path translation (FR-011) have been applied and **before** the field update is written to the target. The transformed field dictionary MUST replace the original before the target API call is made. Field transforms run in the order defined in `FieldTransformOptions.TransformGroups`.
- **FR-041a**: If `IFieldTransformTool.ApplyTransforms` throws any exception during Stage B, the import MUST halt immediately — a field transform failure is considered a critical configuration error, not a recoverable per-item failure. The exception MUST be logged at `Error` level with the work item ID, revision index, and exception type. This failure does NOT count against `MaxGracefulFailures`; it always halts regardless of the budget.
- **FR-042**: If the `FieldTransform` extension is absent from the module configuration or `extensions[FieldTransform].enabled = false`, Stage B MUST proceed without invoking `IFieldTransformTool` — there MUST be no default/implicit field transformation.
- **FR-043**: The `FieldTransform` extension MUST be wired via `WorkItemsModuleExtensions` (a new `FieldTransformEnabled` property) and resolved from the job contract's `extensions` array using `Type = "FieldTransform"`, so the extension is equally available when the module configuration is loaded from a job contract (`FromModule`) and from typed options (`FromOptions`). The extension acts as a **toggle only** — it enables or disables the globally-configured `IFieldTransformTool`; no per-job `TransformGroups` override is supported via the job contract.

**Dependency Ordering**

- **FR-021**: `WorkItemsModule` MUST declare `DependsOn` containing both `IdentitiesModule` (import phase) and `NodesModule` (import phase).
- **FR-022**: If any declared dependency module fails its import phase, the orchestrator MUST NOT start `WorkItemsModule ImportAsync`.
- **FR-023**: If `prepare.complete.json` is absent, the import job MUST automatically execute prepare before beginning any module import.

**Connector Coverage**

- **FR-024**: All import behaviours described above MUST be testable via the Simulated connector without requiring a live Azure DevOps organisation.
- **FR-025**: The AzureDevOps connector MUST implement `IWorkItemImportTarget.UploadAttachmentAsync` by calling the ADO Attachments REST API and returning the resulting attachment URL.
- **FR-026**: The AzureDevOps connector MUST implement `INodeEnsurer` for `AutoCreateNodes` and `ReplicateSourceTree` by calling the ADO Classification Nodes REST API.
- **FR-027**: TFS is a source-only connector; `NodesModule ImportAsync` and `WorkItemsModule ImportAsync` are not required for the TFS connector (the TFS agent only runs export).

### Key Entities

- **Revision Folder**: A named sub-folder of a `WorkItems/yyyy-MM-dd/` date folder. Name format: `<ticks>-<workItemId>-<revisionIndex>`. Contains `revision.json` and zero or more binary attachment files.
- **`revision.json`**: A JSON document describing one historical revision of a work item: field values, link references, and attachment metadata (name, relative path, SHA256).
- **`idmap.db`**: A SQLite database at `.migration/Checkpoints/idmap.db` that stores `sourceId → targetId` work item mappings and `(workItemId, revisionIndex, relativePath) → targetAttachmentId` attachment mappings.
- **Node**: An area path or iteration path entry in the Azure DevOps classification tree, e.g. `Project\Team A\Sprint 1`.
- **Import Cursor**: A JSON file under `.migration/Checkpoints/` recording the last successfully processed folder path and stage. Enables resume after interruption.
- **`prepare.complete.json`**: A marker file written to `.migration/Checkpoints/` after all module `PrepareAsync` calls complete with no Error-level findings. Import skips Prepare when this file is present. Prepare overwrites it on successful re-run.
- **`Nodes/prepare-report.json`**: Written by `NodesModule.PrepareAsync`. Contains `Present` (paths on target), `Missing` (paths absent), `WouldAutoCreate` boolean.
- **`WorkItems/prepare-report.json`**: Written by `WorkItemsModule.PrepareAsync`. Contains `WorkItemTypeErrors`, `UnmappedFields`, `MissingNodePaths`, `UnresolvedIdentityCount`, `OversizedAttachments`.
- **`PrepareContext`**: A new context type (to be created in `DevOpsMigrationPlatform.Abstractions.Agent.Context`) passed to `IModule.PrepareAsync`. Provides `IArtefactStore`, `Job`, and target connectivity info.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A package containing 1,000 work items with 10 revisions each and 3 attachments per revision imports completely without duplicate work items or duplicate attachment uploads, as verified by `idmap.db` row counts matching expected totals.
- **SC-002**: Interrupting an import mid-run and re-running it produces identical target state as a clean run — no duplicates, no missing items — verifiable by comparing `idmap.db` after both runs.
- **SC-003**: A node import run that creates 500 area and iteration paths completes with every path present on the target and zero duplicate-creation errors.
- **SC-004**: An import run over a package containing 50,000 revision folders does not require memory proportional to the total number of revisions (one folder in memory at a time, verified by peak memory measurement).
- **SC-005**: Every import operation (nodes, revisions, attachments) emits observable progress events that are visible in the CLI and TUI progress display, reporting item counts, current cursor position, and stage.
- **SC-006**: All import behaviours have passing Simulated connector system tests (`[TestCategory("SystemTest")]`) that assert non-trivial artefact content — not merely absence of exceptions.
- **SC-008**: A Simulated system test for FR-041 (`FieldTransform` extension enabled) MUST assert that the target connector received a field value matching the post-transform result. Example: a `SetField` or regex-replace transform on `System.AreaPath` is applied, and the `UpdateFields` call on the simulated target receives the transformed value — not the original source value.
- **SC-009**: A Simulated system test for the Prepare phase (FR-P01 through FR-P09) MUST assert: (a) when a missing WI type is present in the package, `WorkItems/prepare-report.json` contains the type in `WorkItemTypeErrors` and `prepare.complete.json` is NOT written; (b) when all checks pass, `prepare.complete.json` IS written; (c) Prepare is idempotent — running it twice overwrites the report artefacts without error.
- **SC-007**: Import runs in modes `Import` and `Migrate` complete without error for the reference scenario config (`scenarios/queue-export-ado-workitems-single-project.json`).

## Assumptions

- The source connector (AzureDevOps or TFS) has already run export and produced a well-formed package under the configured `Package.WorkingDirectory`. The import phase does not re-contact the source system.
- Identity resolution (`IdentitiesModule`) has run before work item import is attempted. If identities are unresolved and the operator has not edited `Identities/mapping.json`, identity fields will be written with the source descriptor value, which may not exist on the target.
- The target is an Azure DevOps Services or Azure DevOps Server organisation. TFS is source-only in this platform.
- Package attachments stored beside `revision.json` are the authoritative source; the original source system is not contacted during attachment import.
- `CollapseRevisions` and `MaxRevisions` options (from `proposed-features.md` M4) are export-side concerns. The import phase replays whatever revisions are present in the package — a package containing fewer revisions due to collapsing at export time is a valid, fully-importable input. `ReplayRevisions = false` (tip-only mode in the legacy tool) is also an export-side decision; there is no import-side equivalent.
- Pre-flight identity validation (G-12): If identities are unresolved, `FR-010` and `FR-035` ensure a `Warning` is emitted before import starts. Identity field values will be written with the source descriptor value; this may produce disconnected user references on the target. The operator is responsible for running `IdentitiesModule` before `WorkItemsModule`.
- The `WorkItemResolutionTool` (T7) configurable per-type strategy is partially implemented; this spec requires only the default `ReflectedWorkItemId` strategy to work correctly for all standard work item types.
- Embedded images are exported to the package by the export phase (`IEmbeddedImageDownloader`). If `extensions[EmbeddedImages].enabled = false` at export time, binaries will not be in the package and URLs in imported HTML fields will remain as dead source-system URLs.
- Read of docs: `docs/module-development-guide.md`, `docs/architecture.md`, `.agents/context/import-streaming.md`, `.agents/context/migration-package-concept.md`, `.agents/context/checkpointing-summary.md`, `.agents/guardrails/architecture-boundaries.md`, `analysis/proposed-features.md`.

## Legacy Parity Gap Analysis

> **Source**: `azure-devops-migration-tools` (`TfsWorkItemMigrationProcessor`, `TfsNodeStructureTool`, `TfsEmbededImagesTool`, `TfsRevisionManagerTool`, `TfsAttachmentTool`, `TfsWorkItemEmbededLinkTool`, `TfsWorkItemTypeValidatorTool`).
> This section records capabilities implemented in the legacy tool that are **not yet represented** in this spec (or the current platform implementation), isolated to the work item / attachment / node import story. "Out of scope" does not mean "will never be built" — it means the current spec does not require it for its acceptance criteria to pass.

### Gaps requiring new FRs or spec amendments

| # | Legacy Feature | Legacy Class/Option | Gap in Spec 029 | Recommended Disposition |
|---|---|---|---|---|
| G-01 | **Work Item Type Mapping** | `WorkItemTypeMappingTool`, `Mappings` dict | No FR for mapping source WI type to a different target type (e.g. `"Bug" → "Defect"`). Type is written from `revision.json` as-is. | ✅ **Added FR-032** |
| G-02 | **Work Item Type Change mid-revision** | `ReplayRevisions` — REST patch to change `System.WorkItemType` when type changes across revisions | No edge case or FR covers the scenario where a WI changed type during its history | ✅ **Added FR-033 + Edge Case** |
| G-03 | **Field Mapping Tool** | `FieldMappingTool.ApplyFieldMappings()` — 10 map types (regex, value map, field-to-field, merge, clear, literal, tag, tree-to-tag, etc.) | No FR for arbitrary field transformations during import. Spec only covers identity and node path translation. | ✅ **Added FR-041/FR-042/FR-043** — `FieldTransformTool` (`IFieldTransformTool`) is already implemented and is in scope as a `WorkItemsModule` extension. The broader `FieldMappingTool` 10-type design from the legacy tool may warrant a future spec for additional transform types not yet in `FieldTransformOptions`. |
| G-04 | **Generate Migration Comment** | `GenerateMigrationComment` option — appends HTML comment with link to source work item | Not in spec. Useful for audit trail and navigation back to source. | ✅ **Added FR-036** (opt-in, off by default) |
| G-05 | **Attach Revision History JSON** | `AttachRevisionHistory` option — attaches a JSON file of the full revision history as an attachment | Not in spec. Useful when `MaxRevisions` collapses history. | ✅ **Added FR-037** (opt-in, off by default) |
| G-06 | **Graceful failure tolerance** | `MaxGracefulFailures` — continue after N errors rather than halting | Spec says import halts on unresolvable path; no concept of fault tolerance with a budget | ✅ **Added FR-038** |
| G-07 | **Save retry on network failure** | `WorkItemCreateRetryLimit` — retry save up to N times on `WebException` | FR-008 stages have no retry/back-off on target API call failure | ✅ **Added FR-040** |
| G-08 | **Attachment count limit** | `TfsAttachmentTool` hardcodes limit of 100 attachments per revision | Spec has `maxSizeBytes` but no `maxCount` per revision | ✅ **Added Edge Case** |
| G-09 | **Embedded work item mention link rewrite** | `TfsWorkItemEmbededLinkTool` — rewrites `/_workitems/edit/{id}` URLs in HTML using `idmap.db` source→target mapping | No FR covers rewriting work item hyperlinks embedded in description/comment HTML | ✅ **Added FR-030, FR-031** |
| G-10 | **Git commit link rewrite** | `GitRepository.Enrich()` — rewrites Git commit links in work item links | Not in scope for attachment/node story but worth noting | ⬜ Separate spec |
| G-11 | **Pre-flight WI type validation** | `TfsWorkItemTypeValidatorTool.ValidateWorkItemTypes()` — checks all source types/fields exist in target before starting | Spec has no ValidateAsync that checks WI type schema compatibility | ✅ **Added FR-034** |
| G-12 | **Pre-flight user validation** | `ValidateAllUsersExistOrAreMapped()` — warn if unmapped identities before starting | Spec mentions `IdentitiesModule` dependency but no pre-flight warning about unmapped identities | ✅ **Added FR-035 + amended Assumptions** |
| G-13 | **ClosedDate / state consistency check** | `CheckClosedDateIsValid()` — warns when `ClosedDate` is null but state is Closed/Done | Not in spec | ✅ **Added Edge Case** |
| G-14 | **ReplayRevisions = false (tip-only)** | `TfsRevisionManagerToolOptions.ReplayRevisions = false` — write only the final state | Not in spec. `MaxRevisions` is noted as export concern but tip-only import is separate | ✅ **Amended Assumptions** |
| G-15 | **MaxRevisions (revision collapsing)** | `TfsRevisionManagerToolOptions.MaxRevisions` — first + last N revisions | Noted out of scope as export concern; clarify that collapsed packages are still valid import inputs | ✅ **Amended Assumptions** |

### Already covered (parity confirmed)

| Legacy Feature | Spec Coverage |
|---|---|
| Node path translation (AreaPath, IterationPath) | FR-011 + NodeTranslationTool |
| SkipRevisionWithInvalidAreaPath / IterationPath | Edge Cases section, NodeTranslationOptions |
| Attachment migration | FR-015 to FR-020 (Stage D) |
| Embedded image download + URL rewrite | FR-019, FR-028, FR-029 |
| Identity (user) field mapping | FR-010 |
| ReflectedWorkItemId for idempotency | FR-009, Key Entities (idmap.db) |
| Resume / idempotency | FR-013 (cursors), FR-015 (idmap.db) |
| Node creation (AutoCreateNodes) | FR-001 to FR-005 |
| Streaming / memory safety | FR-006 to FR-008, SC-004 |

### Recommended actions

All high and medium priority gaps have been addressed with new FRs in this spec update. Remaining:

1. **G-10 (Git commit link rewrite)** — warrants a separate spec covering link enrichment.
