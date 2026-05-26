# Tasks: Test Project Lifecycle for Connector Tests

**Input**: Design documents from `/specs/036-test-project-lifecycle/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/project-lifecycle-contract.md

**Tests**: Included because the feature specification defines explicit user scenarios and independent test criteria.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no unresolved dependencies)
- **[Story]**: User story label (`[US1]`, `[US2]`, `[US3]`)
- All tasks include exact target file paths

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare feature artifacts and test scaffolding entry points.

- [X] T001 Create lifecycle feature file scaffold in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [X] T002 Create lifecycle scenario context scaffold in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleScenarioContext.cs`
- [X] T003 [P] Create lifecycle step bindings scaffold in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleSteps.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core contracts and orchestration infrastructure required by all stories.

**⚠️ CRITICAL**: Complete this phase before user story work.

- [X] T004 Define lifecycle seam contract in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/IProjectLifecycleService.cs`
- [X] T005 Define lifecycle record and status types in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/ProjectLifecycleRecord.cs`
- [X] T006 Define lifecycle eligibility marker contract in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/LifecycleEligibilityFlag.cs`
- [X] T007 Implement connector-dispatch lifecycle orchestrator in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/CompositeProjectLifecycleService.cs`
- [X] T008 Wire foundational lifecycle DI registrations in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/FactoryRegistrationExtensions.cs`
- [X] T009 [P] Implement run-correlated project naming helper in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/ProjectLifecycleNameGenerator.cs`
- [X] T010 [P] Add foundational lifecycle contract tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/CompositeProjectLifecycleServiceTests.cs`
- [X] T011 [P] Add architecture boundary guard test to confirm no migration runtime/package behavior changes in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleArchitectureBoundaryTests.cs`

**Checkpoint**: Foundational lifecycle seam is in place; story implementation can proceed.

---

## Phase 3: User Story 1 - Automatic Project Setup and Cleanup (Priority: P1) 🎯 MVP

**Goal**: Eligible tests create an isolated project before execution and tear it down after successful completion.

**Independent Test**: Run an eligible connector test with no pre-existing project and verify it creates, uses, and deletes a run-specific project.

### Tests for User Story 1

- [X] T012 [P] [US1] Add success-path acceptance scenarios in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [X] T013 [P] [US1] Implement acceptance step definitions for setup/cleanup success in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleSteps.cs`
- [X] T014 [P] [US1] Add Simulated connector lifecycle success tests in `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/ProjectLifecycle/SimulatedProjectLifecycleServiceTests.cs`
- [X] T015 [P] [US1] Add Azure DevOps connector lifecycle success tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/AzureDevOpsProjectLifecycleServiceTests.cs`
- [X] T016 [P] [US1] Add parallel-run naming collision test for run-correlated identity in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleNameGeneratorTests.cs`
- [X] T017 [P] [US1] Add execution-context binding assertion test in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestContextTests.cs`

### Implementation for User Story 1

- [X] T018 [US1] Implement Simulated lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/ProjectLifecycle/SimulatedProjectLifecycleService.cs`
- [X] T019 [US1] Implement Azure DevOps lifecycle adapter create/delete flow in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ProjectLifecycle/AzureDevOpsProjectLifecycleService.cs`
- [X] T020 [US1] Register Simulated lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs`
- [X] T021 [US1] Register Azure DevOps lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ImportServiceCollectionExtensions.cs`
- [X] T022 [US1] Integrate lifecycle setup/teardown and execution project binding in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestContext.cs`

**Checkpoint**: User Story 1 is independently testable and delivers MVP value.

---

## Phase 4: User Story 2 - Guaranteed Cleanup on Failure (Priority: P2)

**Goal**: Cleanup is attempted and recorded even when test execution fails after project creation.

**Independent Test**: Run an eligible test that fails mid-run and verify teardown still executes with visible outcome.

### Tests for User Story 2

- [X] T023 [P] [US2] Add failure-path cleanup scenarios in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [X] T024 [P] [US2] Add harness-level cleanup-on-failure tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestContextTests.cs`
- [X] T025 [P] [US2] Add lifecycle record failure outcome tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleRecordTests.cs`
- [X] T026 [P] [US2] Add fail-fast creation failure test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/AzureDevOpsProjectLifecycleServiceTests.cs`
- [X] T027 [P] [US2] Add teardown safety test to block deletion of foreign projects in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/CompositeProjectLifecycleServiceTests.cs`
- [X] T028 [P] [US2] Add permission-denied and partial-cleanup visibility tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleRecordTests.cs`
- [X] T029 [P] [US2] Add readiness delay and teardown-latency assertion tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/AzureDevOpsProjectLifecycleServiceTests.cs`

### Implementation for User Story 2

- [X] T030 [US2] Implement guaranteed teardown execution path in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/CompositeProjectLifecycleService.cs`
- [X] T031 [US2] Implement fail-fast project creation error propagation in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ProjectLifecycle/AzureDevOpsProjectLifecycleService.cs`
- [X] T032 [US2] Implement teardown blocking-reason and partial-cleanup capture in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/ProjectLifecycleRecord.cs`
- [X] T033 [US2] Implement foreign-project teardown protection in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/CompositeProjectLifecycleService.cs`
- [X] T034 [US2] Implement readiness retry/timeout policy in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ProjectLifecycle/AzureDevOpsProjectLifecycleService.cs`
- [X] T035 [US2] Implement teardown latency measurement output in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/ProjectLifecycleProgressEmitter.cs`

**Checkpoint**: User Stories 1 and 2 are independently functional and resilient to failure paths.

---

## Phase 5: User Story 3 - Connector-Specific Eligibility and Visibility (Priority: P3)

**Goal**: Lifecycle behavior applies to Azure DevOps and TFS test flows with explicit run-time visibility.

**Independent Test**: Execute one Azure DevOps and one TFS eligible run and verify lifecycle outcomes are attributable and visible.

### Tests for User Story 3

- [X] T036 [P] [US3] Add connector-eligibility scenarios for Azure DevOps and TFS in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [X] T037 [P] [US3] Add TFS lifecycle adapter tests in `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/ProjectLifecycle/TfsProjectLifecycleServiceTests.cs`
- [X] T038 [P] [US3] Add lifecycle visibility assertion tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/ProjectLifecycle/ProjectLifecycleVisibilityTests.cs`
- [X] T039 [P] [US3] Add connector parity lifecycle record tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/CompositeProjectLifecycleServiceTests.cs`

### Implementation for User Story 3

- [X] T040 [US3] Implement TFS lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/ProjectLifecycle/TfsProjectLifecycleService.cs`
- [X] T041 [US3] Register TFS lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/TfsJobServiceFactory.cs`
- [X] T042 [US3] Implement eligibility evaluation in test pipeline in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestBase.cs`
- [X] T043 [US3] Implement structured lifecycle outcome emission and correlation fields in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/ProjectLifecycleProgressEmitter.cs`

**Checkpoint**: All user stories independently deliver and connector visibility is complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Hardening, documentation, and rollout measurement coverage.

- [X] T044 [P] Update operator/testing documentation for lifecycle-enabled tests in `docs/testing-guide.md`
- [X] T045 [P] Update connector development guidance for lifecycle parity expectations in `docs/connector-development-guide.md`
- [X] T046 [P] Update telemetry context for lifecycle outcome visibility and latency tracking in `.agents/30-context/domains/telemetry-model.md`
- [X] T047 Update quickstart alignment with final task flow in `specs/036-test-project-lifecycle/quickstart.md`
- [X] T048 Define SC-004 measurement plan for cleanup-intervention reduction in `docs/testing-guide.md`
- [X] T049 Add cleanup-intervention metric collection script for rollout reporting in `scripts/project-lifecycle/collect-cleanup-metrics.ps1`

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup): start immediately
- Phase 2 (Foundational): depends on Phase 1 and blocks all user stories
- Phase 3 (US1): depends on Phase 2
- Phase 4 (US2): depends on Phase 3 adapter baseline for failure-path extensions
- Phase 5 (US3): depends on Phase 2 and shared lifecycle seam
- Phase 6 (Polish): depends on completion of desired user stories

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories after foundational phase
- **US2 (P2)**: extends US1 behavior with guaranteed cleanup and failure handling
- **US3 (P3)**: adds TFS parity and cross-connector visibility over shared seam

### Within Each User Story

- Tests are sequenced before implementation tasks
- Safety and error-path tests are explicit before behavior changes
- Story checkpoint must be reached before moving to next priority

---

## Parallel Execution Opportunities

- Phase 1: T002 and T003 can run together after T001
- Phase 2: T009, T010, and T011 can run in parallel after T004-T008
- US1: T014, T015, T016, and T017 can run in parallel
- US2: T024-T029 are parallelizable test tasks across distinct files
- US3: T037-T039 are parallelizable test tasks
- Polish: T044, T045, T046, and T048 can run in parallel

## Parallel Example: User Story 2

```bash
Task: "T026 [US2] Add fail-fast creation failure test in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/AzureDevOpsProjectLifecycleServiceTests.cs"
Task: "T027 [US2] Add teardown safety test to block deletion of foreign projects in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/CompositeProjectLifecycleServiceTests.cs"
Task: "T028 [US2] Add permission-denied and partial-cleanup visibility tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleRecordTests.cs"
```

## Parallel Example: User Story 3

```bash
Task: "T037 [US3] Add TFS lifecycle adapter tests in tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/ProjectLifecycle/TfsProjectLifecycleServiceTests.cs"
Task: "T038 [US3] Add lifecycle visibility assertion tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/ProjectLifecycle/ProjectLifecycleVisibilityTests.cs"
Task: "T040 [US3] Implement TFS lifecycle adapter in src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/ProjectLifecycle/TfsProjectLifecycleService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 and Phase 2
2. Deliver Phase 3 (US1) end-to-end
3. Validate US1 independent test criteria before expanding scope

### Incremental Delivery

1. US1 (setup/cleanup happy path + collision-safe identity + binding)
2. US2 (cleanup-on-failure + teardown safety + fail-fast behavior + readiness/latency handling)
3. US3 (connector eligibility + TFS parity + visibility)
4. Polish/documentation + rollout measurement coverage

### Team Parallelization

1. Team completes Setup + Foundational together
2. Split by story lane after foundational checkpoint
3. Merge for cross-cutting polish and rollout metrics tasks
