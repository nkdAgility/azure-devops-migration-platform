# Implementation Plan: Inline Comment Fetching for Edit/Delete Revisions

**✅ STATUS: IMPLEMENTED (feature-gated); reconciled to current repository evidence**

---

## Implementation Summary

Tasks 1–6 are complete. Task 7 remains incomplete pending a fresh full-suite build/test run.
The feature is gated through `Modules.WorkItems.Extensions.Comments.Enabled`.

**Known limitation:** inline comment fetch failures are handled as non-fatal (warning + continue),
so export remains resumable and deterministic even when comment API calls fail.

---

## Task List

- [x] Task 1: Add Comment Detection Method — Status: complete
**File:** `WorkItemExportOrchestrator.cs`  
**Description:** Implemented `IsCommentEditOrDeleteRevision()` static method.
Guards `RevisionIndex == 0` (creation revision excluded — all fields appear as changed when
previous is null, making CommentCount unreliable).  
**Evidence:** `src\DevOpsMigrationPlatform.Infrastructure.Agent\Export\WorkItemExportOrchestrator.cs` (`IsCommentEditOrDeleteRevision`) and tests `IsCommentEditOrDeleteRevision_*` in `tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\Export\WorkItemExportOrchestratorTests.cs`.

---

- [x] Task 2: Add Factory Injection to Orchestrator — Status: complete
**File:** `WorkItemExportOrchestrator.cs`  
**Description:** Added `IWorkItemCommentSourceFactory?` optional constructor parameter and field.  
**Evidence:** `WorkItemExportOrchestrator` constructor parameter `inlineCommentSourceFactory` and `_inlineCommentSourceFactory` assignment.

---

- [x] Task 3: Update Dependency Injection in WorkItemsModule — Status: complete
**File:** `WorkItemsModule.cs`  
**Description:** Module accepts `IWorkItemCommentSourceFactory?` from DI and passes it to the
orchestrator when `Comments.Enabled` is true.  
**Evidence:** `src\DevOpsMigrationPlatform.Infrastructure.Agent\Modules\WorkItemsModule.cs` (warning on missing factory and conditional `inlineFactory` wiring to orchestrator).

---

- [x] Task 4: Integrate Inline Comment Fetching — Status: complete
**File:** `WorkItemExportOrchestrator.cs` - `ExportAsync()` method  
**Description:** For detected comment edit/delete revisions, fetches comments via
`IWorkItemCommentSource.GetCommentsAsync()`, filters by ±1 second timestamp window, and
writes `comment.json` beside `revision.json`. Failures are warning-level and non-fatal.  
**Evidence:** `src\DevOpsMigrationPlatform.Infrastructure.Agent\Export\WorkItemExportOrchestrator.cs` (inline fetch/write block) and tests `ExportAsync_WhenCommentEditRevision_*`.

---

- [x] Task 5: Remove Legacy Comment Export Post-Processing — Status: complete
**File:** `WorkItemExportOrchestrator.cs` - `ExportAsync()` method  
**Description:** Legacy post-processing path is no longer present in runtime code.  
**Evidence:** repository search in `src\` for `IWorkItemCommentExportService` / `WorkItemCommentExportService` returns no matches.

---

- [x] Task 6: Add Using Statements — Status: complete
**File:** `WorkItemExportOrchestrator.cs`  
**Description:** Required `using DevOpsMigrationPlatform.Abstractions.Models;` is present.  
**Evidence:** `src\DevOpsMigrationPlatform.Infrastructure.Agent\Export\WorkItemExportOrchestrator.cs` imports include the model namespace.

---

- [ ] Task 7: Build and Verify — Status: incomplete
**File:** All modified files  
**Description:** Fresh full-repository build + full test suite verification for this reconciliation snapshot is not yet recorded in this spec artifact.  
**Evidence:** Targeted checks passed (`dotnet build src\DevOpsMigrationPlatform.Infrastructure.Agent\DevOpsMigrationPlatform.Infrastructure.Agent.csproj` and `dotnet test tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests\DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj --filter "WorkItemExportOrchestratorTests"` with 29 passed), but no completed full-solution test run evidence is captured here.

---

## Dependencies

Task execution order:
1. Task 1 → Task 2 → Task 3 → Task 4 → Task 5 → Task 6 → Task 7

Each task blocks the next because later tasks depend on earlier code changes being compiled.

---

## Testing Strategy

### Unit Tests (Implemented)
- `IsCommentEditOrDeleteRevision()` with various field combinations
- Timestamp correlation logic (±1 second boundary cases)
- Null-safety: factory not available, no credentials, empty comment list

### Integration Tests (Implemented)
- Comment source factory mock returns synthetic comments
- Verify `comment.json` written for edit/delete revisions
- Verify NO `comment.json` for addition revisions
- Verify cursor advancement with comment revisions

### System/End-to-End Tests (Partially verified in this reconciliation)
- Run full export on work item with comments (add + edit + delete)
- Verify output folder structure matches spec
- Verify `comment.json` contains expected comment versions
- Resume from checkpoint with comment-edit revision

---

## Remaining Work

Before marking Task 7 complete:

1. Run a fresh full-solution build.
2. Run a fresh full-solution test suite.
3. Record command evidence in this file.

