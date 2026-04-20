# Feature Specification: Work Item ID Map — Integrity, Rebuild, and Sync Support

**Feature Branch**: `019-workitem-idmap-sync`
**Created**: 2026-04-19
**Status**: Draft
**Input**: User description: "Prevent duplicate work items, enable resume and sync by maintaining a SQLite lookup table in the migration package that maps source to target work items. Support rebuild from provenance markers (ReflectedWorkItemId via field or hyperlink). Rerun for additional revisions affects both export and import. For fresh migration reruns (e.g., offline TFS re-export + fresh job), the lookup table must be rebuildable from the target."

## Architecture References

| File | Status |
|------|--------|
| `docs/architecture.md` | Confirmed accurate — package-first model, streaming import, cursor-based resume |
| `docs/modules.md` | Confirmed accurate — IModule contract, WorkItemsModule with extensions |
| `docs/configuration.md` | Confirmed accurate — WorkItemResolutionStrategy extension documented (TargetField, TargetHyperlink) |
| `docs/work-item-iteration-pattern.md` | Confirmed accurate — mandatory reuse of WorkItemExportOrchestrator, ICheckpointingService |
| `.agents/guardrails/system-architecture.md` | Confirmed accurate — streaming, cursor, IArtefactStore rules enforced; rule 4 (cursor-based checkpoints); rule 12 (agents stateless, all durable state in package) |
| `.agents/context/package-format.md` | Confirmed accurate — Checkpoints/idmap.db documented |
| `.agents/context/import-streaming.md` | Confirmed accurate — staged import (A→B→C→D), idempotency via idmap.db |
| `.agents/context/checkpointing.md` | Confirmed accurate — idmap.db documented as source-to-target mapping store |
| `.agents/context/identity-and-mapping.md` | Discrepancy logged — idmap.db described as PostgreSQL Portable but implementation is SQLite; discrepancy in `discrepancies.md` |
| `docs/cli.md` | Needs review — no rebuild-idmap command documented yet |

## Clarifications

### Session 2026-04-20

- Q: Should ID map rebuild and integrity check be exposed as explicit CLI sub-commands? → A: No — these are agent-side operations executed as part of import job execution, not CLI commands. The rebuild happens implicitly at import startup; the integrity check runs as part of import job startup. No CLI commands are needed.
- Q: If `idmap.db` maps source WI → target WI but the target work item has since been deleted, what should the import do? → A: Fail the revision with a structured error, log it, and skip to the next work item. Silent re-creation is prohibited.
- Q: How should the system handle existing `idmap.db` files that pre-date the `last_revision_index` column? → A: No migration path is required; there are no existing users with prior runs. The schema is defined from scratch with `last_revision_index` included from the outset.
- Q: How should integrity check results be surfaced? → A: Structured OpenTelemetry telemetry only (logs and metrics). No report file is written.
- Q: Can multiple workers access `idmap.db` concurrently? → A: No — single-writer, single-reader per job run. SQLite WAL mode and retry logic are not required. However, if a second agent job is started pointing to the same artifact location, the system MUST detect the conflict at startup and hard-bounce (fail-fast with a clear error) the second job's queue, leaving the first agent undisturbed.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prevent Duplicate Work Items During Import (Priority: P1)

As a migration operator, I want the import to check the ID map before creating work items so that re-running an import (after interruption, crash, or intentional rerun) never creates duplicate work items in the target project.

**Why this priority**: Duplicate work items are the most damaging failure mode — they pollute the target project and are difficult to clean up. This is the primary purpose of the ID map.

**Independent Test**: Can be fully tested by importing a package, interrupting it, and re-running. Verify that work items already created in the first run are reused (not duplicated) in the second run.

**Acceptance Scenarios**:

1. **Given** a package with revision folders and an existing `idmap.db` containing source-to-target mappings, **When** the import processes Stage A (CreatedOrUpdated) for a revision whose source work item ID is already mapped, **Then** the existing target work item ID is used and no new work item is created.
2. **Given** a package with revision folders and an empty `idmap.db`, **When** the import processes Stage A for a new source work item ID, **Then** a new work item is created in the target and the mapping is recorded in `idmap.db`.
3. **Given** a revision folder referencing a source work item that has already been partially imported (e.g., Stage B completed but Stage C did not), **When** the import resumes, **Then** the existing target work item is used and processing continues from the next incomplete stage.

---

### User Story 2 - Rebuild ID Map from Target Provenance Markers (Priority: P1)

As a migration operator who has lost or corrupted the `idmap.db` (or is starting a fresh job against a target that was already partially migrated), I want to rebuild the ID map by scanning the target project for provenance markers (ReflectedWorkItemId stored in a custom field or hyperlink), so that the import can continue without creating duplicates.

**Why this priority**: Without rebuild capability, a lost `idmap.db` means the operator must either accept duplicates or start from scratch with a clean target project. This is operationally unacceptable.

**Independent Test**: Can be tested by deleting `idmap.db`, running the import, and verifying the ID map is rebuilt from target provenance markers before any work items are created.

**Acceptance Scenarios**:

1. **Given** a target project with previously migrated work items bearing provenance markers (custom field or hyperlink), and `idmap.db` does not exist, **When** the import starts, **Then** the system queries the target for all work items with provenance markers, rebuilds `idmap.db`, and proceeds without creating duplicates.
2. **Given** a target project with provenance markers and `idmap.db` already exists, **When** the import starts, **Then** the system seeds missing entries from the target into `idmap.db` (existing entries are not overwritten) using INSERT OR IGNORE semantics.
3. **Given** the operator configures `WorkItemResolutionStrategy` as `TargetField` with `fieldName: "Custom.SourceId"`, **When** the rebuild runs, **Then** all target work items with `Custom.SourceId` populated are queried and their values are used to seed source-to-target mappings.
4. **Given** the operator configures `WorkItemResolutionStrategy` as `TargetHyperlink` with a URL pattern, **When** the rebuild runs, **Then** all target work items with hyperlinks matching the pattern are queried and source IDs are extracted from the URLs.

---

### User Story 3 - Rerun Export to Pick Up New Revisions (Priority: P1)

As a migration operator, I want to re-export a source system (e.g., after time has passed and new revisions have been created, or for an offline TFS that has been updated) and have the export produce only the new revision folders that are not yet in the package, so that I can then import only the delta.

**Why this priority**: Real-world migrations are rarely one-shot. Source systems continue to receive changes. The operator must be able to pick up new revisions without re-exporting the entire dataset. This is equally important for export and import.

**Independent Test**: Can be tested by exporting a package, adding new revisions to the source, re-exporting, and verifying only new revision folders appear in the package.

**Acceptance Scenarios**:

1. **Given** a package that was previously exported, **When** the operator re-runs an export against the same source, **Then** the export cursor (`Checkpoints/workitems.cursor.json`) is read and only revision folders newer than the cursor are written to the package.
2. **Given** a re-export has added new revision folders to the package, **When** the operator runs an import, **Then** the import cursor resumes from its last position and processes only the newly added revision folders.
3. **Given** a re-export for an offline TFS (fresh export + fresh job), **When** the operator starts with a clean package but an existing target with previously migrated work items, **Then** the ID map is rebuilt from target provenance markers (User Story 2) and the import processes all revision folders, using existing target work items for already-migrated sources and creating new ones for new sources.

---

### User Story 4 - ID Map Integrity Check (Priority: P2)

As a migration operator, I want to verify the integrity of the ID map to detect and report inconsistencies (e.g., mappings that point to deleted target work items, source IDs without mappings that exist in the target), so that I can resolve issues before continuing a migration.

**Why this priority**: Corruption or drift in the ID map leads to subtle bugs (wrong work item updated, orphaned items). An integrity check is a safety net for complex multi-run migrations.

**Independent Test**: Can be tested by deliberately corrupting `idmap.db` (e.g., adding a mapping to a non-existent target work item) and running the integrity check to verify it reports the issue.

**Acceptance Scenarios**:

1. **Given** an `idmap.db` with mappings, **When** the import job starts, **Then** the system verifies each mapping by confirming the target work item exists and logs a structured warning via OpenTelemetry for any orphaned or invalid entries.
2. **Given** an integrity check that finds invalid mappings, **When** the import job starts, **Then** a structured warning is logged via OpenTelemetry for each invalid entry including the reason (e.g., "target work item 12345 does not exist"). No report file is written. The job is not aborted.
3. **Given** `idmap.db` exists but is empty (no mappings recorded), **When** the import job starts, **Then** the integrity check completes with no warnings logged and the job proceeds normally.

---

### User Story 5 - Revision-Level Progress Tracking in ID Map (Priority: P2)

As a migration operator, I want the ID map to track the last successfully migrated revision index for each work item, so that a rerun can skip already-applied revisions within a work item and apply only new ones.

**Why this priority**: For sync scenarios where new revisions are exported after an initial migration, the system needs to know which revisions have already been applied to each target work item — not just whether the work item exists.

**Independent Test**: Can be tested by importing a package, adding new revision folders for an existing work item, re-importing, and verifying only the new revisions are applied.

**Acceptance Scenarios**:

1. **Given** a work item with 5 revisions already imported, **When** the operator re-exports with 3 new revisions and re-imports, **Then** the import skips the 5 already-applied revisions and applies only the 3 new ones.
2. **Given** the ID map records `last_revision_index = 4` for source work item 42, **When** the import encounters revision index 5 for source work item 42, **Then** it applies the revision. When it encounters revision index 3, it skips it.
3. **Given** the ID map has no `last_revision_index` for a mapped work item, **When** the import encounters any revision for that work item, **Then** it falls back to the cursor-based approach (process if beyond the cursor, skip if at or before the cursor).

---

### Edge Cases

- What happens when the target work item referenced in `idmap.db` has been deleted in the target system? The integrity check detects this via telemetry. During import, the revision fails with a structured error, is logged, and the import skips to the next work item. Silent re-creation is prohibited.
- What happens when two source work items are mapped to the same target work item? This is an invalid state. The integrity check should flag it. `idmap.db` enforces a unique constraint on `source_id` (PRIMARY KEY), preventing this on the source side.
- What happens when the `idmap.db` file is locked or corrupted? The system should report a clear error and suggest rebuilding.
- What happens when a second agent job is started against the same artifact location while a job is already running? The system acquires an exclusive `Checkpoints/agent.lock` at startup. If a live lock is detected, the incoming job is hard-bounced (fail-fast, structured error, no work done). Stale locks (owning process dead) are replaced and the new job proceeds normally.
- What happens when a re-export produces revision folders that overlap with existing ones (same ticks-workItemId-revisionIndex)? The export should overwrite (idempotent write) and the cursor should advance past the overlap.
- What happens during a rebuild when the target contains work items migrated from a different source project? The provenance marker must include enough context (source org + project + work item ID) to distinguish sources. The URL pattern or field value disambiguates.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST maintain a SQLite database (`Checkpoints/idmap.db`) in the migration package that stores source-to-target work item ID mappings.
- **FR-002**: The system MUST check `idmap.db` before creating a work item in the target (Stage A — CreatedOrUpdated). If a mapping exists, the existing target work item ID MUST be used.
- **FR-003**: The system MUST record a new mapping in `idmap.db` immediately after creating a new work item in the target.
- **FR-004**: The system MUST write a provenance marker (ReflectedWorkItemId) to the target work item after creation, using the configured resolution strategy (custom field or hyperlink).
- **FR-005**: The system MUST support rebuilding `idmap.db` from target provenance markers automatically at import startup when `idmap.db` does not exist. This is an agent-side operation, not a CLI command.
- **FR-006**: The system MUST support two provenance strategies — `TargetField` (custom field storing source work item ID) and `TargetHyperlink` (hyperlink with a URL pattern containing the source ID) — selectable via the `WorkItemResolutionStrategy` extension in configuration.
- **FR-007**: The rebuild process MUST use INSERT OR IGNORE semantics — existing mappings in `idmap.db` are never overwritten by the rebuild.
- **FR-008**: The system MUST support re-export of a source system, producing only new revision folders beyond the export cursor, and subsequent re-import that processes only the newly added folders.
- **FR-009**: The ID map MUST track the last successfully imported revision index per work item, enabling revision-level skip logic during sync/rerun imports. The `last_revision_index` column is part of the base `work_item_map` schema; no schema migration path is needed.
- **FR-010**: The system MUST provide an integrity check that validates `idmap.db` entries against the target system. Results MUST be surfaced exclusively via structured OpenTelemetry telemetry (logs and metrics). No report file is written. The integrity check runs as part of import job startup (agent-side).
- **FR-011**: *(Covered by FR-005.)* The offline TFS re-export scenario — where the operator starts a fresh job against an already-migrated target after a fresh export — is a supported use case of FR-005 (automatic rebuild at startup when `idmap.db` is absent). No additional system requirement beyond FR-005 is needed.
- **FR-012**: The `idmap.db` MUST store attachment upload tracking records (`source_work_item_id, revision_index, relative_path → target_attachment_id`) for idempotent attachment uploads during resume.
- **FR-013**: The rebuild and integrity check operations MUST be streaming — they must not load all target work items into memory at once.
- **FR-014**: The provenance marker WRITE operation (`WriteProvenanceAsync` / ReflectedWorkItemId) MUST work for both Azure DevOps Services and Team Foundation Server export sources. The import-side existence check (`WorkItemExistsAsync`) is scoped to ADO targets only — there is no TFS import target in this platform.
- **FR-015**: When an `idmap.db` mapping references a target work item that has since been deleted in the target system, the import MUST fail that revision with a structured error, log it via OpenTelemetry, and skip to the next work item. Silent re-creation is prohibited.
- **FR-016**: `idmap.db` is accessed by a single writer and single reader per job run. No concurrent multi-process access is supported; SQLite WAL mode and retry logic are not required.
- **FR-017**: At import job startup, the system MUST acquire an exclusive package-level lock (e.g., `Checkpoints/agent.lock` containing the job ID and PID). If a lock file already exists and the owning process is still running, the system MUST hard-bounce the incoming job — fail-fast with a structured error logged via OpenTelemetry and refuse to proceed. The running job is left undisturbed. If the lock file exists but the owning process is no longer running (stale lock), the system MUST replace it and proceed.

### Key Entities

- **WorkItemMapping**: A record linking a source work item ID to a target work item ID, with an optional last-imported revision index. Stored in `idmap.db` table `work_item_map`.
- **AttachmentMapping**: A record linking a source attachment (by work item ID, revision index, and relative path) to a target attachment identifier. Stored in `idmap.db` table `attachment_map`.
- **ProvenanceMarker (ReflectedWorkItemId)**: A value written to the target work item (in a custom field or as a hyperlink) that encodes the source work item identity, enabling discovery and rebuild from the target.
- **ResolutionStrategy**: The configured method for discovering existing source-to-target mappings from the target system (`TargetField` or `TargetHyperlink`).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Zero duplicate work items are created in the target when re-running an import against a previously partially-migrated target.
- **SC-002**: Rebuilding the ID map from a target with 10,000 migrated work items completes within 5 minutes.
- **SC-003**: A re-export followed by re-import processes only the delta — new revision folders — with no reprocessing of previously imported revisions.
- **SC-004**: The integrity check correctly identifies 100% of invalid mappings (target work item deleted, mapping pointing to wrong project).
- **SC-005**: Memory usage during rebuild and integrity check remains bounded regardless of the number of work items (streaming, not batch-load).
- **SC-006**: An operator who loses `idmap.db` can recover and continue the migration without any data loss or duplication in the target.

## Assumptions

- The existing `IIdMapStore` interface and `SqliteIdMapStore` implementation (in `DevOpsMigrationPlatform.Infrastructure`) are the foundation; this feature extends them rather than replacing them.
- The existing `IWorkItemResolutionStrategy` interface (`SeedAsync`, `ResolveSingleAsync`, `WriteProvenanceAsync`) and its implementations (`TargetFieldResolutionStrategy`, `TargetHyperlinkResolutionStrategy`) already provide the core seeding and provenance write logic; this feature builds on that.
- The export cursor (`Checkpoints/workitems.cursor.json`) already enables re-export to produce only new revision folders; this feature ensures the import side correctly handles the delta.
- The `idmap.db` file is package-local (inside `Checkpoints/`) and travels with the package. It is not a control-plane database.
- Revision-level tracking (FR-009) requires a schema addition to the existing `work_item_map` table (adding a `last_revision_index` column).
- The integrity check (FR-010) requires network access to the target system to verify work item existence.
- Architecture docs read: `agents.md`, `docs/architecture.md`, `docs/modules.md`, `docs/configuration.md`, `docs/work-item-iteration-pattern.md`, `.agents/guardrails/system-architecture.md`, `.agents/context/package-format.md`, `.agents/context/import-streaming.md`, `.agents/context/checkpointing.md`, `.agents/context/identity-and-mapping.md`.
