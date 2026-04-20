---
name: vertical-slice-check
description: Scans the codebase for Vertical Slice Architecture violations — cross-slice coupling, anemic slices missing end-to-end ownership, and feature leakage — and produces a prioritised fix list.
---

# Skill: Vertical Slice Check

Use this skill when you need to audit the codebase for violations of vertical-slice ownership rules — that each feature or use case owns its full stack from request to storage — or before declaring a feature complete after adding a new export/import operation.

---

## Role

When this skill is active, scan the specified scope (entire solution, single project, or single slice) for violations of vertical-slice cohesion. Produce a prioritised list of violations and, for each one, suggest the minimal safe fix. Do **not** apply fixes automatically unless explicitly instructed — report findings first.

---

## Slice Model

Each migration operation (e.g., export work items, import work items, export pipelines) is a **vertical slice** that owns everything required to deliver that outcome:

```
┌─────────────────────────────────────────────────────────────────┐
│  Slice: Export Work Items                                        │
│                                                                 │
│  CLI entry point  →  Job / Orchestrator  →  Module logic        │
│                                         →  IWorkItemRevisionSource │
│                                         →  IArtefactStore       │
│                                         →  ICheckpointingService│
└─────────────────────────────────────────────────────────────────┘
```

**The hard rule:** A slice should be able to evolve — or be deleted — without modifying any other slice. Shared infrastructure (stores, checkpointing) is permitted; shared business logic between slices is a violation.

---

## Check 1 — Business Logic Shared Across Slices via Static Helper

**Smell:** A `static` helper or utility class contains logic that is called by more than one slice, creating invisible coupling between otherwise independent features.

```csharp
// BAD — static helper shared between Export and Import slices
public static class WorkItemFieldHelper
{
    public static string NormaliseFieldValue(string key, object? value) // ❌
        => ...;
}

// Used in both WorkItemsExportOrchestrator and WorkItemsImportOrchestrator
```

**Fix:** If the logic genuinely belongs to both slices, elevate it to a domain service in `Abstractions` and inject it. If it is slice-specific, duplicate it within each slice and accept the duplication — it will diverge anyway.

```csharp
// GOOD — domain service in Abstractions, injected into each slice independently
public interface IWorkItemFieldNormaliser
{
    string Normalise(string key, object? value); // ✅
}
```

**How to find:**

```bash
grep -rn "public static " \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Abstractions \
  | grep -v "ServiceCollectionExtensions\|Test\|Fixture"
```

Any `static` method not in `Abstractions` that is referenced by more than one slice project is a candidate violation.

---

## Check 2 — Slice Delegates to Another Slice's Internal Class

**Smell:** One slice directly references a class from another slice's internal namespace, creating a compile-time dependency between features.

```csharp
// BAD — Import slice references Export slice's internal class
using DevOpsMigrationPlatform.WorkItems.Export.Internal; // ❌

public class WorkItemsImportOrchestrator
{
    private readonly RevisionMapper _mapper; // ❌ belongs to Export slice
}
```

**Fix:** Extract the shared concept to `DevOpsMigrationPlatform.Abstractions` or duplicate the minimal logic within the importing slice.

**How to find:**

```bash
grep -rn "^using DevOpsMigrationPlatform\." \
  src/ --include="*.cs" \
  | grep -v "\.Abstractions\|\.Infrastructure\|Tests"
```

Review each `using` that references a peer module project. Any cross-slice `using` that is not through `Abstractions` is a violation.

---

## Check 3 — Slice Missing End-to-End Test Coverage

**Smell:** A vertical slice (e.g., export work items, import work items) has unit tests for individual classes but no integration or acceptance test that exercises the full path from entry point to observable output.

**Fix:** Add a `[TestCategory("SystemTest")]` scenario test that invokes the CLI command or job trigger for the slice and asserts on the package output (folder structure, file presence, content).

**How to find:**

```bash
grep -rn "TestCategory" \
  tests/ --include="*.cs" \
  | grep "SystemTest"
```

Cross-reference the list of slice operations against the list of system tests. Any slice with no corresponding `SystemTest` entry is a gap.

---

## Check 4 — Cross-Slice State Written to a Shared, Unscoped Key

**Smell:** Two slices write checkpoint or state data under the same key prefix in `IStateStore`, risking key collision and phantom resume behaviour.

```csharp
// BAD — both slices use the same checkpoint key namespace
await _stateStore.SetAsync("workitems/checkpoint", cursor, ct); // ❌ used by both Export and Import
```

**Fix:** Scope checkpoint keys by operation type and job ID so each slice writes to a unique namespace.

```csharp
// GOOD — key scoped to slice and job
var key = $"export/workitems/{_jobId}/checkpoint";
await _stateStore.SetAsync(key, cursor, ct); // ✅
```

**How to find:**

```bash
grep -rn "SetAsync\|GetAsync\|DeleteAsync" \
  src/ --include="*.cs" \
  | grep "stateStore\|StateStore"
```

Review each key string literal. Any key that does not include both the operation type (export/import) and a job or session scope is a candidate violation.

---

## Check 5 — Slice Feature File Missing or Mislocated

**Smell:** A migration operation has no corresponding Gherkin `.feature` file under `/features`, or its feature file is placed under the wrong category folder.

```
// BAD — feature file absent or misplaced
features/
  export/                     ← present ✅
  import/                     ← present ✅
  // pipelines/export/        ← MISSING ❌ if pipelines export exists
```

**Fix:** Create a `.feature` file under the appropriate `features/<operation>/<module>/` path. Each scenario in the file should represent one acceptance criterion for the slice.

**How to find:**

```bash
find features/ -name "*.feature" | sort
```

Cross-reference against the list of registered CLI commands and job types. Any operation without a feature file is a gap.

---

## Severity Classification

| Severity | Criteria |
|---|---|
| **Critical** | Slice references another slice's internal class — breaks independent evolution and testability |
| **High** | Business logic shared via static helper across slices — hidden coupling |
| **Medium** | Slice missing end-to-end system test — no observable validation of the full path |
| **Low** | Checkpoint keys not scoped by operation and job — latent collision risk on resume |

---

## Vertical Slice Check Checklist

Run this checklist after adding or modifying a migration operation:

- [ ] **Check 1**: No `static` helper class contains logic shared between two or more slices.
- [ ] **Check 2**: No slice references an internal class from another slice's project namespace.
- [ ] **Check 3**: Every slice has at least one `[TestCategory("SystemTest")]` test asserting observable output.
- [ ] **Check 4**: All `IStateStore` keys used by the slice include an operation-type and job-scope prefix.
- [ ] **Check 5**: Every slice has a corresponding `.feature` file under the correct `features/` subfolder.

All items must be checked before a feature or refactoring is declared complete. Any unchecked item is a blocking violation.
