# Feature Specification: Azure DevOps Work Items Import

**Feature Branch**: `013-ado-workitems-import`  
**Created**: 2026-04-15  
**Status**: Draft  
**Input**: User description: "Implement Azure DevOps work items import from artefact package to target project using streaming chronological replay, reading from the Artefacts repository and iterating in order as per documentation, using the Azure DevOps SDK as much as possible."

## Architecture References

| File | Status |
|------|--------|
| `docs/architecture.md` | Confirmed accurate — import mode documented |
| `docs/module-development-guide.md` | Confirmed accurate — IModule contract with ImportAsync defined |
| `docs/cli-guide.md` | Confirmed accurate — `import` command documented (currently stubbed) |
| `docs/configuration-reference.md` | Confirmed accurate — target block documented for Import/Both modes |
| `docs/validation.md` | Confirmed accurate — Tiers 2 and 3 defined for import |
| `docs/work-item-iteration-guide.md` | Confirmed accurate — import enumeration via IArtefactStore.EnumerateAsync documented |
| `.agents/20-guardrails/core/architecture-boundaries.md` | Confirmed accurate — streaming, cursor, and IArtefactStore rules enforced |
| `.agents/30-context/domains/migration-package-concept.md` | Confirmed accurate — WorkItems layout canonical |
| `.agents/30-context/domains/import-streaming.md` | Confirmed accurate — staged import semantics (A→B→C→D) fully specified |
| `.agents/30-context/domains/workitems-format-summary.md` | Confirmed accurate — revision.json and comment.json schemas documented |
| `.agents/30-context/domains/checkpointing-summary.md` | Confirmed accurate — cursor schema and resume logic specified |
| `.agents/30-context/domains/identity-and-mapping.md` | Confirmed accurate — ID map and identity resolution documented |
| `.agents/30-context/domains/job-lifecycle.md` | Confirmed accurate — MigrationJob with target block documented |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Import Work Items from Exported Package (Priority: P1)

As a migration operator, I want to import work items from an existing exported package into a target Azure DevOps project, so that I can complete a migration from source to target using the package as the intermediary.

The operator has previously run an export (e.g. `devopsmigration export --config migration.json`) which produced a package with `WorkItems/` revision folders. Now the operator runs `devopsmigration import --config migration.json` (with mode set to `Import` and a `target` block pointing to Azure DevOps Services) to replay all revision folders in chronological order into the target project.

**Why this priority**: This is the core import capability. Without it, the platform is export-only and cannot complete a migration.

**Independent Test**: Can be fully tested by running an import against a target Azure DevOps project with a pre-built package. Verify work items appear in the target with correct field values, revision history, links, and attachments.

**Acceptance Scenarios**:

1. **Given** a valid exported package with WorkItems revision folders, **When** the operator runs `import`, **Then** each revision folder is processed in lexicographic (chronological) order and work items are created or updated in the target project.
2. **Given** a revision folder with field values, **When** the import processes Stage B (AppliedFields), **Then** all fields from `revision.json` are applied to the target work item using the Azure DevOps SDK.
3. **Given** a revision folder with related links, external links, and hyperlinks, **When** the import processes Stage C (AppliedLinks), **Then** links are created on the target work item (skipping duplicates).
4. **Given** a revision folder with attachment files, **When** the import processes Stage D (UploadedAttachments), **Then** binaries are streamed from the package and uploaded to the target work item.
5. **Given** a revision folder referencing a work item not yet in the target, **When** the import processes Stage A (CreatedOrUpdated), **Then** a new work item is created in the target and the source-to-target ID mapping is recorded in `Checkpoints/idmap.db` (SQLite).
6. **Given** a revision folder referencing a work item already mapped in `idmap.db`, **When** the import processes Stage A, **Then** the existing target work item ID is used (no duplicate created).
7. **Given** the `WorkItemResolutionStrategy` extension is set to `TargetField`, **When** the import starts, **Then** the system queries the target for all work items with the configured custom field populated, seeds `idmap.db` from the results, and uses WIQL single-item lookup as a fallback during processing if an item is not in the local map.
8. **Given** the `WorkItemResolutionStrategy` extension is set to `TargetHyperlink`, **When** the import starts, **Then** the system queries the target for all work items with `System.HyperLinkCount > 0`, inspects their relations via the REST API, filters for hyperlinks matching the configured URL pattern, and seeds `idmap.db` from the results. No per-item live lookup is performed (WIQL cannot filter by hyperlink URL content).

---

### User Story 2 - Resumable Import with Cursor-Based Checkpointing (Priority: P1)

As a migration operator, I want the import to be resumable so that if it is interrupted (crash, network failure, manual pause), I can restart it and it picks up where it left off without reprocessing completed items.

**Why this priority**: Large migrations can take hours. Resumability is essential for operational reliability and is architecturally non-negotiable per the guardrails.

**Independent Test**: Can be tested by importing a package, interrupting mid-way, then restarting. Verify the cursor file records the last processed folder and stage, and that resume skips already-completed work.

**Acceptance Scenarios**:

1. **Given** an import is interrupted after processing some revision folders, **When** the operator re-runs `import`, **Then** the import reads `Checkpoints/workitems.cursor.json` and skips all folders lexicographically at or before the cursor's `lastProcessed` value.
2. **Given** the cursor's `stage` is `AppliedFields` (not `Completed`), **When** the import resumes, **Then** processing continues from Stage C (AppliedLinks) within the same revision folder.
3. **Given** `--force-fresh` is passed, **When** the import starts, **Then** the cursor file is deleted and import begins from the first revision folder (but `idmap.db` is preserved).

---

### User Story 3 - Streaming Memory-Safe Import (Priority: P1)

As a migration operator importing a package with 20,000+ work items, I want the import to process one revision folder at a time without loading all revisions into memory, so that the system does not run out of memory regardless of package size.

**Why this priority**: Streaming is a non-negotiable guardrail. The system must be memory-safe for large datasets.

**Independent Test**: Can be tested by profiling memory usage during import of a large simulated package — memory must not grow proportionally to package size.

**Acceptance Scenarios**:

1. **Given** a package with 20,000 revision folders, **When** the import runs, **Then** only one revision folder is loaded into memory at a time.
2. **Given** the import uses `IArtefactStore.EnumerateAsync("WorkItems/")`, **When** folders are enumerated, **Then** they are returned in strict lexicographic (ascending) order without in-memory sorting.
3. **Given** attachment binaries in a revision folder, **When** they are uploaded to the target, **Then** they are streamed directly from the artefact store — not buffered entirely in memory.

---

### User Story 4 - Identity Resolution During Import (Priority: P2)

As a migration operator, I want source user identities to be mapped to the correct target identities during import, so that work item history shows the right people.

**Why this priority**: Identity mapping is a cross-cutting concern that affects the fidelity of all imported data. It depends on `IdentitiesModule` completing first.

**Independent Test**: Can be tested by importing a package with known source identities and verifying that fields like `System.AssignedTo` and `System.ChangedBy` are mapped to expected target identities.

**Acceptance Scenarios**:

1. **Given** `Identities/mapping.json` contains an explicit source-to-target mapping, **When** a revision contains a mapped identity in a field, **Then** the target identity is used.
2. **Given** a source identity is not in `mapping.json` but can be matched by UPN in the target, **When** the import encounters it, **Then** the automatic match is used.
3. **Given** a source identity cannot be resolved, **When** the import encounters it, **Then** the identity is recorded in `Identities/unresolved.json` and the import continues (does not fail).

---

### User Story 5 - Comment Import (Priority: P2)

As a migration operator, I want comments (both standalone comment sub-folders and inline `comment.json` in revision folders) to be imported into the target, so that discussion history is preserved.

**Why this priority**: Comments are a key part of work item history. The export already stores them; import must replay them.

**Independent Test**: Can be tested by importing a package with comment sub-folders (e.g. `<ticks>-<workItemId>-c<commentId>/`) and inline `comment.json` files, then verifying comments exist on the target work items.

**Acceptance Scenarios**:

1. **Given** a comment sub-folder with `comment.json`, **When** the import processes it, **Then** a comment is created on the target work item via the Azure DevOps Comments API.
2. **Given** a revision folder containing an inline `comment.json` array, **When** the import processes it, **Then** comments are created on the target work item.
3. **Given** a comment with embedded images, **When** the import processes it, **Then** embedded images are uploaded and URLs are rewritten in the comment text.

---

### User Story 6 - Embedded Image URL Rewriting (Priority: P3)

As a migration operator, I want embedded images in field values and comments to be re-uploaded to the target and their URLs rewritten, so that images render correctly after migration.

**Why this priority**: Without URL rewriting, embedded images would point to the source system. This is important for fidelity but is lower priority than core field/link/attachment import.

**Independent Test**: Can be tested by importing revision folders with `embeddedImages` metadata, verifying that image binaries are uploaded to the target, and that field values reference the new target URLs.

**Acceptance Scenarios**:

1. **Given** a `revision.json` with `embeddedImages` entries, **When** the import processes the revision, **Then** each embedded image file is uploaded to the target Azure DevOps attachments API.
2. **Given** an uploaded embedded image returns a new target URL, **When** field values are applied, **Then** the original source URLs in field HTML are replaced with the new target URLs.

---

### Edge Cases

- What happens when the target project does not have a matching work item type for a revision? The import logs an error and skips the revision folder, recording it in `Logs/`.
- What happens when a link target work item has not yet been imported (forward reference)? The import logs a warning and moves on; a second pass or post-processing step may be needed.
- What happens when an attachment upload fails due to size limits on the target? The failure is logged, the stage is recorded in the cursor, and on resume the upload is retried.
- What happens when the target API rate-limits the import? Retry with exponential back-off per the `policies.retries` configuration.
- What happens when a revision folder contains both a `revision.json` and a `comment.json`? Both are processed — the revision fields are applied first, then the inline comments are created.
- What happens when the `TargetHyperlink` strategy startup scan finds no matching hyperlinks? The idmap starts empty and all work items are created as new — equivalent to a first-time import.
- What happens when the `TargetField` strategy live lookup finds a work item but the idmap didn't have it? The mapping is added to `idmap.db` and the existing target work item is used (no duplicate).
- What happens when the `TargetField` custom field does not exist on the target project? The import fails fast with a clear error during Tier 1 connectivity checks.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST implement `WorkItemsModule.ImportAsync(ImportContext, CancellationToken)` to replace the current `NotSupportedException` stub.
- **FR-002**: The system MUST enumerate revision and comment sub-folders under `WorkItems/` using `IArtefactStore.EnumerateAsync("WorkItems/")` in strict lexicographic order.
- **FR-003**: The system MUST process each revision folder through four stages in sequence: CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments, updating the cursor after each stage.
- **FR-004**: The system MUST create work items in the target Azure DevOps project using the Azure DevOps SDK (`Microsoft.TeamFoundationServer.Client`) when a source work item ID has no mapping in `Checkpoints/idmap.db`.
- **FR-005**: The system MUST record source-to-target work item ID mappings in `Checkpoints/idmap.db` (SQLite) during Stage A. The SQLite database contains indexed tables for work item ID mappings (`source_id → target_id`) and attachment mappings (`(source_work_item_id, revision_index, relative_path) → target_attachment_id`).
- **FR-006**: The system MUST apply field values from `revision.json` to the target work item using the Azure DevOps SDK during Stage B.
- **FR-007**: The system MUST create related links, external links, and hyperlinks on the target work item during Stage C, skipping links that already exist.
- **FR-008**: The system MUST stream attachment binaries from the artefact store and upload them to the target work item during Stage D, tracking upload records in `Checkpoints/idmap.db` for idempotency.
- **FR-009**: The system MUST use `ICheckpointingService` with cursor-based state in `IStateStore` for progress tracking — no watermark tables, databases, or in-memory progress tracking.
- **FR-010**: The system MUST use `IIdentityMappingService` for all identity resolution — no module-level identity implementation.
- **FR-011**: The system MUST process comment sub-folders (`<ticks>-<workItemId>-c<commentId>/`) in chronological order alongside revision sub-folders, creating comments on the target via the Comments API.
- **FR-012**: The system MUST handle inline `comment.json` arrays within revision folders by creating comments on the target work item.
- **FR-013**: The system MUST upload embedded images to the target and rewrite URLs in field values and comment text before applying them.
- **FR-014**: The system MUST emit progress events via `IProgressSink` for each processed revision folder.
- **FR-015**: The system MUST be idempotent — re-running an import on a partially completed package must not create duplicate work items, links, or attachments.
- **FR-016**: The system MUST respect the `Extensions` enabled flags (Revisions, Links, Attachments, Comments, EmbeddedImages) during import, skipping disabled stages.
- **FR-017**: The `import` CLI command MUST be enabled (remove the `[HideFromChannel(ReleaseChannel.Preview)]` attribute and the stub implementation).
- **FR-018**: The system MUST wrap all Azure DevOps SDK calls behind an abstraction (e.g. `IWorkItemImportTarget`) defined in `DevOpsMigrationPlatform.Abstractions`, consistent with the existing pattern where `IWorkItemRevisionSource` abstracts the export source.
- **FR-019**: The system MUST store the ID map in a SQLite database at `Checkpoints/idmap.db`. The SQLite file is a portable, single-file, indexed store that lives inside the package and is included in zip exports. The SQLite prohibition in the guardrails applies to the control plane only, not to package-local indexed data.
- **FR-020**: The system MUST support a `WorkItemResolutionStrategy` extension on the WorkItems module that controls how source-to-target work item mappings are discovered. The extension is configured via the `Extensions` array with `{ "Type": "WorkItemResolutionStrategy", "Enabled": true, "Parameters": { ... } }`.
- **FR-021**: The `WorkItemResolutionStrategy` extension MUST support a `TargetField` strategy that: (a) at startup, queries the target project via WIQL for all work items where a configured custom field (e.g. `Custom.MigratedSourceId`) is populated and seeds `idmap.db` from the results; (b) during per-revision processing, if a source ID is not found in `idmap.db`, performs a single-item WIQL query against the target field as a live fallback; (c) after creating a new work item in the target, writes the source work item ID into the configured custom field on the target work item.
- **FR-022**: The `WorkItemResolutionStrategy` extension MUST support a `TargetHyperlink` strategy that: (a) at startup, queries the target project via WIQL for all work items where `System.HyperLinkCount > 0`, then fetches each work item's relations via the REST API, filters hyperlinks matching a configured URL pattern (e.g. `migration://source/{org}/{project}/{workItemId}`), and seeds `idmap.db` from the results; (b) during per-revision processing, does NOT perform live single-item lookup because WIQL cannot filter by hyperlink URL content — if a source ID is not in `idmap.db` after startup seeding, the work item is assumed to not exist and is created; (c) after creating a new work item in the target, adds a hyperlink with the configured URL pattern containing the source work item ID.
- **FR-023**: When no `WorkItemResolutionStrategy` extension is configured (or `Enabled: false`), the system MUST fall back to using `Checkpoints/idmap.db` as the sole source of truth with no target querying — suitable for first-time imports where the target is known to be empty.

### Key Entities

- **Revision Folder**: A folder under `WorkItems/yyyy-MM-dd/` containing `revision.json` and optionally attachments, embedded images, and `comment.json`.
- **Comment Folder**: A folder under `WorkItems/yyyy-MM-dd/` named `<ticks>-<workItemId>-c<commentId>/` containing `comment.json`.
- **ID Map**: A SQLite database at `Checkpoints/idmap.db` storing source-to-target work item ID mappings and attachment upload records. Indexed for fast lookup. Portable, zippable, single-file.
- **Cursor**: The checkpoint file at `Checkpoints/workitems.cursor.json` tracking the last processed folder path and stage.
- **Target Work Item**: A work item in the target Azure DevOps project created or updated by the import.
- **Work Item Resolution Strategy**: A pluggable mechanism (configured via the `WorkItemResolutionStrategy` extension) that seeds and maintains the ID map. Strategies differ in startup behaviour (bulk query), per-item fallback (live lookup), and target marker (field value or hyperlink written after creation).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An exported package of work items can be successfully imported into a target Azure DevOps project with all revision field values preserved.
- **SC-002**: An interrupted import resumes from the last checkpoint within 5 seconds of startup, with zero re-processing of completed revision folders.
- **SC-003**: A package with 20,000+ revision folders imports with constant memory usage (no growth proportional to package size).
- **SC-004**: Post-flight validation (Tier 3) passes: target work item count matches source count, link sampling confirms integrity, attachment sampling confirms accessibility.
- **SC-005**: Unresolved identities are logged in `Identities/unresolved.json` without halting the import (unless configured otherwise).
- **SC-006**: Re-running the import on a completed package produces no duplicate work items, links, or attachments.
- **SC-007**: The import completes for typical packages (1,000 work items with revision history) in under 30 minutes on a standard connection.

## Assumptions

- The target Azure DevOps project already exists and has the required work item types and area/iteration paths configured. The import does not create projects, types, or classification nodes.
- The export package was produced by this platform and conforms to the canonical `WorkItems/` layout documented in `.agents/30-context/domains/migration-package-concept.md` and `.agents/30-context/domains/workitems-format-summary.md`.
- The `IdentitiesModule` completes its import phase before `WorkItemsModule.ImportAsync` begins (enforced by the `DependsOn` declaration).
- The target Azure DevOps REST API version is 7.1 or compatible.
- The Azure DevOps SDK (`Microsoft.TeamFoundationServer.Client` v20.x) provides the required APIs for work item creation, field updates, link management, attachment upload, and comment creation.
- The `WorkItemResolutionStrategy` extension uses `Microsoft.Data.Sqlite` for the `Checkpoints/idmap.db` file. This is package-local indexed storage, not a control-plane database — the SQLite prohibition in the guardrails applies only to the control plane.
- All architecture docs listed in the Architecture References table above were read and confirmed. No gaps were found that would block import implementation.
- The existing `MigrationAgentWorker` already handles calling `ImportAsync` on modules — no changes needed to the agent orchestration.
- `FileSystemArtefactStore.EnumerateAsync` already returns results in lexicographic order — no changes needed to the store.

## Current status

- Reconciled against repository implementation on 2026-05-16.
- Core import architecture is implemented (`WorkItemsModule.ImportAsync`, `WorkItemImportOrchestrator`, `RevisionFolderProcessor`, SQLite `idmap.db`, resolution strategies, simulated + Azure DevOps target implementations).
- `tasks.md` statuses are now explicit per task line with evidence-backed exceptions.

## Remaining incomplete work (IDs)

- T043 — `docs/configuration-reference.md` still missing `WorkItemResolutionStrategy` in WorkItems extension table.
- T046 — `discrepancies.md` items are not individually marked `Resolved`/`N/A`.
- T049 — fresh full-solution `dotnet test` completion evidence is missing for this reconciliation pass.
- T050 — fresh scenario-run evidence for `scenarios/import-ado-workitems-single-project.json` is missing for this reconciliation pass.
- T051 — `ValidateAsync` currently performs Tier 2 checks only; Tier 3 checks from task text are not implemented.

## Completed because superseded (IDs + source)

- T016 — superseded by `features/import/work-items/revisions/import-work-item-revisions.feature` (renamed canonical feature file containing the story coverage).

## Contradictions and reconciliation

- Historical task/doc paths in this spec (for example `Infrastructure/...`) differ from current repository layout (`Infrastructure.Agent/...`, `Abstractions.Agent/...`, `Abstractions.Storage/...`) after later architectural restructuring.
- The spec narrative mentions `devopsmigration import`, while current runtime flow uses `queue` with `Mode: Import`.
- This reconciliation keeps original intent but records truth in `tasks.md` statuses and evidence notes.

## Verification evidence

- Build succeeded: `dotnet build DevOpsMigrationPlatform.slnx --no-incremental`.
- Targeted import tests passed: `dotnet test tests\\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\\DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj --no-build -v minimal --filter "FullyQualifiedName~WorkItemsModuleImportTests|FullyQualifiedName~WorkItemImportOrchestrator"` (12 passed).
- Full-solution test run did not complete in this reconciliation session (recorded under T049 as incomplete evidence).

