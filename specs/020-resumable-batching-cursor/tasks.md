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

- [ ] T001 Create `features/platform/checkpointing/resumable-batching-cursor.feature` with baseline scenarios mapped from `specs/020-resumable-batching-cursor/spec.md` user stories (US1: resume-from-token, no-token; US2: fingerprint match/mismatch; US3: drift ordering, duplicate tolerance).
- [ ] T002 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Checkpointing/ResumableBatchingCursorContext.cs` with shared scenario state for resume token, fingerprint, and caller checkpoint cadence.
- [ ] T003 [P] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Checkpointing/ResumableBatchingCursorSteps.cs` with intentionally failing Reqnroll bindings for initial scenarios in `features/platform/checkpointing/resumable-batching-cursor.feature`.

**Checkpoint**: Acceptance scaffolding exists and fails before production changes.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Introduce shared contracts and primitives required by all user stories.

**CRITICAL**: User story implementation starts only after this phase is complete.

- [ ] T004 Create `src/DevOpsMigrationPlatform.Abstractions/Models/BatchContinuationToken.cs` — sealed record with `StrategyVersion` (string), `ChangedDateUtc` (DateTime), `WorkItemId` (int), `FallbackBatchSize` (int), `FallbackBatchIndex` (int), `FallbackChecksum` (string), `QueryFingerprint` (string), `GeneratedAtUtc` (DateTime), `Completed` (bool). All init-only properties per data-model.md.
- [ ] T005 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/ResumeDecisionStatus.cs` — enum with `Accepted`, `RejectedQueryMismatch`, `Unavailable`.
- [ ] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/ResumeDecision.cs` — sealed record with `Status` (ResumeDecisionStatus), `Reason` (string?), `SavedQueryFingerprint` (string?), `CurrentQueryFingerprint` (string?), `TokenStrategyVersion` (string?).
- [ ] T007 Create `src/DevOpsMigrationPlatform.Abstractions/Models/ResumeRejectedException.cs` — extends `InvalidOperationException`; carries `ResumeDecision` payload as a read-only property (per plan.md Finding 1: mismatch delivered via exception).
- [ ] T008 Create `src/DevOpsMigrationPlatform.Abstractions/Services/IQueryFingerprintService.cs` — interface with `string Compute(string queryText, IReadOnlyDictionary<string, string>? parameters = null)`.
- [ ] T009 Create `src/DevOpsMigrationPlatform.Infrastructure/Services/QueryFingerprintService.cs` — SHA-256 deterministic fingerprint from normalised query text + lexicographically sorted parameters. Excludes post-fetch filters.
- [ ] T010 Register `IQueryFingerprintService` as singleton in `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformServiceExtensions.cs`.
- [ ] T011 Extend `src/DevOpsMigrationPlatform.Abstractions/Services/WorkItemQueryWindow.cs` — add `ResumeEnabled` (bool, default false), `SavedContinuationToken` (BatchContinuationToken?, default null), `QueryParameters` (IReadOnlyDictionary<string, string>?, default null) to `WorkItemQueryWindowOptions`. No regression for existing callers.
- [ ] T012 Extend `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemFetchScope.cs` — add `ResumeEnabled` (bool, default false), `SavedContinuationToken` (BatchContinuationToken?, default null), `ContinuationCheckpointWriter` (Func<BatchContinuationToken, CancellationToken, Task>?, default null).
- [ ] T013 Add `ContinuationFile()` static method to `src/DevOpsMigrationPlatform.Abstractions/PackagePaths.cs` for continuation token persistence path under `.migration/Checkpoints/`.
- [ ] T014 Add `ReadContinuationTokenAsync`, `WriteContinuationTokenAsync`, `DeleteContinuationTokenAsync` methods to `src/DevOpsMigrationPlatform.Abstractions/Services/ICheckpointingService.cs`.
- [ ] T015 Implement continuation token read/write/delete in `src/DevOpsMigrationPlatform.Infrastructure/Checkpointing/CheckpointingService.cs` using `IStateStore` and `PackagePaths.ContinuationFile()`.
- [ ] T016 [P] Add abstraction contract tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Checkpointing/ResumableBatchingContractTests.cs` for token validity, `ResumeDecision` transition invariants, fingerprint determinism, and `ResumeRejectedException` payload.

**Checkpoint**: Shared contract layer compiles with deterministic resume primitives. All existing tests pass.

---

## Phase 3: User Story 1 — Resume Long-Running Iteration Safely (Priority: P1) 🎯 MVP

**Goal**: Resume near the saved continuation position when resume is enabled; start from beginning when no token exists.

**Independent Test**: Interrupt a long enumeration, restart with resume enabled, and verify traversal continues from the saved continuation point.

### Tests First (Must fail before implementation)

- [ ] T017 [US1] Expand `features/platform/checkpointing/resumable-batching-cursor.feature` with US1 scenarios: resume-from-token and no-token-unavailable handling.
- [ ] T018 [P] [US1] Add failing resume-start tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/WorkItemQueryWindowStrategyTests.cs` — verify window skipping when token is present and `ResumeEnabled = true`.
- [ ] T019 [P] [US1] Add failing checkpoint-emission tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs` — verify per-batch `ContinuationCheckpointWriter` callbacks and completion checkpoint.

### Implementation

- [ ] T020 [US1] Implement resume-aware window skipping in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` — when token present, skip unbounded probe and set window start from saved `ChangedDateUtc`/`WorkItemId`.
- [ ] T021 [US1] Wire resume inputs and per-batch checkpoint emission via `ContinuationCheckpointWriter` callback in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs`. Inject `IQueryFingerprintService` via constructor (per plan.md Finding 2). Emit warning log when `ResumeEnabled=true` but `ContinuationCheckpointWriter` is null (per plan.md Finding 3).
- [ ] T022 [US1] Emit mandatory completion checkpoint (`Completed=true`) at end-of-stream in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs`.

**Checkpoint**: Resume-enabled callers continue from saved position; no-token paths start fresh without error.

---

## Phase 4: User Story 2 — Prevent Unsafe Resume After Query Changes (Priority: P1)

**Goal**: Reject continuation when query fingerprint changes; throw `ResumeRejectedException` with `ResumeDecision` payload so caller can decide recovery.

**Independent Test**: Persist token with fingerprint H1, rerun with H2, verify `ResumeRejectedException` is thrown with mismatch details.

### Tests First (Must fail before implementation)

- [ ] T023 [US2] Expand `features/platform/checkpointing/resumable-batching-cursor.feature` with US2 query-mismatch acceptance scenarios.
- [ ] T024 [P] [US2] Add failing fingerprint-mismatch tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs` — verify `ResumeRejectedException` thrown with correct `ResumeDecision` payload when fingerprints differ.
- [ ] T025 [P] [US2] Add failing fingerprint-match acceptance test in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs` — verify continuation accepted when fingerprints match.

### Implementation

- [ ] T026 [US2] Implement fingerprint comparison at start of `FetchAsync()` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs` — compute current fingerprint from `WorkItemFetchScope.BaseQuery` + `WorkItemQueryWindowOptions.QueryParameters`, compare with saved token fingerprint, throw `ResumeRejectedException` on mismatch, accept on match, log info on unavailable.
- [ ] T027 [US2] Add resume decision OTel metric (`migration.resume.decision`) with tags `decision` and `module` — add constant to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`, recording method to `IMigrationMetrics`, instrument to `MigrationMetrics`, and emit from fetch service.
- [ ] T028 [US2] Add resume decision structured logging in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs` for all three decision outcomes (`Accepted`, `RejectedQueryMismatch`, `Unavailable`).

**Checkpoint**: Query changes cannot silently reuse stale continuation tokens. Mismatch is observable via logs and OTel.

---

## Phase 5: User Story 3 — Handle Duplicates and Data Drift Predictably (Priority: P2)

**Goal**: Ensure deterministic oldest-first traversal (`ChangedDate ASC, WorkItemId ASC`) and explicit duplicate-tolerant behavior under source drift.

**Independent Test**: Resume after source mutations and verify no missed items attributable to resume ordering; duplicate-safe caller policy preserves correctness.

### Tests First (Must fail before implementation)

- [ ] T029 [US3] Expand `features/platform/checkpointing/resumable-batching-cursor.feature` with US3 drift and duplicate-tolerance scenarios.
- [ ] T030 [P] [US3] Add failing deterministic-order tests (`ORDER BY [System.ChangedDate] ASC, [System.Id] ASC`) in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/WorkItemQueryWindowStrategyTests.cs`.
- [ ] T031 [P] [US3] Add failing duplicate-tolerance tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs` — verify items with duplicate IDs across resumed windows are yielded (no strategy-level dedup).

### Implementation

- [ ] T032 [US3] Update WIQL ordering in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` to use `ORDER BY [System.ChangedDate] ASC, [System.Id] ASC` when `ResumeEnabled = true`. Non-resume callers retain existing traversal behavior per FR-011.
- [ ] T033 [US3] Ensure resumed window enumeration yields overlapping items without dedup in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemFetchService.cs` — strategy explicitly does NOT deduplicate per FR-008.

**Checkpoint**: Drift-safe deterministic continuation works without internal dedup side effects.

---

## Phase 6: Documentation Sync (MANDATORY)

**Purpose**: Verify doc sections added during spec phase match the implementation; finalize any discrepancies.

> **Note**: Initial doc sections were added during spec resolution (see discrepancies.md — all 3 items Resolved). Phase 6 tasks verify these sections are accurate against the final implementation.

- [ ] T034 Verify `docs/work-item-iteration-pattern.md` section 11 (Resumable Batching Contract) matches implemented behavior — update if any contract details changed during implementation.
- [ ] T035 [P] Verify `.agents/context/checkpointing.md` section (Query Fingerprint Compatibility) matches implemented behavior — update if mismatch decision flow or storage paths changed.
- [ ] T036 [P] Verify `docs/configuration.md` section (Resumable Batching — Operational Responsibilities) matches implemented behavior — update persistence cadence or restart guidance if needed.
- [ ] T037 Confirm all items in `specs/020-resumable-batching-cursor/discrepancies.md` remain `Resolved` — re-open and fix if implementation diverged from documented contract.
- [ ] T038 Review and update `analysis/pending-actions.md` to remove entries satisfied by this spec.

---

## Phase 7: Guardrail Validation Gates (MANDATORY)

**Purpose**: Prove build, test, and scenario execution compliance before closure.

- [ ] T039 Run `dotnet clean && dotnet build --no-incremental` and record pass status.
- [ ] T040 Run `dotnet test` and record pass status.
- [ ] T041 Run scenario via `.vscode/launch.json` using `scenarios/queue-export-ado-workitems-single-project.json` and verify observable output (resume decision telemetry and checkpoint progression).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: starts immediately.
- **Foundational (Phase 2)**: depends on Phase 1 completion; blocks all user stories.
- **User Story phases (Phases 3–5)**: depend on Phase 2 completion.
- **Documentation Sync (Phase 6)**: depends on completion of implemented user stories.
- **Guardrail Validation (Phase 7)**: depends on code and documentation completion.

### User Story Dependencies

- **US1 (Phase 3)**: no dependency on other user stories; MVP.
- **US2 (Phase 4)**: independent from US1 but shares foundational contracts. Can run in parallel with US1.
- **US3 (Phase 5)**: depends on foundational contracts; should run after US1/US2 resume behavior is stable.

### Within Each User Story

- Acceptance scenarios and failing tests must be created before implementation tasks.
- Strategy/fetch service changes land before caller integrations.
- Observability tasks run after core behavior is verified.

---

## Parallel Opportunities

- T002 and T003 can run in parallel (Phase 1).
- T005 and T006 can run in parallel (Phase 2 — independent model files).
- T016 can run in parallel with T009/T010 after T004–T008 are in place.
- T018 and T019 can run in parallel (Phase 3 tests).
- T024 and T025 can run in parallel (Phase 4 tests).
- T030 and T031 can run in parallel (Phase 5 tests).
- T035 and T036 can run in parallel (Phase 6 doc verification).

---

## Implementation Strategy

### MVP First

1. Complete Phases 1–2 (scaffolding + contracts).
2. Complete Phase 3 (US1) — validate interruption/resume behavior.
3. Run targeted tests before expanding to mismatch and drift behavior.

### Incremental Delivery

4. Add US2 (Phase 4) — query-fingerprint safety and `ResumeRejectedException`.
5. Add US3 (Phase 5) — deterministic drift ordering and duplicate tolerance.
6. Complete doc verification (Phase 6) and validation gates (Phase 7).

### Completion Rule

A phase is not complete until its checklist items are all checked and all mandatory validation gates in Phase 7 pass.
