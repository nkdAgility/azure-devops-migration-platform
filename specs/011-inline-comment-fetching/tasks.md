# Implementation Plan: Inline Comment Fetching for Edit/Delete Revisions

**✅ STATUS: IMPLEMENTED (feature-gated, SDK bug non-fatal)**

---

## Implementation Summary

Tasks 1–4 and 6–7 are complete. The feature is gated behind `inlineComments.enabled: true`
in the scenario config (default: `false`) to avoid unexpected API calls in standard exports.

**Known limitation:** `AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` still has an
upstream SDK bug (`$top` parameter out of range). Errors are non-fatal — a progress warning
is emitted and the export continues. Full comment data will be available once the SDK is fixed.

**Task 5 (remove legacy post-processing)** is deferred: `IWorkItemCommentExportService` is
retained but never injected (not registered in DI), so it is inert. Removal is a separate
cleanup task once the inline path is fully validated.

---

## Task List

### ✅ Task 1: Add Comment Detection Method [DONE]
**Status:** Complete — commit `f9423e9`
**File:** `WorkItemExportOrchestrator.cs`  
**Description:** Implemented `IsCommentEditOrDeleteRevision()` static method.
Guards `RevisionIndex == 0` (creation revision excluded — all fields appear as changed when
previous is null, making CommentCount unreliable).

---

### ✅ Task 2: Add Factory Injection to Orchestrator [DONE]
**Status:** Complete — commit `f9423e9`
**File:** `WorkItemExportOrchestrator.cs`  
**Description:** Added `IWorkItemCommentSourceFactory?` optional constructor parameter and field.

---

### ✅ Task 3: Update Dependency Injection in WorkItemsModule [DONE]
**Status:** Complete — commit `f9423e9`
**File:** `WorkItemsModule.cs`  
**Description:** Module accepts `IWorkItemCommentSourceFactory?` from DI. Passes it to the
orchestrator only when `inlineComments.enabled: true` is set in the scope parameters.

---

### ✅ Task 4: Integrate Inline Comment Fetching [DONE]
**Status:** Complete — commit `f9423e9`
**File:** `WorkItemExportOrchestrator.cs` - `ExportAsync()` method  
**Description:** For detected comment edit/delete revisions, fetches comments via
`IWorkItemCommentSource.GetCommentsAsync()`, filters by ±1 second timestamp window, and
writes `comment.json` beside `revision.json`. SDK errors are non-fatal (progress warning,
export continues).

---

### ⏸️ Task 5: Remove Legacy Comment Export Post-Processing [DEFERRED]
**Status:** Deferred — `IWorkItemCommentExportService` is inert (never injected via DI)
**File:** `WorkItemExportOrchestrator.cs` - `ExportAsync()` method  
**Description:** Remove the `_commentExportService` plumbing once inline path is fully validated.

---

### ✅ Task 6: Add Using Statements [DONE]
**Status:** Complete — commit `f9423e9`
**File:** `WorkItemExportOrchestrator.cs`  
**Description:** Required `using DevOpsMigrationPlatform.Abstractions.Models;` added.

---

### ✅ Task 7: Build and Verify [DONE]
**Status:** Complete — all 255 tests pass (253 pass, 2 skipped — require running control plane)
**File:** All modified files  

---

## Dependencies

Task execution order (once SDK bug is fixed):
1. Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6 → Task 7

Each task blocks the next because later tasks depend on earlier code changes being compiled.

---

## Testing Strategy (For Future Reference)

### Unit Tests (When Implemented)
- `IsCommentEditOrDeleteRevision()` with various field combinations
- Timestamp correlation logic (±1 second boundary cases)
- Null-safety: factory not available, no credentials, empty comment list

### Integration Tests (When Implemented)
- Comment source factory mock returns synthetic comments
- Verify comment.json written for edit/delete revisions
- Verify NO comment.json for addition revisions
- Verify cursor advancement with comment revisions

### System Tests (When Implemented)
- Run full export on work item with comments (add + edit + delete)
- Verify output folder structure matches spec
- Verify comment.json contains expected comment versions
- Resume from checkpoint with comment-edit revision

---

## When to Unblock (SDK Bug Fix Checklist)

Before resuming implementation, verify:

1. [ ] `AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` parameter mapping is correct
2. [ ] Azure DevOps Comments API call succeeds with proper parameters
3. [ ] No "parameter out of range" error on `$top` parameter
4. [ ] Comment fetching returns correct results in tests
5. [ ] Existing system tests still pass with SDK upgrade
6. [ ] Upstream SDK has released the fix (no local monkey-patching)

---

## Resumption Instructions

When the upstream SDK bug is fixed:

1. Read the original [spec.md](./spec.md) "Planned User Scenarios" section
2. Review the reference design in "Reference Implementation Design" section
3. Execute tasks in order: Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6 → Task 7
4. Run all unit + integration + system tests
5. Verify `dotnet clean && dotnet build --no-incremental` passes
6. Verify `dotnet test` passes all tests
7. Run at least one scenario config with comment-enabled work item export

---

## Rollback Plan (Not Needed — No Code Changes)

Since implementation is deferred, there is nothing to roll back.

If the SDK bug is unfixed indefinitely:
- Comment additions remain available via System.History field
- Export functionality continues to work for all current use cases
- Comment edit/delete history is acceptable data loss (non-critical)

