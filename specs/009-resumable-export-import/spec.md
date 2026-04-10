# Feature Specification: Resumable Export and Import

**Feature Branch**: `009-resumable-export-import`  
**Created**: 2026-04-10  
**Status**: Draft  
**Input**: User description: "We need to have the Export resume from where it left off if possible. We may have already exported 20k work items, and we don't want to overwrite them unnecessarily. This should work for both export and import."

## Architecture References

| Document | Status |
|---|---|
| `.agents/guardrails/system-architecture.md` | Confirmed accurate — Rule #4 mandates cursor-based checkpoints; Rule #2 mandates streaming import |
| `.agents/context/checkpointing.md` | Confirmed: defines cursor schema, stage values, and resume logic for import; **export cursor is not yet specified — discrepancy logged** |
| `.agents/context/import-streaming.md` | Confirmed accurate — staged import, idempotency notes, and failure behaviour already defined |
| `.agents/context/job-contract.md` | Confirmed accurate — `MigrationJob` schema does not include a resume mode flag; **gap logged** |
| `docs/architecture.md` | Confirmed accurate — resumability described as a property of the Files layer; no implementation detail |
| `docs/modules.md` | Confirmed accurate — `IDataTypeModule.ExportAsync` and `ImportAsync` contracts |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resume an Interrupted Export (Priority: P1)

An operator ran an export of a large project (e.g. 20,000 work items) and the process was interrupted — either by a network failure, a crash, or a deliberate stop. When the operator re-runs the same export command with the same package destination, the system detects the existing progress record, skips all already-exported work items, and continues from the last successfully exported item. No previously written package files are overwritten or deleted.

**Why this priority**: This is the core pain point described by the user. Large exports can take hours; without resume, any interruption means starting from scratch and risks corrupting already-usable output. Resume on export delivers immediate, standalone value.

**Independent Test**: Run an export against a project with at least 50 work items. Interrupt it after ~half the items are written. Re-run with the same config. Verify that the second run writes only the remaining items and that total exported item count equals the full project count.

**Acceptance Scenarios**:

1. **Given** an export has written 20,000 of 25,000 work items before being interrupted, **When** the operator re-runs the export with the same package path, **Then** the system skips the first 20,000 items and exports only the remaining 5,000.
2. **Given** a progress record exists for a completed export, **When** the operator re-runs the export, **Then** the system reports that all items are already exported and exits successfully without writing any new files.
3. **Given** an export is running for the first time with no existing progress record, **When** the export starts, **Then** the system processes all work items from the beginning and creates a new progress record.
4. **Given** an operator explicitly requests a fresh start (overriding any existing progress), **When** the export runs, **Then** the system deletes the progress record for the module and re-exports all items from scratch.

---

### User Story 2 - Resume an Interrupted Import (Priority: P1)

An operator ran an import from a migration package and the process was interrupted mid-way. When the operator re-runs the same import command, the system detects the existing progress record, skips already-completed work items, and — for a partially processed work item — resumes from the last completed stage (fields, links, or attachments) rather than re-processing stages that already succeeded.

**Why this priority**: Import is a write operation against the target system. Re-processing items already imported can cause duplicates or data inconsistencies. Stage-level resume is critical for data integrity.

**Independent Test**: Run an import from a package with 50 work items. Interrupt mid-way. Re-run with the same config. Verify that already-imported work items are not duplicated in the target and that partially processed work items resume from the correct stage.

**Acceptance Scenarios**:

1. **Given** an import has fully processed 500 of 1,000 work items before being interrupted, **When** the operator re-runs the import, **Then** the system skips the first 500 items and imports only the remaining 500.
2. **Given** an import was interrupted after applying fields but before applying links for a specific work item, **When** the operator re-runs the import, **Then** the system resumes that work item from the link-application stage without re-applying fields.
3. **Given** a target work item was already created in a previous run, **When** an import resumes and encounters that item's creation stage, **Then** the system uses the existing mapped target ID and skips creation.
4. **Given** an attachment was already uploaded in a previous run, **When** an import resumes and encounters that attachment's upload stage, **Then** the system skips the upload and reuses the previously recorded target attachment ID.

---

### User Story 3 - Resume a Both-Mode Job (Priority: P2)

An operator ran a migration in Both mode (export then import in a single job) and the job was interrupted during the import phase after export completed. When the operator re-runs the job, the export phase is marked as complete and is not re-run; the import phase resumes from where it was interrupted.

**Why this priority**: Both-mode is the most common production pattern. Without phase-level resume, operators would be forced to manually reconstruct a two-job workflow to avoid re-running the (often slow) export.

**Independent Test**: Run a Both-mode job. Interrupt it during import. Re-run. Verify the export phase is skipped and the import resumes.

**Acceptance Scenarios**:

1. **Given** a Both-mode job where export completed and import was interrupted, **When** the job is re-run, **Then** the export phase is skipped entirely and the import resumes from the cursor position.
2. **Given** a Both-mode job where export was interrupted before completing, **When** the job is re-run, **Then** the export resumes first; once complete, the import runs from the beginning.
3. **Given** a Both-mode job that completed fully, **When** the job is re-run without a forced fresh start, **Then** the system reports all export and import items as already processed and exits cleanly.

---

### User Story 4 - Operator Visibility into Resume State (Priority: P2)

Before re-running an export or import, an operator wants to see what the current progress record contains — how many items have been processed, which module's cursor is at which position, and whether a resume or a fresh start is the planned action.

**Why this priority**: Operators need confidence that resume will do the right thing before committing a long re-run. Without visibility, they have no way to verify the state before proceeding.

**Independent Test**: Run an export, interrupt it, then run a `status` or `dry-run` check against the package. Verify output describes what has been done and what remains.

**Acceptance Scenarios**:

1. **Given** a package with a partial export progress record, **When** the operator queries the package status, **Then** the system reports how many items the cursor has passed and what the next item to be processed is.
2. **Given** a package with no progress record, **When** the operator queries the package status, **Then** the system reports that no previous progress exists and a full run will begin.

---

### Edge Cases

- What happens when the package path does not exist on resume? The system treats this as a fresh run and creates a new package.
- What happens when the progress record is corrupt or partially written? The system falls back to the last valid cursor position it can read; if the file is unreadable, it treats this as a fresh start and logs a warning.
- What happens when the source data has changed between the interrupted run and the resume? The export cursor skips already-exported positions by folder path; new work items added to the source since the last run that fall within the query scope will be exported if their natural sort position is after the cursor. Work items that were added before the cursor position will not be re-fetched. This is documented as a known limitation.
- What happens on forced fresh start if the package already contains partial data? The existing package files and progress records for that module are deleted, and the module starts from scratch. Files belonging to other completed modules in the same package are not affected.
- Can two jobs share the same package path? Two concurrent jobs writing to the same package path is forbidden. The system must detect this condition and reject the second job.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST check for an existing per-module progress record (cursor file) at the start of each export run and, if found, resume enumeration from the position recorded in the cursor.
- **FR-002**: During export, the system MUST write or update the cursor file after each work item (or configurable batch of items) is successfully written to the package, so that an interruption results in at most a bounded amount of re-work.
- **FR-003**: The system MUST check for an existing per-module cursor file at the start of each import run and, if found, skip all revision folders whose path is lexicographically less than or equal to the recorded cursor position.
- **FR-004**: When resuming an import at a partially processed revision folder, the system MUST resume from the last completed stage (`CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`) within that folder rather than restarting the folder from scratch.
- **FR-005**: The system MUST use the identity map (`Checkpoints/idmap.db` or `idmap.json`) to detect already-created target work items during resume and skip creation without duplicating them.
- **FR-006**: The system MUST use the attachment record in the identity map to detect already-uploaded attachments during resume and skip re-upload.
- **FR-007**: The system MUST support a forced fresh-start option that deletes the module's cursor file (and, for import, the relevant identity map entries) and re-processes all items.
- **FR-008**: In Both mode, the job engine MUST track export-phase and import-phase completion independently. If the export phase has a `Completed` cursor and the import phase has an in-progress or absent cursor, the job engine MUST skip the export phase entirely and proceed to resume the import.
- **FR-009**: Each module MUST maintain its own cursor file using the naming convention `Checkpoints/<moduleName-lowercase>.cursor.json`. Modules MUST NOT share cursor files.
- **FR-010**: The export cursor schema MUST follow the same structure as the import cursor schema: `lastProcessed` (relative path of last written revision folder), `stage` (`Completed` for export), and `updatedAt` (UTC timestamp).
- **FR-011**: The system MUST emit an observable progress event when a resume is detected, stating the module name, the cursor position, and the number of items skipped.
- **FR-012**: Resuming MUST NOT overwrite or delete any previously written package files for items already past the cursor position.
- **FR-013**: The system MUST detect and reject a job submission if another job is currently writing to the same package path.

### Key Entities

- **Cursor File**: A JSON document stored at `Checkpoints/<moduleName-lowercase>.cursor.json` within the package. Records the last successfully processed revision folder path, the last completed stage, and the timestamp. One file per module per package.
- **Identity Map**: A local store (`Checkpoints/idmap.db` or `Checkpoints/idmap.json`) that maps source work item IDs to target work item IDs and records uploaded attachment IDs. Used to detect already-created or already-uploaded items on resume.
- **Resume Position**: The point in chronological revision folder order from which a re-run begins. Derived from the cursor file's `lastProcessed` field.
- **Stage**: One of `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`. The granularity at which import resume operates within a single revision folder.
- **Forced Fresh Start**: A job or command option that causes the system to discard the existing cursor and re-process from the beginning.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A re-run of an interrupted export skips all already-written work items and produces a complete package whose total item count equals the source project's item count — verified with no duplicates.
- **SC-002**: A re-run of an interrupted import does not create duplicate work items in the target system — verified by querying the target before and after resume.
- **SC-003**: The time to resume an export of 20,000 already-exported items is under 60 seconds (cursor evaluation is O(1) to find the resume position, not O(n)).
- **SC-004**: A forced fresh start option reliably discards progress and produces a clean full run, verified by confirming all progress record files are absent at the start of the run.
- **SC-005**: Resume correctly handles an interruption at any stage boundary within import (fields applied but links not yet applied) without data loss or duplication in the target.
- **SC-006**: Operators can observe in the run log or progress output that a resume was detected and how many items were skipped.

## Assumptions

- The package path is the same between the original interrupted run and the resume run. If the operator changes the package path, a fresh run begins.
- The scope query (WIQL) for export is assumed to be deterministic and returns the same logical set of work items in the same order across runs. New items added to the source between runs that fall after the cursor position in sort order will be included; items before the cursor will not be re-fetched on resume.
- Both export cursor and import cursor use the same JSON schema (defined in `.agents/context/checkpointing.md`). The export cursor always writes `stage: "Completed"` since export has no intra-item stages.
- The forced fresh-start option applies at the module level. The operator can reset one module's cursor without affecting other modules' cursors in the same package.
- The `idmap.db`/`idmap.json` identity map is scoped to a single package. It is never shared between packages.
- Import-phase resume is only safe if the package was not modified after the interrupted run (i.e., no new export was run against the same package between the interrupted import and the resume). This constraint is not enforced by the system in v1 but is documented as an operator responsibility.
- The feature does not add retry-on-failure within a single run (that is covered by resilience policies elsewhere). It handles restart of the entire job process.
