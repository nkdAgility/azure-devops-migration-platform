# Feature Specification: Runtime State Categories and Resume Semantics Alignment

**Feature Branch**: `[033-runtime-state-categories]`  
**Created**: 2026-05-07  
**Status**: Reconciled (Partially Implemented)  
**Input**: User description: "Align runtime package-state behavior with the documented multi-scope model and formalize distinct orchestration, project-resume, batch-iteration, and run-audit state categories."

## Reconciliation Update (2026-05-17)

- **Authority order applied**: `.agents` guidance → specs/034 + specs/035 → this spec → implementation evidence → tests.
- **Superseded implementation references**:
  - Path references to `Infrastructure.Agent/Context/PackagePaths.cs` and `Infrastructure.Agent/WorkItems/*` are superseded by spec 034/035 refactors into `Infrastructure.Storage.FileSystem/*`, `Infrastructure.Agent/Export/*`, and `Infrastructure.Agent/Import/*`.
- **Remaining incomplete work**:
  - Explicit O-1 spans for `state.paths.resolve`, `state.workitems.batch.save`, and `state.progress.emit`.
  - Commit-evidence tasks recorded in `tasks.md` (T076-T078) remain incomplete.
- **Verification evidence**:
  - `/speckit.analyze` and `/speckit.checklist` executed for this folder.
  - Targeted runtime-state tests: `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests.csproj --filter "RuntimeState|RunScope|Cursor|Cadence|Checkpoint"` (pass in checklist run output).

## Clarifications

### Session 2026-05-07

- Q: How granular should progress updates and save checkpoints be across processing workloads? → A: Use the finest practical progress notifications and reasonably fine-grained save checkpoints for all processing; for work items specifically, persist at work-item-batch boundaries and emit work-item-level progress updates.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Enforce Authoritative State Scopes (Priority: P1)

As a migration operator, I need package-level, project-level, and run-level state to have clearly enforced responsibilities so resume and phase-gate behavior is consistent and predictable across runs.

**Why this priority**: If authoritative state boundaries are not enforced, resume and gating can use the wrong files and produce incorrect migration outcomes.

**Independent Test**: Run an interrupted migration and verify that resume and phase-gate decisions use only authoritative project/org/package scoped state, never run-scoped audit copies.

**Acceptance Scenarios**:

1. **Given** a package with root orchestration state and project resume state, **When** a run is resumed, **Then** resume and phase-gate decisions are derived only from those authoritative locations.
2. **Given** a package containing run audit folders, **When** later runs execute, **Then** run-scoped files are treated as audit records only and cannot change resume behavior.

---

### User Story 2 - Isolate Module Resume Identity by Action (Priority: P1)

As a migration operator, I need resume identity to include action and module so export, import, and inventory progress do not collide or overwrite each other.

**Why this priority**: Colliding cursor identity causes incorrect resume points and cross-phase corruption of progress.

**Independent Test**: Execute inventory, export, and import for the same project and confirm each action keeps independent resume state without cross-action interference.

**Acceptance Scenarios**:

1. **Given** progress has been recorded for one action, **When** a different action runs for the same module and project, **Then** it reads and writes a distinct resume identity.
2. **Given** export and import both process work items, **When** each action resumes after interruption, **Then** each resumes from its own action-specific state and not the other action's state.

---

### User Story 3 - Fine-Grained Progress and Save Cadence (Priority: P2)

As a migration operator, I need all long-running processing to emit fine-grained progress updates and persist resume state at a reasonably fine-grained cadence so reruns continue close to the interruption point and execution remains observable.

**Why this priority**: Coarse updates and save points reduce operator visibility and can force expensive reprocessing after interruptions.

**Independent Test**: Interrupt representative long-running operations, rerun them, and verify progress visibility remains detailed and resume restarts near the latest durable checkpoint.

**Acceptance Scenarios**:

1. **Given** several work item batches already completed, **When** work item processing resumes, **Then** completed batches are skipped and progress resumes from the next incomplete batch.
2. **Given** a long-running non-work-item dataset, **When** processing is interrupted and resumed, **Then** progress resumes from the latest reasonable checkpoint with minimal replay.

---

### Edge Cases

- A package contains both legacy root-scoped checkpoint files and current project-scoped resume files.
- A run audit folder is missing or incomplete while authoritative root/project state is intact.
- An interruption occurs between batch completion and the next batch start.
- Inventory, export, and import all run for the same module and project in close succession.
- A rerun uses the same package after a prior failed run and a subsequent successful run.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The system MUST define four runtime state categories: package-wide orchestration state, project-scoped module resume state, batch-scoped work item iteration state, and run-scoped audit state.
- **FR-002**: The system MUST treat root `.migration/` as the authoritative package-wide orchestration state for phase planning, completion markers, and package-level coordination artifacts.
- **FR-003**: The system MUST treat `/{org}/{project}/.migration/` (project scope), `/{org}/.migration/` (org scope), and `/.migration/` (package scope) as the authoritative resume-state hierarchy for module progress.
- **FR-004**: The system MUST treat `.migration/runs/<runId>/` as run-scoped audit output only and MUST NOT use it for resume, phase-gate, or orchestration decisions.
- **FR-005**: Resume identity for project-scoped module state MUST include both action and module so different actions do not share a cursor namespace.
- **FR-006**: Work item export and work item import MUST maintain independent action-specific resume identities even when operating on the same project and module family.
- **FR-007**: Inventory behavior MUST align with the same action-aware resume identity model and MUST NOT rely on inconsistent fallback semantics that conflict with authoritative state rules.
- **FR-014**: Resume-state reads MUST use precedence order project → org → package, and writes/resets MUST target only the most-specific resolved scope.
- **FR-008**: Processing workflows MUST emit fine-grained progress notifications so operators can follow active work with meaningful in-flight detail.
- **FR-009**: Processing workflows MUST persist resume state at the finest reasonable cadence for the workload so resumed runs restart near the latest durable checkpoint.
- **FR-010**: A run restart MUST preserve correctness when interruption occurs at project scope or within batch iteration scope.
- **FR-011**: Phase-gate and resume decisions MUST remain correct when multiple runs exist for the same package.
- **FR-012**: Documentation-facing state semantics and runtime behavior MUST remain aligned for all four state categories.
- **FR-013**: Work item processing MUST persist save state at completed work-item-batch boundaries and emit work-item-level progress updates.

### Key Entities *(include if feature involves data)*

- **Package Orchestration State**: Package-wide authoritative state used for phase planning and completion gating.
- **Project Module Resume State**: Authoritative per-project, per-action, per-module resume records used to continue module execution safely.
- **Processing Progress State**: Fine-grained progress and notification records that let operators understand and follow in-flight execution.
- **Work Item Batch Save State**: Work-item-specific durable state that records completed batch boundaries for resumable export/import.
- **Run Audit Record**: Run-scoped copy of execution metadata and logs retained for traceability only.
- **Cursor Identity**: The action-and-module-qualified identity that partitions resume state namespaces.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In acceptance testing, 100% of resume and phase-gate decisions use only authoritative package-wide and project-scoped state sources.
- **SC-002**: In acceptance testing, 0 cross-action resume collisions occur when inventory, export, and import execute for the same project/module combination.
- **SC-003**: In interrupted processing runs, resumed execution restarts from the latest durable checkpoint with minimal replay in at least 95% of replayed interruption points.
- **SC-004**: In operator validation scenarios, run-scoped audit records remain fully inspectable while contributing 0 inputs to authoritative resume or gating decisions.
- **SC-005**: In operator follow-mode scenarios, progress updates are granular enough to show steady forward movement for long-running operations, with work-item-level progress visible for work item processing.

## Assumptions

- Existing package contents and naming conventions remain the baseline and are extended without redefining unrelated module data layouts.
- The feature scope is limited to runtime state semantics and resume behavior, not redesigning unrelated migration modules.
- Legacy package compatibility is handled through controlled migration or fallback behavior without weakening authoritative-scope rules.
- Operators continue to use package artifacts as the long-lived source of migration truth across reruns.
