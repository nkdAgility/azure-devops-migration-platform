# Development Tasks: Feature 011 Implementation

**Status:** Ready for Execution (pending upstream SDK fix)  
**Total Tasks:** 17 tasks across 4 work streams  
**Estimated Effort:** 13-16 hours (2-3 developer days)  
**Dependencies:** All tasks blocked until upstream SDK bug is fixed  

---

## Work Stream 1: Pre-Implementation (1 hour, 2 tasks)

### Task 1.1: Verify Upstream SDK Fix Available
**Effort:** 30 minutes  
**Type:** Verification/Research  
**Assigned To:** Tech Lead / Senior Developer  

**Acceptance Criteria:**
- [ ] Azure DevOps SDK changelog reviewed
- [ ] Latest SDK version downloaded/available
- [ ] No "$top parameter out of range" error in API calls
- [ ] `GetCommentsAsync()` returns comments successfully
- [ ] Test confirms API works with synthetic work item ID

**Steps:**
1. Check Azure DevOps SDK GitHub releases
2. Verify fix date and version number
3. Update `Directory.Build.props` or `.csproj` to latest version
4. Create quick unit test to verify API works
5. Run test and confirm success

**Deliverables:**
- SDK version documented
- Verification test passes
- Go/No-Go decision for implementation

---

### Task 1.2: Prepare Feature Branch & Dependencies
**Effort:** 30 minutes  
**Type:** Setup  
**Assigned To:** Any Developer  
**Depends On:** Task 1.1 (Go decision received)

**Acceptance Criteria:**
- [ ] Feature branch exists: `feature/011-inline-comment-fetching`
- [ ] Latest main branch merged in
- [ ] NuGet packages restored
- [ ] Solution builds successfully
- [ ] All existing tests pass
- [ ] Development environment ready

**Steps:**
1. Checkout main and pull latest
2. Create feature branch (if not exists)
3. `dotnet restore`
4. `dotnet build --no-incremental`
5. `dotnet test` (verify no regressions)
6. Confirm development machine ready

**Deliverables:**
- Clean feature branch
- Passing build
- Passing tests

---

## Work Stream 2: Core Implementation (6 hours, 6 tasks)

### Task 2.1: Implement Comment Detection Method
**Effort:** 2 hours  
**Type:** Feature Implementation  
**Assigned To:** Developer A  
**Depends On:** Task 1.2

**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**Acceptance Criteria:**
- [ ] Static method `IsCommentEditOrDeleteRevision()` exists
- [ ] Method returns `bool`
- [ ] Takes `WorkItemRevision` parameter
- [ ] Returns false for additions (System.History present)
- [ ] Returns true for edits (CommentCount only or + ChangedDate)
- [ ] Returns false for mixed field changes
- [ ] No side effects (pure function)
- [ ] Code compiles without warnings

**Implementation Notes:**
```csharp
// Location: WorkItemExportOrchestrator.cs, private section
private static bool IsCommentEditOrDeleteRevision(WorkItemRevision revision)
{
    // Implementation per IMPLEMENTATION_PLAN.md Task 4.1
    // Check field names, return bool
}
```

**Testing:**
- Create unit test file or add to existing tests
- Tests will be covered in Task 3.1

**Deliverables:**
- Method implemented
- Code compiles
- Local tests pass (will be formalized in Task 3.1)

---

### Task 2.2: Add Factory Parameter to WorkItemExportOrchestrator
**Effort:** 1 hour  
**Type:** Feature Implementation  
**Assigned To:** Developer A  
**Depends On:** Task 2.1

**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**Acceptance Criteria:**
- [ ] Field added: `private readonly IWorkItemCommentSourceFactory? _commentSourceFactory;`
- [ ] Constructor parameter added: `IWorkItemCommentSourceFactory? commentSourceFactory = null`
- [ ] Parameter assigned to field
- [ ] Constructor signature backward-compatible
- [ ] Null-safe (no NullReferenceException)
- [ ] Code compiles without warnings

**Implementation Notes:**
```csharp
// In constructor
public WorkItemExportOrchestrator(
    ...,
    IWorkItemCommentSourceFactory? commentSourceFactory = null,
    ...)
{
    _commentSourceFactory = commentSourceFactory;
}
```

**Deliverables:**
- Field and parameter added
- Code compiles
- No breaking changes

---

### Task 2.3: Update WorkItemsModule Constructor
**Effort:** 1 hour  
**Type:** Feature Implementation  
**Assigned To:** Developer B  
**Depends On:** Task 2.2 (not strictly, can run parallel)

**File:** `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs`

**Acceptance Criteria:**
- [ ] Field added: `private readonly IWorkItemCommentSourceFactory? _commentSourceFactory;`
- [ ] Constructor parameter added: `IWorkItemCommentSourceFactory? commentSourceFactory = null`
- [ ] Parameter assigned to field
- [ ] Factory passed to orchestrator: `new WorkItemExportOrchestrator(..., _commentSourceFactory, ...)`
- [ ] Module remains backward-compatible
- [ ] Code compiles without warnings

**Implementation Notes:**
- Follow same pattern as Task 2.2
- Pass factory to orchestrator during construction

**Deliverables:**
- Module constructor updated
- Factory passed to orchestrator
- Code compiles

---

### Task 2.4: Implement Inline Comment Fetching Logic
**Effort:** 4 hours  
**Type:** Feature Implementation (Core Logic)  
**Assigned To:** Developer A (primary)  
**Depends On:** Task 2.2, Task 2.3

**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**Location in Code:** In `ExportAsync()` method, after `await _artefactStore.WriteAsync(revisionJsonPath, ...)`

**Acceptance Criteria:**
- [ ] Check if revision is comment edit/delete via `IsCommentEditOrDeleteRevision()`
- [ ] Verify factory is not null
- [ ] Verify credentials are available (org URL, project, PAT)
- [ ] Create comment source from factory
- [ ] Call `source.GetCommentsAsync(workItemId, includeDeleted: true, cancellationToken)`
- [ ] Stream comments as async enumerable (no buffering)
- [ ] Filter by timestamp (±1 second from revision.ChangedDate)
- [ ] Collect matching comments in list
- [ ] Write `comment.json` only if matches found
- [ ] JSON serialization matches spec format
- [ ] CancellationToken propagated throughout
- [ ] No comment accumulation (streaming semantics)
- [ ] Code compiles without warnings

**Implementation Pseudo-code:**
```csharp
// After: await _artefactStore.WriteAsync(revisionJsonPath, revisionJson, ct);

if (IsCommentEditOrDeleteRevision(revision) && 
    _commentSourceFactory != null &&
    !string.IsNullOrEmpty(_organisationUrl) && 
    !string.IsNullOrEmpty(_project) && 
    _pat != null)
{
    var commentSource = _commentSourceFactory.Create(_organisationUrl!, _project!, _pat);
    var matchingComments = new List<WorkItemComment>();
    
    await foreach (var comment in commentSource.GetCommentsAsync(...))
    {
        // Calculate comment time
        // Check timestamp diff
        // Add to matching list if within ±1 second
    }

    if (matchingComments.Count > 0)
    {
        var commentJson = JsonSerializer.Serialize(matchingComments, JsonOptions);
        await _artefactStore.WriteAsync($"{folderPath}comment.json", commentJson, ct);
    }
}
```

**Sub-Tasks:**
- [ ] Create comment source from factory
- [ ] Implement async enumeration loop
- [ ] Implement timestamp calculation
- [ ] Implement timestamp filtering logic
- [ ] Implement JSON serialization
- [ ] Implement write to artifact store
- [ ] Test with mock data locally

**Deliverables:**
- Comment fetching logic implemented
- Code compiles
- Local testing shows correct timestamp filtering
- Streaming semantics verified (no lists)

---

### Task 2.5: Add Required Using Statements
**Effort:** 30 minutes  
**Type:** Code Cleanup  
**Assigned To:** Any Developer  
**Depends On:** Task 2.4

**File:** `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`

**Acceptance Criteria:**
- [ ] `using System.Linq;` added (for `.Select()`)
- [ ] `using DevOpsMigrationPlatform.Abstractions.Models;` added (for WorkItemComment)
- [ ] No unresolved symbol errors
- [ ] Code compiles cleanly
- [ ] No unused usings

**Deliverables:**
- Using statements added
- Code compiles without errors or warnings

---

### Task 2.6: Verify Build Success
**Effort:** 30 minutes  
**Type:** Build & Verification  
**Assigned To:** Any Developer  
**Depends On:** Task 2.5

**Acceptance Criteria:**
- [ ] `dotnet clean` succeeds
- [ ] `dotnet build --no-incremental` succeeds
- [ ] Zero compilation errors
- [ ] Zero new compiler warnings
- [ ] All project outputs generated

**Steps:**
```bash
dotnet clean
dotnet build --no-incremental
```

**Deliverables:**
- Clean build output
- Zero errors/warnings
- Code ready for testing

---

## Work Stream 3: Testing (5 hours, 7 tasks)

### Task 3.1: Create & Execute Unit Tests for Detection Method
**Effort:** 2 hours  
**Type:** Testing  
**Assigned To:** Developer B (QA)  
**Depends On:** Task 2.1

**File:** `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorCommentTests.cs` (create new)

**Acceptance Criteria:**
- [ ] Test file created with proper naming
- [ ] 5 unit tests for `IsCommentEditOrDeleteRevision()`
- [ ] All tests pass
- [ ] Code coverage ≥ 85%

**Test Cases to Implement:**
1. `IsCommentEditOrDeleteRevision_WithNoFields_ReturnsFalse()`
   - Input: Empty fields list
   - Expected: false

2. `IsCommentEditOrDeleteRevision_WithSystemHistory_ReturnsFalse()`
   - Input: Fields include System.History
   - Expected: false (comment addition)

3. `IsCommentEditOrDeleteRevision_WithCommentCountOnly_ReturnsTrue()`
   - Input: Fields include only System.CommentCount
   - Expected: true (comment edit/delete)

4. `IsCommentEditOrDeleteRevision_WithCommentCountAndChangedDate_ReturnsTrue()`
   - Input: Fields include System.CommentCount + System.ChangedDate
   - Expected: true (comment edit/delete)

5. `IsCommentEditOrDeleteRevision_WithCommentCountAndOtherFields_ReturnsFalse()`
   - Input: Fields include CommentCount + unrelated field
   - Expected: false (not pure comment change)

**Execution:**
```bash
dotnet test --filter "WorkItemExportOrchestratorCommentTests"
```

**Deliverables:**
- 5 passing unit tests
- ≥85% code coverage
- Test file committed

---

### Task 3.2: Create & Execute Integration Tests for Factory & Fetching
**Effort:** 2 hours  
**Type:** Testing  
**Assigned To:** Developer B (QA)  
**Depends On:** Task 2.4

**File:** `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Modules/WorkItemsModuleCommentTests.cs` (create new)

**Acceptance Criteria:**
- [ ] Test file created with proper naming
- [ ] 3 integration tests for comment fetching
- [ ] All tests pass
- [ ] Mock factory working correctly

**Test Cases to Implement:**
1. `ExportAsync_WithNullFactory_SkipsCommentFetching()`
   - Setup: No factory registered
   - Action: Export work item with comment-edit revision
   - Expected: No comment.json created; export completes

2. `ExportAsync_WithMockFactory_ReturnsMatchingComments()`
   - Setup: Mock factory with 5 synthetic comments
   - Action: Export revision with 2 matching comments (within ±1 sec)
   - Expected: comment.json created with 2 comments

3. `ExportAsync_WithNoMatchingComments_DoesNotCreateCommentJson()`
   - Setup: Mock factory with comments outside ±1 sec window
   - Action: Export revision
   - Expected: No comment.json created; export completes

**Execution:**
```bash
dotnet test --filter "WorkItemsModuleCommentTests"
```

**Deliverables:**
- 3 passing integration tests
- Mock factory implementation
- Test file committed

---

### Task 3.3: Create & Execute Timestamp Boundary Tests
**Effort:** 1 hour  
**Type:** Testing (Edge Cases)  
**Assigned To:** Developer B (QA)  
**Depends On:** Task 2.4

**File:** `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorCommentTests.cs`

**Acceptance Criteria:**
- [ ] Timestamp edge case tests added
- [ ] Boundary conditions at ±1.0 second verified
- [ ] All tests pass

**Test Cases:**
1. `TimestampFiltering_ExactlyAtBoundary_1SecondMatches()`
   - Comment at exactly revision.ChangedDate + 1.0 second
   - Expected: Match (included)

2. `TimestampFiltering_JustOutsideBoundary_1Point1SecondDoesNotMatch()`
   - Comment at revision.ChangedDate + 1.1 seconds
   - Expected: No match (excluded)

3. `TimestampFiltering_UnderBoundary_0Point5SecondsMatches()`
   - Comment at revision.ChangedDate - 0.5 seconds
   - Expected: Match (included)

**Deliverables:**
- 3 boundary test cases
- Tests passing
- Timestamp logic verified
- Edge cases documented

---

### Task 3.4: System Test – Export with Real Comments
**Effort:** 1 hour  
**Type:** System Testing  
**Assigned To:** Developer A  
**Depends On:** Task 2.6 (build succeeds)

**Test Scenario:** Export Azure DevOps work item with comment additions + edits

**Acceptance Criteria:**
- [ ] Export completes successfully
- [ ] Revision folders created with correct naming
- [ ] Addition revisions: revision.json only (no comment.json)
- [ ] Edit revisions: revision.json + comment.json
- [ ] comment.json contains valid JSON array of WorkItemComment
- [ ] Comment timestamps match source data
- [ ] Folder structure intact

**Execution:**
```bash
dotnet run --project src/DevOpsMigrationPlatform.CLI.Migration -- \
  export \
  --config scenarios/export-ado-workitems-single-project.json \
  --output "$env:TEMP/SystemTests/011-comments"
```

**Validation Steps:**
1. Check folder structure: `WorkItems/yyyy-MM-dd/<ticks>-<id>-<rev>/`
2. Count revision folders with `comment.json`
3. Verify comment.json is valid JSON
4. Spot-check comment content matches Azure DevOps

**Deliverables:**
- System test executed
- Output validated
- Issues documented (if any)

---

### Task 3.5: Resume Test – Verify Checkpoint Behavior
**Effort:** 1 hour  
**Type:** System Testing  
**Assigned To:** Developer B  
**Depends On:** Task 3.4

**Test Scenario:** Interrupt export at comment-edit revision, resume, verify idempotency

**Acceptance Criteria:**
- [ ] Export interrupted mid-way (force stop at specific revision)
- [ ] Cursor saved at last completed revision
- [ ] Resume export from checkpoint
- [ ] Comment-edit revision reprocessed
- [ ] New comment.json created (idempotent)
- [ ] No duplicate data
- [ ] Export continues from correct cursor position
- [ ] Final output consistent

**Steps:**
1. Start export
2. Force interrupt at comment-edit revision (manually)
3. Check cursor file: `Checkpoints/workitems.cursor.json`
4. Resume export: `devopsmigration export --resume`
5. Verify revision 11 (example) reprocessed
6. Compare output with non-interrupted run
7. Verify no duplication/corruption

**Deliverables:**
- Resume test executed
- Checkpoint behavior verified
- Idempotency confirmed

---

## Work Stream 4: Review & Release (1 hour, 2 tasks)

### Task 4.1: Code Review – SOLID & Architectural Compliance
**Effort:** 30 minutes  
**Type:** Code Review  
**Assigned To:** Tech Lead / Architecture Review  
**Depends On:** Task 2.6 (build succeeds), Task 3.1-3.5 (all tests pass)

**Review Checklist:**
- [ ] All 5 SOLID principles verified (per SOLID_COMPLIANCE_CHECKLIST.md)
- [ ] All 28/28 guardrails verified
- [ ] No hardcoded types: grep for `new Azure`, `new File`, `new Concrete`
- [ ] No direct file I/O: all writes via `IArtefactStore`
- [ ] Streaming semantics preserved: no `.ToList()` on enumerables
- [ ] CancellationToken propagated throughout async chain
- [ ] No service locator patterns
- [ ] No static mutable state
- [ ] Comments are clear and meaningful
- [ ] No dead code or commented-out logic

**Review Artifacts:**
- SOLID_COMPLIANCE_CHECKLIST.md (already completed)
- DESIGN_DECISIONS_RATIONALE.md (already completed)
- Code diff review in PR

**Deliverables:**
- Code review approval
- Any issues logged & fixed
- Architecture sign-off

---

### Task 4.2: Documentation & Merge to Main
**Effort:** 30 minutes  
**Type:** Documentation & Release  
**Assigned To:** Tech Lead  
**Depends On:** Task 4.1 (approval received)

**Acceptance Criteria:**
- [ ] XML doc-comments added to public methods
- [ ] IMPLEMENTATION_PLAN.md marked as "Completed"
- [ ] tasks.md status updated to "Implemented"
- [ ] Any package format docs updated (if needed)
- [ ] PR created with detailed description
- [ ] PR reviewed and approved
- [ ] Feature branch merged to main
- [ ] Feature tag created (v20.0.0-011-inline-comments)

**Steps:**
1. Add XML doc-comments to new public/internal methods
2. Update IMPLEMENTATION_PLAN.md completion date
3. Create or update CHANGELOG entry
4. Create pull request (PR) on GitHub
5. PR description includes:
   - Feature overview
   - Deferred status rationale
   - Test coverage summary
   - Breaking changes (none)
6. Await code review approval
7. Merge to main
8. Create release tag

**Deliverables:**
- Merged to main
- Release notes created
- Feature complete

---

## Task Dependencies Graph

```
Task 1.1 (Verify SDK)
  └─ Task 1.2 (Branch Setup)
      └─ Task 2.1 (Detection Method)
          ├─ Task 2.2 (Orchestrator Factory) ─┐
          │   └─ Task 2.4 (Fetching Logic) ───┼─ Task 2.5 (Usings) ─ Task 2.6 (Build)
          └─ Task 2.3 (Module Factory) ───────┘

Task 2.1 ─ Task 3.1 (Unit Tests)
Task 2.4 ─ Task 3.2 (Integration Tests)
Task 2.4 ─ Task 3.3 (Boundary Tests)
Task 2.6 ─ Task 3.4 (System Test) ─ Task 3.5 (Resume Test)

Task 3.5 ─ Task 4.1 (Code Review) ─ Task 4.2 (Merge)
```

---

## Effort Summary

| Work Stream | Task Count | Effort | Assigned |
|-------------|-----------|--------|----------|
| **Pre-Implementation** | 2 | 1 hr | Tech Lead / Senior Dev |
| **Core Implementation** | 6 | 6 hrs | Developer A & B |
| **Testing** | 5 | 5 hrs | Developer B (QA) |
| **Review & Release** | 2 | 1 hr | Tech Lead |
| **TOTAL** | **17** | **13 hrs** | **2-3 devs** |

---

## Success Criteria (Final)

- [ ] All 17 tasks completed
- [ ] Build: 0 errors, 0 warnings
- [ ] Tests: 18+ tests passing (13 unit/integration + 5 system scenarios)
- [ ] SOLID: 5/5 principles verified
- [ ] Guardrails: 28/28 rules verified
- [ ] Code review: Architecture approved
- [ ] Merged to main branch
- [ ] Feature released

---

## When to Start

**Blocker:** Upstream AWS DevOps SDK bug fix availability  
**Trigger:** PR merged with SDK fix, verified working in sandbox  
**Start Date:** To be determined (TBD)  
**Duration:** 2-3 consecutive working days for implementation + tests  

---

## References

- [IMPLEMENTATION_PLAN.md](./IMPLEMENTATION_PLAN.md) — Phase-by-phase guide
- [SOLID_COMPLIANCE_CHECKLIST.md](./SOLID_COMPLIANCE_CHECKLIST.md) — Compliance details
- [DESIGN_DECISIONS_RATIONALE.md](./DESIGN_DECISIONS_RATIONALE.md) — Design reasoning
- [spec.md](./spec.md) — Feature specification
