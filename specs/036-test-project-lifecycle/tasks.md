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

- [ ] T001 Create lifecycle feature file scaffold in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [ ] T002 Create lifecycle scenario context scaffold in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleScenarioContext.cs`
- [ ] T003 [P] Create lifecycle step bindings scaffold in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleSteps.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core contracts and orchestration infrastructure required by all stories.

**⚠️ CRITICAL**: Complete this phase before user story work.

- [ ] T004 Define lifecycle seam contract in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/IProjectLifecycleService.cs`
- [ ] T005 Define lifecycle record and status types in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/ProjectLifecycleRecord.cs`
- [ ] T006 Define lifecycle eligibility marker contract in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/LifecycleEligibilityFlag.cs`
- [ ] T007 Implement connector-dispatch lifecycle orchestrator in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/CompositeProjectLifecycleService.cs`
- [ ] T008 Wire foundational lifecycle DI registrations in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/FactoryRegistrationExtensions.cs`
- [ ] T009 [P] Implement run-correlated project naming helper in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/ProjectLifecycleNameGenerator.cs`
- [ ] T010 [P] Add foundational lifecycle contract tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/CompositeProjectLifecycleServiceTests.cs`

**Checkpoint**: Foundational lifecycle seam is in place; story implementation can proceed.

---

## Phase 3: User Story 1 - Automatic Project Setup and Cleanup (Priority: P1) 🎯 MVP

**Goal**: Eligible tests create an isolated project before execution and tear it down after successful completion.

**Independent Test**: Run an eligible connector test with no pre-existing project and verify it creates, uses, and deletes a run-specific project.

### Tests for User Story 1

- [ ] T011 [P] [US1] Add success-path acceptance scenarios in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [ ] T012 [P] [US1] Implement acceptance step definitions for setup/cleanup success in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleSteps.cs`
- [ ] T013 [P] [US1] Add Simulated connector lifecycle tests in `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/ProjectLifecycle/SimulatedProjectLifecycleServiceTests.cs`
- [ ] T014 [P] [US1] Add Azure DevOps connector lifecycle success tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/AzureDevOpsProjectLifecycleServiceTests.cs`

### Implementation for User Story 1

- [ ] T015 [US1] Implement Simulated lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/ProjectLifecycle/SimulatedProjectLifecycleService.cs`
- [ ] T016 [US1] Implement Azure DevOps lifecycle adapter create/delete flow in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ProjectLifecycle/AzureDevOpsProjectLifecycleService.cs`
- [ ] T017 [US1] Register Simulated lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs`
- [ ] T018 [US1] Register Azure DevOps lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ImportServiceCollectionExtensions.cs`
- [ ] T019 [US1] Integrate lifecycle setup/teardown into system test context in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestContext.cs`

**Checkpoint**: User Story 1 is independently testable and delivers MVP value.

---

## Phase 4: User Story 2 - Guaranteed Cleanup on Failure (Priority: P2)

**Goal**: Cleanup is attempted and recorded even when test execution fails after project creation.

**Independent Test**: Run an eligible test that fails mid-run and verify teardown still executes with visible outcome.

### Tests for User Story 2

- [ ] T020 [P] [US2] Add failure-path cleanup scenarios in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [ ] T021 [P] [US2] Add harness-level cleanup-on-failure tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestContextTests.cs`
- [ ] T022 [P] [US2] Add lifecycle record failure outcome tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/ProjectLifecycleRecordTests.cs`

### Implementation for User Story 2

- [ ] T023 [US2] Implement guaranteed teardown execution path in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/CompositeProjectLifecycleService.cs`
- [ ] T024 [US2] Implement fail-fast project creation error propagation in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ProjectLifecycle/AzureDevOpsProjectLifecycleService.cs`
- [ ] T025 [US2] Implement teardown blocking-reason capture in `src/DevOpsMigrationPlatform.Abstractions.Agent/ProjectLifecycle/ProjectLifecycleRecord.cs`
- [ ] T026 [US2] Implement project-readiness retry/timeout policy in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ProjectLifecycle/AzureDevOpsProjectLifecycleService.cs`

**Checkpoint**: User Stories 1 and 2 are independently functional and resilient to failure paths.

---

## Phase 5: User Story 3 - Connector-Specific Eligibility and Visibility (Priority: P3)

**Goal**: Lifecycle behavior applies to Azure DevOps and TFS test flows with explicit run-time visibility.

**Independent Test**: Execute one Azure DevOps and one TFS eligible run and verify lifecycle outcomes are attributable and visible.

### Tests for User Story 3

- [ ] T027 [P] [US3] Add connector-eligibility scenarios for Azure DevOps and TFS in `features/platform/project-lifecycle/ephemeral-project-lifecycle.feature`
- [ ] T028 [P] [US3] Add TFS lifecycle adapter tests in `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/ProjectLifecycle/TfsProjectLifecycleServiceTests.cs`
- [ ] T029 [P] [US3] Add lifecycle visibility assertion tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/ProjectLifecycle/ProjectLifecycleVisibilityTests.cs`

### Implementation for User Story 3

- [ ] T030 [US3] Implement TFS lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/ProjectLifecycle/TfsProjectLifecycleService.cs`
- [ ] T031 [US3] Register TFS lifecycle adapter in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/TfsJobServiceFactory.cs`
- [ ] T032 [US3] Implement eligibility evaluation in test pipeline in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestBase.cs`
- [ ] T033 [US3] Implement structured lifecycle outcome emission in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ProjectLifecycle/ProjectLifecycleProgressEmitter.cs`

**Checkpoint**: All user stories independently deliver and connector visibility is complete.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Hardening, documentation, and final validation updates across stories.

- [ ] T034 [P] Update operator/testing documentation for lifecycle-enabled tests in `docs/testing-guide.md`
- [ ] T035 [P] Update connector development guidance for lifecycle parity expectations in `docs/connector-development-guide.md`
- [ ] T036 [P] Update telemetry context for lifecycle outcome visibility in `.agents/30-context/domains/telemetry-model.md`
- [ ] T037 Run quickstart alignment updates in `specs/036-test-project-lifecycle/quickstart.md`

---

## Dependencies & Execution Order

### Phase Dependencies

- Phase 1 (Setup): start immediately
- Phase 2 (Foundational): depends on Phase 1 and blocks all user stories
- Phase 3 (US1): depends on Phase 2
- Phase 4 (US2): depends on Phase 2 (can begin after US1 core seam tasks are present; preferred order US1 then US2)
- Phase 5 (US3): depends on Phase 2 (and on shared lifecycle seam from Phase 2)
- Phase 6 (Polish): depends on completion of desired user stories

### User Story Dependencies

- **US1 (P1)**: no dependency on other stories after foundational phase
- **US2 (P2)**: depends on shared lifecycle seam; extends US1 behavior for failure paths
- **US3 (P3)**: depends on shared lifecycle seam; adds TFS parity and visibility

### Within Each User Story

- Implement story tests first, then adapter/service code, then registrations/integration
- Complete independent story checkpoint before advancing

---

## Parallel Execution Opportunities

- Phase 1: T002 and T003 can run together after T001
- Phase 2: T009 and T010 are parallel after T004-T008 contracts are in place
- US1: T011-T014 are parallelizable; T015-T016 parallelizable on separate connector files
- US2: T020-T022 parallelizable; T024 and T026 share file and should be sequenced
- US3: T027-T029 parallelizable; T030 and T031 sequenced for TFS adapter + registration
- Polish: T034-T036 parallelizable

## Parallel Example: User Story 1

```bash
Task: "T013 [US1] Add Simulated connector lifecycle tests in tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/ProjectLifecycle/SimulatedProjectLifecycleServiceTests.cs"
Task: "T014 [US1] Add Azure DevOps connector lifecycle success tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/ProjectLifecycle/AzureDevOpsProjectLifecycleServiceTests.cs"
Task: "T016 [US1] Implement Azure DevOps lifecycle adapter create/delete flow in src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ProjectLifecycle/AzureDevOpsProjectLifecycleService.cs"
```

## Parallel Example: User Story 3

```bash
Task: "T028 [US3] Add TFS lifecycle adapter tests in tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/ProjectLifecycle/TfsProjectLifecycleServiceTests.cs"
Task: "T029 [US3] Add lifecycle visibility assertion tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/ProjectLifecycle/ProjectLifecycleVisibilityTests.cs"
Task: "T030 [US3] Implement TFS lifecycle adapter in src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/ProjectLifecycle/TfsProjectLifecycleService.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1)

1. Complete Phase 1 and Phase 2
2. Deliver Phase 3 (US1) end-to-end
3. Validate US1 independent test criteria before expanding scope

### Incremental Delivery

1. US1 (setup/cleanup happy path)
2. US2 (cleanup-on-failure guarantees)
3. US3 (connector eligibility + visibility parity)
4. Polish/documentation updates

### Team Parallelization

1. Team completes Setup + Foundational together
2. Then split: one stream on US2 hardening, one on US3 connector parity
3. Merge for polish and documentation updates

