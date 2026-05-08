---
name: nkda-core-plan-architecture-compliance
description: Design-time compliance review — validates spec.md and plan.md against all five architecture perspectives (Modular Monolith, Clean, Hexagonal, Vertical Slice, Screaming) plus constitution and guardrails before task generation begins. Analyses proposed design, not code.
---

# Skill: Plan Architecture Compliance (Design-Time)

Use this skill as an `after_plan` hook to validate that the **proposed changes** in `spec.md` and `plan.md` are architecturally compliant **before any code is written or tasks generated**. This is a design-time review — it analyses specification and plan artifacts, not source code.

> **This skill does NOT scan `.cs` files or `.csproj` references.**
> For post-implementation code scanning, use `nkda-architecture-review` (which invokes
> `nkda-modular-monolith-check`, `nkda-clean-architecture-check`, `nkda-hexagonal-check`,
> `nkda-vertical-slice-check`, `nkda-screaming-architecture-check`).

---

## Role

When this skill is active:

1. Read the feature's `spec.md` and `plan.md`.
2. Read all guardrail files in `/.agents/guardrails/` and the constitution (`.specify/memory/constitution.md`).
3. Evaluate the **proposed** architecture, data model, module placement, interface design, and naming against each of the five perspectives.
4. Check alignment with constitution principles and system-architecture guardrails.
5. Produce a consolidated compliance report with findings and recommendations.
6. Do **not** modify any files — this is a read-only analysis.

The goal is to catch architectural violations **in the design** before they become code — preventing rework, not detecting it after the fact.

---

## Input Artifacts

The skill requires these files to exist in the feature directory:

| Artifact | Purpose |
|---|---|
| `spec.md` | Functional requirements, user stories, acceptance criteria, edge cases |
| `plan.md` | Architecture choices, data model, phases, file paths, module placement, technical constraints |

Additionally, the skill loads:
- `/.agents/guardrails/architecture-boundaries.md` — hard architectural rules
- `/.agents/guardrails/coding-standards.md` — engineering practice categories
- `.specify/memory/constitution.md` — non-negotiable principles

---

## The Five Perspectives

Each perspective asks a different design-time question about the **proposed changes**. The rules below are derived from the corresponding code-scanning skills (`nkda-modular-monolith-check`, `nkda-clean-architecture-check`, `nkda-hexagonal-check`, `nkda-vertical-slice-check`, `nkda-screaming-architecture-check`) — adapted for plan-level analysis.

| Perspective | Design-Time Question | Tag |
|---|---|---|
| **Modular Monolith** | Will the proposed changes respect module boundaries and isolation? | `[MM]` |
| **Clean Architecture** | Do the proposed dependencies point inward? Is business logic in the right ring? | `[CA]` |
| **Hexagonal Architecture** | Do the proposed types and interfaces keep infrastructure out of domain/module code? | `[HX]` |
| **Vertical Slice** | Does the proposal deliver a complete vertical slice with end-to-end ownership? | `[VS]` |
| **Screaming Architecture** | Do the proposed names, projects, and structures communicate business intent? | `[SA]` |

These perspectives are **orthogonal, not mutually exclusive**. A single design decision may violate more than one perspective.

---

## Execution Order

Run all five perspective checks, then the constitution and guardrail alignment checks. Each check is independent; run all even if earlier checks find violations.

### Step 1 — Modular Monolith Compliance [MM]

Review `plan.md` for proposed module structure, project references, and DI registration patterns.

**Flag if the plan proposes:**
- A new module that references another module project directly (not through `Abstractions`)
- New shared types placed inside a module project instead of `Abstractions`
- DI registration scattered across host startup instead of a self-contained `Add<Module>` extension method
- Cross-module state sharing through anything other than `IArtefactStore` or `IStateStore`
- A new module without a self-contained registration entry point

**Pass criteria (from `nkda-modular-monolith-check`):**
- Every new module depends only on `DevOpsMigrationPlatform.Abstractions`
- Every new shared type is placed in `Abstractions`
- Every new module exposes a single `Add<ModuleName>` extension method
- Module internals are organised by concern, not by technical layer

### Step 2 — Clean Architecture Compliance [CA]

Review `plan.md` for proposed dependency directions and layer assignments.

**Flag if the plan proposes:**
- A domain or use-case type that depends on an infrastructure or SDK type
- Business logic placed in CLI commands, TUI, or infrastructure adapters
- A use-case method signature that accepts or returns an infrastructure DTO (e.g., TFS `WorkItem`)
- A domain port interface placed outside `Abstractions`
- An orchestrator or job that directly constructs infrastructure types

**Pass criteria (from `nkda-clean-architecture-check`):**
- All proposed dependencies point inward (Infrastructure → Use Cases → Domain)
- All domain interfaces are placed in `Abstractions`
- Business logic is in modules/orchestrators, not in CLI or infrastructure
- No use-case method signature leaks infrastructure DTOs

### Step 3 — Hexagonal Architecture Compliance [HX]

Review `plan.md` for proposed port/adapter boundaries.

**Flag if the plan proposes:**
- Module code that references concrete store implementations (`FileSystemArtefactStore`, `AzureBlobArtefactStore`)
- Module code that calls Azure DevOps or TFS SDK types directly
- Direct file I/O (`System.IO`) in module code instead of `IArtefactStore`
- New interfaces defined in infrastructure or CLI projects instead of `Abstractions`
- Direct construction of infrastructure services with `new` instead of injection
- Environment branching (`if (env == "Production")`) in module/domain code
- `Console.Write*` usage outside CLI/TUI boundary

**Pass criteria (from `nkda-hexagonal-check`):**
- All proposed module code depends only on abstractions
- All proposed infrastructure interaction flows through injected interfaces
- No proposed file I/O, SDK calls, or `Console` usage in module/domain layer
- All shared interfaces placed in `Abstractions`

### Step 4 — Vertical Slice Compliance [VS]

Review `spec.md` and `plan.md` for proposed slice boundaries and ownership.

**Flag if the plan proposes:**
- Business logic shared across slices via static helpers instead of injected domain services
- A slice that delegates to another slice's internal classes
- A new migration operation without a corresponding `.feature` file or system test plan
- Checkpoint/state keys not scoped by operation type and job ID
- A new slice that cannot evolve or be deleted independently
- A slice without planned end-to-end `[TestCategory("SystemTest")]` coverage

**Pass criteria (from `nkda-vertical-slice-check`):**
- Each proposed slice owns its full path from entry point to storage
- Shared logic is elevated to `Abstractions` interfaces, not static helpers
- Each slice has planned acceptance tests and feature files
- All `IStateStore` keys are scoped by operation and job

### Step 5 — Screaming Architecture Compliance [SA]

Review `spec.md` and `plan.md` for proposed naming, project structure, and intent clarity.

**Flag if the plan proposes:**
- New projects, namespaces, or classes with generic names (`Helper`, `Util`, `Manager`, `Common`, `Shared`, `Processor` without qualifier, `Service` as standalone)
- Project names that describe technology roles instead of business operations
- Scenario names using technical language instead of business language
- Public method names using technical verbs (`Process`, `Execute`, `Handle`, `Perform`) where business verbs would be clearer
- Folder structure organised by technical layer (`Services/`, `Models/`, `Helpers/`) instead of by concern

**Pass criteria (from `nkda-screaming-architecture-check`):**
- All proposed names communicate business purpose
- All proposed projects are named after business operations or well-understood platform boundaries
- Proposed feature files use domain language
- Public method names use business verbs

---

### Step 6 — Constitution Alignment

Check the proposed plan against constitution principles (`.specify/memory/constitution.md`):

- **Principle I (Package Platform):** Does the plan maintain Source → Package → Target flow?
- **Principle III (Streaming & Memory Safety):** Does the plan propose streaming or does it buffer unbounded data?
- **Principle V (Module Isolation):** Does the plan keep modules decoupled?
- **Principle VI (TFS Agent Boundary):** If TFS-related, does it respect the net481 boundary?
- **Principle VII (Determinism):** Are the proposed operations idempotent and resumable?
- **Principle IX (SOLID & DI):** Does the plan use constructor injection and IOptions<T>?
- **Principle X (Engineering Practice):** Does the plan align with all 21 engineering-practice categories?
- **Principle XI (Connector Coverage):** Does the plan cover Simulated, AzureDevOps, AND TFS?

Constitution violations are automatically **Critical**.

### Step 7 — Guardrail Alignment

Check the proposed plan against `/.agents/guardrails/architecture-boundaries.md`:

Flag any proposed design that would violate the 23 absolute rules. Pay special attention to:
- Rule 2 (streaming import) — does the plan load all revisions into memory?
- Rule 6 (no source-to-target) — does the plan bypass the package?
- Rule 7 (IArtefactStore/IStateStore only) — does the plan access filesystem directly?
- Rule 11 (ControlPlane must not execute) — does the plan put execution logic in the wrong component?
- Rule 13 (IArtefactStore only) — does the plan reference concrete store implementations?
- Rule 16 (CLI must not contain migration logic) — does the plan put business logic in the CLI?
- Rule 21 (reuse existing patterns) — does the plan invent new abstractions where existing ones suffice?
- Rule 22 (no workarounds) — does the plan propose adapter shims or deferred alignment?
- Rule 23 (data residency) — does the plan have non-Agent components writing to the package?

Guardrail violations are automatically **Critical**.

---

## Combined Report Format

After completing all seven checks, produce a report in the following format:

---

### Plan Architecture Compliance — Design-Time Report

**Date:** `<date>`
**Feature:** `<feature name from spec.md>`
**Artifacts reviewed:** `spec.md` · `plan.md`
**Perspectives checked:** Modular Monolith · Clean Architecture · Hexagonal · Vertical Slice · Screaming Architecture · Constitution · Guardrails

---

#### Summary Table

| Perspective | Critical | High | Medium | Low | Informational |
|---|---|---|---|---|---|
| Modular Monolith [MM] | `n` | `n` | `n` | `n` | — |
| Clean Architecture [CA] | `n` | `n` | `n` | `n` | — |
| Hexagonal [HX] | `n` | `n` | `n` | `n` | — |
| Vertical Slice [VS] | `n` | `n` | `n` | `n` | — |
| Screaming Architecture [SA] | `n` | `n` | `n` | `n` | `n` |
| Constitution [CN] | `n` | — | — | — | — |
| Guardrails [GR] | `n` | — | — | — | — |
| **Total** | **`n`** | **`n`** | **`n`** | **`n`** | **`n`** |

---

#### Critical Violations (must fix in plan before task generation)

List each Critical violation in descending priority:

```
[HX-C1] Plan proposes WorkItemsExportModule depends on FileSystemArtefactStore
  Location: plan.md § Data Model / Dependencies
  Fix:  Change to IArtefactStore dependency; concrete binding belongs in Infrastructure.

[CN-C1] Plan proposes direct Source → Target migration, violating Principle I
  Location: plan.md § Architecture Overview
  Fix:  Ensure Source → Package → Target flow; add export-to-package phase.

[GR-C1] Plan proposes ControlPlane executes migration logic (Rule 11)
  Location: plan.md § Component Responsibilities
  Fix:  Move execution logic to MigrationAgent; ControlPlane coordinates only.
```

#### High Violations (should fix before task generation)

```
[CA-H1] Plan places IWorkItemRevisionSource interface in Infrastructure project
  Location: plan.md § New Interfaces
  Fix:  Move proposed interface to DevOpsMigrationPlatform.Abstractions.
```

#### Medium Violations (recommend fixing before task generation)

```
[SA-M1] Proposed class "Processor" lacks domain-qualifying prefix
  Location: plan.md § New Types
  Fix:  Rename to WorkItemRevisionProcessor.
```

#### Low / Informational (note for implementation)

```
[SA-I1] Proposed feature scenario uses technical language
  Location: spec.md § Acceptance Criteria
  Fix:  Rewrite using business language.
```

---

#### Cross-Cutting Patterns

Note any design decisions that violate multiple perspectives — these indicate systemic design issues that should be resolved at the plan level:

```
Example:
  Plan proposes WorkItemRevision declared in Infrastructure project
    → [CA-H1] domain type proposed in wrong layer
    → [MM-H1] shared type not proposed for Abstractions
    → [HX-H1] domain type in infrastructure project
  Root cause: single misplacement in the plan; correcting the proposed location resolves all three.
```

---

#### Verdict

State one of:

- **✅ PASS** — No Critical or High violations. Plan is architecturally compliant. Proceed to task generation.
- **⚠️ PASS WITH WARNINGS** — No Critical violations, but High violations exist. Recommend fixing before task generation; may proceed at user's discretion.
- **❌ FAIL** — Critical violations found. Plan MUST be corrected before task generation. Re-run `/speckit.plan` after addressing findings.

---

#### Recommended Next Actions

- If **FAIL**: List the specific plan edits needed, then "Re-run `/speckit.plan` to incorporate fixes."
- If **PASS WITH WARNINGS**: List recommended improvements, then "Proceed to `/speckit.tasks` or fix first."
- If **PASS**: "Proceed to `/speckit.tasks`."

---

## Severity Classification

| Severity | Design-Time Criteria |
|---|---|
| **Critical** | Plan violates a constitution MUST, a system-architecture absolute rule, or the dependency rule. Blocks task generation. |
| **High** | Plan proposes shared types or interfaces in the wrong project, business logic in the wrong layer, or cross-module coupling. Should fix before tasks. |
| **Medium** | Plan proposes generic naming, missing test coverage for a slice, or underspecified module boundaries. Recommend fixing. |
| **Low** | Plan uses technical language in scenario names, or proposes method names with technical verbs. Note for implementation. |
| **Informational** | Style suggestions that improve clarity but do not affect correctness. |

---

## Plan Architecture Compliance Checklist

- [ ] Feature `spec.md` loaded and reviewed.
- [ ] Feature `plan.md` loaded and reviewed.
- [ ] Constitution (`.specify/memory/constitution.md`) loaded and checked.
- [ ] System architecture guardrails (`/.agents/guardrails/architecture-boundaries.md`) loaded and checked.
- [ ] **Modular Monolith** perspective checked and findings recorded.
- [ ] **Clean Architecture** perspective checked and findings recorded.
- [ ] **Hexagonal Architecture** perspective checked and findings recorded.
- [ ] **Vertical Slice** perspective checked and findings recorded.
- [ ] **Screaming Architecture** perspective checked and findings recorded.
- [ ] **Constitution alignment** checked and findings recorded.
- [ ] **Guardrail alignment** checked and findings recorded.
- [ ] Combined summary table completed.
- [ ] All Critical violations listed with plan location and fix suggestion.
- [ ] Cross-cutting patterns identified.
- [ ] Verdict issued (PASS / PASS WITH WARNINGS / FAIL).
- [ ] Recommended next actions provided.

The review is not complete until all five perspectives, the constitution check, and the guardrail alignment check have been run, and a verdict has been issued.
