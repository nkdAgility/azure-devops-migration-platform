---
name: clean-architecture-check
description: Scans the codebase for Clean Architecture violations — business rule leakage, inverted dependencies, and use-case pollution — and produces a prioritised fix list.
---

# Skill: Clean Architecture Check

Use this skill when you need to audit the codebase for violations of the dependency rule — that all dependencies must point inward toward the domain — or before declaring a feature complete after touching domain, use-case, or infrastructure code.

---

## Role

When this skill is active, scan the specified scope (entire solution, single project, or single file) for violations of Clean Architecture's dependency rule and ring separation. Produce a prioritised list of violations and, for each one, suggest the minimal safe fix. Do **not** apply fixes automatically unless explicitly instructed — report findings first.

---

## Dependency Model

```
                    ┌────────────────────────────┐
                    │  Domain / Abstractions      │  ← innermost ring
                    │  (entities, value objects,  │
                    │   domain interfaces)        │
                    └──────────────┬─────────────┘
                                   │ depended on ▲
                    ┌──────────────▼─────────────┐
                    │  Use Cases / Application    │  ← orchestrators,
                    │  (export/import jobs,       │     job handlers
                    │   WorkItemExportOrchestrator│
                    │   pipeline steps)           │
                    └──────────────┬─────────────┘
                                   │ depended on ▲
          ┌────────────────────────▼──────────────────────────┐
          │  Interface Adapters / Infrastructure               │  ← outermost ring
          │  (FileSystemArtefactStore, AzureDevOps adapters,  │
          │   CLI commands, TUI, HTTP controllers)            │
          └───────────────────────────────────────────────────┘
```

**The dependency rule:** Source code dependencies must point only inward. Nothing in an inner ring may name anything in an outer ring.

---

## Check 1 — Domain Type References Infrastructure or Framework Type

**Smell:** A domain record, entity, or value object in `DevOpsMigrationPlatform.Abstractions` imports a type from an infrastructure project or framework (`Azure.Storage`, `Microsoft.TeamFoundation`, `System.Net.Http`, etc.).

```csharp
// BAD — domain type imports infrastructure namespace
using Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models; // ❌

public record WorkItemRevision(WorkItem Source, ...); // ❌ coupled to TFS SDK
```

**Fix:** Replace the SDK type with a pure domain record that carries only the data the domain needs.

```csharp
// GOOD — domain type owns its data
public record WorkItemRevision(
    int WorkItemId,
    int RevisionIndex,
    IReadOnlyDictionary<string, object?> Fields,
    ...); // ✅ no SDK dependency
```

**How to find:**

```bash
grep -rn "^using Microsoft\.TeamFoundation\|^using Azure\.\|^using Microsoft\.VisualStudio\.Services" \
  src/DevOpsMigrationPlatform.Abstractions \
  --include="*.cs"
```

Any hit in the Abstractions project is a violation.

---

## Check 2 — Use-Case Layer References Concrete Infrastructure Type

**Smell:** An orchestrator, job, or pipeline step in module code directly names a concrete infrastructure class instead of depending on an abstraction.

```csharp
// BAD — use-case layer depends on concrete store
public class WorkItemExportOrchestrator
{
    private readonly FileSystemArtefactStore _store; // ❌ outer ring leaks inward

    public WorkItemExportOrchestrator(FileSystemArtefactStore store)
    {
        _store = store;
    }
}
```

**Fix:** Depend on the `IArtefactStore` abstraction defined in the inner ring.

```csharp
// GOOD — use-case depends only on abstraction
public class WorkItemExportOrchestrator
{
    private readonly IArtefactStore _store; // ✅

    public WorkItemExportOrchestrator(IArtefactStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
    }
}
```

**How to find:**

```bash
grep -rn "FileSystemArtefactStore\|AzureBlobArtefactStore\|WorkItemTrackingHttpClient\|VssConnection" \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure \
  --exclude-dir=DevOpsMigrationPlatform.AppHost \
  --exclude-dir=DevOpsMigrationPlatform.CLI.TfsMigration
```

Any hit outside the outermost ring is a violation.

---

## Check 3 — Business Logic Placed in CLI or Infrastructure Layer

**Smell:** A CLI command, TUI component, or infrastructure adapter contains conditional business logic that should live in a use-case or domain class.

```csharp
// BAD — business logic in CLI command
public override async Task<int> ExecuteAsync(CommandContext context, ExportSettings settings)
{
    // ❌ filtering logic belongs in the use-case layer
    var items = await _source.GetWorkItemsAsync(ct);
    var filtered = items.Where(x => x.Fields["System.WorkItemType"] != "Test Case");
    foreach (var item in filtered)
        await _store.WriteAsync(...);
}
```

**Fix:** Move the filtering / transformation logic into the use-case orchestrator or a domain service.

```csharp
// GOOD — CLI delegates to use-case
public override async Task<int> ExecuteAsync(CommandContext context, ExportSettings settings)
{
    await _orchestrator.ExportAsync(settings.ToExportRequest(), ct); // ✅
    return 0;
}
```

**How to find:**

```bash
grep -rn "\.Where(\|\.Select(\|\.OrderBy(\|\.GroupBy(" \
  src/DevOpsMigrationPlatform.CLI.Migration \
  src/DevOpsMigrationPlatform.CLI.TfsMigration \
  --include="*.cs"
```

LINQ query operators in CLI code that encode filtering rules are candidates for extraction to the use-case layer.

---

## Check 4 — Use-Case Returns or Accepts Infrastructure DTO

**Smell:** An orchestrator's public API accepts or returns a type that is defined in an infrastructure project, coupling the use-case boundary to a specific integration technology.

```csharp
// BAD — use-case method signature leaks infrastructure DTO
public async Task ImportAsync(
    Microsoft.TeamFoundation.WorkItemTracking.WebApi.Models.WorkItem workItem, // ❌
    CancellationToken ct)
```

**Fix:** Define a domain DTO in `Abstractions` and have the infrastructure adapter translate before calling the use-case.

```csharp
// GOOD — use-case accepts a domain type
public async Task ImportAsync(WorkItemRevision revision, CancellationToken ct) // ✅
```

**How to find:**

```bash
grep -rn "WorkItemTrackingHttpClient\|WorkItem workItem\|TeamProject " \
  src/ --include="*.cs" \
  --exclude-dir=DevOpsMigrationPlatform.Infrastructure \
  --exclude-dir=DevOpsMigrationPlatform.CLI.TfsMigration
```

---

## Check 5 — Domain Interface Defined in Infrastructure or Application Layer

**Smell:** An interface that represents a domain port (e.g., `IWorkItemRevisionSource`, `IArtefactStore`) is declared in an infrastructure or CLI project instead of `DevOpsMigrationPlatform.Abstractions`.

```csharp
// BAD — port interface in wrong layer
// File: DevOpsMigrationPlatform.Infrastructure/Ports/IWorkItemRevisionSource.cs
public interface IWorkItemRevisionSource { ... } // ❌ must be in Abstractions
```

**Fix:** Move the interface to `DevOpsMigrationPlatform.Abstractions`.

**How to find:**

```bash
grep -rn "^public interface " \
  src/DevOpsMigrationPlatform.Infrastructure \
  src/DevOpsMigrationPlatform.CLI.Migration \
  src/DevOpsMigrationPlatform.MigrationAgent \
  --include="*.cs"
```

Any interface consumed by more than one project must live in `Abstractions`.

---

## Severity Classification

| Severity | Criteria |
|---|---|
| **Critical** | Domain or use-case type directly references a concrete outer-ring type — breaks the dependency rule |
| **High** | Domain interface declared outside `Abstractions` — prevents inner-ring independence |
| **Medium** | Business logic placed in CLI/TUI/Infrastructure — correct behaviour but wrong layer |
| **Low** | Use-case returns framework DTO — works today, breaks on adapter replacement |

---

## Clean Architecture Check Checklist

Run this checklist after any code change that touches domain types, use-case orchestrators, or infrastructure adapters:

- [ ] **Check 1**: No domain type in `Abstractions` imports a type from an infrastructure or SDK namespace.
- [ ] **Check 2**: All use-case orchestrators and jobs depend only on abstractions from the inner ring.
- [ ] **Check 3**: No filtering, validation, or transformation business logic exists in CLI commands or infrastructure adapters.
- [ ] **Check 4**: No use-case method signature accepts or returns a type defined in an infrastructure project.
- [ ] **Check 5**: All domain port interfaces are declared in `DevOpsMigrationPlatform.Abstractions`.

All items must be checked before a feature or refactoring is declared complete. Any unchecked item is a blocking violation.
