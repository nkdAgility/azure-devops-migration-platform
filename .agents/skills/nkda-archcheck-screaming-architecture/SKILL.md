---
name: nkda-archcheck-screaming-architecture
description: Scans the codebase for Screaming Architecture violations — generic names, purpose-obscuring structure, and missing intent signals — and produces a prioritised fix list.
---

# Skill: Screaming Architecture Check

Use this skill when you need to audit whether the codebase structure communicates its business purpose clearly, or before declaring a feature complete to ensure new code integrates with the naming and structural conventions of this platform.

---

## Role

When this skill is active, scan the specified scope (entire solution, single project, or single file) for violations of Screaming Architecture intent-clarity rules. Produce a prioritised list of violations and, for each one, suggest a minimal, intent-revealing rename or restructure. Do **not** apply fixes automatically unless explicitly instructed — report findings first.

---

## Intent Model

A codebase screams its purpose when:

1. **Project names** state what the system does, not what technology it uses.
2. **Namespace and folder names** reflect business operations, not technical layers.
3. **Class and method names** describe what they accomplish for the business, not how they accomplish it technically.
4. **Shared types** are named after domain concepts, not implementation artefacts.

```
// The folder tree should read like a business glossary:
src/
  DevOpsMigrationPlatform.WorkItems.Export/     ← "Export Work Items" ✅
  DevOpsMigrationPlatform.WorkItems.Import/     ← "Import Work Items" ✅
  DevOpsMigrationPlatform.Abstractions/         ← shared domain contracts ✅

// Not like this:
src/
  Processors/                                   ← what does it process? ❌
  Utilities/                                    ← what utilities? ❌
  Helpers/                                      ← helpers for what? ❌
  Common/                                       ← common to what? ❌
```

---

## Check 1 — Generic Class or Namespace Names

**Smell:** A class, namespace, or folder is named with a generic technical term that does not communicate its business purpose.

Generic terms to flag: `Helper`, `Util`, `Utility`, `Manager`, `Handler` (without a qualifying noun), `Common`, `Shared`, `Base` (as a suffix without context), `Service` (as a standalone class name).

```csharp
// BAD — generic names obscure purpose
namespace DevOpsMigrationPlatform.Utilities         // ❌
{
    public class DataHelper { ... }                 // ❌
    public class ExportManager { ... }              // ❌
    public class BaseService { ... }                // ❌
}
```

**Fix:** Rename to reflect the specific business concern.

```csharp
// GOOD — names communicate purpose
namespace DevOpsMigrationPlatform.WorkItems.Export.Revision  // ✅
{
    public class WorkItemRevisionMapper { ... }               // ✅
    public class WorkItemExportOrchestrator { ... }           // ✅
    public class WorkItemExportJobBase { ... }                // ✅ — Base qualified
}
```

**How to find:**

```bash
grep -rn "class.*Helper\b\|class.*Util\b\|class.*Manager\b\|namespace.*\.Common\b\|namespace.*\.Utilities\b\|namespace.*\.Helpers\b" \
  src/ --include="*.cs"
```

Each hit should be reviewed: is the name the most precise business description available?

---

## Check 2 — Project Name Does Not Reflect Business Operation

**Smell:** A project is named after a technical role or layer rather than the business operation it supports.

```
// BAD — project names that say nothing about the business
DevOpsMigrationPlatform.DataAccess            ❌
DevOpsMigrationPlatform.Processing            ❌
DevOpsMigrationPlatform.Services              ❌
```

**Fix:** Rename the project to reflect the migration operation it supports.

```
// GOOD — project names state what the system does
DevOpsMigrationPlatform.WorkItems.Export      ✅
DevOpsMigrationPlatform.WorkItems.Import      ✅
DevOpsMigrationPlatform.Abstractions          ✅  (well-understood technical boundary)
DevOpsMigrationPlatform.Infrastructure        ✅  (well-understood technical boundary)
```

**How to find:**

```bash
find src/ -name "*.csproj" | xargs -I{} basename {} .csproj | sort
```

Review each project name. Any name that does not include a business operation noun (WorkItems, Pipelines, Boards, Attachments, etc.) or a well-understood platform boundary term (Abstractions, Infrastructure, CLI, AppHost) is a candidate violation.

---

## Check 3 — Class Responsibility Not Deducible from Name Alone

**Smell:** Reading a class name alone does not tell the reader what business outcome the class produces. The reader must open the file to understand its purpose.

```csharp
// BAD — name requires investigation to understand purpose
public class Processor { ... }       // ❌ processes what?
public class Runner { ... }          // ❌ runs what?
public class Executor { ... }        // ❌ executes what?
public class Worker { ... }          // ❌ works on what?
public class Step { ... }            // ❌ which step of what?
```

**Fix:** Prefix the class name with its domain noun.

```csharp
// GOOD — name announces purpose
public class WorkItemRevisionProcessor { ... }   // ✅
public class MigrationJobRunner { ... }          // ✅
public class ExportPhaseExecutor { ... }         // ✅
public class AttachmentDownloadWorker { ... }    // ✅
public class CheckpointResumptionStep { ... }    // ✅
```

**How to find:**

```bash
grep -rn "^public class Processor\b\|^public class Runner\b\|^public class Executor\b\|^public class Worker\b\|^public class Step\b\|^public class Handler\b" \
  src/ --include="*.cs"
```

Any standalone technical noun as a class name is a candidate for a domain-qualified rename.

---

## Check 4 — Feature File Scenario Names Do Not Reflect Business Language

**Smell:** Gherkin `.feature` files use technical terms in scenario names, making it hard for a non-technical stakeholder to understand what the system does.

```gherkin
# BAD — technical language in acceptance criteria
Scenario: Test serialisation of WorkItemRevision to JSON     ❌
Scenario: Verify FileSystemArtefactStore write operation     ❌
```

**Fix:** Rewrite scenario names using the language of the business.

```gherkin
# GOOD — business language throughout
Scenario: Export preserves all field values for each revision  ✅
Scenario: Attachments are stored alongside the revision they belong to  ✅
```

**How to find:**

```bash
grep -rn "^  Scenario:" features/ --include="*.feature" \
  | grep -i "test\|verify\|check\|serialis\|deserialis\|json\|store\|class\|method"
```

Any scenario name containing technical terms that a business analyst would not use is a candidate for a plain-language rewrite.

---

## Check 5 — Public Method Name Uses Technical Verb Instead of Business Verb

**Smell:** A public method on a domain or use-case class uses a technical verb (`Process`, `Execute`, `Run`, `Handle`, `Perform`) where a business verb would communicate intent more clearly.

```csharp
// BAD — technical verbs obscure business intent
public Task ProcessAsync(WorkItemRevision revision, CancellationToken ct);   // ❌
public Task ExecuteAsync(MigrationJob job, CancellationToken ct);             // ❌
public Task HandleAsync(ExportRequest request, CancellationToken ct);         // ❌
```

**Fix:** Use the business verb that describes the outcome.

```csharp
// GOOD — business verbs reveal intent
public Task ExportRevisionAsync(WorkItemRevision revision, CancellationToken ct);  // ✅
public Task RunMigrationAsync(MigrationJob job, CancellationToken ct);             // ✅
public Task ExportAsync(ExportRequest request, CancellationToken ct);              // ✅
```

**Exception:** Framework-mandated method names (`ExecuteAsync` on `ICommandHandler`, `HandleAsync` on `IJobHandler`) are permitted on framework-facing types. The check applies to domain and use-case method names that are chosen freely.

**How to find:**

```bash
grep -rn "public.*Task ProcessAsync\|public.*Task ExecuteAsync\|public.*Task HandleAsync\|public.*Task PerformAsync" \
  src/DevOpsMigrationPlatform.Abstractions \
  src/ --include="*.cs" \
  | grep -v "IJobHandler\|ICommandHandler\|IHostedService\|BackgroundService"
```

---

## Severity Classification

| Severity | Criteria |
|---|---|
| **High** | Project or namespace name is generic technical term with no business noun — hides system purpose |
| **Medium** | Class name does not communicate its business responsibility — requires opening the file to understand |
| **Low** | Method name uses technical verb where a business verb would improve clarity |
| **Informational** | Feature file scenario names use technical language — impacts stakeholder readability only |

---

## Screaming Architecture Check Checklist

Run this checklist when reviewing new projects, features, or significant renames:

- [ ] **Check 1**: No class, namespace, or folder uses a generic technical name (`Helper`, `Util`, `Manager`, `Common`, `Shared`) without a qualifying business noun.
- [ ] **Check 2**: Every project name reflects a business operation or a well-understood platform boundary.
- [ ] **Check 3**: Every public class name announces its business responsibility without requiring the reader to open the file.
- [ ] **Check 4**: All `.feature` file scenario names use business language understandable by a non-technical stakeholder.
- [ ] **Check 5**: Public method names on domain and use-case classes use business verbs, not generic technical verbs.

All items must be checked before a feature or refactoring is declared complete. Any unchecked item is a blocking violation.
