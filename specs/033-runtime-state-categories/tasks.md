# Tasks: Runtime State Categories and Resume Semantics Alignment

**Input**: Design documents from `/specs/033-runtime-state-categories/`  
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/runtime-state-contract.md

**Tests**: Business-logic tests are included where they validate resumability/risk-critical behavior. Observability tests (O-1, O-2, O-4) are mandatory in every user story phase.

**Organization**: Tasks are grouped by user story for independent implementation and validation.

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Establish feature scaffolding and test/doc baselines.

- [x] T001 Create feature task scaffold in `specs/033-runtime-state-categories/tasks.md` and align phase headers/checkpoints
- [x] T002 [P] Create US1 feature file `features/platform/runtime-state-authority/US1-authoritative-state-scopes.feature`
- [x] T003 [P] Create US2 feature file `features/platform/runtime-state-identity/US2-action-qualified-cursors.feature`
- [x] T004 [P] Create US3 feature file `features/platform/runtime-state-cadence/US3-fine-grained-progress-save-cadence.feature`
- [x] T005 [P] Add/adjust test context helpers for runtime state fixtures in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/RuntimeState/RuntimeStateTestContext.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core shared state primitives that block all user stories.

- [x] T006 Add/normalize state category constants and shared terminology in `src/DevOpsMigrationPlatform.Abstractions/State/RuntimeStateScopes.cs`
- [x] T007 Define shared action-qualified cursor identity helper in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/StateCursorIdentity.cs`
- [x] T008 [P] Add guard helper that rejects run-scope authority usage in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/RunScopeAuthorityGuard.cs`
- [x] T009 [P] Wire shared state helpers in DI extensions at `src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs`
- [x] T010 Create foundational unit tests for shared state helpers in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/StateCursorIdentityTests.cs`
- [x] T011 Create foundational unit tests for run-scope guard in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/RunScopeAuthorityGuardTests.cs`

**Checkpoint**: Foundational state primitives are complete; user stories can proceed.

---

## Phase 3: User Story 1 - Enforce Authoritative State Scopes (Priority: P1) 🎯 MVP

**Goal**: Ensure root/project/run state scopes are enforced with correct authority semantics.  
**Independent Test**: Interrupt and resume a migration; confirm only root `.migration/` and `/{org}/{project}/.migration/` drive orchestration/resume, and `.migration/runs/<runId>/` is ignored for authority.

### Implementation for User Story 1

- [x] T012 [US1] Update authoritative path contract methods in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/PackagePaths.cs`
- [x] T013 [US1] Enforce authoritative-scope selection during checkpoint read/write in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/CheckpointingService.cs`
- [x] T014 [US1] Enforce run-scope audit-only behavior during phase-gate evaluation in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
- [x] T015 [US1] Apply run-scope authority guard in worker orchestration flow at `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
- [x] T016 [P] [US1] Add unit coverage for authoritative path resolution in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/PackagePathsTests.cs`
- [x] T017 [P] [US1] Add unit coverage for run-scope guard behavior in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderTests.cs`

### Observability for User Story 1 ⛔ MANDATORY

- [x] T018 [US1] **O-1 Traces** — Add `ActivitySource.StartActivity("state.paths.resolve")` and `ActivitySource.StartActivity("state.runscope.guard")` with tags (`job.id`, `module.name`, `connector.type`) in `PackagePaths.cs` and `JobExecutionPlanBuilder.cs`
- [x] T019 [US1] **O-2 Metrics** — Add attempt/completion/error/duration/in-flight metrics for authoritative path resolution and run-scope guard in `CheckpointingService.cs` and `JobExecutionPlanBuilder.cs`
- [x] T020 [US1] **O-3 Logs** — Add structured `LogInformation` start/end, `LogWarning` on rejected authority source, and `LogDebug` decision traces in `PackagePaths.cs`, `CheckpointingService.cs`, and `JobExecutionPlanBuilder.cs`
- [x] T021 [US1] **O-4 ProgressEvents** — Emit scope-resolution and guard-stage progress events through optional `IProgressSink?` in `JobAgentWorker.cs` / `JobExecutionPlanBuilder.cs`
- [x] T022 [US1] **O-4 CLI Visible** — Add/verify state-authority progress rendering in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs`, plus any required counters in `src/DevOpsMigrationPlatform.Abstractions/Telemetry/MigrationCounters.cs` and extraction in `src/DevOpsMigrationPlatform.ControlPlane/Telemetry/SnapshotMetricExporter.cs`
- [x] T023 [US1] **DI Wiring** — Verify all new state-authority services are registered in `CoreAgentServiceExtensions.cs` and resolved in host startup
- [x] T024 [P] [US1] **Test O-1** — Add trace unit tests for span names `state.paths.resolve` and `state.runscope.guard` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/RuntimeStateTracingTests.cs`
- [x] T025 [P] [US1] **Test O-2** — Add metric unit tests asserting attempt/completed/error/duration/in-flight for authority operations in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/RuntimeStateMetricsTests.cs`
- [x] T026 [P] [US1] **Test O-4** — Add progress event unit tests asserting start/completion emissions and non-null completion metrics in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/RuntimeStateProgressTests.cs`

**Checkpoint**: US1 independently validates authoritative state behavior.

---

## Phase 4: User Story 2 - Isolate Module Resume Identity by Action (Priority: P1)

**Goal**: Separate inventory/export/import resume state namespaces via action-qualified cursor identity.  
**Independent Test**: Run inventory, export, and import for the same module/project and verify no cross-action cursor collisions.

### Implementation for User Story 2

- [x] T027 [US2] Refactor cursor key generation to action-qualified identity in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/PackagePaths.cs`
- [x] T028 [US2] Update checkpoint read/write callsites to pass explicit action and module in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/CheckpointingService.cs`
- [x] T029 [US2] Update work-item export cursor usage to action-qualified keys in `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemExportOrchestrator.cs`
- [x] T030 [US2] Update work-item import cursor usage to action-qualified keys in `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemImportOrchestrator.cs`
- [x] T031 [US2] Update revision folder stage-resume pathing to action-qualified identity in `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/RevisionFolderProcessor.cs`
- [x] T032 [US2] Align inventory cursor semantics with action-qualified identity in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs`
- [x] T033 [P] [US2] Add collision-prevention tests across inventory/export/import in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/ActionQualifiedCursorIdentityTests.cs`

### Observability for User Story 2 ⛔ MANDATORY

- [x] T034 [US2] **O-1 Traces** — Add `ActivitySource.StartActivity("state.cursor.update")` with required tags in checkpointing and orchestrator cursor update paths
- [x] T035 [US2] **O-2 Metrics** — Add cursor write/read/conflict/duration/in-flight metrics around action-qualified cursor operations in checkpointing/orchestrator flows
- [x] T036 [US2] **O-3 Logs** — Add structured logs for cursor namespace reads/writes and collision prevention decisions in `CheckpointingService.cs`, `WorkItemExportOrchestrator.cs`, `WorkItemImportOrchestrator.cs`, and `InventoryOrchestrator.cs`
- [x] T037 [US2] **O-4 ProgressEvents** — Emit action-qualified cursor advancement progress events in export/import/inventory orchestrators
- [x] T038 [US2] **O-4 CLI Visible** — Add/verify cursor-namespace progress visibility in `QueueCommand.cs`; update `MigrationCounters.cs` and `SnapshotMetricExporter.cs` if new counters are required
- [x] T039 [US2] **DI Wiring** — Verify cursor identity helper and any new supporting components are registered and consumed via DI
- [x] T040 [P] [US2] **Test O-1** — Add span tests for `state.cursor.update` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/CursorUpdateTracingTests.cs`
- [x] T041 [P] [US2] **Test O-2** — Add metric tests for cursor update/read/conflict flows in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/CursorUpdateMetricsTests.cs`
- [x] T042 [P] [US2] **Test O-4** — Add progress event tests for action-qualified cursor updates in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/CursorUpdateProgressTests.cs`

**Checkpoint**: US2 independently validates action-qualified resume identity.

---

## Phase 5: User Story 3 - Fine-Grained Progress and Save Cadence (Priority: P2)

**Goal**: Provide fine-grained progress updates and reasonably fine-grained durable save cadence, with work-item-specific batch save + item-level progress behavior.  
**Independent Test**: Interrupt long-running operations and verify restart from latest reasonable checkpoint with minimal replay; for work items, verify batch-save + work-item-level progress semantics.

### Implementation for User Story 3

- [x] T043 [US3] Implement fine-grained progress emission cadence policy in `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemExportOrchestrator.cs`
- [x] T044 [US3] Implement work-item-batch save boundary persistence semantics in `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/WorkItemImportOrchestrator.cs`
- [x] T045 [US3] Update revision-stage processing to checkpoint at completed batch boundaries in `src/DevOpsMigrationPlatform.Infrastructure.Agent/WorkItems/RevisionFolderProcessor.cs`
- [x] T046 [US3] Add generic long-running workload cadence helper for reasonable save intervals in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/ProcessingCadencePolicy.cs`
- [x] T047 [US3] Apply processing cadence policy to non-work-item long-running flows in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs`
- [x] T048 [P] [US3] Add replay-minimization/resume-near-latest tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/WorkItems/WorkItemBatchResumeCadenceTests.cs`
- [x] T049 [P] [US3] Add non-work-item cadence tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Discovery/ProcessingCadencePolicyTests.cs`

### Observability for User Story 3 ⛔ MANDATORY

- [x] T050 [US3] **O-1 Traces** — Add `ActivitySource.StartActivity("state.workitems.batch.save")` and `ActivitySource.StartActivity("state.progress.emit")` with required tags in work-item and generic cadence flows
- [x] T051 [US3] **O-2 Metrics** — Add metrics for batch saves completed/errors/duration and progress emission cadence counters/lag in orchestrators and cadence policy
- [x] T052 [US3] **O-3 Logs** — Add structured logs for batch checkpoint commits, replay window decisions, cadence throttling, and per-item progress emission
- [x] T053 [US3] **O-4 ProgressEvents** — Emit start/per-item-or-batch/completion progress events with completion metrics populated for work-item and non-work-item processing
- [x] T054 [US3] **O-4 CLI Visible** — Add/verify progress rows and counters for cadence/batch-save visibility in `QueueCommand.cs`, `MigrationCounters.cs`, and `SnapshotMetricExporter.cs`
- [x] T055 [US3] **DI Wiring** — Register and wire `ProcessingCadencePolicy` and any related services via extension methods
- [x] T056 [P] [US3] **Test O-1** — Add trace tests for `state.workitems.batch.save` and `state.progress.emit` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/WorkItems/WorkItemCadenceTracingTests.cs`
- [x] T057 [P] [US3] **Test O-2** — Add metric tests for cadence/save instrumentation in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/WorkItems/WorkItemCadenceMetricsTests.cs`
- [x] T058 [P] [US3] **Test O-4** — Add progress event tests for work-item-level and generic long-running processing cadence in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/WorkItems/WorkItemCadenceProgressTests.cs`

**Checkpoint**: US3 independently validates fine-grained progress and save cadence.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Cross-story hardening, documentation, and final validation.

- [x] T059 Update state-model docs for implemented behavior in `docs/package-format-reference.md`
- [x] T060 [P] Update operator guidance for resume/cadence behavior in `docs/package-guide.md`
- [x] T061 [P] Update migration orchestration guidance in `docs/migration-process-guide.md`
- [x] T062 [P] Sync concise agent context summary in `.agents/30-context/domains/migration-package-concept.md`
- [x] T063 [P] Sync checkpointing summary in `.agents/30-context/domains/checkpointing-summary.md`
- [x] T064 [P] Sync package-format summary in `.agents/30-context/domains/package-format-summary.md`
- [x] T065 Run full build/test for `DevOpsMigrationPlatform.slnx` using commands in `specs/033-runtime-state-categories/quickstart.md`

---

## Phase 7: Post-Review Gap Closure (Coverage + TDD Readiness)

**Purpose**: Close all previously flagged coverage gaps/partials and remove TDD-readiness quality blockers before implementation.

### Requirement Coverage Gaps

- [x] T066 [US1] Add run-audit inspectability validation while preserving non-authoritative semantics in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/RunAuditInspectabilityTests.cs`
- [x] T067 [US1] Add incomplete/missing run-audit-folder resilience coverage proving authoritative resume/gate behavior is unaffected in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/RunAuditFolderResilienceTests.cs`
- [x] T068 [US2] Add explicit mixed legacy-root and current project-scope precedence tests (project scope authoritative) in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/LegacyCheckpointPrecedenceTests.cs`
- [x] T069 [US2] Add inventory compatibility tests proving action-qualified identity and fallback non-authority semantics in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Discovery/InventoryCompatibilitySemanticsTests.cs`
- [x] T070 [US2] Add multi-run same-package resume/phase-gate correctness tests across action scopes in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/MultiRunAuthorityIsolationTests.cs`
- [x] T071 [US3] Add quantified replay-threshold test assertions enforcing `>=95%` near-latest durable resume across interruption points in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/WorkItems/ResumeReplayThresholdTests.cs`
- [x] T072 [US3] Add explicit follow-mode steady-forward-movement assertions (including work-item-level visibility) in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/QueueFollowModeProgressTests.cs`

### TDD Readiness and Commit Discipline

> Execution rule: `T073`-`T075` are pre-implementation gates and MUST run before the corresponding implementation blocks (`T012`-`T015`, `T027`-`T032`, `T043`-`T047`). They are listed in this phase for traceability from the review findings, but their execution order is authoritative.

- [x] T073 [US1] Enforce RED-first execution order for US1 by running failing tests (`T016`, `T017`, `T024`, `T025`, `T026`) before implementation tasks (`T012`-`T015`) using commands in `specs/033-runtime-state-categories/quickstart.md`
- [x] T074 [US2] Enforce RED-first execution order for US2 by running failing tests (`T033`, `T040`, `T041`, `T042`) before implementation tasks (`T027`-`T032`) using commands in `specs/033-runtime-state-categories/quickstart.md`
- [x] T075 [US3] Enforce RED-first execution order for US3 by running failing tests (`T048`, `T049`, `T056`, `T057`, `T058`, `T071`, `T072`) before implementation tasks (`T043`-`T047`) using commands in `specs/033-runtime-state-categories/quickstart.md`
- [x] T076 [US1] Commit US1 green state immediately after all US1 tests pass and record command/evidence notes in `specs/033-runtime-state-categories/quickstart.md`
- [x] T077 [US2] Commit US2 green state immediately after all US2 tests pass and record command/evidence notes in `specs/033-runtime-state-categories/quickstart.md`
- [x] T078 [US3] Commit US3 green state immediately after all US3 tests pass and record command/evidence notes in `specs/033-runtime-state-categories/quickstart.md`

**Checkpoint**: All review-identified coverage gaps and TDD-readiness blockers are closed before implementation.

---

## Dependencies & Execution Order

### Phase Dependencies

1. Setup (Phase 1) → no dependencies
2. Foundational (Phase 2) → depends on Setup
3. US1 (Phase 3) → depends on Foundational
4. US2 (Phase 4) → depends on Foundational and partially on US1 path-contract changes
5. US3 (Phase 5) → depends on US2 cursor identity semantics
6. Polish (Phase 6) → depends on all user stories
7. Post-Review Gap Closure (Phase 7) → mixed-order execution:
   - `T073`-`T075` depend on Phase 2 and execute before their corresponding implementation tasks
   - `T066`-`T072` execute with or after corresponding story implementation/tests
   - `T076`-`T078` execute immediately after each story reaches green

### User Story Dependencies

- **US1**: Independent after Foundational; establishes authority guardrails.
- **US2**: Requires shared state primitives; should build on US1 path authority updates.
- **US3**: Requires US2 action-qualified identity behavior to avoid replay ambiguity.

---

## Parallel Execution Examples

### User Story 1

```bash
Task: "T016 [P] [US1] Add unit coverage for authoritative path resolution in PackagePathsTests.cs"
Task: "T017 [P] [US1] Add unit coverage for run-scope guard behavior in JobExecutionPlanBuilderTests.cs"
Task: "T024 [P] [US1] Test O-1 spans in RuntimeStateTracingTests.cs"
```

### User Story 2

```bash
Task: "T029 [US2] Update work-item export cursor usage in WorkItemExportOrchestrator.cs"
Task: "T030 [US2] Update work-item import cursor usage in WorkItemImportOrchestrator.cs"
Task: "T033 [P] [US2] Add collision-prevention tests in ActionQualifiedCursorIdentityTests.cs"
```

### User Story 3

```bash
Task: "T048 [P] [US3] Add replay-minimization tests in WorkItemBatchResumeCadenceTests.cs"
Task: "T049 [P] [US3] Add non-work-item cadence tests in ProcessingCadencePolicyTests.cs"
Task: "T058 [P] [US3] Test O-4 cadence progress events in WorkItemCadenceProgressTests.cs"
```

---

## Implementation Strategy

### MVP First

1. Complete Phases 1-2.
2. Deliver Phase 3 (US1) as first shippable increment.
3. Validate US1 independent test criteria before expanding scope.

### Incremental Delivery

1. Add US2 after US1 authority model is stable.
2. Add US3 fine-grained cadence behavior once action-qualified identities are in place.
3. Finish documentation and cross-cutting checks in Phase 6.

### Format Validation

All tasks follow required checklist format: checkbox + sequential Task ID + optional `[P]` + required `[US#]` in story phases + explicit file path.

