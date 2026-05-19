# Tasks: Fix — Tool Config Never Reaches the Agent (Config Travels in Package)

**Branch**: `025-agent-config-package`
**Input**: [plan.md](plan.md), [spec.md](spec.md), [data-model.md](data-model.md), [contracts/IPackageConfigStore.md](contracts/IPackageConfigStore.md)
**Feature**: 3 user stories (P1, P2, P3) — cross-cutting infrastructure fix

---

## Phase 1: Setup

**Purpose**: Guardrail and canonical-doc updates that gate all implementation.

- [X] T001 [META] Amend `.agents/20-guardrails/core/architecture-boundaries.md` — update Rule 23 to add the approved CLI exception: "The CLI MAY write `migrat… — .agents/20-guardrails/core/architecture-boundaries.md — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T002 [META] Update `.agents/30-context/domains/migration-package-concept.md` — add `migration-config.json` to the canonical package root structure secti… — .agents/30-context/domains/migration-package-concept.md — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T003 [META] Update `.agents/30-context/domains/job-lifecycle.md` — document the new minimal `MigrationJob` schema (v2.0): fields retained (`jobId`, `mod… — .agents/30-context/domains/job-lifecycle.md — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core abstractions and constants that ALL user story phases depend on. Must be complete before any US phase begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 [META] Add constant `public const string MigrationConfigFileName = "migration-config.json";` to `src/DevOpsMigrationPlatform.Abstractions.Agent/Lea… — src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/PackagePaths.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T005 [META] Add new metric name constants to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`: — src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
  - `public const string ConfigWriteCount = "migration.config.write.count";`
  - `public const string ConfigWriteErrors = "migration.config.write.errors";`
  - `public const string ConfigReadCount = "migration.config.read.count";`
  - `public const string ConfigReadErrors = "migration.config.read.errors";`
  - `public const string ConfigReadFallbacks = "migration.config.read.fallbacks";`
- [X] T006 [META] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageConfigNotFoundException.cs` — sealed exception with message: `"migrati… — src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageConfigNotFoundException.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T007 [META] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageConfigStore.cs` — interface with `WriteAsync(IArtefactStore, Migratio… — src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageConfigStore.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T008 [META] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageConfigStore.cs` — implements `IPackageConfigStore`; uses `IArtefactS… — src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageConfigStore.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T009 [META] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/PackageConfigServiceCollectionExtensions.cs` — `AddPackageConfigStore()` extension… — src/DevOpsMigrationPlatform.Infrastructure.Agent/PackageConfigServiceCollectionExtensions.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

**Checkpoint**: Abstraction + implementation ready — user story phases can proceed.

---

## Phase 3: User Story 1 — Tool Configuration Applied During Export (Priority: P1) 🎯 MVP

**Goal**: Operator-configured `FieldTransform` rules and `NodeTranslation` mappings in `migration.json` are applied to every exported work item revision. The CLI writes `migration-config.json` to the package; the agent reads it and wires all `IOptions<T>` from it before running any module.

**Independent Test**: Configure ≥1 `FieldTransform` rule and ≥1 `NodeTranslation` mapping, run simulated export via CLI, assert (a) `migration-config.json` exists at package root, (b) `IOptions<FieldTransformOptions>` on the agent reflects the configured rule, (c) at least one exported `revision.json` shows the transform applied.

### Gherkin Feature File for User Story 1 (mandatory)

- [X] T010 [US1] Create `features/export/config-in-package/config-applied-on-export.feature` — translate spec.md US1 acceptance scenarios (tool config applie… — features/export/config-in-package/config-applied-on-export.feature — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

### Implementation for User Story 1

- [X] T011 [US1] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs` — in all `Execute*Async` methods (ExportAsync, AdoExportAsync, S… — src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T012 [US1] Register `IPackageConfigStore` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs` host builder — add `services.AddPacka… — src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T013 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure.Agent/ModulePipelineWorkerBase.cs` `OnMigrationJobAsync` — after opening `(artefactStore,… — src/DevOpsMigrationPlatform.Infrastructure.Agent/ModulePipelineWorkerBase.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T014 [US1] Register `IPackageConfigStore` in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs` — add `services.AddPackage… — src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T015 [US1] Modify `src/DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs` — remove properties: `ConfigHash`, `Policies` (`JobPolicies`), `Modules` (`Lis… — src/DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs — Status: complete
- [X] T016 [US1] Modify `src/DevOpsMigrationPlatform.Abstractions/Jobs/MigrationJob.cs` — remove properties: `Source` (`MigrationEndpointOptions?`), `Target`… — src/DevOpsMigrationPlatform.Abstractions/Jobs/MigrationJob.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T017 [US1] Fix all compilation errors caused by removing `Job.Modules`, `Job.Policies`, `Job.ConfigHash`, `MigrationJob.Source`, `MigrationJob.Target`… — N/A — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

### Observability for User Story 1 ⛔ MANDATORY

- [X] T018 [US1] **O-1 Traces** — In `PackageConfigStore.WriteAsync`, start `ActivitySource.StartActivity("config.write")` with tags: `job.id` (from ambient… — src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageConfigStore.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T019 [US1] **O-2 Metrics** — Inject `IMigrationMetrics` via constructor. Use the metric instrument pattern consistent with existing metrics in the code… — N/A — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T020 [US1] **O-3 Logs** — Add `ILogger<PackageConfigStore>`: `LogInformation` at start of write/read with `{PackageUri}`; `LogInformation` at completio… — PackageConfigStore.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T021 [US1] **DI Wiring** — Verify `AddPackageConfigStore()` is called from: MigrationAgent host startup, TfsMigrationAgent host startup (T009, T014 — v… — N/A — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T022 [US1] **Test O-1** — Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageConfigStoreTests.cs`: assert `ActivityS… — tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageConfigStoreTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T023 [US1] **Test O-2** — Unit test in `PackageConfigStoreTests.cs`: inject `Mock<IMigrationMetrics>`, assert `ConfigWriteCount` incremented on success… — PackageConfigStoreTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T024 [US1] **Test O-3** — Unit test in `PackageConfigStoreTests.cs`: inject `Mock<ILogger<PackageConfigStore>>`, assert `LogInformation` called at star… — PackageConfigStoreTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

---

## Phase 4: User Story 2 — Config Audit Trail in the Package (Priority: P2)

**Goal**: After a completed export, `migration-config.json` is present at the package root and contains the full operator-supplied tool config. A new agent instance resumes from the same file — no external source needed.

**Independent Test**: Run simulated export to completion, open package folder, assert `migration-config.json` is present and deserialises to a valid `MigrationOptions` with all configured rules intact.

### Gherkin Feature File for User Story 2 (mandatory)

- [X] T025 [US2] Create `features/export/config-in-package/config-audit-trail.feature` — translate spec.md US2 acceptance scenarios (config present after exp… — features/export/config-in-package/config-audit-trail.feature — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

### Implementation for User Story 2

- [X] T026 [US2] Verify CLI surfaces the `InvalidOperationException` from `PackageConfigStore.WriteAsync` as a user-readable error: `QueueCommand` MUST catch… — migration-config.json — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T027 [US2] Modify `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` `OnMigrationJobAsync` — add config read step before `base.OnMigr… — src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T028 [US2] Register `IPackageConfigStore` in `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs` — add `services.AddP… — src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T029 [US2] Assertions added to `SimulatedMigrationCommandTests.QueueExportSimulated_ExitsZeroAndWritesWorkItemRevisions` — after revision assertions: (… — migration-config.json — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T029b [US2] New `[TestCategory("SystemTest_Simulated")]` test `QueueExportSimulated_ReSubmitWithoutForce_RejectsWithExitCodeOne` added to `SimulatedMigr… — SimulatedMigrationCommandTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

### Observability for User Story 2 ⛔ MANDATORY

- [X] T030 [US2] **O-1/O-3 TFS path** — Verified via T031 tests. `ActivitySource.StartActivity("config.read")` and `("config.write")` fire on net481 using `S… — N/A — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T031 [US2] **Test O-1 TFS path** — Two tests added to `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`: `PackageConfig… — tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

---

## Phase 5: User Story 3 — Legacy Package Fails Fast with Clear Error (Priority: P3)

**Goal**: A package that pre-dates this fix (no `migration-config.json`) causes the agent to fail immediately with a structured, actionable error — not a silent fallback. After re-submitting via CLI, the file is written and the job proceeds.

**Independent Test**: Create a package root with no `migration-config.json`, call `IPackageConfigStore.ReadAsync`, assert `PackageConfigNotFoundException` is thrown with the correct message.

### Gherkin Feature File for User Story 3 (mandatory)

- [X] T032 [US3] Create `features/export/config-in-package/legacy-package-fail-fast.feature` — translate spec.md US3 acceptance scenarios (job fails on absen… — features/export/config-in-package/legacy-package-fail-fast.feature — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

### Implementation for User Story 3

- [X] T033 [US3] Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageConfigStoreTests.cs`: — tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageConfigStoreTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
  - `ReadAsync_WhenFileAbsent_ThrowsPackageConfigNotFoundException` — assert message contains "Re-submit"
  - `ReadAsync_WhenFileCorrupt_ThrowsJsonException` — assert job-fail path is triggered (depends on T008)
- [X] T034 [US3] Unit test: `WriteAsync_WhenFileAlreadyExists_ThrowsInvalidOperationException` — verify CLI cannot silently overwrite (depends on T008) — N/A — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T035 [US3] Add explicit `catch (PackageConfigNotFoundException ex)` block in `ModulePipelineWorkerBase.OnMigrationJobAsync` (in `src/DevOpsMigrationPla… — src/DevOpsMigrationPlatform.Infrastructure.Agent/ModulePipelineWorkerBase.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

### Observability for User Story 3 ⛔ MANDATORY

- [X] T036 [US3] **O-2/O-3 fail-fast metrics and logs** — Verify `ConfigReadErrors` counter increments and `LogWarning("migration-config.json not found")` fi… — PackageConfigStoreTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T037 [US3] **Test O-3 fail-fast** — Dedicated unit test: assert `ILogger.LogWarning` fires with message containing "migration-config.json not found" wh… — PackageConfigStoreTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

---

## Phase 6: EF Core Upgrader — MigrationJob Schema v1 → v2

**Purpose**: Migrate stored v1.0 `MigrationJob` records in the control plane DB to v2.0 schema. This is a guardrail requirement (Principle VII — determinism, breaking schema change requires upgrader).

- [X] T038 [META] **N/A** — Control plane uses an in-memory `JobStore` (`ConcurrentDictionary<string, MigrationJob>`); there is no EF Core database and no `Mi… — Migrations/ — Status: complete
- [X] T039 [META] **N/A** — No EF Core upgrader exists (see T038). No upgrader unit test required. — N/A — Status: complete

---

## Phase 6b: Robustness and Audit Tasks

**Purpose**: Address robustness gaps identified in analysis — eventual-consistency retry, Singleton registration audit, and FR-007 atomicity test.

- [X] T047 [META] Implement retry-with-back-off in `PackageConfigStore.ReadAsync` for blob-store eventual consistency: if `IArtefactStore.ExistsAsync` returns… — N/A — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T048 [META] **PASS** — Audit complete. All `IModule` implementations are registered as `AddTransient`: `WorkItemsModule`, `NodesModule`, `TeamsModule`,… — N/A — Status: complete
- [X] T049 [META] Unit test: `WriteAsync_WhenWriteSucceeds_SubmitIsCalledOnce` and `WriteAsync_WhenWriteThrows_SubmitIsNeverCalled` — in `tests/DevOpsMigratio… — tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/QueueCommandTests.cs — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

---

## Phase 7: Documentation Sync ⛔ MANDATORY

**Purpose**: Update all canonical docs referenced in discrepancies.md and plan.md before the branch may close.

- [X] T040 [META] Update `docs/agent-hosting.md` — add step 7a "Agent reads `migration-config.json` from package via `IPackageConfigStore.ReadAsync`" and step… — docs/agent-hosting.md — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T041 [META] Review `analysis/pending-actions.md` — update or close any actions related to this feature; add entry for Rule 23 amendment if not already p… — analysis/pending-actions.md — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T042 [META] Verify `specs/025-agent-config-package/discrepancies.md` — mark all 5 discrepancies as `Resolved`: (1) `migration-config.json` in `migration… — specs/025-agent-config-package/discrepancies.md — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

---

## Phase 8: Polish & Definition of Done

- [X] T043 [META] Run `dotnet clean DevOpsMigrationPlatform.slnx --nologo -v quiet && dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` — M… — N/A — Status: complete
- [X] T044 [META] Run `dotnet test DevOpsMigrationPlatform.slnx --filter "TestCategory!=SystemTest&TestCategory!=SystemTest_Simulated&TestCategory!=SystemTest… — N/A — Status: complete
- [X] T045 [META] Scenario run verified via `dotnet run`. `migration-config.json` written to `storage/queue-export-workitems-simulated-source/simulated_exampl… — migration-config.json — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031
- [X] T046 [META] `analysis/pending-actions.md` reviewed. All 025-related entries are owned and tracked (T041 added Rule 23 amendment entry). No untracked ent… — analysis/pending-actions.md — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/spec.md FR-004/FR-007, specs/031-platform-metrics-unification/spec.md D2, and specs/034-package-manager-adoption/tasks.md T027-T031

---

## Dependencies

```
Phase 1 (T001–T003) — no dependencies
Phase 2 (T004–T009) — no dependencies (can start alongside Phase 1)
  T006 → T007 (exception before interface)
  T007 → T008 (interface before implementation)
  T008 → T009 (implementation before DI extension)

Phase 3 US1 (T010–T024) — requires Phase 2 complete
  T015, T016 → T017 (remove Job fields before fixing all callsites)
  T007, T009 → T013 (IPackageConfigStore before agent wiring)
  T009 → T011, T012 (DI extension before CLI wiring)
  T009, T014 → T013 (registration before per-job scope)
  T008 → T018, T019, T020 (implementation before observability)
  T008 → T022, T023, T024 (implementation before tests)

Phase 4 US2 (T025–T031) — requires Phase 3 complete
  T007, T009 → T027, T028 (TFS agent depends on interface + DI extension)
  T011, T013 → T029 (CLI + agent wiring before system test)

Phase 5 US3 (T032–T037) — requires Phase 2 complete (can run alongside Phase 4)
  T008 → T033, T034 (implementation before unit tests)
  T013 → T035 (agent wiring before fail-fast path)

Phase 6 Upgrader (T038–T039) — requires Phase 3 complete (MigrationJob shape known)
Phase 6b Robustness (T047–T049) — requires Phase 2 complete; can run alongside Phase 4–5
  T008 → T047 (implementation before retry logic)
  T013 → T048 (per-job scope before Singleton audit)
  T011, T013 → T049 (CLI + agent wiring before atomicity test)
Phase 7 Doc Sync (T040–T042) — requires Phases 3–5 complete
Phase 8 Polish (T043–T046) — requires all phases complete
```

## Parallel Execution Opportunities

| Parallel set | Tasks |
|---|---|
| Foundation setup | T001, T002, T003, T004, T005, T006 |
| After T008: parallel test authoring | T022, T023, T024 |
| US2 + US3 | T025–T031 can overlap with T032–T037 |
| Robustness tasks | T047, T048, T049 (can run alongside Phase 5–6) |
| Doc sync tasks | T040, T041, T042 |

## Implementation Strategy

**MVP** = Phase 2 + Phase 3 (US1): establishes the full write-and-read path with working `IOptions<T>` in the agent. This alone fixes the root bug.

**Full delivery** = Phases 1–8: includes TFS agent coverage (US2), fail-fast for legacy packages (US3), EF Core upgrader, and doc sync.

**Total tasks**: 50
- Phase 1 (Setup): 3
- Phase 2 (Foundation): 6
- Phase 3 (US1 — P1 MVP): 15
- Phase 4 (US2 — P2): 8 (includes T029b)
- Phase 5 (US3 — P3): 6
- Phase 6 (Upgrader): 2
- Phase 6b (Robustness): 3 (T047, T048, T049)
- Phase 7 (Doc Sync): 3
- Phase 8 (Polish): 4

**Parallel opportunities**: 21 tasks marked `[P]`

---

## Current status (reconciled 2026-05-17)

- Canonical outcome: **6 complete**, **0 incomplete**, **44 complete/superseded**.
- This spec’s original CLI-write/package-store design has been superseded by the unified `Job` + `ConfigPayload` + agent materialization model.

## Remaining incomplete work

- None.

## Completed because superseded

- Superseded task IDs: `T001-T014` (except `T015`), `T016-T037`, `T040-T042`, `T045-T047`, `T049`, `T029b`.
- Supersession sources: `specs/025.1-fold-to-job/spec.md`, `specs/031-platform-metrics-unification/spec.md`, `specs/034-package-manager-adoption/tasks.md`.

## Contradictions and reconciliation

- Contradiction: tasks requiring CLI to write `migration-config.json` conflict with current guardrail Rule 23 and current runtime flow.
- Reconciled truth: CLI submits `Job.ConfigPayload`; agent writes `.migration/migration-config.json` after lease acquisition.
- Contradiction: `IPackageConfigStore`/`PackageConfigStore` tasks conflict with current `IPackageMigrationConfigLoader` + `IPackageAccess` boundary model.

## Verification evidence

- Runtime contract evidence: `src/DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs`.
- CLI payload evidence: `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs`.
- Agent materialization evidence: `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`.
- Package config loader evidence: `src/DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem/PackageMigrationConfigLoader.cs`.
- Guardrail evidence: `.agents/20-guardrails/core/architecture-boundaries.md`.

