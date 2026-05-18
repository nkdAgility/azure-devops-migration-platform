# Tasks: Package Manager Adoption

**Input**: Design documents from `specs/034-package-manager-adoption/`  
**Prerequisites**: `plan.md`, `spec.md`, `research.md`, `data-model.md`, `contracts/package-boundary-contract.md`, `quickstart.md`

**Tests**: Included. This feature explicitly requires behavioral coverage for routing, route validation, resume and phase safety, no-bypass enforcement, and connector parity.

**Organization**: Tasks are grouped by user story to enable independent implementation and validation.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on incomplete tasks)
- **[Story]**: User story mapping label (`[US1]`, `[US2]`, `[US3]`)
- Every task includes an exact file path

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Prepare acceptance and package-boundary test scaffolding for the updated `IPackageAccess` architecture.

- [X] T001 Add failing acceptance coverage for package-boundary adoption in `features/platform/package-manager-adoption/package-boundary-adoption.feature` — Status: complete
- [X] T002 [P] Add failing acceptance step bindings for package-boundary adoption in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Platform/PackageBoundaryAdoptionSteps.cs` — Status: complete
- [X] T003 [P] Add package-boundary fixture utilities in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryTestFixture.cs` — Status: complete
- [X] T004 [P] Add package payload and log helper builders in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackagePayloadBuilder.cs` — Status: complete
- [X] T005 Add foundational RED tests for the `IPackageAccess` contract surface in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageAccessContractRedTests.cs` — Status: complete

**Gate**: Execute T001-T005 as a failing baseline before beginning foundational implementation tasks.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Align the core typed package contracts and baseline routing implementation required by all user stories.

**⚠️ CRITICAL**: No user story work begins until this phase is complete.

- [X] T006 [P] Align the `IPackageAccess` content API verb surface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageAccess.cs` — Status: complete
- [X] T007 [P] Align the `IPackageContentAddress` module-relative suffix contract in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageContentAddress.cs` — Status: complete
- [X] T008 [P] Align typed content scope and address fields in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageContentContext.cs` — Status: complete
- [X] T009 [P] Align the closed content-kind set in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageContentKind.cs` — Status: complete
- [X] T010 [P] Align metadata context semantics in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageMetaContext.cs` — Status: complete
- [X] T011 [P] Align metadata category definitions in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageMetaKind.cs` — Status: complete
- [X] T012 [P] Align run-log context semantics in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageLogContext.cs` — Status: complete
- [X] T013 [P] Align run-log stream definitions in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageLogStream.cs` — Status: complete
- [X] T014 [P] Align content payload contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackagePayload.cs` — Status: complete
- [X] T015 [P] Align metadata payload contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageMetaPayload.cs` — Status: complete
- [X] T016 [P] Align log payload contracts in `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageLogPayload.cs` — Status: complete
- [X] T017 Add guard tests for abstraction placement and stale contract removal in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryAbstractionsPlacementTests.cs` — Status: complete
- [X] T018 Implement package-owned prefix routing and unsafe-address rejection in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackagePathRouter.cs` — Status: complete
- [X] T019 Implement the typed package boundary over persistence stores in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/ActivePackageAccess.cs` — Status: complete
- [X] T020 Register `IPackageAccess` and router services in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageServiceCollectionExtensions.cs` — Status: complete

**Checkpoint**: The typed `IPackageAccess` boundary exists, rejects invalid addresses, and is injectable across runtime services.

---

## Phase 3: User Story 1 - Standardize Package Access (Priority: P1) 🎯 MVP

**Goal**: Route package content, metadata, and log operations through `IPackageAccess` instead of direct path composition or package-facing store bypasses.

**Independent Test**: Exercise package-config loading plus progress and diagnostics log routing flows, and verify those package-facing operations are issued through `IPackageAccess`, with canonical outputs unchanged for the covered surfaces.

### Tests for User Story 1

- [X] T021 [P] [US1] Add package-owned prefix and caller-supplied address tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackagePathRouterTests.cs` — Status: complete
- [X] T022 [P] [US1] Add typed content request and persist API tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageAccessTests.cs` — Status: complete
- [X] T023 [P] [US1] Add metadata mirroring and authority tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageMetaRoutingTests.cs` — Status: complete
- [X] T024 [P] [US1] Add run-log append behavior tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageLogAppendTests.cs` — Status: complete
- [X] T025 [P] [US1] Add streaming behavior tests proving no global buffering or sorting in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageStreamingBehaviorTests.cs` — Status: complete
- [X] T026 [P] [US1] Add fail-fast validation and stable error-code tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryErrorContractTests.cs` — Status: complete

### Implementation for User Story 1

- [X] T027 [US1] Harden `migration-config.json` loading on mandatory `IPackageAccess` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageMigrationConfigLoader.cs` — Status: complete
- [X] T028 [US1] Route progress log appends through `IPackageAccess.AppendLogAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PackageProgressSink.cs` — Status: complete
- [X] T029 [US1] Route diagnostics log appends through `IPackageAccess.AppendLogAsync` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PackageLoggerProvider.cs` — Status: complete
- [X] T030 [US1] Contain remaining string-path adaptation rules in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/LegacyPackagePathShim.cs` — Status: complete
- [X] T031 [US1] Align package-config service wiring on the canonical boundary in `src/DevOpsMigrationPlatform.Infrastructure.Agent/PackageConfigServiceCollectionExtensions.cs` — Status: complete
- [X] T032 [US1] Add O-1 `ActivitySource.StartActivity` coverage for package content, metadata, and log routing in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/ActivePackageAccess.cs` — Status: complete
- [X] T033 [US1] Add O-2 package-boundary latency and outcome metrics in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/ActivePackageAccess.cs` — Status: complete
- [ ] T034 [US1] Add O-3 structured boundary logs with `job.id`, operation, outcome, duration, and correlation fields in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/ActivePackageAccess.cs` — Status: incomplete
  - Evidence (2026-05-17): `src/DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem/ActivePackageAccess.cs` emits spans/metrics tags but does not emit per-operation structured `ILogger` boundary records with outcome + duration + correlation.
- [ ] T035 [US1] Add O-4 `IProgressSink.Emit` coverage for package progress and diagnostics flow in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PackageProgressSink.cs` — Status: incomplete
  - Evidence (2026-05-17): `PackageProgressSink` implements sink buffering and `AppendLogAsync` writes, but no additional O-4 boundary-transition event emission was added for diagnostics flow coverage.

**Checkpoint**: Runtime package config, progress, and diagnostics operations are centralized through `IPackageAccess` without direct package-store fallback.

---

## Phase 4: User Story 2 - Preserve Deterministic Resume and Phase Gates (Priority: P1)

**Goal**: Keep resume and phase-gate semantics unchanged while moving checkpoint and orchestration state through the package boundary.

**Independent Test**: Interrupt and resume jobs, then verify identical cursor, plan, and phase-gate behavior compared to baseline.

### Tests for User Story 2

- [X] T036 [P] [US2] Add checkpoint cursor routing and continuation-token tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/CheckpointingServiceTests.cs` — Status: complete
- [X] T037 [P] [US2] Add authoritative and run-audit execution-plan persistence tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs` — Status: complete
- [X] T038 [P] [US2] Add job-plan prerequisite and persistence tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs` — Status: complete
- [X] T039 [P] [US2] Add phase tracking no-regression tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/PhaseTrackingServiceTests.cs` — Status: complete
- [X] T040 [P] [US2] Add legacy compatibility resume tests for shim-backed package state in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/LegacyStateResumeCompatibilityTests.cs` — Status: complete

### Implementation for User Story 2

- [X] T041 [US2] Route checkpoint cursor and continuation-token persistence through `IPackageAccess` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Checkpointing/CheckpointingService.cs` — Status: complete
- [X] T042 [US2] Route authoritative and run-audit plan persistence through `IPackageAccess` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` — Status: complete
- [X] T043 [US2] Route per-task plan persistence and prerequisite reads through `IPackageAccess` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs` — Status: complete
- [X] T044 [US2] Route phase-record persistence through `IPackageAccess` metadata flows in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Checkpointing/PhaseTrackingService.cs` — Status: complete
- [X] T045 [US2] Align agent worker package-state reads and writes on the canonical boundary in `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` — Status: complete
- [X] T046 [US2] Add O-1 `ActivitySource.StartActivity` coverage for checkpoint and phase persistence in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Checkpointing/CheckpointingService.cs` — Status: complete
- [ ] T047 [US2] Add O-2 resume and phase-gate outcome metrics in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` — Status: incomplete
  - Evidence (2026-05-17): `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` has activity/log usage but no dedicated O-2 outcome metrics calls for resume/phase-gate outcomes.
- [ ] T048 [US2] Add O-3 structured resume and prerequisite logs with `job.id`, outcome, duration, and correlation fields in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs` — Status: incomplete
  - Evidence (2026-05-17): `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs` logs lifecycle events but does not consistently include the required structured resume/prerequisite fields (`job.id`, outcome, duration, correlation).
- [X] T049 [US2] Add O-4 `IProgressSink.Emit` coverage for resume and plan-rebuild transitions in `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` — Status: complete

**Checkpoint**: Resume checkpoints and phase gates remain deterministic and unchanged after package-boundary adoption.

---

## Phase 5: User Story 3 - Ensure Cross-Connector Consistency (Priority: P2)

**Goal**: Ensure package-boundary behavior is consistent across Simulated, Azure DevOps Services, and Team Foundation Server execution paths.

**Independent Test**: Run connector-specific flows and verify equivalent package-boundary semantics and observable outputs.

### Tests for User Story 3

- [X] T050 [P] [US3] Add cross-connector parity tests for package-boundary semantics in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorParityTests.cs` — Status: complete
- [X] T051 [P] [US3] Add Azure DevOps Services package-boundary integration tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/AdoPackageBoundaryIntegrationTests.cs` — Status: complete
- [X] T052 [P] [US3] Add TFS worker package-boundary tests in `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs` — Status: complete
- [X] T053 [P] [US3] Add simulated migration package-boundary tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs` — Status: complete
- [X] T054 [P] [US3] Add unsupported-capability guardrail tests per connector in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorLimitationsTests.cs` — Status: complete

### Implementation for User Story 3

- [X] T055 [US3] Route TFS worker package operations through `IPackageAccess` or the explicit legacy shim seam in `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` — Status: complete
- [X] T056 [US3] Reduce path-based package access in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs` — Status: complete
- [X] T057 [US3] Reduce path-based package access in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsOrchestrator.cs` — Status: complete
- [X] T058 [US3] Reduce path-based package access in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesOrchestrator.cs` — Status: complete
- [X] T059 [US3] Reduce path-based package access in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs` — Status: complete
- [X] T060 [US3] Reduce path-based package access in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/DependencyOrchestrator.cs` — Status: complete
- [ ] T061 [US3] Add O-1 `ActivitySource.StartActivity` coverage for connector-specific package operations in `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` — Status: incomplete
  - Evidence (2026-05-17): `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` currently has no `StartActivity` instrumentation for connector-specific package operations.
- [X] T062 [US3] Add O-2 connector parity outcome metrics in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesOrchestrator.cs` — Status: complete
- [ ] T063 [US3] Add O-3 structured connector-boundary logs with `job.id`, connector, outcome, duration, and correlation fields in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsOrchestrator.cs` — Status: incomplete
  - Evidence (2026-05-17): `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsOrchestrator.cs` has informational/warning/error logs but lacks a consistent structured connector-boundary log schema with `job.id`, outcome, duration, and correlation fields.
- [X] T064 [US3] Add O-4 `IProgressSink.Emit` coverage for connector package-boundary progress in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs` — Status: complete

**Checkpoint**: Package-boundary behavior is consistent across connector execution paths and no connector is left on untracked legacy package access for covered surfaces.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Finalize documentation, observability verification, boundary enforcement, and full-suite validation.

- [X] T065 [P] Update package-boundary architecture context in `.agents/30-context/domains/package-manager.md` — Status: complete
- [X] T066 [P] Update package format and routing context in `.agents/30-context/domains/migration-package-concept.md` — Status: complete
- [X] T067 [P] Update architecture guidance for package-boundary usage in `docs/architecture.md` — Status: complete
- [X] T068 [P] Update module guidance for `IPackageAccess` usage in `docs/module-development-guide.md` — Status: complete
- [X] T069 [P] Update canonical package layout guidance in `docs/package-format-reference.md` — Status: complete
- [ ] T070 [P] Add package-boundary observability assertions for spans, metrics, `job.id`, outcome, duration, and correlation fields in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryObservabilityTests.cs` — Status: incomplete
  - Evidence (2026-05-17 reconciliation): observability assertions exist, but open instrumentation tasks (T034, T047, T048, T061, T063) mean full operation coverage for required fields is not yet demonstrated.
- [X] T071 [P] Add runtime no-bypass enforcement audit tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageRuntimeBoundaryEnforcementTests.cs` — Status: complete
- [X] T072 [P] Add no-new-callsite enforcement for the legacy shim in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/LegacyPackagePathShimUsageTests.cs` — Status: complete
- [X] T073 [P] Add failure-path observability tests for structured error logs with `job.id` and correlation fields in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryErrorObservabilityTests.cs` — Status: complete
- [X] T074 [P] Add active-run log rotation continuity tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageLogRotationContinuityTests.cs` — Status: complete
- [ ] T075 Run full build, full test, and representative scenario validation from `specs/034-package-manager-adoption/quickstart.md` — Status: incomplete
  - Evidence (2026-05-17 reconciliation): full solution build completed and focused package-boundary tests passed, but full `dotnet test DevOpsMigrationPlatform.slnx --nologo` did not complete in-session, so the end-to-end validation gate is not yet satisfied.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies
- **Phase 2 (Foundational)**: Depends on the failing baseline from Phase 1 and blocks all user stories
- **Phase 3 (US1)**: Depends on Phase 2
- **Phase 4 (US2)**: Depends on Phase 2
- **Phase 5 (US3)**: Depends on Phase 2 and should start after US1 and US2 stabilize shared boundary behavior
- **Phase 6 (Polish)**: Cross-cutting polish tasks may start during user-story stabilization, but final completion requires all user story phases to be complete

### User Story Dependencies

- **US1 (P1)**: Starts after foundational phase; defines the MVP boundary behavior
- **US2 (P1)**: Starts after foundational phase; can run in parallel with US1 once the boundary contract is stable
- **US3 (P2)**: Depends on stable shared boundary behavior from US1 and US2

### Within Each User Story

- Story tests are implemented first, then runtime migrations, then story-level validation.
- Parallel tasks `[P]` must target independent files and avoid incomplete dependencies.

### Parallel Opportunities

- Foundational contract files `T006-T016` can run in parallel.
- The abstraction guard `T017` can run in parallel with contract alignment tasks.
- US1 tests `T021-T026` can run in parallel.
- US2 tests `T036-T040` can run in parallel.
- US3 tests `T050-T054` can run in parallel.
- Documentation updates `T065-T069` can run in parallel.
- Observability and enforcement tests `T070-T074` can run in parallel.

---

## Parallel Example: User Story 1

```text
Task: "T021 [P] [US1] Add package-owned prefix and caller-supplied address tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackagePathRouterTests.cs"
Task: "T022 [P] [US1] Add typed content request and persist API tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageAccessTests.cs"
Task: "T023 [P] [US1] Add metadata mirroring and authority tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageMetaRoutingTests.cs"
Task: "T024 [P] [US1] Add run-log append behavior tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageLogAppendTests.cs"
Task: "T025 [P] [US1] Add streaming behavior tests proving no global buffering or sorting in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageStreamingBehaviorTests.cs"
Task: "T026 [P] [US1] Add fail-fast validation and stable error-code tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryErrorContractTests.cs"
```

## Parallel Example: User Story 2

```text
Task: "T036 [P] [US2] Add checkpoint cursor routing and continuation-token tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/CheckpointingServiceTests.cs"
Task: "T037 [P] [US2] Add authoritative and run-audit execution-plan persistence tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs"
Task: "T038 [P] [US2] Add job-plan prerequisite and persistence tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs"
Task: "T039 [P] [US2] Add phase tracking no-regression tests in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/PhaseTrackingServiceTests.cs"
Task: "T040 [P] [US2] Add legacy compatibility resume tests for shim-backed package state in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/LegacyStateResumeCompatibilityTests.cs"
```

## Parallel Example: User Story 3

```text
Task: "T050 [P] [US3] Add cross-connector parity tests for package-boundary semantics in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorParityTests.cs"
Task: "T051 [P] [US3] Add Azure DevOps Services package-boundary integration tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/AdoPackageBoundaryIntegrationTests.cs"
Task: "T052 [P] [US3] Add TFS worker package-boundary tests in tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs"
Task: "T053 [P] [US3] Add simulated migration package-boundary tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs"
Task: "T054 [P] [US3] Add unsupported-capability guardrail tests per connector in tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/Package/PackageBoundaryConnectorLimitationsTests.cs"
```

---

## Implementation Strategy

### MVP First (US1 only)

1. Complete Phase 1 and Phase 2.
2. Complete Phase 3 (US1).
3. Validate package-boundary routing for config, progress, diagnostics, and route validation.
4. Demo and baseline before resume and connector expansions.

### Incremental Delivery

1. Foundation
2. US1 (standardized access)
3. US2 (resume and phase safety)
4. US3 (connector parity)
5. Polish, docs, and validation

### Parallel Team Strategy

1. Team completes Setup and Foundational phases together.
2. Then split:
   - Engineer A: US1
   - Engineer B: US2
   - Engineer C: US3 test scaffolding and connector parity work
3. Merge and finish with Phase 6.
Generated from `spec.md`, `plan.md`, `research.md`, `data-model.md`, `contracts/package-boundary-contract.md`, and `quickstart.md`.

