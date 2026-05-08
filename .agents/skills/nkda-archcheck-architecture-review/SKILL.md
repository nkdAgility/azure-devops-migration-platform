---
name: nkda-archcheck-architecture-review
description: Runs all five architecture perspective checks (Modular Monolith, Clean Architecture, Hexagonal, Vertical Slice, Screaming Architecture), then runs nkda-improve-codebase-architecture to propose deepening opportunities, and produces a single combined, prioritised report.
---

# Skill: Architecture Review

Use this skill when you need a complete, cross-perspective audit of the codebase before a major release, after a significant refactoring, or when onboarding to the codebase for the first time. It runs all five architecture checks, then runs a deepening-opportunities pass, and aggregates findings into a single prioritised report.

---

## Role

When this skill is active, execute all five architecture-perspective skills in sequence, then execute `nkda-improve-codebase-architecture`, collect every violation and deepening opportunity found, and produce a consolidated report. Do **not** apply any fixes automatically — report findings first. If instructed to fix, address the highest-severity violations first and re-run the affected checks to confirm resolution.

---

## The Six Perspectives

Each perspective answers a different architectural question about this codebase:

| Perspective | Question it answers | Skill |
|---|---|---|
| **Modular Monolith** | How do we isolate change? | `nkda-modular-monolith-check` |
| **Clean Architecture** | What should be central? | `nkda-clean-architecture-check` |
| **Hexagonal Architecture** | How do we interact with the outside world? | `nkda-hexagonal-check` |
| **Vertical Slice** | How do we deliver value? | `nkda-vertical-slice-check` |
| **Screaming Architecture** | How do we make intent visible? | `nkda-screaming-architecture-check` |
| **Architecture Deepening** | Where can we increase depth, leverage, and locality? | `nkda-improve-codebase-architecture` |

These perspectives are **orthogonal, not mutually exclusive**. A single violation may surface in more than one perspective — this is intentional and helps prioritise fixes.

---

## Execution Order

Run the checks in the following order. Each check is independent; run all even if earlier checks find violations.

### Step 1 — Modular Monolith Check

Execute the full `nkda-modular-monolith-check` skill. Record all findings tagged `[MM]`.

Key questions:
- Are module projects coupled to each other directly?
- Does every module expose a self-contained registration entry point?
- Are shared types declared in `Abstractions`?

### Step 2 — Clean Architecture Check

Execute the full `nkda-clean-architecture-check` skill. Record all findings tagged `[CA]`.

Key questions:
- Does any inner-ring type reference an outer-ring type?
- Does business logic live in the correct layer?
- Are all domain port interfaces declared in `Abstractions`?

### Step 3 — Hexagonal Architecture Check

Execute the full `nkda-hexagonal-check` skill. Record all findings tagged `[HX]`.

Key questions:
- Do modules reference concrete store implementations?
- Do modules call Azure DevOps or TFS SDKs directly?
- Are all shared interfaces in `Abstractions`?

### Step 4 — Vertical Slice Check

Execute the full `nkda-vertical-slice-check` skill. Record all findings tagged `[VS]`.

Key questions:
- Is business logic shared across slices via static helpers?
- Does each slice have an end-to-end system test?
- Are all `IStateStore` keys properly scoped per slice?

### Step 5 — Screaming Architecture Check

Execute the full `nkda-screaming-architecture-check` skill. Record all findings tagged `[SA]`.

Key questions:
- Are project, namespace, and class names business-meaningful?
- Do `.feature` file scenario names use business language?
- Do public method names use business verbs?

### Step 6 — Architecture Deepening Pass

Execute the full `nkda-improve-codebase-architecture` skill. Record all deepening opportunities tagged `[DC]`.

Key questions:
- Which seams are currently shallow and leaking complexity to callers?
- Which modules can be deepened to improve leverage and locality?
- Which refactors improve testability and AI-navigability without violating ADRs?

---

## Combined Report Format

After completing all six checks, produce a report in the following format:

---

### Architecture Review — Combined Report

**Date:** `<date>`  
**Scope:** `<entire solution | project name | file path>`  
**Perspectives checked:** Modular Monolith · Clean Architecture · Hexagonal · Vertical Slice · Screaming Architecture · Architecture Deepening

---

#### Summary Table

| Perspective | Critical | High | Medium | Low | Informational |
|---|---|---|---|---|---|
| Modular Monolith [MM] | `n` | `n` | `n` | `n` | — |
| Clean Architecture [CA] | `n` | `n` | `n` | `n` | — |
| Hexagonal [HX] | `n` | `n` | `n` | `n` | — |
| Vertical Slice [VS] | `n` | `n` | `n` | `n` | — |
| Screaming Architecture [SA] | `n` | `n` | `n` | `n` | `n` |
| Architecture Deepening [DC] | `n` | `n` | `n` | `n` | `n` |
| **Total** | **`n`** | **`n`** | **`n`** | **`n`** | **`n`** |

---

#### Critical Violations (must fix before merge)

List each Critical violation in descending priority:

```
[HX-C1] FileSystemArtefactStore referenced in WorkItemsExportModule
  File: src/DevOpsMigrationPlatform.WorkItems.Export/WorkItemsExportModule.cs:42
  Fix:  Replace with IArtefactStore via constructor injection.

[MM-C1] WorkItems.Import project references WorkItems.Export directly
  File: src/DevOpsMigrationPlatform.WorkItems.Import/WorkItems.Import.csproj:8
  Fix:  Extract shared type to Abstractions; remove ProjectReference.
```

#### High Violations (fix in current sprint)

List each High violation:

```
[CA-H1] Domain port IWorkItemRevisionSource declared in Infrastructure project
  File: src/DevOpsMigrationPlatform.Infrastructure/Ports/IWorkItemRevisionSource.cs
  Fix:  Move to DevOpsMigrationPlatform.Abstractions.

[VS-H1] RevisionHelper.NormaliseField() shared between Export and Import slices
  File: src/DevOpsMigrationPlatform.WorkItems.Export/Helpers/RevisionHelper.cs:17
  Fix:  Elevate to IWorkItemFieldNormaliser interface in Abstractions.
```

#### Medium Violations (fix in next sprint)

```
[MM-M1] WorkItemsExportModule registration scattered across MigrationAgent startup
  Fix:  Create ServiceCollectionExtensions.AddWorkItemsExportModule().

[SA-M1] Class named "Processor" without domain-qualifying prefix
  File: src/DevOpsMigrationPlatform.WorkItems.Export/Processing/Processor.cs
  Fix:  Rename to WorkItemRevisionProcessor.
```

#### Low / Informational Violations (address in backlog)

```
[SA-L1] Scenario name uses technical language: "Test serialisation of WorkItemRevision"
  File: features/export/workitems/export-work-items.feature:14
  Fix:  Rewrite as "Export preserves all field values for each revision".

[VS-L1] IStateStore key "workitems/checkpoint" not scoped by operation or job ID
  File: src/DevOpsMigrationPlatform.WorkItems.Import/ImportJob.cs:88
  Fix:  Use key pattern "import/workitems/{jobId}/checkpoint".
```

#### Deepening Opportunities (candidate refactors)

List each deepening opportunity surfaced by `nkda-improve-codebase-architecture`:

```
[DC-H1] Work item field normalisation split across three pass-through helpers
  Files: src/DevOpsMigrationPlatform.WorkItems.Export/..., src/DevOpsMigrationPlatform.WorkItems.Import/...
  Problem: shallow seams force callers to orchestrate ordering and error handling.
  Solution: deepen into one module-level normalisation seam with explicit invariants.
  Benefits: higher locality (one place to change), higher leverage (simpler callers), cleaner test surface.
```

---

#### Cross-Cutting Patterns

Note any violations that appear in more than one perspective — these indicate systemic issues:

```
Example:
  WorkItemRevision declared in Infrastructure project
    → [CA-H1] domain type in wrong layer
    → [MM-H1] shared type not in Abstractions
    → [HX-H1] domain type in infrastructure project
  Root cause: single misplacement; one move to Abstractions resolves all three.
```

---

#### Recommended Fix Order

1. Resolve all **Critical** violations (blocking).
2. Resolve all **High** violations before the next release.
3. Resolve cross-cutting pattern violations in a single refactoring session to avoid partial fixes.
4. Schedule **Medium** violations as tech-debt stories for the next sprint.
5. Treat **Low / Informational** as living backlog items — address during normal feature work.

---

## Architecture Review Checklist

- [ ] **Modular Monolith** check executed and all findings recorded.
- [ ] **Clean Architecture** check executed and all findings recorded.
- [ ] **Hexagonal Architecture** check executed and all findings recorded.
- [ ] **Vertical Slice** check executed and all findings recorded.
- [ ] **Screaming Architecture** check executed and all findings recorded.
- [ ] **Architecture Deepening** pass executed and all opportunities recorded.
- [ ] Combined summary table completed.
- [ ] All Critical violations listed with file, line, and fix suggestion.
- [ ] All High violations listed with file, line, and fix suggestion.
- [ ] Cross-cutting patterns identified and noted.
- [ ] Recommended fix order provided.

The review is not complete until all six checks have been run and all sections of the report are populated.
