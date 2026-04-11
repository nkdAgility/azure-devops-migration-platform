# Design Decisions Rationale – Feature 011 SOLID Principles Applied

**Feature:** Inline Comment Fetching for Edit/Delete Revisions  
**Purpose:** Document WHY each design choice reflects SOLID principles  
**Audience:** Future implementers; code reviewers  

---

## Design Decision 1: Static Method for Comment Detection

### Decision
Implement comment type detection as a **static, pure method**:
```csharp
private static bool IsCommentEditOrDeleteRevision(WorkItemRevision revision)
{
    // Detects if revision is comment edit/delete (not addition)
}
```

### SOLID Principle: Single Responsibility + Dependency Inversion

**Why Not Create an ICommentRevisionDetector Interface?**
- ❌ Would add ceremony for logic that never needs to be swapped
- ❌ Would require injecting an extra dependency in the constructor
- ❌ Would violate YAGNI (You Aren't Gonna Need It)

**Why Static + Pure?**
- ✅ **SRP:** Detection logic has zero dependencies on orchestrator state
- ✅ **Testability:** Can unit-test in complete isolation (`IsCommentEditOrDeleteRevision(revision) == true`)
- ✅ **No Side Effects:** Pure function; deterministic for same input
- ✅ **DI-Free:** No factory or container needed for unit tests

**When to Refactor to Interface:**
- Only if 2+ strategies exist (e.g., field-count-based vs. history-based detection)
- Then: Introduce `ICommentRevisionDetectionStrategy` interface
- But for now: YAGNI applies

---

## Design Decision 2: IWorkItemCommentSourceFactory Pattern

### Decision
Create comments via **factory pattern**, not direct instantiation:
```csharp
// Injected
private readonly IWorkItemCommentSourceFactory? _commentSourceFactory;

// In export loop
if (_commentSourceFactory != null)
{
    var source = _commentSourceFactory.Create(orgUrl, project, pat);
    var comments = await source.GetCommentsAsync(workItemId, includeDeleted, ct);
}
```

### SOLID Principle: Dependency Inversion + Open/Closed

**Why Not Just Inject IWorkItemCommentSource Directly?**
- ❌ `IWorkItemCommentSource` needs credentials (org, project, PAT)
- ❌ Can't inject credentials in constructor (not DI-friendly)
- ❌ Would require storing credentials in orchestrator (security issue)

**Why Use Factory Pattern?**
- ✅ **Dependency Inversion:** Orchestrator depends on factory abstraction, not concrete `AzureDevOpsWorkItemCommentSource`
- ✅ **Open/Closed:** Can swap factories (Azure DevOps → alternative API) without changing orchestrator
- ✅ **Security:** Credentials passed at creation time, not stored in orchestrator
- ✅ **Testability:** Mock factory can return mock sources without real credentials

**Design Pattern:**
```csharp
// Standard factory interface
public interface IWorkItemCommentSourceFactory
{
    IWorkItemCommentSource Create(string orgUrl, string project, string pat);
}

// Concrete implementation (infrastructure)
public class AzureDevOpsWorkItemCommentSourceFactory : IWorkItemCommentSourceFactory
{
    public IWorkItemCommentSource Create(string orgUrl, string project, string pat)
        => new AzureDevOpsWorkItemCommentSource(orgUrl, project, pat);
}

// Mock implementation (testing)
public class MockWorkItemCommentSourceFactory : IWorkItemCommentSourceFactory
{
    private readonly List<WorkItemComment> _comments;
    public IWorkItemCommentSource Create(...) => new MockCommentSource(_comments);
}
```

---

## Design Decision 3: IWorkItemCommentSource as Async Enumerable

### Decision
Comments fetched via **streaming interface**, not batch methods:
```csharp
public interface IWorkItemCommentSource
{
    IAsyncEnumerable<WorkItemComment> GetCommentsAsync(
        int workItemId, 
        bool includeDeleted, 
        CancellationToken ct);
}
```

### SOLID Principle: Single Responsibility + Memory Safety

**Why Not Use GetAllCommentsAsync() → Task<List<WorkItemComment>>?**
- ❌ Violates streaming memory-safety requirement from guardrails
- ❌ Accumulates all comments in memory before filtering
- ❌ Work item with 100K+ comment versions = OOM
- ❌ Blocks on full API traversal before returning first comment

**Why Use IAsyncEnumerable<T>?**
- ✅ **Single Responsibility:** Source fetches; caller filters
- ✅ **Memory Safe:** O(1) memory overhead; processes one comment at a time
- ✅ **Cancellable:** `CancellationToken` propagated; can stop mid-stream
- ✅ **Composable:** Caller can chain `.Where()`, `.Select()` without buffering

**Implementation Flow:**
```
Orchestrator calls GetCommentsAsync()
  ↓
Source streams comments one-by-one
  ↓
Orchestrator checks timestamp for each comment (±1 second)
  ↓
Match → write to comment.json
  ↓
No match → skip (memory freed)
  ↓
Next comment → repeat
```

**Only acceptable alternative:** Paged enumeration with explicit page size limit (future enhancement).

---

## Design Decision 4: File I/O Only via IArtefactStore

### Decision
**All** comment.json writes go through injected `IArtefactStore`:
```csharp
// ❌ NEVER do this
File.WriteAllText($"{folderPath}comment.json", json);

// ✅ ALWAYS do this
await _artefactStore.WriteAsync($"{folderPath}comment.json", jsonStream, ct);
```

### SOLID Principle: Dependency Inversion + Architecture Enforcement

**Why Not Direct File I/O?**
- ❌ Ties code to `FileSystemArtefactStore` implementation
- ❌ Breaks when storage switches to Azure Blob (cloud deployment)
- ❌ Makes package format implicit in code (hard to maintain)
- ❌ Violates guardrail rule #13 (`IArtefactStore` is only abstraction)

**Why Inject IArtefactStore?**
- ✅ **Dependency Inversion:** Depends on abstraction, not filesystem details
- ✅ **Deployment Agnostic:** Same code works with:
  - `FileSystemArtefactStore` — local/server deployments
  - `AzureBlobArtefactStore` — cloud deployments
- ✅ **Testability:** Can inject mock store that validates writes
- ✅ **Package Consistency:** Format enforcement at single point (store implementation)

**Guardrail Reference:**
- Rule #13 from `.agents/guardrails/system-architecture.md`: "IArtefactStore is the only permitted file abstraction"
- Rule #7: "Modules only through IArtefactStore and IStateStore"

---

## Design Decision 5: Cursor Management (No Changes)

### Decision
**Reuse existing cursor-based checkpointing** for comment revisions:
```csharp
// No new cursor logic added
// Orchestrator advances cursor per revision (not per comment)
// comment.json is side artifact; cursor advances regardless
```

### SOLID Principle: Single Responsibility + Architectural Consistency

**Why Not Add Comment-Level Cursors?**
- ❌ Violates rule #4 from guardrails (cursor-based, not watermark tables)
- ❌ Doubles cursor complexity for marginal benefit
- ❌ Comment.json is optional (not present for additions); cursor should treat all revisions equally

**Why Reuse Revision-Level Cursors?**
- ✅ **SRP:** Cursor only tracks revision progress; comments are bonus data
- ✅ **Simplicity:** Same cursor logic; no branching on "is this comment revision?"
- ✅ **Resume Safety:** If export interrupted on comment-edit revision, resume automatically refetches comments
- ✅ **DDD Alignment:** Revision is the aggregate root, not individual comments

**Resume Behavior:**
```
Export interrupted at revision 11 (comment-edit)
  ↓
Resume from cursor pointing to revision 11
  ↓
Orchestrator reprocesses revision 11
  ↓
Detects comment edit; fetches comments again
  ↓
Writes comment.json (idempotent; overwrites previous)
  ↓
Advances cursor
```

---

## Design Decision 6: Timestamp Filtering Inline (Not Post-Processing)

### Decision
Filter comments **during fetch loop**, not in separate post-processing pass:
```csharp
// ✅ Good: Filtering inline
await foreach (var comment in source.GetCommentsAsync(...))
{
    var timeDiff = Math.Abs((comment.ModifiedDate - revision.ChangedDate).TotalSeconds);
    if (timeDiff <= 1.0)
    {
        matches.Add(comment);
    }
}

// ❌ Bad: Buffering all, then filtering post-loop
var allComments = await source.GetCommentsAsync(...).ToListAsync(); // ACCUMULATES MEMORY
var matches = allComments.Where(c => ...);  // SORTS IN MEMORY
```

### SOLID Principle: Single Responsibility + Memory Safety

**Why Inline?**
- ✅ **SRP:** Orchestrator has one loop; timestamp matching is part of that loop
- ✅ **Memory Safe:** Comments discarded immediately if no match (not accumulated)
- ✅ **Streaming:** Honors async enumerable contract; processes as-you-go
- ✅ **Efficient:** One pass; no list copy or re-enumeration

**Why Not Post-Process?**
- ❌ Violates memory-safety requirement (forbids `.ToList()` on enumeration)
- ❌ Doubles memory usage during filter
- ❌ Might sort in-memory (violates rule #3: no global sorts)
- ❌ Violates guardrail: "Sorting `.EnumerateAsync()` in memory is forbidden"

---

## Design Decision 7: Optional Factory Injection (Nullable)

### Decision
Factory parameter is **nullable** with safe default behavior:
```csharp
public WorkItemExportOrchestrator(
    ...,
    IWorkItemCommentSourceFactory? commentSourceFactory = null,  // ← Nullable
    ...)
{
    _commentSourceFactory = commentSourceFactory;
}

// In export loop
if (IsCommentEditOrDeleteRevision(revision) && _commentSourceFactory != null)
{
    // Only fetch if factory is available
}
```

### SOLID Principle: Open/Closed + Graceful Degradation

**Why Nullable?**
- ✅ **Open/Closed:** Orchestrator works with OR without factory (extensible)
- ✅ **Backward Compatibility:** Existing code continues if factory not registered
- ✅ **Optional Feature:** Comment fetching is enhancement; not breaking if unavailable
- ✅ **Testing:** Easy to test both codepaths (with mock factory, without)

**Why Not Required?**
- ❌ Would force all deployments to register factory
- ❌ Would break if upstream SDK fix is delayed
- ❌ Would prevent partial rollout (some regions with factory, some without)

**DI Container Registration:**
```csharp
// Required (always register)
services.AddSingleton<IArtefactStore>(artefactStore);
services.AddSingleton<IStateStore>(stateStore);

// Optional (register only if factory available)
if (commentSourceAvailable)
{
    services.AddSingleton<IWorkItemCommentSourceFactory>(factory);
}
```

---

## Design Decision 8: Package Layout (comment.json Beside revision.json)

### Decision
**Store comment.json in same folder as revision.json**, not in separate area:
```
WorkItems/
  2026-02-25/
    638760123456789012-12345-17/
      revision.json        ← Revision metadata
      comment.json         ← Comment versions (optional)
      attachment1.pdf      ← Attachment (optional)
      attachment2.zip      ← Attachment (optional)
```

### SOLID Principle: Single Responsibility + Architectural Consistency

**Why Not Separate comment/ Folders?**
- ❌ Breaks chronological layout rule (rule #1 from guardrails)
- ❌ Requires two separate cursors (one for revisions, one for comments)
- ❌ Violates rule #5: "Attachments beside revision.json" — comment attachments should follow same pattern
- ❌ Makes package structure implicit / harder to understand

**Why Beside revision.json?**
- ✅ **SRP:** Revision folder is "unit of work" — includes all data for that revision
- ✅ **Resume Safe:** Cursor points to revision folder; all its artifacts (revision.json + comment.json) are co-located
- ✅ **Streaming Import:** One folder = one import operation; atomic per-revision
- ✅ **Package Consistency:** Follows attachment placement rule exactly

**Guardrail Reference:**
- Rule #1: "WorkItems chronological layout is canonical"
- Rule #5: "Attachments are stored beside revision.json"
- Rule #14: "EnumerateAsync must be lexicographic" — folder-based enumeration maintains order

---

## Design Decision 9: No New IStateStore (Cursor) Logic

### Decision
**Reuse existing WorkItems cursor** for comment revisions:
```csharp
// No new cursor type: Checkpoints/workitems-comments.cursor.json
// No new stage: stages remain [CreatedOrUpdated, AppliedFields, AppliedLinks, UploadedAttachments, Completed]
// Comment fetch is internal to export; not a staged import step
```

### SOLID Principle: Single Responsibility

**Why Not Add Comment Stages?**
- ❌ Comments are **export-only** feature (import doesn't change)
- ❌ Adding cursor stages would require updating import orchestrator (SRP violation)
- ❌ Would complicate resume logic unnecessarily
- ❌ Comment fetch happens **before** attachment upload; doesn't need its own stage

**Why Reuse Existing Cursor?**
- ✅ **SRP:** Each component (export, import) has single cursor
- ✅ **Simplicity:** No branching on "is this a comment revision?"
- ✅ **Consistency:** Resume logic unchanged; same semantics apply
- ✅ **Testability:** Existing cursor tests still pass

---

## Anti-Patterns Explicitly Rejected

### ❌ Service Locator Pattern
**Rejected Design:**
```csharp
var commentSource = ServiceLocator.GetService<IWorkItemCommentSource>();
```
**Why:** Hides dependencies; makes testing hard; violates DIP  
**Chosen:** Factory injected; dependencies explicit

---

### ❌ Static Mutable State
**Rejected Design:**
```csharp
public static class CommentExportContext
{
    public static List<WorkItemComment> AllComments { get; set; } // SHARED STATE!
}
```
**Why:** Not thread-safe; violates SRP; hard to test  
**Chosen:** Orchestrator is stateless; comments ephemeral

---

### ❌ Direct Concrete Instantiation
**Rejected Design:**
```csharp
var source = new AzureDevOpsWorkItemCommentSource(orgUrl, project, pat);
```
**Why:** Ties code to concrete type; violates DIP; breaks in tests  
**Chosen:** Factory injected; swappable

---

### ❌ Loading All Revisions into Memory
**Rejected Design:**
```csharp
var allRevisions = await _store.EnumerateAsync().ToListAsync(); // ACCUMULATES MEMORY
```
**Why:** Violates streaming rule; breaks memory safety; defeats chronological processing  
**Chosen:** Streaming enumeration; one revision at a time

---

### ❌ Sorting in Memory
**Rejected Design:**
```csharp
var sortedComments = allComments.OrderBy(c => c.CreatedDate).ToList();
```
**Why:** Violates rule #3; defeats streaming; wastes memory  
**Chosen:** Timestamp filtering (no reordering); accept source order

---

## Conclusion

Every design decision in feature 011 traces back to a SOLID principle or architectural guardrail. This specification demonstrates that **compliance is achievable** without added complexity — in fact, adherence to SOLID makes the code simpler, more testable, and more maintainable.

When implementation begins (post-SDK-fix), each design decision above can be validated in code review against this rationale document. No surprises; just following the specification.
