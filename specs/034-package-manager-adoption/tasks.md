# Tasks: Package Manager Adoption

**Input**: Design documents from `specs/034-package-manager-adoption/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/package-boundary-contract.md`

**Tests**: Included. This feature explicitly requires behavioral coverage for routing, resume/phase safety, and connector parity.

**Organization**: Tasks are grouped by user story to enable independent implementation and validation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: User story mapping label (`[US1]`, `[US2]`, `[US3]`)
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare test and implementation scaffolding for package-boundary work.

- [ ] T001 Create package-boundary test folder structure under `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/`
- [ ] T002 [P] Add package-boundary fixture utilities in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryTestFixture.cs`
- [ ] T003 [P] Add package-boundary sample payload helpers in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackagePayloadBuilder.cs`
- [ ] T046 Add failing acceptance scenario coverage for package-boundary adoption in `features/platform/package-manager-adoption/package-boundary-adoption.feature`
- [ ] T047 Add failing acceptance step bindings for package-boundary adoption scenarios in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PackageBoundaryAdoptionSteps.cs`
- [ ] T053 Add foundational RED tests for `IPackage` contract and boundary routing behavior in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryContractRedTests.cs`

**Gate**: Execute T046-T047 and T053 as a failing baseline before beginning T004-T015 implementation tasks.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement core typed package contracts and baseline routing implementation required by all stories.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [ ] T004 Add `IPackage` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackage.cs`
- [ ] T005 [P] Add package context contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageContext.cs`
- [ ] T006 [P] Add metadata context contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageMetaContext.cs`
- [ ] T007 [P] Add log context contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageLogContext.cs`
- [ ] T008 [P] Add package payload contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackagePayload.cs`
- [ ] T009 [P] Add metadata payload contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageMetaPayload.cs`
- [ ] T010 [P] Add log payload contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageLogPayload.cs`
- [ ] T011 [P] Add package metadata kind enum in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageMetaKind.cs`
- [ ] T012 [P] Add package log stream enum in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageLogStream.cs`
- [ ] T059 Add guard test to enforce package-boundary contract/type placement in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryAbstractionsPlacementTests.cs` (contracts allowed in `src/DevOpsMigrationPlatform.Abstractions.Agent/` and disallowed in `src/DevOpsMigrationPlatform.Abstractions/`)
- [ ] T013 Implement package routing resolver in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackagePathRouter.cs`
- [ ] T014 Implement package boundary over stores in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageBoundary.cs`
- [ ] T015 Register package boundary and router in `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs`

**Checkpoint**: Typed package boundary exists and is injectable across runtime services.

---

## Phase 3: User Story 1 - Standardize Package Access (Priority: P1) 🎯 MVP

**Goal**: Route package content/metadata/log operations through one typed boundary instead of direct path composition in runtime flow code.

**Independent Test**: Run a migration flow and verify runtime package reads/writes/appends are issued through `IPackage`, with canonical package outputs unchanged.

### Tests for User Story 1

- [ ] T016 [P] [US1] Add package routing tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackagePathRouterTests.cs`
- [ ] T017 [P] [US1] Add package boundary persist/request tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryTests.cs`
- [ ] T018 [P] [US1] Add metadata mirroring tests (`RelatedToRun`) in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageMetaRoutingTests.cs`
- [ ] T019 [P] [US1] Add run-log append stream tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageLogAppendTests.cs`
- [ ] T048 [P] [US1] Add streaming behavior tests proving no global buffering/sorting in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageStreamingBehaviorTests.cs`
- [ ] T049 [P] [US1] Add fail-fast error contract tests with stable codes in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryErrorContractTests.cs`

### Implementation for User Story 1

- [ ] T020 [US1] Migrate package config operations to `IPackage` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageConfigStore.cs`
- [ ] T021 [US1] Migrate progress sink append path handling to `IPackage.AppendLogAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PackageProgressSink.cs`
- [ ] T022 [US1] Migrate diagnostics logger append path handling to `IPackage.AppendLogAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PackageLoggerProvider.cs`
- [ ] T023 [US1] Ensure `ActivePackageState` and package-boundary run context integration in `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/ActivePackageState.cs`
- [ ] T024 [US1] Add/adjust DI and constructor wiring for updated services in `src/DevOpsMigrationPlatform.Infrastructure.Agent/DiagnosticsServiceExtensions.cs`

**Checkpoint**: Runtime config + progress + diagnostics package operations are centralized through package boundary APIs.

---

## Phase 4: User Story 2 - Preserve Deterministic Resume and Phase Gates (Priority: P1)

**Goal**: Keep resume and phase-gate semantics unchanged while migrating orchestration/checkpoint paths to typed package intents.

**Independent Test**: Interrupt and resume jobs; verify identical cursor/stage progression and phase-gate behavior compared to baseline.

### Tests for User Story 2

- [ ] T025 [P] [US2] Add checkpoint cursor routing/resolution tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/CheckpointingServiceTests.cs`
- [ ] T026 [P] [US2] Add plan persistence authority tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs`
- [ ] T027 [P] [US2] Add plan executor persistence tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs`
- [ ] T028 [P] [US2] Add phase tracking no-regression tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/PhaseTrackingServiceTests.cs`
- [ ] T054 [P] [US2] Add legacy package-state path resume compatibility tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/LegacyStateResumeCompatibilityTests.cs`

### Implementation for User Story 2

- [ ] T029 [US2] Migrate checkpoint cursor and continuation token persistence to package boundary in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Checkpointing/CheckpointingService.cs`
- [ ] T030 [US2] Migrate execution-plan authoritative/run-audit writes to package boundary in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
- [ ] T031 [US2] Migrate per-task plan persistence to package boundary in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs`
- [ ] T032 [US2] Align phase-record persistence with package boundary metadata routing in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Checkpointing/PhaseTrackingService.cs`

**Checkpoint**: Resume checkpoints and phase gates remain deterministic and unchanged after package-boundary adoption.

---

## Phase 5: User Story 3 - Ensure Cross-Connector Consistency (Priority: P2)

**Goal**: Ensure package-boundary behavior is consistent across Simulated, Azure DevOps Services, and TFS runtime paths.

**Independent Test**: Run connector-specific flows and verify equivalent package boundary semantics and observable outputs.

### Tests for User Story 3

- [ ] T033 [P] [US3] Add cross-connector parity tests for package-boundary semantics in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorParityTests.cs`
- [ ] T050 [P] [US3] Add Azure DevOps Services package-boundary behavior tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/AdoPackageBoundaryIntegrationTests.cs`
- [ ] T034 [P] [US3] Add TFS worker package-boundary behavior tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/TfsPackageBoundaryIntegrationTests.cs`
- [ ] T035 [P] [US3] Add simulated-system coverage for package-boundary routing in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs`
- [ ] T055 [P] [US3] Add explicit unsupported-capability guardrail error tests per connector in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorLimitationsTests.cs`

### Implementation for User Story 3

- [ ] T036 [US3] Migrate TFS job worker package operations to package boundary where applicable in `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs`
- [ ] T037 [US3] Migrate Nodes orchestrator package path composition seams to package intents in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs`
- [ ] T038 [US3] Migrate Teams orchestrator package path composition seams to package intents in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsOrchestrator.cs`
- [ ] T039 [US3] Migrate Identities orchestrator package path composition seams to package intents in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesOrchestrator.cs`

**Checkpoint**: Package manager behavior is consistent across connector execution paths and no connector is left on legacy package access for covered surfaces.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Finalize documentation, observability verification, and full-suite validation.

- [ ] T040 [P] Update package-boundary architecture context in `.agents/context/package-manager.md`
- [ ] T041 [P] Update package format/routing docs in `.agents/context/migration-package-concept.md`
- [ ] T042 [P] Update operator/developer docs for package-boundary usage in `docs/architecture.md`
- [ ] T043 [P] Update implementation guidance in `docs/module-development-guide.md`
- [ ] T044 Verify observability instrumentation coverage for package-boundary operations in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageBoundary.cs`
- [ ] T045 Run full build/test validation from quickstart in `specs/034-package-manager-adoption/quickstart.md`
- [ ] T051 Add measurable observability assertions (span, metric, structured log fields) in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryObservabilityTests.cs`
- [ ] T052 Document and justify permitted direct low-level persistence internals (FR-008 scope exceptions) in `.agents/context/package-manager.md`
- [ ] T056 Add active-run log rotation continuity tests for diagnostics/progress streams in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageLogRotationContinuityTests.cs`
- [ ] T057 Add runtime package-access audit test enforcing boundary-only writes with explicit allowlist in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryAdoptionAuditTests.cs`
- [ ] T058 Add failure-path observability tests for structured error logs with correlation fields in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryErrorObservabilityTests.cs`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies
- **Phase 2 (Foundational)**: Depends on Phase 1 and the failing acceptance baseline gate (T046-T047, T053); blocks all user stories
- **Phase 3 (US1)**: Depends on Phase 2
- **Phase 4 (US2)**: Depends on Phase 2
- **Phase 5 (US3)**: Depends on Phase 2 and should start after US1 and US2 stabilize shared boundary behavior
- **Phase 6 (Polish)**: Depends on completion of all user story phases

### User Story Dependencies

- **US1 (P1)**: Starts after foundational phase; baseline MVP
- **US2 (P1)**: Starts after foundational phase; can run in parallel with US1
- **US3 (P2)**: Depends on stable shared package boundary and should follow US1/US2 completion

### Within Each User Story

- Story tests are implemented first, then runtime migrations, then story-level validation.
- Parallel tasks `[P]` must target independent files and avoid incomplete dependencies.

### Parallel Opportunities

- Foundational contract files (`T005`-`T012`) can run in parallel.
- Contract placement guard (`T059`) can run in parallel with foundational contract files.
- US1 tests (`T016`-`T019`) can run in parallel.
- US1 additional behavior tests (`T048`-`T049`) can run in parallel with other US1 test tasks.
- US2 tests (`T025`-`T028`) can run in parallel.
- US2 additional compatibility tests (`T054`) can run in parallel with other US2 test tasks.
- US3 tests (`T033`-`T035`, `T050`) can run in parallel.
- US3 connector limitation tests (`T055`) can run in parallel with other US3 test tasks.
- Documentation updates (`T040`-`T043`) can run in parallel.
- Observability/log continuity tests (`T051`, `T056`) can run in parallel.
- Boundary-adoption and error-observability audit tests (`T057`, `T058`) can run in parallel with other polish tests.

---

## Parallel Example: User Story 1

```text
Task: "T016 [US1] Add package routing tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackagePathRouterTests.cs"
Task: "T017 [US1] Add package boundary persist/request tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryTests.cs"
Task: "T018 [US1] Add metadata mirroring tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageMetaRoutingTests.cs"
Task: "T019 [US1] Add run-log append stream tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageLogAppendTests.cs"
Task: "T048 [US1] Add streaming behavior tests proving no global buffering/sorting in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageStreamingBehaviorTests.cs"
Task: "T049 [US1] Add fail-fast error contract tests with stable codes in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryErrorContractTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T025 [US2] Add checkpoint cursor routing/resolution tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/CheckpointingServiceTests.cs"
Task: "T026 [US2] Add plan persistence authority tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs"
Task: "T027 [US2] Add plan executor persistence tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs"
Task: "T028 [US2] Add phase tracking no-regression tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/PhaseTrackingServiceTests.cs"
Task: "T054 [US2] Add legacy package-state path resume compatibility tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/LegacyStateResumeCompatibilityTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T033 [US3] Add connector parity tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorParityTests.cs"
Task: "T050 [US3] Add Azure DevOps Services behavior tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/AdoPackageBoundaryIntegrationTests.cs"
Task: "T034 [US3] Add TFS worker behavior tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/TfsPackageBoundaryIntegrationTests.cs"
Task: "T035 [US3] Add simulated-system coverage in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs"
Task: "T055 [US3] Add unsupported-capability guardrail error tests per connector in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorLimitationsTests.cs"
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate package-boundary routing for config/progress/diagnostics paths.
4. Demo and baseline before resume/connector expansions.

### Incremental Delivery

1. Foundation
2. US1 (standardized access)
3. US2 (resume/phase safety)
4. US3 (connector parity)
5. Polish/documentation/validation

### Parallel Team Strategy

1. Team completes Setup + Foundational together.
2. Then split:
   - Engineer A: US1
   - Engineer B: US2
   - Engineer C: prepares US3 test scaffolding
3. Merge and finalize with Phase 6.

