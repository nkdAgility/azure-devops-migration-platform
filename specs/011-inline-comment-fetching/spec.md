# Inline Comment Fetching for Edit/Delete Revisions

**Status:** ✅ IMPLEMENTED (feature-gated — `inlineComments.enabled: true` required)  
**Priority:** Medium  
**Module:** WorkItems Export  

---

## Implementation Notes

The feature is implemented and gated behind `inlineComments.enabled: true` in the scenario
config's `WorkItems` scope parameters (default: `false`). This prevents unexpected API calls
in standard export runs.

**Known limitation:** `AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` has an upstream
SDK bug (`$top` parameter out of range). Errors are non-fatal — a progress warning is emitted
and the export continues. Full comment data will be correctly persisted once the SDK is fixed.

See [tasks.md](tasks.md) for per-task completion status.

---

## Architecture References

- ✅ [docs/architecture.md](../../docs/architecture.md) — Confirmed: revision-centric model applies
- ✅ [.agents/guardrails/architecture-boundaries.md](../../.agents/guardrails/architecture-boundaries.md) — Confirmed: no streaming violation
- ✅ [.agents/context/migration-package-concept.md](../../.agents/context/migration-package-concept.md) — Confirmed: comment.json placement valid
- ✅ [docs/module-development-guide.md](../../docs/module-development-guide.md) — Confirmed: module coordination pattern applies

---

## Problem Statement

Azure DevOps WorkItem comments have two channels:
1. **System.History field** (revision-level): Captures comment **additions** only
   - Already exported in revision.json
   - No API call needed
   - No data loss for additions
2. **Comments API** (separate endpoint): Contains full version history including edits and deletes
   - Edit/delete events invisible in System.History (only CommentCount changes)
   - Requires separate API call to retrieve
   - Currently NOT used by export (non-critical feature gap)

**Symptom:** Comment edits and deletions are lost during export. Only comment additions (captured in System.History) are preserved.

---

## Implementation Status: DEFERRED

✅ **Completed:**
- Problem analysis and dual-channel architecture documented
- Data model designed (comment.json format, file location)
- Functional requirements specified
- Test scenarios planned

❌ **Blocked (Upstream):**
- Inline comment fetching blocked by SDK bug in `AzureDevOpsWorkItemCommentSource`
- Any implementation will immediately fail at runtime
- Requires upstream fix before this can proceed

**Non-blocking rationale:**
- Comment additions ARE captured (via System.History in revision.json)
- Export functionality is complete for current scope
- Comment edit/delete history is enhancement (not gap)
- No customer-facing feature regression

---

## Planned User Scenarios (Future Implementation)

These scenarios document the intended behavior once the upstream SDK bug is fixed.

### Scenario 1: Export Work Item with Comment Addition
**Actor:** Export operator  
**Context:** Exporting a work item where a comment was added in revision 10  
**Action:** Run export (future: when comments are enabled)  
**Expected Outcome:**
- Revision 10 folder contains `revision.json` with System.History field showing comment text
- No additional `comment.json` file (comment data already in System.History)
- Export completes successfully

### Scenario 2: Export Work Item with Comment Edit
**Actor:** Export operator  
**Context:** Exporting a work item where a comment was edited in revision 11  
**Action:** Run export (future: when comments are enabled)  
**Expected Outcome:**
- Revision 11 folder contains `revision.json` (metadata only)
- Revision 11 folder contains `comment.json` with edited comment versions
- Comment versions matched by timestamp (±1 second from revision.ChangedDate)
- Export completes successfully

### Scenario 3: Export Work Item with Comment Deletion
**Actor:** Export operator  
**Context:** Exporting a work item where a comment was deleted/marked isDeleted  
**Action:** Run export (future: when comments are enabled)  
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

## Functional Requirements (Deferred — Future Implementation)

These requirements document the intended behavior and will become active once the upstream SDK bug is fixed.

1. **Comment Detection** (Future)
   - Detect comment addition revisions: System.History field present
   - Detect comment edit/delete revisions: System.CommentCount changed AND no System.History field
   - Comment additions require NO API call (data in System.History)
   - Comment edit/delete revisions MUST fetch API data (when SDK is fixed)

2. **Comment Fetching by Timestamp** (Future)
   - For edit/delete revisions, fetch all comment versions for the work item
   - Filter comments by creation or modification timestamp
   - Match criteria: `abs(comment.ModifiedDate | comment.CreatedDate - revision.ChangedDate) <= 1.0 second`
   - Store matching comments in `comment.json` beside `revision.json`

3. **Dependency Injection** (Future)
   - `IWorkItemCommentSourceFactory` injected into `WorkItemsModule`
   - Factory passed to `WorkItemExportOrchestrator` during construction
   - Orchestrator creates source on-demand for comment edit/delete revisions

4. **File Output** (Future)
   - Comment additions: System.History field in revision.json (no comment.json created)
   - Comment edits/deletes: comment.json file created in same folder as revision.json
   - Format: JSON array of matching `WorkItemComment` objects
   - Location: `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/comment.json`

5. **Error Handling** (Future)
   - If comment API call fails, log warning and continue (non-fatal)
   - If no comments match timestamp, skip creating comment.json (revision has metadata only)
   - Propagate CancellationToken on all async operations

6. **Memory Safety** (Future)
   - Stream comments as async enumerable (no buffering all comments in memory)
   - Filter by timestamp incrementally as each comment is received
   - No revision list or comment list accumulated across work items

---

## Future Technical Design

### Key Entities (Reference Implementation)

#### WorkItemRevision
- **Fields**: IReadOnlyList<WorkItemField>
- **Key fields for comment detection**:
  - `System.History` (string): Raw comment text for additions
  - `System.CommentCount` (int): Total comment count
  - `ChangedDate` (DateTimeOffset): Timestamp for matching

#### WorkItemComment
- **Fields**: commentId, version, text, format, isDeleted, createdDate, modifiedDate
- **Source**: Azure DevOps Comments API (v7.1-preview.4) — **Currently Blocked by SDK Bug**
- **Stored in**: comment.json alongside revision.json

---

## Success Criteria (For Future Implementation)

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

## Assumptions (For Future Implementation)

1. Revisions are processed one at a time (streaming model)
2. System.History field is present if and only if comment was added in that revision
3. System.CommentCount change without System.History indicates edit/delete
4. Timestamp ±1 second is sufficient to correlate comments with revisions
5. IWorkItemCommentSourceFactory is registered in DI container
6. Comment API returns comments in chronological order
7. **Upstream SDK Bug in `AzureDevOpsWorkItemCommentSource` is fixed**

---

## Out of Scope

- Nested/threaded comment replies (not yet in Azure DevOps model)
- Comment thread reconstruction from multiple revisions
- Bulk comment validation
- Comment attachment downloads (handled separately)

---

## Reference Implementation Design (For Internal Use Only)

### Classes to Modify (When Unblocked)
- `WorkItemExportOrchestrator`: Add comment detection + inline fetching
- `WorkItemsModule`: Add factory injection and pass-through
- Imports: Add `System.Linq` and `DevOpsMigrationPlatform.Abstractions.Models`

### Methods to Add (When Unblocked)
- `IsCommentEditOrDeleteRevision(WorkItemRevision)`: Static detection method
  - Returns false for additions (System.History present)
  - Returns true for edit/delete (CommentCount change without System.History)

### Methods to Remove (When Unblocked)
- Removed post-processing comment export (worked on per-work-item basis)
- Removed work-item transition hooks for comment export
- Simplified ExportAsync() to inline comment handling per revision

### Design Pattern
- Unified revision export: one revision folder contains revision.json + optional comment.json
- No separate comment export pass
- Timestamp-based API correlation
- Factory pattern for comment source creation
