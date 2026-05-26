# Tasks: Work Item Orchestrator and Resolution Architecture Alignment

**Input**: Design documents from `/specs/037-workitem-resolution-arch/`

**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/, quickstart.md

**Tests**: Included (feature spec defines independent test criteria and deterministic/runtime-verifiable outcomes).

**Organization**: Tasks are grouped by user story for independent implementation and validation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: User story label (`[US1]`, `[US2]`, `[US3]`)
- Every task includes explicit file path(s)

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare feature work artifacts and baseline wiring for task execution.

- [X] T001 Align feature documentation chain in `specs/037-workitem-resolution-arch/{spec.md,plan.md,research.md,data-model.md,quickstart.md}`
- [X] T002 [P] Create/update task execution notes in `specs/037-workitem-resolution-arch/contracts/{orchestrator-shape-contract.md,workitems-resolution-service-contract.md}` for implementation traceability
- [X] T003 [P] Add/refresh feature linkage in `AGENTS.md` (`SPECKIT START/END` section) to `specs/037-workitem-resolution-arch/plan.md`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Establish architecture guardrails and abstraction standards required by all stories.

**⚠️ CRITICAL**: No user story implementation starts before this phase is complete.

- [X] T004 Codify canonical runtime chain wording in `.agents/20-guardrails/domains/module-rules.md`
- [X] T005 [P] Codify orchestrator first-class contract semantics in `.agents/10-contracts/specs/orchestrator-contract.md`
- [X] T006 [P] Align seam/surface catalogs for orchestration in `.agents/10-contracts/{surface-catalog.yaml,seam-catalog.yaml}`
- [X] T007 [P] Align context docs for module/orchestrator split in `.agents/30-context/domains/{module-model.md,orchestrator-model.md}`
- [X] T008 Add drift reject criteria for module wrapper/orchestrator boundary in `.agents/20-guardrails/domains/module-rules.md`
- [X] T009 Validate `.agents` consistency sweep, confirm Class C consent/evidence gate for abstraction-shape changes, and remove contradictory phrasing in `.agents/**/*` references touched by T004-T008

**Checkpoint**: Guardrail and contract baseline is stable for feature implementation.

---

## Phase 3: User Story 1 - Deterministic Resolution Flow (Priority: P1) 🎯 MVP

**Goal**: Make WorkItems Import deterministic and resumable through a single orchestrated resolution flow.

**Independent Test**: Run import with mixed mapped/unmapped revisions; verify deterministic create/update outcomes and resume correctness.

### Tests for User Story 1

- [ ] T010 [P] [US1] Add/extend unit tests for deterministic sequence enforcement in `tests/DevOpsMigrationPlatform.Tests/Agent/Import/WorkItemsOrchestrator*Tests.cs`
- [ ] T011 [P] [US1] Add/extend system simulated test for mixed mapped/unmapped revision behavior and stale-mapping recovery in `tests/DevOpsMigrationPlatform.SystemTests/WorkItems/Import/*Deterministic*Tests.cs`
- [ ] T012 [US1] Add/extend resume stage transition test coverage in `tests/DevOpsMigrationPlatform.Tests/Agent/Import/RevisionFolderProcessor*Tests.cs`

### Implementation for User Story 1

- [ ] T013 [US1] Implement/complete shared resolution lifecycle service in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemResolutionService.cs`
- [ ] T014 [US1] Refactor `WorkItemsOrchestrator` to delegate resolution lifecycle to shared service in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemsOrchestrator.cs`
- [ ] T015 [US1] Refactor revision processor flow to resolve through shared service before create/update/replay in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs`
- [ ] T016 [US1] Ensure mandatory stage markers are emitted for import flow in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/{WorkItemsOrchestrator.cs,RevisionFolderProcessor.cs}`
- [ ] T017 [US1] Wire checkpoint/stage progression consistency for deterministic resume in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/{WorkItemsOrchestrator.cs,ImportWorkItemStateStore.cs}`
- [ ] T036 [US1] Preserve existing field/node default policy behavior in deterministic resolution flow in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/{WorkItemResolutionService.cs,RevisionFolderProcessor.cs}`
- [ ] T037 [P] [US1] Add regression tests for field/node default policy preservation in `tests/DevOpsMigrationPlatform.Tests/Agent/Import/*DefaultPolicy*Tests.cs`
- [ ] T040 [US1] Rename/split revision processor to `WorkItemImportRevisionProcessor` and align call sites in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/{RevisionFolderProcessor.cs,WorkItemImportRevisionProcessor.cs,WorkItemsOrchestrator.cs}`
- [ ] T042 [US1] Make startup policy assembly explicit in `WorkItemsOrchestrator` before revision dispatch in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemsOrchestrator.cs`

**Checkpoint**: User Story 1 is independently deterministic and resumable.

---

## Phase 4: User Story 2 - Connector Boundary Consistency (Priority: P2)

**Goal**: Ensure shared cache/lifecycle behavior remains in orchestrated shared logic while adapter-side code remains lookup/query focused.

**Independent Test**: Verify import behavior executes shared lifecycle policies while adapter-side paths only provide lookup/query mechanics.

### Tests for User Story 2

- [X] T018 [P] [US2] Add architecture boundary tests for WorkItems module/orchestrator split in `tests/DevOpsMigrationPlatform.Tests/Architecture/WorkItemsOrchestrationBoundaryTests.cs`
- [X] T019 [P] [US2] Add tests to assert no inline concrete WorkItems export orchestrator construction in module wrapper in `tests/DevOpsMigrationPlatform.Tests/Agent/Modules/WorkItemsModule*Tests.cs`

### Implementation for User Story 2

- [X] T020 [US2] Introduce single orchestration abstraction contract `IWorkItemsOrchestrator` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IWorkItemsOrchestrator.cs`
- [X] T021 [US2] Refactor `WorkItemsModule` to consume only `IWorkItemsOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
- [X] T022 [US2] Register single `IWorkItemsOrchestrator` abstraction in DI wiring in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleServiceCollectionExtensions.cs`
- [X] T023 [US2] Keep inventory path layering explicit (orchestrator vs wrapper) by clarifying internal role naming/docs in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/{InventoryOrchestrator.cs,InventoryService.cs}`
- [ ] T043 [US2] Narrow `IWorkItemResolutionStrategy` usage to connector find/get behavior and strategy provenance semantics while keeping cache/idmap lifecycle in `WorkItemResolutionService` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Import/IWorkItemResolutionStrategy.cs` and `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemResolutionService.cs`

**Checkpoint**: Module wrapper and orchestration boundaries are consistent and enforceable.

---

## Phase 5: User Story 3 - Connector Parity for Resolution Modes (Priority: P3)

**Goal**: Provide equivalent resolution strategy orchestration across Simulated, AzureDevOpsServices, and TeamFoundationServer where APIs allow.

**Independent Test**: Run connector-specific flows and verify configured resolution modes use the same orchestration model without no-op fallback where supported.

### Tests for User Story 3

- [ ] T024 [P] [US3] Add/extend simulated parity tests for resolution modes in `tests/DevOpsMigrationPlatform.SystemTests/WorkItems/Import/Simulated/*Resolution*Tests.cs`
- [ ] T025 [P] [US3] Add/extend AzureDevOps parity tests in `tests/DevOpsMigrationPlatform.SystemTests.AzureDevOps/WorkItems/Import/*Resolution*Tests.cs`
- [ ] T026 [P] [US3] Add/extend TFS parity tests in `tests/DevOpsMigrationPlatform.SystemTests/WorkItems/Import/Tfs/*Resolution*Tests.cs`
- [ ] T038 [P] [US3] Add deterministic multi-candidate tie-break tests across connectors in `tests/DevOpsMigrationPlatform.SystemTests/WorkItems/Import/*CandidateTieBreak*Tests.cs`
- [ ] T039 [P] [US3] Add mid-stream cache rebuild resume-equivalence tests across connectors in `tests/DevOpsMigrationPlatform.SystemTests/WorkItems/Import/*CacheRebuildResume*Tests.cs`

### Implementation for User Story 3

- [ ] T027 [US3] Implement/complete TFS resolution strategy path(s) for supported lookup modes in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Import/*ResolutionStrategy*.cs`
- [ ] T028 [US3] Align strategy factory routing across adapters in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemResolutionStrategyFactory.cs`
- [ ] T029 [US3] Remove/replace no-op strategy fallback where adapter capability exists in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/NullResolutionStrategy.cs`
- [ ] T030 [US3] Validate adapter parity behavior and warning semantics for unsupported capabilities in `src/DevOpsMigrationPlatform.Infrastructure.{AzureDevOps,Simulated,TfsObjectModel}/Import/*`

**Checkpoint**: All supported adapters follow equivalent orchestration for resolution modes.

---

## Final Phase: Polish & Cross-Cutting Concerns

**Purpose**: Finalize cross-cutting architecture, observability, docs, and verification.

- [ ] T031 [P] Update architecture documentation and context for final implemented shape in `.agents/10-contracts/specs/orchestrator-contract.md` and `.agents/30-context/domains/{module-model.md,orchestrator-model.md}`
- [ ] T032 [P] Update feature docs with delivered behavior and evidence links in `specs/037-workitem-resolution-arch/{spec.md,plan.md,research.md,quickstart.md}`
- [ ] T033 Run architecture compliance checks and capture outputs for this feature in `specs/037-workitem-resolution-arch/`
- [ ] T034 Run full relevant test suites for touched scope, execute baseline-vs-post-change comparison for SC-005, and capture evidence in `specs/037-workitem-resolution-arch/`
- [ ] T035 Confirm no remaining contradiction to canonical chain wording in `.agents/**/*` touched files
- [ ] T041 Verify screaming architecture naming for touched WorkItems runtime types and remove ambiguous role names in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/*` and `src/DevOpsMigrationPlatform.Abstractions.Agent/*`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: no dependencies
- **Phase 2 (Foundational)**: depends on Phase 1; blocks all user stories
- **Phase 3 (US1)**: depends on Phase 2; MVP
- **Phase 4 (US2)**: depends on Phase 2 and benefits from US1 completion
- **Phase 5 (US3)**: depends on Phase 2 and US1 shared resolution flow
- **Final Phase**: depends on all implemented user stories

### User Story Dependencies

- **US1 (P1)**: foundational dependency only; independent MVP
- **US2 (P2)**: foundational dependency; integrates with US1 orchestration outputs
- **US3 (P3)**: foundational dependency; relies on shared orchestration/resolution path from US1

### Within Each User Story

- Tests first, then implementation.
- Abstractions before module wiring.
- Shared orchestration changes before adapter parity rollout.

### Parallel Opportunities

- T002, T003 can run in parallel.
- T005, T006, T007 can run in parallel after T004.
- US1 tests T010/T011 parallel; then implementation sequence T013-T017.
- US2 tests T018/T019 parallel; implementation T020/T021/T022 can be partially parallel after abstraction signature is settled.
- US3 tests T024/T025/T026 parallel; adapter implementations can proceed in parallel after factory contract alignment.

---

## Parallel Example: User Story 3

```bash
# Parallel adapter tests
Task: "T024 [US3] Simulated resolution parity tests"
Task: "T025 [US3] AzureDevOps resolution parity tests"
Task: "T026 [US3] TFS resolution parity tests"

# Parallel adapter implementation tracks (after shared contract alignment)
Task: "Implement TFS strategy path(s)"
Task: "Align AzureDevOps strategy routing"
Task: "Align Simulated strategy routing"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Setup + Foundational.
2. Deliver US1 deterministic/resumable import flow.
3. Validate independent US1 behavior before moving on.

### Incremental Delivery

1. US1 establishes shared deterministic orchestration baseline.
2. US2 standardizes module/orchestrator boundary and abstraction parity.
3. US3 completes adapter parity across supported resolution modes.

### Parallel Team Strategy

1. Team A: foundational + US1 shared resolution lifecycle.
2. Team B: US2 abstraction parity and module wrapper standardization.
3. Team C: US3 adapter parity implementation/tests once US1 contracts settle.

---

## Notes

- All tasks follow strict checklist format with IDs and file paths.
- `[US1]/[US2]/[US3]` labels are used only in user story phases.
- Canonical architecture chain to preserve in implementation:
  `Module -> Orchestrator(s) -> Package + Adapter(s) + Strategy(s).`
