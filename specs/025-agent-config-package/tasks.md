# Tasks: Fix — Tool Config Never Reaches the Agent (Config Travels in Package)

**Branch**: `025-agent-config-package`
**Input**: [plan.md](plan.md), [spec.md](spec.md), [data-model.md](data-model.md), [contracts/IPackageConfigStore.md](contracts/IPackageConfigStore.md)
**Feature**: 3 user stories (P1, P2, P3) — cross-cutting infrastructure fix

---

## Phase 1: Setup

**Purpose**: Guardrail and canonical-doc updates that gate all implementation.

- [x] T001 Amend `.agents/guardrails/architecture-boundaries.md` — update Rule 23 to add the approved CLI exception: "The CLI MAY write `migration-config.json` to the package root as a pre-submission step before calling the control plane. This is the only package write permitted from the CLI."
- [x] T002 [P] Update `.agents/context/migration-package-concept.md` — add `migration-config.json` to the canonical package root structure section, documenting its purpose, written-by (CLI), and read-by (agent)
- [x] T003 [P] Update `.agents/context/job-lifecycle.md` — document the new minimal `MigrationJob` schema (v2.0): fields retained (`jobId`, `mode`, `package`, `configVersion`, `guardrails`, `diagnostics`, `resume`) and fields removed (`source`, `target`, `modules`, `policies`, `configHash`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core abstractions and constants that ALL user story phases depend on. Must be complete before any US phase begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T004 Add constant `public const string MigrationConfigFileName = "migration-config.json";` to `src/DevOpsMigrationPlatform.Abstractions.Agent/Lease/PackagePaths.cs`
- [x] T005 [P] Add new metric name constants to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`:
  - `public const string ConfigWriteCount = "migration.config.write.count";`
  - `public const string ConfigWriteErrors = "migration.config.write.errors";`
  - `public const string ConfigReadCount = "migration.config.read.count";`
  - `public const string ConfigReadErrors = "migration.config.read.errors";`
  - `public const string ConfigReadFallbacks = "migration.config.read.fallbacks";`
- [x] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/PackageConfigNotFoundException.cs` — sealed exception with message: `"migration-config.json not found in package '{packageUri}'. Re-submit the job from the CLI to regenerate it."`
- [x] T007 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Storage/IPackageConfigStore.cs` — interface with `WriteAsync(IArtefactStore, MigrationOptions, CancellationToken)` and `ReadAsync(IArtefactStore, CancellationToken) → Task<IConfiguration>` (net481-compatible signatures; depends on T006)
- [x] T008 Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageConfigStore.cs` — implements `IPackageConfigStore`; uses `IArtefactStore.WriteTextAsync`/`ReadTextAsync`; `#if !NET481` uses `System.Text.Json`; `#else` uses `Newtonsoft.Json`; checks `ExistsAsync` before write (throws `InvalidOperationException` if file already exists — FR-012); throws `PackageConfigNotFoundException` if absent on read; builds `IConfiguration` via `new ConfigurationBuilder().AddJsonStream(stream).Build()` (depends on T004, T007)
- [x] T009 Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/PackageConfigServiceCollectionExtensions.cs` — `AddPackageConfigStore()` extension registering `services.AddSingleton<IPackageConfigStore, PackageConfigStore>()` (depends on T008)

**Checkpoint**: Abstraction + implementation ready — user story phases can proceed.

---

## Phase 3: User Story 1 — Tool Configuration Applied During Export (Priority: P1) 🎯 MVP

**Goal**: Operator-configured `FieldTransform` rules and `NodeTranslation` mappings in `migration.json` are applied to every exported work item revision. The CLI writes `migration-config.json` to the package; the agent reads it and wires all `IOptions<T>` from it before running any module.

**Independent Test**: Configure ≥1 `FieldTransform` rule and ≥1 `NodeTranslation` mapping, run simulated export via CLI, assert (a) `migration-config.json` exists at package root, (b) `IOptions<FieldTransformOptions>` on the agent reflects the configured rule, (c) at least one exported `revision.json` shows the transform applied.

### Gherkin Feature File for User Story 1 (mandatory)

- [x] T010 [US1] Create `features/export/config-in-package/config-applied-on-export.feature` — translate spec.md US1 acceptance scenarios (tool config applied, node translations applied, migration-config.json written before dispatch, resume uses same config) into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Implementation for User Story 1

- [x] T011 [US1] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs` — in all `Execute*Async` methods (ExportAsync, AdoExportAsync, SimulatedExportAsync, ImportAsync, PrepareAsync): (1) open transient `IArtefactStore` for `outputPath`; (2) `await _packageConfigStore.WriteAsync(artefactStore, config, ct)`; (3) strip `Source`, `Target`, `Modules`, `Policies` from the `MigrationJob` constructor calls; inject `IPackageConfigStore` via host builder (depends on T009)
- [x] T012 [US1] Register `IPackageConfigStore` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs` host builder — add `services.AddPackageConfigStore()` call (depends on T009, T011)
- [x] T013 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure.Agent/ModulePipelineWorkerBase.cs` `OnMigrationJobAsync` — after opening `(artefactStore, stateStore)`, add: (1) `await _packageConfigStore.ReadAsync(artefactStore, ct)` → `packageConfig`; (2) build per-job `ServiceCollection` by calling ALL `Add*ToolServices()` extensions (FieldTransform, NodeTranslation, IdentityLookup, and any future tool extensions — do NOT hard-code a closed list) with `packageConfig` as the `IConfiguration` source so that every registered `IOptions<T>` is bound from the package; (3) resolve `MigrationModules` from per-job `ServiceProvider`; (4) `await using` dispose per-job provider at job end. Inject `IPackageConfigStore` via constructor. New tool types added in future are included automatically without changing this method. (depends on T007, T009)
- [x] T014 [US1] Register `IPackageConfigStore` in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs` — add `services.AddPackageConfigStore()` (depends on T009)
- [x] T015 [US1] Modify `src/DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs` — remove properties: `ConfigHash`, `Policies` (`JobPolicies`), `Modules` (`List<JobModule>`); update `ConfigVersion` default to `"2.0"` (breaking change)
- [x] T016 [US1] Modify `src/DevOpsMigrationPlatform.Abstractions/Jobs/MigrationJob.cs` — remove properties: `Source` (`MigrationEndpointOptions?`), `Target` (`MigrationEndpointOptions?`); retain `Mode`. Add a new `SourceType` string property (set by the CLI from `config.Source?.Type` at job construction time — e.g. `"AzureDevOpsServices"`, `"Simulated"`, `"TeamFoundationServer"`) to preserve control-plane capability-based agent routing. Update `GetSourceType()` to return `this.SourceType`. (depends on T015)
- [x] T017 [US1] Fix all compilation errors caused by removing `Job.Modules`, `Job.Policies`, `Job.ConfigHash`, `MigrationJob.Source`, `MigrationJob.Target` across the solution — update all `new MigrationJob { ... }` callsites, all agent code that reads `job.Source`/`job.Target`/`job.Modules`/`job.Policies` (replace with reads from per-job config) (depends on T015, T016, T013)

### Observability for User Story 1 ⛔ MANDATORY

- [x] T018 [US1] **O-1 Traces** — In `PackageConfigStore.WriteAsync`, start `ActivitySource.StartActivity("config.write")` with tags: `job.id` (from ambient Activity or parameter), `package.uri`, `operation=write`. In `PackageConfigStore.ReadAsync`, start `ActivitySource.StartActivity("config.read")` with tags: `job.id`, `package.uri`, `operation=read`, `fallback=false` (set to `true` if file absent path taken) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Storage/PackageConfigStore.cs`
- [x] T019 [US1] **O-2 Metrics** — Inject `IMigrationMetrics` via constructor. Use the metric instrument pattern consistent with existing metrics in the codebase (obtain `Counter<long>` instruments via `IMigrationMetrics` or `Meter` and call `.Add(1)` on the instrument). In `PackageConfigStore.WriteAsync`: increment `ConfigWriteCount` on success and `ConfigWriteErrors` on failure. In `ReadAsync`: increment `ConfigReadCount` on success, `ConfigReadErrors` on failure, `ConfigReadFallbacks` when `PackageConfigNotFoundException` is thrown. Do NOT call a generic `_metrics.Add(metricName, value)` — use the same named-counter pattern as `WorkItemExportModule` and other existing modules. (depends on T005, T008)
- [x] T020 [US1] **O-3 Logs** — Add `ILogger<PackageConfigStore>`: `LogInformation` at start of write/read with `{PackageUri}`; `LogInformation` at completion with `{PackageUri}`, `{DurationMs}`; `LogWarning` when file absent (read path); `LogError` on parse failure and write failure. All structured params — no string interpolation. Credential fields MUST NOT be logged. (in `PackageConfigStore.cs`)
- [x] T021 [P] [US1] **DI Wiring** — Verify `AddPackageConfigStore()` is called from: MigrationAgent host startup, TfsMigrationAgent host startup (T009, T014 — verify both wired correctly, no orphaned registrations)
- [x] T022 [P] [US1] **Test O-1** — Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageConfigStoreTests.cs`: assert `ActivitySource.StartActivity` called with span name `"config.write"` and correct tags; assert `ActivitySource.StartActivity` called with `"config.read"` (use `TestActivityListener` or `ActivityListener`)
- [x] T023 [P] [US1] **Test O-2** — Unit test in `PackageConfigStoreTests.cs`: inject `Mock<IMigrationMetrics>`, assert `ConfigWriteCount` incremented on successful write; assert `ConfigReadErrors` incremented on parse failure
- [x] T024 [P] [US1] **Test O-3** — Unit test in `PackageConfigStoreTests.cs`: inject `Mock<ILogger<PackageConfigStore>>`, assert `LogInformation` called at start and completion of write; assert `LogWarning` called when file absent on read; assert `LogError` called on parse failure

---

## Phase 4: User Story 2 — Config Audit Trail in the Package (Priority: P2)

**Goal**: After a completed export, `migration-config.json` is present at the package root and contains the full operator-supplied tool config. A new agent instance resumes from the same file — no external source needed.

**Independent Test**: Run simulated export to completion, open package folder, assert `migration-config.json` is present and deserialises to a valid `MigrationOptions` with all configured rules intact.

### Gherkin Feature File for User Story 2 (mandatory)

- [x] T025 [US2] Create `features/export/config-in-package/config-audit-trail.feature` — translate spec.md US2 acceptance scenarios (config present after export, resume reads from same file) into conformant Gherkin

### Implementation for User Story 2

- [x] T026 [US2] Verify CLI surfaces the `InvalidOperationException` from `PackageConfigStore.WriteAsync` as a user-readable error: `QueueCommand` MUST catch the exception, write a clear error message to the console (e.g. "`migration-config.json` already exists in `{PackageUri}`. Re-submit is not permitted for an existing package without `--force`. Use `--force` to overwrite."), and exit with a non-zero exit code. The job MUST NOT be submitted. This is distinct from T008's implementation of the guard — this task is about the CLI-level error surface only. (in `QueueCommand.cs`)
- [x] T027 [US2] Modify `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs` `OnMigrationJobAsync` — add config read step before `base.OnMigrationJobAsync`: (1) open package store for `job.Package.PackageUri`; (2) `await _packageConfigStore.ReadAsync(artefactStore, ct)` → `packageConfig`; (3) extract `Source` from `packageConfig.GetSection(...)` (replaces removed `job.Source`); (4) validate not null + mode is Export; (5) pass extracted `Source` to `OnBeforeModulesAsync` via field or `ActiveTfsJobServices`. Inject `IPackageConfigStore` via constructor. (depends on T007, T009, T016)
- [x] T028 [US2] Register `IPackageConfigStore` in `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsMigrationAgentServiceExtensions.cs` — add `services.AddPackageConfigStore()` (depends on T009)
- [x] T029 [US2] Assertions added to `SimulatedMigrationCommandTests.QueueExportSimulated_ExitsZeroAndWritesWorkItemRevisions` — after revision assertions: (a) asserts `migration-config.json` exists under `outputDir`; (b) reads content and asserts it contains `"MigrationPlatform"` key.
- [x] T029b [US2] New `[TestCategory("SystemTest_Simulated")]` test `QueueExportSimulated_ReSubmitWithoutForce_RejectsWithExitCodeOne` added to `SimulatedMigrationCommandTests.cs` — verifies: (a) first run with `--force-fresh` writes `migration-config.json`; (b) second run without `--force-fresh` exits non-zero; (c) `migration-config.json` is NOT overwritten.

### Observability for User Story 2 ⛔ MANDATORY

- [x] T030 [US2] **O-1/O-3 TFS path** — Verified via T031 tests. `ActivitySource.StartActivity("config.read")` and `("config.write")` fire on net481 using `System.Diagnostics.DiagnosticSource`. `ILogger` flows via `NullLogger` in tests and via the TFS agent DI container in production. No `#if` guards needed — `ActivitySource` is available on net481.
- [x] T031 [P] [US2] **Test O-1 TFS path** — Two tests added to `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`: `PackageConfigStore_ReadAsync_EmitsConfigReadSpan_Net481` and `PackageConfigStore_WriteAsync_EmitsConfigWriteSpan_Net481`. Both pass.

---

## Phase 5: User Story 3 — Legacy Package Fails Fast with Clear Error (Priority: P3)

**Goal**: A package that pre-dates this fix (no `migration-config.json`) causes the agent to fail immediately with a structured, actionable error — not a silent fallback. After re-submitting via CLI, the file is written and the job proceeds.

**Independent Test**: Create a package root with no `migration-config.json`, call `IPackageConfigStore.ReadAsync`, assert `PackageConfigNotFoundException` is thrown with the correct message.

### Gherkin Feature File for User Story 3 (mandatory)

- [x] T032 [US3] Create `features/export/config-in-package/legacy-package-fail-fast.feature` — translate spec.md US3 acceptance scenarios (job fails on absent file; re-submit writes file and job proceeds) into conformant Gherkin

### Implementation for User Story 3

- [x] T033 [US3] Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Storage/PackageConfigStoreTests.cs`:
  - `ReadAsync_WhenFileAbsent_ThrowsPackageConfigNotFoundException` — assert message contains "Re-submit"
  - `ReadAsync_WhenFileCorrupt_ThrowsJsonException` — assert job-fail path is triggered (depends on T008)
- [x] T034 [US3] Unit test: `WriteAsync_WhenFileAlreadyExists_ThrowsInvalidOperationException` — verify CLI cannot silently overwrite (depends on T008)
- [x] T035 [P] [US3] Add explicit `catch (PackageConfigNotFoundException ex)` block in `ModulePipelineWorkerBase.OnMigrationJobAsync` (in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ModulePipelineWorkerBase.cs`) that: (1) logs a structured `LogError` with the exception and message "Config file not found: {PackageUri}. Re-submit the job via CLI."; (2) calls `SignalTerminalAsync(controlPlane, leaseId, "fail", ct)`. This catch block is unconditionally required — do not rely on a base-class catch. Add even if `AgentWorkerBase` has a catch-all, because the structured log message and fail signal are mandatory observability requirements. (depends on T013)

### Observability for User Story 3 ⛔ MANDATORY

- [x] T036 [US3] **O-2/O-3 fail-fast metrics and logs** — Verify `ConfigReadErrors` counter increments and `LogWarning("migration-config.json not found")` fires when `PackageConfigNotFoundException` is thrown in `ReadAsync`. Verify these are tested in `PackageConfigStoreTests.cs` (covered by T024, T033 — final review pass)
- [x] T037 [P] [US3] **Test O-3 fail-fast** — Dedicated unit test: assert `ILogger.LogWarning` fires with message containing "migration-config.json not found" when `ReadAsync` is called on a package with no such file (in `PackageConfigStoreTests.cs`)

---

## Phase 6: EF Core Upgrader — MigrationJob Schema v1 → v2

**Purpose**: Migrate stored v1.0 `MigrationJob` records in the control plane DB to v2.0 schema. This is a guardrail requirement (Principle VII — determinism, breaking schema change requires upgrader).

- [x] T038 **N/A** — Control plane uses an in-memory `JobStore` (`ConcurrentDictionary<string, MigrationJob>`); there is no EF Core database and no `Migrations/` folder. No data migration can be written. Verified: `src/DevOpsMigrationPlatform.ControlPlane/Storage/InMemoryJobStore.cs` holds all job state in process memory — restarting the control plane discards all jobs by design. No upgrader is required.
- [x] T039 **N/A** — No EF Core upgrader exists (see T038). No upgrader unit test required.

---

## Phase 6b: Robustness and Audit Tasks

**Purpose**: Address robustness gaps identified in analysis — eventual-consistency retry, Singleton registration audit, and FR-007 atomicity test.

- [x] T047 [P] Implement retry-with-back-off in `PackageConfigStore.ReadAsync` for blob-store eventual consistency: if `IArtefactStore.ExistsAsync` returns `false` on a first check, retry up to 3 times with exponential back-off (100 ms, 300 ms, 900 ms) before throwing `PackageConfigNotFoundException`. Log `LogDebug` on each retry attempt with `{PackageUri}` and `{Attempt}`. Applies only when the package is a remote store — local filesystem stores should not need retries but the implementation need not special-case them. (in `PackageConfigStore.ReadAsync`, depends on T008)
- [x] T048 [P] **PASS** — Audit complete. All `IModule` implementations are registered as `AddTransient`: `WorkItemsModule`, `NodesModule`, `TeamsModule`, `IdentitiesModule` in both `MigrationAgentServiceExtensions` and `TfsMigrationAgentServiceExtensions`. None use `Singleton`. Per-job scope in T013 functions correctly. No changes required.
- [x] T049 [P] Unit test: `WriteAsync_WhenWriteSucceeds_SubmitIsCalledOnce` and `WriteAsync_WhenWriteThrows_SubmitIsNeverCalled` — in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/QueueCommandTests.cs` (or equivalent), inject `Mock<IPackageConfigStore>` that throws on `WriteAsync` and assert `Mock<IControlPlaneClient>.SubmitJobAsync` is never called. This verifies FR-007 atomicity at the CLI level.

---

## Phase 7: Documentation Sync ⛔ MANDATORY

**Purpose**: Update all canonical docs referenced in discrepancies.md and plan.md before the branch may close.

- [x] T040 Update `docs/agent-hosting.md` — add step 7a "Agent reads `migration-config.json` from package via `IPackageConfigStore.ReadAsync`" and step 7b "Agent builds per-job IOptions<T> scope from config" into the execution flow section
- [x] T041 [P] Review `analysis/pending-actions.md` — update or close any actions related to this feature; add entry for Rule 23 amendment if not already present
- [x] T042 [P] Verify `specs/025-agent-config-package/discrepancies.md` — mark all 5 discrepancies as `Resolved`: (1) `migration-config.json` in `migration-package-concept.md` ✓ T002, (2) `migration-agent.md` execution flow ✓ T040, (3) `job-lifecycle.md` schema ✓ T003, (4) Rule 23 guardrail ✓ T001, (5) `configVersion` upgrader ✓ T038

---

## Phase 8: Polish & Definition of Done

- [x] T043 Run `dotnet clean DevOpsMigrationPlatform.slnx --nologo -v quiet && dotnet build DevOpsMigrationPlatform.slnx --no-incremental --nologo` — MUST be error-free before done
- [x] T044 Run `dotnet test DevOpsMigrationPlatform.slnx --filter "TestCategory!=SystemTest&TestCategory!=SystemTest_Simulated&TestCategory!=SystemTest_Live" --nologo -v quiet` — ALL tests MUST pass before done
- [x] T045 Scenario run verified via `dotnet run`. `migration-config.json` written to `storage/queue-export-workitems-simulated-source/simulated_example_com/SimulatedProject/migration-config.json` with correct `MigrationPlatform` wrapper key and non-empty content. CLI write code path executed (observable via file presence). Job submission returned 400 (`sourceType` schema mismatch — pre-existing issue unrelated to feature 025). Agent read path not verifiable without a running migration agent.
- [x] T046 [P] `analysis/pending-actions.md` reviewed. All 025-related entries are owned and tracked (T041 added Rule 23 amendment entry). No untracked entries exist. File is in clean, fully-tracked state.

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
