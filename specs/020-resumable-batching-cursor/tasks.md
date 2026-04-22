# Tasks: Resumable Work Item Batching

**Spec**: `specs/020-resumable-batching-cursor/spec.md`
**Plan**: `specs/020-resumable-batching-cursor/plan.md`
**Data Model**: `specs/020-resumable-batching-cursor/data-model.md`
**Contracts**: `specs/020-resumable-batching-cursor/contracts/resumable-batching-contract.md`
**Discrepancies**: `specs/020-resumable-batching-cursor/discrepancies.md`
**Branch**: `020-resumable-batching-cursor`

---

## Guardrail Alignment (Applies To All Phases)

Applicable guardrails for this feature:
- Deterministic ordering and streaming-only processing (`.agents/guardrails/system-architecture.md`, `.agents/guardrails/migration-rules.md`, `.agents/context/import-streaming.md`)
- Cursor/checkpoint state in `.migration/Checkpoints/` with no hidden state (`.agents/guardrails/workitems-rules.md`, `.agents/context/checkpointing.md`)
- Reuse existing work item iteration abstractions before introducing new patterns (`.agents/guardrails/system-architecture.md` rule 21, `docs/work-item-iteration-pattern.md`)
- Async/cancellation propagation, immutable models, and DI boundaries (`.agents/guardrails/coding-standards.md`)
- Tests-first ATDD workflow (`.agents/guardrails/testing-standards.md`, `.agents/guardrails/atdd-workflow.md`, `.agents/guardrails/acceptance-test-format.md`)

Explicitly rejected approaches:
- Any resume implementation that skips query fingerprint compatibility checks.
- Any approach that buffers full result sets or sorts all work items in memory.
- Any strategy-owned duplicate suppression or checkpoint persistence that overrides caller ownership.
- Any hidden progress state outside existing checkpoint/state abstractions.

---

## Phase 1: Setup (Shared Test Scaffolding)

**Purpose**: Establish acceptance-test scaffolding and shared test context before implementation.

- [X] T001 Create `features/platform/checkpointing/resumable-batching-cursor.feature` with baseline scenarios mapped from `specs/020-resumable-batching-cursor/spec.md` user stories.
- [X] T002 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Checkpointing/ResumableBatchingCursorContext.cs` with shared scenario state for resume token, fingerprint, and caller checkpoint cadence.
- [X] T003 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Checkpointing/ResumableBatchingCursorSteps.cs` with intentionally failing Reqnroll bindings for initial scenarios in `features/platform/checkpointing/resumable-batching-cursor.feature`.

**Checkpoint**: Acceptance scaffolding exists and fails before production changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Introduce shared contracts and primitives required by all user stories.

**CRITICAL**: User story implementation starts only after this phase is complete.

- [X] T004 Create `src/DevOpsMigrationPlatform.Abstractions/Models/BatchContinuationToken.cs` with primary key tuple, fallback token/checksum, query fingerprint, and completion marker.
- [X] T005 Create `src/DevOpsMigrationPlatform.Abstractions/Models/ResumeDecision.cs` and `src/DevOpsMigrationPlatform.Abstractions/Models/ResumeDecisionStatus.cs` for `Accepted`, `RejectedQueryMismatch`, and `Unavailable` outcomes.
- [X] T006 Extend `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemFetchScope.cs` with optional caller resume inputs (resume-enabled flag, saved token, caller duplicate-handling metadata).
- [X] T007 Extend `src/DevOpsMigrationPlatform.Abstractions/Services/WorkItemQueryWindow.cs` (`WorkItemQueryWindowOptions`) to carry deterministic ordering/resume compatibility inputs without regressing default behavior.
- [X] T008 Create `src/DevOpsMigrationPlatform.Abstractions/Services/IQueryFingerprintService.cs` for deterministic query fingerprint generation from enumeration query text and parameters.
- [X] T009 Create `src/DevOpsMigrationPlatform.Infrastructure/Services/QueryFingerprintService.cs` implementing `IQueryFingerprintService` with stable hashing that excludes post-fetch filters.
- [X] T010 Register `IQueryFingerprintService` in `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformServiceExtensions.cs`.
- [X] T011 [P] Add abstraction contract tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Checkpointing/ResumableBatchingContractTests.cs` for token validity and `ResumeDecision` transition invariants.

**Checkpoint**: Shared contract layer compiles with deterministic resume primitives.

---

## Phase 3: User Story 1 - Resume Long-Running Iteration Safely (Priority: P1) 🎯 MVP

**Goal**: Resume near the saved continuation position when resume is enabled.

**Independent Test**: Interrupt a long enumeration, restart with resume enabled, and verify traversal continues from the saved continuation point.

### Tests First (Must fail before implementation)

- [X] T012 [US1] Expand `features/platform/checkpointing/resumable-batching-cursor.feature` with US1 scenarios for resume-from-token and no-token-unavailable handling.
- [X] T013 [P] [US1] Add failing resume-start tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/WorkItemQueryWindowStrategyTests.cs`.
- [X] T014 [P] [US1] Add failing checkpoint-emission tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs`.

### Implementation

- [ ] T015 [US1] Implement continuation-start behavior in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` using caller-supplied token position.
- [ ] T016 [US1] Propagate resume inputs and checkpoint event emission through `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs`.
- [ ] T017 [US1] Wire caller-owned checkpoint persistence integration in `src/DevOpsMigrationPlatform.Infrastructure/Checkpointing/CheckpointingService.cs` for emitted continuation snapshots.
- [ ] T018 [US1] Emit mandatory completion checkpoint at end-of-stream in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs`.

**Checkpoint**: Resume-enabled callers continue from saved position; no-token paths remain deterministic.

---

## Phase 4: User Story 2 - Prevent Unsafe Resume After Query Changes (Priority: P1)

**Goal**: Reject continuation when query fingerprint changes and return deterministic `ResumeDecision` to caller.

**Independent Test**: Persist token with fingerprint H1, rerun with H2, verify mismatch decision is returned and caller policy is respected.

### Tests First (Must fail before implementation)

- [ ] T019 [US2] Expand `features/platform/checkpointing/resumable-batching-cursor.feature` with US2 query-mismatch acceptance scenarios.
- [ ] T020 [P] [US2] Add failing fingerprint mismatch tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/WorkItemQueryWindowStrategyTests.cs`.
- [ ] T021 [P] [US2] Add failing caller-policy tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` for fail/fresh-start decision handling.

### Implementation

- [ ] T022 [US2] Enforce query fingerprint compatibility checks in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` and return `ResumeDecision` outcomes instead of implicit reset.
- [ ] T023 [US2] Apply caller-owned mismatch recovery integration in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs`.
- [ ] T024 [US2] Apply caller-owned mismatch recovery integration in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemDiscoveryService.cs`.
- [ ] T025 [US2] Add resume decision observability (`accepted`, `rejected`, `unavailable`) in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs` and `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs`.

**Checkpoint**: Query changes cannot silently reuse stale continuation tokens.

---

## Phase 5: User Story 3 - Handle Duplicates and Data Drift Predictably (Priority: P2)

**Goal**: Preserve deterministic oldest-first traversal and caller-owned duplicate handling under source drift.

**Independent Test**: Resume after source mutations and verify no missed items attributable to resume ordering, while duplicate-safe caller policy preserves correctness.

### Tests First (Must fail before implementation)

- [ ] T026 [US3] Expand `features/platform/checkpointing/resumable-batching-cursor.feature` with US3 drift and duplicate-tolerance scenarios.
- [ ] T027 [P] [US3] Add failing deterministic-order tests (ChangedDate ASC, WorkItemId ASC) in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/WorkItemQueryWindowStrategyTests.cs`.
- [ ] T028 [P] [US3] Add failing duplicate-tolerant caller tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` and `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/InventoryServiceTests.cs`.

### Implementation

- [ ] T029 [US3] Update WIQL ordering and resume-key comparisons to deterministic oldest-first traversal in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs`.
- [ ] T030 [US3] Propagate deterministic ordering and continuation semantics in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemRevisionSource.cs` and `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/TfsWorkItemRevisionSource.cs`.
- [ ] T031 [US3] Preserve caller-owned duplicate handling boundaries in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs` and `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` (no strategy-level dedup).

**Checkpoint**: Drift-safe deterministic continuation works without internal dedup side effects.

---

## Phase 6: Documentation Sync (MANDATORY)

**Purpose**: Resolve all discrepancies and align canonical docs with implementation.

- [ ] T032 Update `docs/work-item-iteration-pattern.md` with resumable batching token semantics, caller responsibilities, and non-resume backward compatibility.
- [ ] T033 Update `.agents/context/checkpointing.md` with query-fingerprint compatibility and mismatch decision behavior for resumable batching.
- [ ] T034 Update `docs/configuration.md` with caller persistence cadence, duplicate handling expectations, and safe restart guidance for resumable batching.
- [ ] T035 Mark all items in `specs/020-resumable-batching-cursor/discrepancies.md` as `Resolved` or `N/A` with task references.
- [ ] T036 Review and update `analysis/pending-actions.md` to remove entries satisfied by `specs/020-resumable-batching-cursor` implementation.

---

## Phase 7: Guardrail Validation Gates (MANDATORY)

**Purpose**: Prove build, test, and scenario execution compliance before closure.

- [ ] T037 Run `dotnet clean && dotnet build --no-incremental` from `DevOpsMigrationPlatform.slnx` and record pass status in `specs/020-resumable-batching-cursor/tasks.md` checklist progress.
- [ ] T038 Run `dotnet test` from `DevOpsMigrationPlatform.slnx` and record pass status in `specs/020-resumable-batching-cursor/tasks.md` checklist progress.
- [ ] T039 Run a scenario via `.vscode/launch.json` using `scenarios/queue-export-ado-workitems-single-project.json` and verify observable output (resume decision telemetry and checkpoint progression).

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (Phase 1): starts immediately.
- Foundational (Phase 2): depends on Phase 1 completion; blocks all user stories.
- User Story phases (Phases 3-5): depend on Phase 2 completion.
- Documentation Sync (Phase 6): depends on completion of implemented user stories.
- Guardrail Validation Gates (Phase 7): depend on code and documentation completion.

### User Story Dependencies

- US1 (Phase 3): no dependency on other user stories; MVP.
- US2 (Phase 4): independent from US1 implementation but shares foundational contracts.
- US3 (Phase 5): depends on foundational contracts and should run after US1/US2 resume contract behavior is stable.

### Within Each User Story

- Acceptance scenarios and failing tests must be created before implementation tasks.
- Query/window strategy updates must land before caller integrations.
- Caller integrations complete before observability and completion-checkpoint finalization.

---

## Parallel Opportunities

- T002 and T003 can run in parallel.
- T011 can run in parallel with T009 and T010 after T004-T008 are in place.
- T013 and T014 can run in parallel.
- T020 and T021 can run in parallel.
- T027 and T028 can run in parallel.
- T032-T034 can run in parallel.

---

## Parallel Example: User Story 1

- Run T013 and T014 together to establish failing tests in separate files.
- After T015 lands, run T016 and T017 in parallel because they affect different services.

---

## Implementation Strategy

### MVP First

1. Complete Phases 1-2.
2. Complete Phase 3 (US1) and validate interruption/resume behavior.
3. Run targeted tests before expanding to mismatch and drift behavior.

### Incremental Delivery

1. Add US2 query-fingerprint safety and caller mismatch policy handling.
2. Add US3 deterministic drift behavior and duplicate-tolerance integration.
3. Complete doc sync and mandatory validation gates.

### Completion Rule

A phase is not complete until its checklist items are all checked and all mandatory validation gates in Phase 7 pass.
