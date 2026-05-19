# Inline Comment Fetching for Edit/Delete Revisions

**Status:** ✅ IMPLEMENTED (feature-gated — `Modules.WorkItems.Extensions.Comments.Enabled`)  
**Priority:** Medium  
**Module:** WorkItems Export  

---

## Implementation Notes

The feature is implemented and gated behind `Modules.WorkItems.Extensions.Comments.Enabled`.
Inline comment fetch failures are non-fatal by design (warning + continue) so export remains
deterministic and resumable.

**Note:** Earlier versions of this spec documented an SDK `$top` bug as a blocker. Current code
uses the project-scoped comments API overload and includes handling/notes in the connector.

See [tasks.md](tasks.md) for per-task completion status.

---

## Current status

- Implemented in export orchestration, DI wiring, connector factories, and tests.
- `comment.json` is written beside `revision.json` for detected comment edit/delete revisions.
- Task-level reconciliation is captured in `tasks.md`.

## Remaining incomplete work (IDs)

- Task 7 — full-solution build + full-solution test run evidence still needs to be freshly captured in this spec set.

## Completed because superseded (IDs + source)

- None.

## Contradictions and reconciliation

- Reconciled status contradiction (`IMPLEMENTED` vs `DEFERRED`) to implemented.
- Reconciled stale config key references from `inlineComments.enabled` to `Modules.WorkItems.Extensions.Comments.Enabled`.
- Reconciled stale blocker narrative: SDK issue is no longer treated as an implementation blocker in this spec.

## Verification evidence

- `src\DevOpsMigrationPlatform.Infrastructure.Agent\Export\WorkItemExportOrchestrator.cs` (`IsCommentEditOrDeleteRevision`, inline comment fetch/write block).
- `src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\WorkItemsModule.cs` (conditional factory wiring via `Comments.Enabled`).
- `src\DevOpsMigrationPlatform.Infrastructure.AzureDevOps\Export\AzureDevOpsWorkItemCommentSource.cs` (project-scoped `GetCommentsAsync` overload and pagination).
- `tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\Export\WorkItemExportOrchestratorTests.cs` (detection and comment.json behavior tests).
- Reconciliation session commands: targeted build and tests succeeded (`Infrastructure.Agent` build; `WorkItemExportOrchestratorTests` 29/29 passed).

---

## Architecture References

- ✅ [docs/architecture.md](../../docs/architecture.md) — Confirmed: revision-centric model applies
- ✅ [.agents/20-guardrails/core/architecture-boundaries.md](../../.agents/20-guardrails/core/architecture-boundaries.md) — Confirmed: no streaming violation
- ✅ [.agents/30-context/domains/migration-package-concept.md](../../.agents/30-context/domains/migration-package-concept.md) — Confirmed: comment.json placement valid
- ✅ [docs/module-development-guide.md](../../docs/module-development-guide.md) — Confirmed: module coordination pattern applies

---

## Problem Statement

Azure DevOps WorkItem comments have two channels:
1. **System.History field** (revision-level): Captures comment **additions** only
   - Already exported in revision.json
   - No API call needed
   - No data loss for additions
2. **Comments API** (separate endpoint): Contains full version history including edits and deletes
   - Edit/delete events are invisible in System.History (only CommentCount changes)
   - Requires separate API call to retrieve
   - Used by export for inline edit/delete comment capture

**Symptom addressed by this feature:** Comment edits and deletions can now be captured into inline `comment.json` artifacts instead of being lost.

---

## Implementation Status

✅ **Completed:**
- Problem analysis and dual-channel architecture documented
- Data model designed (comment.json format, file location)
- Functional requirements specified
- Test scenarios planned

✅ **Implemented in code:**
- Inline comment detection and fetch path exists in `WorkItemExportOrchestrator`
- `IWorkItemCommentSourceFactory` is wired through `WorkItemsModule`
- Connector factories are registered (Azure DevOps and Simulated)
- Unit/integration tests exist for detection and comment.json behavior

**Non-blocking rationale:**
- Comment additions ARE captured (via System.History in revision.json)
- Export functionality is complete for current scope
- Comment edit/delete history is enhancement (not gap)
- No customer-facing feature regression

---

## User Scenarios

### Scenario 1: Export Work Item with Comment Addition
**Actor:** Export operator  
**Context:** Exporting a work item where a comment was added in revision 10  
**Action:** Run export with `Comments.Enabled = true`  
**Expected Outcome:**
- Revision 10 folder contains `revision.json` with System.History field showing comment text
- No additional `comment.json` file (comment data already in System.History)
- Export completes successfully

### Scenario 2: Export Work Item with Comment Edit
**Actor:** Export operator  
**Context:** Exporting a work item where a comment was edited in revision 11  
**Action:** Run export with `Comments.Enabled = true`  
**Expected Outcome:**
- Revision 11 folder contains `revision.json` (metadata only)
- Revision 11 folder contains `comment.json` with edited comment versions
- Comment versions matched by timestamp (±1 second from revision.ChangedDate)
- Export completes successfully

### Scenario 3: Export Work Item with Comment Deletion
**Actor:** Export operator  
**Context:** Exporting a work item where a comment was deleted/marked isDeleted  
**Action:** Run export with `Comments.Enabled = true`  
**Expected Outcome:**
- Revision with deletion contains `revision.json`
- Revision contains `comment.json` with isDeleted=true for the comment
- Export completes successfully

### Scenario 4: Resume Export with Comment Edit Revisions
**Actor:** Export operator  
**Context:** Export was interrupted at revision 11 (a comment edit revision)  
**Action:** Resume export from checkpoint  
**Expected Outcome:**
- Comment-edit revision is re-exported with correct comment.json
- No duplicate data in package
- Export continues from correct cursor position

---

## Functional Requirements

1. **Comment Detection**
   - Detect comment addition revisions: System.History field present
   - Detect comment edit/delete revisions: System.CommentCount changed AND no System.History field
   - Comment additions require NO API call (data in System.History)
   - Comment edit/delete revisions MUST fetch API data via the configured comment source

2. **Comment Fetching by Timestamp**
   - For edit/delete revisions, fetch all comment versions for the work item
   - Filter comments by creation or modification timestamp
   - Match criteria: `abs(comment.ModifiedDate | comment.CreatedDate - revision.ChangedDate) <= 1.0 second`
   - Store matching comments in `comment.json` beside `revision.json`

3. **Dependency Injection**
   - `IWorkItemCommentSourceFactory` injected into `WorkItemsModule`
   - Factory passed to `WorkItemExportOrchestrator` during construction
   - Orchestrator creates source on-demand for comment edit/delete revisions

4. **File Output**
   - Comment additions: System.History field in revision.json (no comment.json created)
   - Comment edits/deletes: comment.json file created in same folder as revision.json
   - Format: JSON array of matching `WorkItemComment` objects
   - Location: `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/comment.json`

5. **Error Handling**
   - If comment API call fails, log warning and continue (non-fatal)
   - If no comments match timestamp, skip creating comment.json (revision has metadata only)
   - Propagate CancellationToken on all async operations

6. **Memory Safety**
   - Stream comments as async enumerable (no buffering all comments in memory)
   - Filter by timestamp incrementally as each comment is received
   - No revision list or comment list accumulated across work items

---

## Technical Design

### Key Entities

#### WorkItemRevision
- **Fields**: IReadOnlyList<WorkItemField>
- **Key fields for comment detection**:
  - `System.History` (string): Raw comment text for additions
  - `System.CommentCount` (int): Total comment count
  - `ChangedDate` (DateTimeOffset): Timestamp for matching

#### WorkItemComment
- **Fields**: commentId, version, text, format, isDeleted, createdDate, modifiedDate
- **Source**: Azure DevOps Comments API (v7.1-preview.4)
- **Stored in**: comment.json alongside revision.json

---

## Success Criteria

1. **Export Coverage**
   - Comment additions captured in System.History (no additional API calls)
   - Comment edits/deletes fetched and stored in comment.json (API-backed)
   - All 4 user scenarios complete without errors

2. **Memory Efficiency**
   - No revision list accumulated in memory (one at a time via async enumerable)
   - Comments streamed from API without buffering entire result set
   - Timestamp-based filtering applied incrementally

3. **Correctness**
   - Timestamp matching within ±1 second produces correct comment versions
   - Resume functionality restores correct cursor and reprocesses comment revisions
   - No duplicate comment data across package revisions

4. **Testability**
   - System test verifies comment.json exists for revision 11 (edit revision)
   - System test verifies revision 10 has System.History but no comment.json
   - Unit tests verify IsCommentEditOrDeleteRevision() detection logic
   - Timestamp correlation tested with synthetic comment data

---

## Assumptions

1. Revisions are processed one at a time (streaming model)
2. System.History field is present if and only if comment was added in that revision
3. System.CommentCount change without System.History indicates edit/delete
4. Timestamp ±1 second is sufficient to correlate comments with revisions
5. IWorkItemCommentSourceFactory is registered in DI container
6. Comment API returns comments in chronological order
7. Full-repository build/test evidence is captured separately from this spec during reconciliation runs

---

## Out of Scope

- Nested/threaded comment replies (not yet in Azure DevOps model)
- Comment thread reconstruction from multiple revisions
- Bulk comment validation
- Comment attachment downloads (handled separately)

---

## Reference Implementation Design (Implemented)

### Classes Modified
- `WorkItemExportOrchestrator`: Add comment detection + inline fetching
- `WorkItemsModule`: Add factory injection and pass-through
- Imports: Add `System.Linq` and `DevOpsMigrationPlatform.Abstractions.Models`

### Methods Added
- `IsCommentEditOrDeleteRevision(WorkItemRevision)`: Static detection method
  - Returns false for additions (System.History present)
  - Returns true for edit/delete (CommentCount change without System.History)

### Methods Removed
- Removed post-processing comment export (worked on per-work-item basis)
- Removed work-item transition hooks for comment export
- Simplified ExportAsync() to inline comment handling per revision

### Design Pattern
- Unified revision export: one revision folder contains revision.json + optional comment.json
- No separate comment export pass
- Timestamp-based API correlation
- Factory pattern for comment source creation

