# Implementation Plan: Feature 011 – Inline Comment Fetching

**Status:** Ready for Implementation (pending upstream SDK fix)  
**Technology Stack:** .NET 10, C# 10+, MSTest, Reqnroll  
**Architecture:** Modular export platform with streaming semantics  
**Estimated Effort:** 2-3 days (5 tasks)  

---

## Phase 1: Pre-Implementation Setup (1 hour)

### 1.1 Verify Upstream SDK Fix
**Task:** Confirm `AzureDevOpsWorkItemCommentSource.GetCommentsAsync()` bug is resolved

**Steps:**
1. Check Azure DevOps SDK changelog for fix
2. Update NuGet package to latest version with fix
3. Run unit test of `GetCommentsAsync()` to verify it works
4. Verify no new parameter mapping issues

**Success Criteria:**
- ✅ Azure DevOps Comments API v7.1-preview.4 call succeeds
- ✅ Comments returned without "$top parameter out of range" error
- ✅ Test passes with synthetic work item IDs

**If Blocked:**
- Defer implementation indefinitely
- Update status to "Awaiting next SDK release"

---

## Phase 2: Implementation (2-3 days)

### 2.1 Task 1: Add Comment Detection Method (2 hours)
**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**What:** Implement `IsCommentEditOrDeleteRevision()` static method

**Implementation:**
```csharp
private static bool IsCommentEditOrDeleteRevision(WorkItemRevision revision)
{
    if (revision.Fields.Count == 0)
        return false;

    var fieldNames = new HashSet<string>(
        revision.Fields.Select(f => f.ReferenceName), 
        StringComparer.Ordinal);

    // Skip additions (System.History present = addition)
    if (fieldNames.Contains("System.History"))
        return false;

    // Edit/delete: only CommentCount and optionally ChangedDate
    if (fieldNames.Contains("System.CommentCount"))
    {
        return fieldNames.Count <= 2 && 
               (fieldNames.Count == 1 || fieldNames.Contains("System.ChangedDate"));
    }

    return false;
}
```

**Testing:**
1. Unit test: No fields → false
2. Unit test: System.History present → false
3. Unit test: CommentCount only → true
4. Unit test: CommentCount + ChangedDate → true
5. Unit test: CommentCount + other fields → false

**Acceptance Criteria:**
- ✅ Method is pure (no side effects)
- ✅ All boundary cases tested
- ✅ Code compiles without warnings

---

### 2.2 Task 2: Add Factory Injection to Orchestrator (1 hour)
**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**What:** Add `IWorkItemCommentSourceFactory?` parameter to constructor

**Changes:**
1. Add constructor parameter: `IWorkItemCommentSourceFactory? commentSourceFactory = null`
2. Store: `_commentSourceFactory = commentSourceFactory;`
3. Add field declaration: `private readonly IWorkItemCommentSourceFactory? _commentSourceFactory;`

**Testing:**
1. Unit test: Null factory → no error, comments skipped
2. Unit test: Mock factory provided → factory used
3. Integration test: Verify existing tests still pass

**Acceptance Criteria:**
- ✅ Constructor signature backward-compatible
- ✅ Null-safe (no NullReferenceException)
- ✅ All existing tests pass

---

### 2.3 Task 3: Update Dependency Injection in WorkItemsModule (1 hour)
**File:** `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs`

**What:** Accept factory and pass to orchestrator

**Changes:**
1. Add constructor parameter: `IWorkItemCommentSourceFactory? commentSourceFactory = null`
2. Store: `_commentSourceFactory = commentSourceFactory;`
3. Pass to orchestrator: `new WorkItemExportOrchestrator(..., _commentSourceFactory, ...)`

**DI Registration (Startup):**
```csharp
// In Program.cs or Host Builder
if (commentSourceFactoryAvailable)
{
    services.AddSingleton<IWorkItemCommentSourceFactory>(factory);
}
```

**Testing:**
1. Unit test: Factory not registered → orchestrator receives null
2. Unit test: Factory registered → orchestrator receives instance
3. Integration test: Module initialization still works

**Acceptance Criteria:**
- ✅ Module backward-compatible
- ✅ Optional registration pattern works
- ✅ All tests pass

---

### 2.4 Task 4: Integrate Inline Comment Fetching (4 hours)
**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**What:** Fetch and store comments for edit/delete revisions

**Implementation Location:** In `ExportAsync()` method, after writing `revision.json`

**Pseudo-code:**
```csharp
// After: await _artefactStore.WriteAsync(revisionJsonPath, revisionJson, cancellationToken);

if (IsCommentEditOrDeleteRevision(revision) && _commentSourceFactory != null && 
    !string.IsNullOrEmpty(_organisationUrl) && !string.IsNullOrEmpty(_project) && _pat != null)
{
    var commentSource = _commentSourceFactory.Create(_organisationUrl!, _project!, _pat);
    var matchingComments = new List<WorkItemComment>();
    
    await foreach (var comment in commentSource.GetCommentsAsync(
        revision.WorkItemId,
        includeDeleted: true,
        cancellationToken).ConfigureAwait(false))
    {
        var commentTime = comment.ModifiedDate > comment.CreatedDate 
            ? comment.ModifiedDate 
            : comment.CreatedDate;
        
        var timeDiff = Math.Abs((commentTime - revision.ChangedDate).TotalSeconds);
        
        if (timeDiff <= 1.0)
        {
            matchingComments.Add(comment);
        }
    }

    if (matchingComments.Count > 0)
    {
        var commentJson = JsonSerializer.Serialize(matchingComments, JsonOptions);
        await _artefactStore.WriteAsync(
            $"{folderPath}comment.json", 
            commentJson, 
            cancellationToken).ConfigureAwait(false);
    }
}
```

**Testing:**
1. Integration test: Mock factory returns 5 comments; 2 match timestamp
2. Integration test: No matching comments → comment.json not created
3. Integration test: All matching comments → written to comment.json
4. Integration test: CancellationToken propagated correctly

**Acceptance Criteria:**
- ✅ Comment fetching only for edit/delete revisions
- ✅ Timestamp matching within ±1 second works
- ✅ comment.json written only if matches exist
- ✅ No comment accumulation in memory
- ✅ CancellationToken honored

---

### 2.5 Task 5: Add Using Statements (30 minutes)
**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**Changes:**
1. Add: `using System.Linq;` (for `.Select()`)
2. Add: `using DevOpsMigrationPlatform.Abstractions.Models;` (for WorkItemComment)

**Verification:**
- ✅ No unresolved symbol errors
- ✅ Code compiles cleanly

---

## Phase 3: Validation (2-3 hours)

### 3.1 Build Verification
```bash
dotnet clean
dotnet build --no-incremental
```

**Success Criteria:**
- ✅ Zero compilation errors
- ✅ Zero new warnings

---

### 3.2 Unit Tests
**Location:** `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/`

**Test File:** `WorkItemExportOrchestratorCommentTests.cs`

**Test Cases:**
1. `IsCommentEditOrDeleteRevision_WithNoFields_ReturnsFalse()`
2. `IsCommentEditOrDeleteRevision_WithSystemHistory_ReturnsFalse()`
3. `IsCommentEditOrDeleteRevision_WithCommentCountOnly_ReturnsTrue()`
4. `IsCommentEditOrDeleteRevision_WithCommentCountAndChangedDate_ReturnsTrue()`
5. `IsCommentEditOrDeleteRevision_WithCommentCountAndOtherFields_ReturnsFalse()`
6. `ExportAsync_WithNullFactory_SkipsCommentFetching()`
7. `ExportAsync_WithMockFactory_FetchesCommentsSuccessfully()`
8. `ExportAsync_WithNoMatchingComments_DoesNotCreateCommentJson()`
9. `ExportAsync_WithMatchingComments_CreatesCommentJson()`
10. `ExportAsync_WithTimestampBoundary_MatchesAt1SecondExactly()`

**Run Tests:**
```bash
dotnet test --filter "WorkItemExportOrchestratorCommentTests"
```

**Success Criteria:**
- ✅ All 10 tests pass
- ✅ Code coverage ≥ 85%

---

### 3.3 Integration Tests
**Location:** `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Modules/`

**Test File:** `WorkItemsModuleCommentTests.cs`

**Test Cases:**
1. `ExportAsync_WithMockCommentSourceFactory_IntegrationSucceeds()`
2. `ExportAsync_WithMultipleCommentEditRevisions_AllProcessedCorrectly()`
3. `ExportAsync_WithCancellation_CancelsCommentFetching()`

**Run Tests:**
```bash
dotnet test --filter "WorkItemsModuleCommentTests"
```

**Success Criteria:**
- ✅ All integration tests pass
- ✅ Full workflow validated

---

### 3.4 System Test
**Location:** `scenarios/export-ado-workitems-single-project.json`

**Scenario:** Export work item with comment addition + edit revisions

**Execution:**
```bash
dotnet run --project src/DevOpsMigrationPlatform.CLI.Migration -- \
  export \
  --config scenarios/export-ado-workitems-single-project.json \
  --output "$env:TEMP/SystemTests/011-comments"
```

**Validation:**
1. Check folder structure: `WorkItems/yyyy-MM-dd/<ticks>-<id>-<rev>/`
2. Verify `revision.json` exists for all revisions
3. Verify `comment.json` exists ONLY for comment-edit revisions
4. Verify no `comment.json` for addition revisions
5. Verify comment content matches expected timestamps

**Success Criteria:**
- ✅ Addition revision: `revision.json` only (no `comment.json`)
- ✅ Edit revision: `revision.json` + `comment.json`
- ✅ Comment versions correctly captured
- ✅ Export completes without errors

---

### 3.5 Resume Test
**Scenario:** Interrupt export at comment-edit revision, then resume

**Execution:**
1. Start export; force interrupt at revision 11 (comment edit)
2. Resume export from checkpoint
3. Verify revision 11 reprocessed with correct `comment.json`

**Success Criteria:**
- ✅ Cursor correctly positioned after resume
- ✅ Comment.json regenerated (idempotent)
- ✅ No duplicate data in package

---

## Phase 4: Code Review & Release (1 hour)

### 4.1 Code Review Checklist
- ✅ All SOLID principles verified (see SOLID_COMPLIANCE_CHECKLIST.md)
- ✅ All 28/28 guardrails verified
- ✅ No hardcoded types or secrets
- ✅ No direct file I/O (only IArtefactStore)
- ✅ Streaming semantics preserved
- ✅ CancellationToken propagated
- ✅ Error handling is graceful (non-fatal)

### 4.2 Documentation Updates
- ✅ Add comment.json to package-format.md
- ✅ Update workitems-format.md if needed
- ✅ Add XML doc-comments to public methods

### 4.3 Merge to Main
```bash
git checkout main
git pull origin main
git merge feature/011-inline-comment-fetching
git push origin main
```

---

## Task Dependencies & Timeline

```
Day 1 (4 hours):
  └─ Task 1: Comment Detection Method
  └─ Task 2: Factory Injection

Day 2 (6 hours):
  └─ Task 3: Module DI Updates
  └─ Task 4: Inline Comment Fetching (main work)
  └─ Task 5: Using Statements

Day 3 (3 hours):
  └─ Unit Tests (2 hours)
  └─ Integration Tests (1 hour)

Day 4+ (as needed):
  └─ System Test Validation
  └─ Code Review
  └─ Issues & Fix-Up
```

**Total Estimated:** 13-16 hours (2-3 developer days)

---

## Success Criteria Summary

| Category | Criterion | Status |
|----------|-----------|--------|
| **Build** | `dotnet clean && dotnet build --no-incremental` succeeds | ⭕ |
| **Tests** | All unit + integration tests pass | ⭕ |
| **System Test** | Real export with comments succeeds | ⭕ |
| **SOLID** | All 5 principles verified | ⭕ |
| **Guardrails** | All 28/28 rules verified | ⭕ |
| **Resume** | Cursor-based checkpoint works | ⭕ |
| **Memory** | No comment accumulation | ⭕ |
| **Code Review** | Architecture/SOLID approval | ⭕ |

---

## Rollback Plan

If critical issues found:
1. Revert feature branch: `git revert <commit-hash>`
2. Comment additions still work (System.History preserved)
3. No data loss (export functionality unchanged)

---

## Known Limitations & Future Work

1. **Timestamp Threshold:** ±1 second is fixed; make configurable in future
2. **No Caching:** Comments fetched fresh each export; add optional cache strategy
3. **No Threading:** Sequential processing; could parallelize per work item
4. **Limited Filtering:** Only timestamp matching; add strategy interface

---

## References

- **Specification:** [spec.md](./spec.md)
- **SOLID Compliance:** [SOLID_COMPLIANCE_CHECKLIST.md](./SOLID_COMPLIANCE_CHECKLIST.md)
- **Design Rationale:** [DESIGN_DECISIONS_RATIONALE.md](./DESIGN_DECISIONS_RATIONALE.md)
- **Tasks:** [tasks.md](./tasks.md)
- **Index:** [INDEX.md](./INDEX.md)
