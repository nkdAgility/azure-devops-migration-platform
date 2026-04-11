# Implementation Plan: Inline Comment Fetching for Edit/Delete Revisions

**⏸️ STATUS: DEFERRED — ALL TASKS BLOCKED**

---

## Deferral Reason

**Upstream SDK Bug Blocks Implementation:**
- `AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` has incorrect parameter mapping
- API Error: "A query parameter specified in the request URI is outside the permissible range: $top"
- Any implementation will fail at runtime until this is fixed
- **Decision:** Do NOT proceed with implementation until the bug is fixed upstream

**Non-blocking Rationale:**
- Comment additions are already captured (System.History field)
- Full export functionality is complete
- This is an enhancement for comment edit/delete history only

---

## Task List (Deferred — Do Not Execute)

### ❌ Task 1: Add Comment Detection Method [BLOCKED]
**Status:** Pre-implementation (Deferred)  
**File:** `WorkItemExportOrchestrator.cs`  
**Dependencies:** None (but blocked by SDK bug)  
**Description:** Implement `IsCommentEditOrDeleteRevision()` static method  

**Prerequisite:** Upstream SDK bug in `AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` must be fixed

**Method Design** (for reference):
```csharp
private static bool IsCommentEditOrDeleteRevision(WorkItemRevision revision)
{
    // Returns true if only System.CommentCount changed (likely edit/delete)
    // Returns false if System.History present (addition)
}
```

---

### ❌ Task 2: Add Factory Injection to Orchestrator [BLOCKED]
**Status:** Pre-implementation (Deferred)  
**File:** `WorkItemExportOrchestrator.cs`  
**Dependencies:** Task 1  
**Description:** Add `IWorkItemCommentSourceFactory` parameter to constructor  

**Prerequisite:** Task 1 complete + SDK bug fixed

---

### ❌ Task 3: Update Dependency Injection in WorkItemsModule [BLOCKED]
**Status:** Pre-implementation (Deferred)  
**File:** `WorkItemsModule.cs`  
**Dependencies:** Task 2  
**Description:** Accept factory in module and pass to orchestrator  

**Prerequisite:** Task 2 complete + SDK bug fixed

---

### ❌ Task 4: Integrate Inline Comment Fetching [BLOCKED]
**Status:** Pre-implementation (Deferred)  
**File:** `WorkItemExportOrchestrator.cs` - `ExportAsync()` method  
**Dependencies:** Task 3  
**Description:** Fetch and store comments for edit/delete revisions  

**Note:** This task will immediately fail at runtime with the current SDK bug:
```
AzureDevOpsWorkItemCommentSource.GetCommentsAsync()
  ↓
  Calls Azure DevOps Comments API v7.1-preview.4
  ↓
  API Error: "$top parameter out of range"
  ↓
  Export halts
```

**Prerequisite:** Task 3 complete + SDK bug fixed in `AzureDevOpsWorkItemCommentSource`

---

### ❌ Task 5: Remove Legacy Comment Export Post-Processing [BLOCKED]
**Status:** Pre-implementation (Deferred)  
**File:** `WorkItemExportOrchestrator.cs` - `ExportAsync()` method  
**Dependencies:** Task 4  
**Description:** Remove work-item transition hooks and post-processing  

**Prerequisite:** Task 4 complete + all inline fetching working

---

### ❌ Task 6: Add Using Statements [BLOCKED]
**Status:** Pre-implementation (Deferred)  
**File:** `WorkItemExportOrchestrator.cs`  
**Dependencies:** Task 5  
**Description:** Add required imports  

**Prerequisite:** All previous tasks complete

---

### ❌ Task 7: Build and Verify [BLOCKED]
**Status:** Pre-implementation (Deferred)  
**File:** All modified files  
**Dependencies:** Task 6  
**Description:** Full solution build and test verification  

**Prerequisite:** All implementation tasks complete + SDK bug fixed
---

## Dependencies (For Future Reference)

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

