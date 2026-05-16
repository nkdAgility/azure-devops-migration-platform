---

description: "Task list for ICapture Interface — Unified Capture Contract"
---

# Tasks: ICapture Interface — Unified Capture Contract

**Input**: Design documents from `/specs/032-icapture-interface/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅
**Branch**: `032-icapture-interface`

**Tests**: Business logic test tasks generated for `DependencyCapture` (new class) and `SimulatedDependencyDiscoveryServiceFactory` (new class). Observability tests (O-1, O-2, O-4) are MANDATORY for US2 — the only user story that introduces new production code with new telemetry obligations. US1 retains existing signals (no new test obligations). US3 is pure deletion (no test obligations).

**Organization**: Tasks grouped by user story. US1 and US2 are both P1; US2 functionally depends on US1's dispatcher rewrite. US3 (P2) depends on US2 (DependencyCapture replaces IProjectAnalyser's responsibility before the interface is deleted).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks in this phase)
- **[Story]**: Which user story this task belongs to
- Exact file paths included in all descriptions

---

## Phase 1: Foundational (Blocking Prerequisites)

**Purpose**: Define the `ICapture` contract and update the two abstract interfaces that ALL user stories depend on. No user story work can begin until T001–T003 are complete.

**⚠️ CRITICAL**: These three tasks change the public surface of `DevOpsMigrationPlatform.Abstractions.Agent` (ICapture, IModule, IJobPlanExecutor) — all three interfaces are in this assembly. Every subsequent task compiles against them.

- [x] T001 Create `ICapture` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/ICapture.cs` — `string Name { get; }` and `Task CaptureAsync(InventoryContext context, CancellationToken ct)` per contracts/ICapture.md; must compile for both `net481` and `net10.0` TFMs (no BCL-only APIs); file MUST begin with `// SPDX-License-Identifier: AGPL-3.0-only` followed by `// Copyright (c) Naked Agility Limited` (SA1633 is a build error)
- [x] T002 Update `IModule` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs` — inherit `ICapture`; remove `InventoryAsync(InventoryContext, CancellationToken)` declaration; all other members (`DependsOn`, `SupportsInventory`, `SupportsExport`, `SupportsPrepare`, `SupportsImport`, `SupportsValidate`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`) remain unchanged (depends on T001)
- [x] T003 Update `IJobPlanExecutor.ExecuteTasksAsync` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Context/IJobPlanExecutor.cs` — replace `IReadOnlyDictionary<string, IModule> modulesByName` with `IReadOnlyDictionary<string, ICapture> captureHandlersByName` per contracts/IJobPlanExecutor.md; `ExecuteExportPhaseAsync` and `ExecuteImportPhaseAsync` signatures unchanged (depends on T001)

**Checkpoint**: Foundation complete — `ICapture.cs`, `IModule.cs`, and `IJobPlanExecutor.cs` compile cleanly. All user story phases can now begin.

---

## Phase 2: User Story 1 — Modules dispatch via ICapture (Priority: P1) 🎯 MVP

**Goal**: Rename `InventoryAsync` → `CaptureAsync` across `ModuleBase` and all four concrete modules; rewrite `JobPlanExecutor`'s `TaskKind.Capture` dispatch to use a single `captureHandlersByName` dictionary; update `JobAgentWorker` to assemble that dictionary; update all test call-sites. After this phase, any `capture.*` task resolves through a unified lookup — no module-vs-analyser branching.

**Independent Test**: A simulated `capture.workitems.{org}.{project}` task plan executes and calls `WorkItemsModule.CaptureAsync` (not `InventoryAsync`) for each task and the expected inventory artefact is written. All 984+ existing tests pass.

### Gherkin Feature File for User Story 1 (mandatory — ATDD Phase 1 artifact)

- [x] T004 [US1] Create `features/inventory/icapture-rename/US1-modules-dispatch-via-icapture.feature` — translate spec.md US1 acceptance scenarios (3 scenarios) into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md`; commit before any step definitions or production code

### Implementation for User Story 1

- [x] T005 [P] [US1] Rename `InventoryAsync` → `CaptureAsync` (override) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs` — update the virtual no-op body to log `"Capture phase is not supported by module {Module}."` (depends on T002, T004)
- [x] T006 [P] [US1] Rename `InventoryAsync` → `CaptureAsync` (override) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs` — method body, parameters, and return type unchanged (depends on T002)
- [x] T007 [P] [US1] Rename `InventoryAsync` → `CaptureAsync` (override) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs` — method body, parameters, and return type unchanged (depends on T002)
- [x] T008 [P] [US1] Rename `InventoryAsync` → `CaptureAsync` (override) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs` — method body, parameters, and return type unchanged (depends on T002)
- [x] T009 [P] [US1] Rename `InventoryAsync` → `CaptureAsync` (override) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs` — method body, parameters, and return type unchanged (depends on T002)
- [x] T010 [US1] Rewrite `TaskKind.Capture` branch in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs` — replace `IProjectAnalyser`-branching logic with: `handlerName = GetModuleName(task.Id, captureHandlersByName.Keys)`; `if (!captureHandlersByName.TryGetValue(handlerName, out var captureHandler)) { log Error + skip; continue; }`; `await captureHandler.CaptureAsync(scopedCtx, ct)`; remove all `IProjectAnalyser` imports and references from this file (depends on T002, T003)
- [x] T011 [US1] Update `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` — add `private static IReadOnlyDictionary<string, ICapture> BuildCaptureHandlers(IEnumerable<IModule> modules, IServiceProvider serviceProvider)` helper per data-model.md §10 (step-1: SupportsInventory=true modules cast to ICapture; step-2: union with `serviceProvider.GetServices<ICapture>()` de-duplicating by name); replace `depModuleMap` (≡ `modulesByName` in plan.md) in `OnDiscoveryJobAsync` and `inventoryModuleMap` (≡ `modulesByName` in plan.md) in `OnMigrationJobAsync` with `captureHandlersByName = BuildCaptureHandlers(...)` (depends on T001, T002, T003)
- [x] T012 [P] [US1] Update all `InventoryAsync` call-sites in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/` — rename every mock setup and assertion that references `InventoryAsync` to `CaptureAsync` (depends on T005–T009)
- [x] T013 [US1] Update `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobPlanExecutorTests.cs` — replace `modulesByName: IReadOnlyDictionary<string, IModule>` with `captureHandlersByName: IReadOnlyDictionary<string, ICapture>` at all `ExecuteTasksAsync` call-sites; update any mock dictionaries accordingly; add test asserting `GetModuleName("capture.workitems.org.project", ...)` returns `"workitems"` and `GetModuleName("capture.dependencies.org.project", ...)` returns `"dependencies"` (FR-015 name-extraction contract; guards against off-by-one index errors); add test asserting that when `captureHandlersByName` does not contain the extracted handler name, `ILogger.LogError` is called with structured `{TaskId}` and `{HandlerName}` params and the task is skipped without exception (depends on T003, T010)

### Observability for User Story 1 ⛔ MANDATORY

> US1 is a method rename. Existing O-1 spans, O-2 metrics, and O-4 progress events on all four modules carry through the rename unchanged — no new instrumentation required for those signals. The single new observable requirement is the missing-handler `Error` log in `JobPlanExecutor` (Operations Table, `capture.dispatch` row). O-4 CLI Visible and observability unit tests are not applicable here because no new span/metric/progress surface is introduced.

- [x] T014 [US1] **O-3 Logs** — In `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobPlanExecutor.cs` `TaskKind.Capture` missing-handler path (implemented in T010): confirm `_logger.LogError("Task {TaskId} references capture handler '{HandlerName}', but it is not registered. Skipping.", task.Id, handlerName)` uses structured params only (no string interpolation); `DataClassification` not required for `taskId`/`handlerName` (internal routing metadata, not customer data)
- [x] T015 [US1] **DI Wiring** — In `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` (depends on T011): verify `BuildCaptureHandlers` step-1 correctly filters `SupportsInventory=true` and casts `IModule` → `ICapture`; verify step-2 union uses `StringComparer.OrdinalIgnoreCase` and does not double-register a module that is also returned by `GetServices<ICapture>()`; confirm no orphaned `modulesByName` references remain in the file

**Checkpoint**: US1 complete. `capture.workitems.*`, `capture.identities.*`, `capture.nodes.*`, `capture.teams.*` tasks dispatch via `captureHandlersByName`; `InventoryAsync` is gone from the codebase; all existing 984+ tests pass.

---

## Phase 3: User Story 2 — Pure capture handlers (DependencyCapture) (Priority: P1)

**Goal**: Create `DependencyCapture : ICapture` (not `IModule`) to own per-project dependency discovery; create `SimulatedDependencyDiscoveryServiceFactory` to close the Simulated connector gap; register both in DI; add full O-1 through O-4 observability. After this phase, `capture.dependencies.{org}.{project}` tasks execute through the same unified dispatch as `capture.workitems.*` tasks — no special cases.

**Independent Test**: A Dependencies job plan includes `capture.dependencies.{org}.{project}` tasks; each executes `DependencyCapture.CaptureAsync` and writes `discovery/{org}/{project}/dependencies.csv`. A Simulated-sourced plan completes without external connectivity.

### Gherkin Feature File for User Story 2 (mandatory — ATDD Phase 1 artifact)

- [x] T016 [US2] Create `features/inventory/dependency-capture/US2-pure-capture-handlers.feature` — translate spec.md US2 acceptance scenarios (4 scenarios, including US2 Scenario 4 Simulated connector) into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md`; commit before any step definitions or production code

### Metric Infrastructure (prerequisites for DependencyCapture)

- [x] T017 [US2] Add 4 new metric constants to `src/DevOpsMigrationPlatform.Abstractions/WellKnownAgentMetricNames.cs` under `// --- Dependencies Capture ---`: `DependenciesCaptureCount = "platform.dependencies.capture.count"`, `DependenciesCaptureDurationMs = "platform.dependencies.capture.duration_ms"`, `DependenciesCaptureErrors = "platform.dependencies.capture.errors"`, `DependenciesCaptureInFlight = "platform.dependencies.capture.in_flight"` per plan.md §Observability Contract
- [x] T018 [US2] Add 6 `IPlatformMetrics` method declarations to `src/DevOpsMigrationPlatform.Abstractions/IPlatformMetrics.cs` (`DependenciesCaptureStarted`, `DependenciesCaptureCompleted`, `DependenciesCaptureFailed`, `RecordDependenciesCaptureDuration(double milliseconds, MetricsTagList tags)`, `DependenciesCaptureInFlightIncrement`, `DependenciesCaptureInFlightDecrement`) and implement all six in the concrete `PlatformMetrics.cs` using `WellKnownAgentMetricNames` constants from T017; meter: `WellKnownMeterNames.Agent` (no new meter) (depends on T017)

### Implementation for User Story 2

- [x] T019 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Capture/DependencyCapture.cs` — `public sealed class DependencyCapture : ICapture`; constructor injects `IDependencyDiscoveryServiceFactory dependencyFactory`, `IDependencyOrchestrator orchestrator`, `ILogger<DependencyCapture> logger`, optional `IPlatformMetrics? metrics`, optional `IProgressSink? progressSink`; `Name => "dependencies"`; `CaptureAsync` follows data-model.md §6 steps 1–10 (increment in-flight → open root span → log start → emit Capturing → child span create_service → child span execute → child span write_csv → decrement in-flight → record count + duration → log complete → emit Captured; on exception: record errors + duration → log Error → emit Failed → re-throw); file MUST begin with `// SPDX-License-Identifier: AGPL-3.0-only` followed by `// Copyright (c) Naked Agility Limited` (depends on T016, T017, T018)
- [x] T020 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/DependencyDiscovery/SimulatedDependencyDiscoveryServiceFactory.cs` — `public sealed class SimulatedDependencyDiscoveryServiceFactory : IDependencyDiscoveryServiceFactory`; constructor resolves `IWorkItemLinkAnalysisService` from keyed singleton `"Simulated"`; `Create(organisations, policies)` and `CreateForProject(allOrganisations, orgUrl, projectName, policies)` both delegate to the injected `SimulatedWorkItemLinkAnalysisService` per data-model.md §7 and research.md D-004; file MUST begin with `// SPDX-License-Identifier: AGPL-3.0-only` followed by `// Copyright (c) Naked Agility Limited`
- [x] T021 [US2] Update `src/DevOpsMigrationPlatform.Infrastructure.Agent/ServiceCollectionExtensions.cs` — add `public static IServiceCollection AddDependencyCaptureServices(this IServiceCollection services)` extension method; registers `services.AddSingleton<ICapture, DependencyCapture>()` (NOT as IModule); call this extension from the MigrationAgent host startup (depends on T019)
- [x] T022 [US2] Update `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` — inside `AddSimulatedDependencyAnalysis`, add `services.AddSingleton<IDependencyDiscoveryServiceFactory, SimulatedDependencyDiscoveryServiceFactory>()` per data-model.md §7 (depends on T020)
- [x] T023 [P] [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Capture/DependencyCaptureTests.cs` — unit tests using Reqnroll.MSTest + `MockBehavior.Strict`; cover: happy path calls `IDependencyDiscoveryServiceFactory.CreateForProject` and `IDependencyOrchestrator.CaptureProjectAsync`; exception path propagates and calls `DependenciesCaptureFailed`; verify all observable outputs per plan.md §Observability Contract Tests Required (depends on T019)
- [x] T024 [P] [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/DependencyDiscovery/SimulatedDependencyDiscoveryServiceFactoryTests.cs` — unit tests verifying `Create` and `CreateForProject` both delegate to the injected `SimulatedWorkItemLinkAnalysisService`; verify factory can be resolved without external connectivity (depends on T020)

### Observability for User Story 2 ⛔ MANDATORY

> Source of truth for all span names, metric instruments, log events, and ProgressEvent stages: `plan.md ## Observability Contract`, Operations Table, row `dependency.capture`. Use `WellKnownTagNames` constants for all span tags — no string literals.

- [x] T025 [VERIFY] [US2] **O-1 Traces** (depends on T019) — In `src/DevOpsMigrationPlatform.Infrastructure.Agent/Capture/DependencyCapture.cs` `CaptureAsync`: open root span `ActivitySource.StartActivity("dependency.capture")` (source: `WellKnownActivitySourceNames.Discovery`) with tags `WellKnownTagNames.JobId`, `WellKnownTagNames.OrgUrl`, `WellKnownTagNames.ProjectName`, `WellKnownTagNames.CaptureHandler = "dependencies"`; open child span `"dependency.capture.create_service"` (tags: `OrgUrl`, `ProjectName`) wrapping `IDependencyDiscoveryServiceFactory.CreateForProject`; open child span `"dependency.capture.execute"` (tags: `OrgUrl`, `ProjectName`) wrapping `IDependencyOrchestrator.CaptureProjectAsync`; open child span `"dependency.capture.write_csv"` (tags: `OrgUrl`, `ProjectName`, `output.path`) wrapping CSV output path confirmation (VERIFY: implemented within T019)
- [x] T026 [VERIFY] [US2] **O-2 Metrics** (depends on T019, T017, T018) — In `src/DevOpsMigrationPlatform.Infrastructure.Agent/Capture/DependencyCapture.cs` `CaptureAsync`: call `_metrics?.DependenciesCaptureInFlightIncrement(tags)` at entry (decrement in `finally`); call `_metrics?.DependenciesCaptureStarted(tags)` at start; call `_metrics?.DependenciesCaptureCompleted(tags)` + `_metrics?.RecordDependenciesCaptureDuration(sw.Elapsed.TotalMilliseconds, tags)` on success; call `_metrics?.DependenciesCaptureFailed(tags)` + `_metrics?.RecordDependenciesCaptureDuration(sw.Elapsed.TotalMilliseconds, tags)` in catch; all `MetricsTagList` instances carry `job.id`, `org.url`, `project.name`; use `WellKnownAgentMetricNames` constants from T017 (VERIFY: implemented within T019)
- [x] T027 [VERIFY] [US2] **O-3 Logs** (depends on T019) — In `src/DevOpsMigrationPlatform.Infrastructure.Agent/Capture/DependencyCapture.cs` `CaptureAsync`: `_logger.LogInformation("Capture started for {Org}/{Project} via handler {Handler} (job {JobId})", ...)` at start; `_logger.LogInformation("Capture completed for {Org}/{Project} in {DurationMs}ms → {OutputPath} (job {JobId})", ...)` on success; `_logger.LogError("Capture failed for {Org}/{Project}: {ErrorType} {ErrorMessage} (job {JobId})", ...)` on exception; `_logger.LogWarning("Dependency slow: {Dependency} took {DurationMs}ms > {ThresholdMs}ms", ...)` for slow paths; `_logger.LogDebug("CSV already exists at {OutputPath}, overwriting", ...)` on overwrite; all fields use `DataClassification.Customer` scope for `{Org}`, `{Project}`, `{OutputPath}`; no string interpolation (VERIFY: implemented within T019)
- [x] T028 [VERIFY] [US2] **O-4 ProgressEvents** (depends on T019) — In `src/DevOpsMigrationPlatform.Infrastructure.Agent/Capture/DependencyCapture.cs` `CaptureAsync`: `_progressSink?.Emit(new ProgressEvent { Stage = "Capturing", ... })` at start; `_progressSink?.Emit(new ProgressEvent { Stage = "Captured", Metrics = new JobMetrics { Discovery = { Dependencies = new DependencyCounters { WorkItemsAnalysed = ..., ExternalLinksFound = ... } } }, ... })` on completion; `_progressSink?.Emit(new ProgressEvent { Stage = "Failed", ... })` on exception; `IProgressSink?` is optional constructor parameter (VERIFY: implemented within T019)
- [x] T029 [US2] **O-4 CLI Visible** — In `src/DevOpsMigrationPlatform.MigrationAgent/QueueCommand.cs` `BuildProgressRenderable`: add progress bar row for `DependencyCapture` in execution order (after inventory module rows, before `analyse.dependencies` row); add `DependencyCounters` property to `MigrationCounters` (type `DependencyCounters`, nullable — null when no dependency capture ran; confirmed absent from `MigrationCounters.cs`); create the `DependencyCounters` record type with at minimum `WorkItemsAnalysed` and `ExternalLinksFound` long properties if it does not already exist; verify `SnapshotMetricExporter.cs` already extracts `JobMetrics.Discovery.Dependencies` counters (plan.md §Observability Contract Wiring Checklist confirms `SnapshotMetricExporter.cs` requires no changes — verify and mark confirmed)
- [x] T030 [US2] **DI Wiring** — Verify `AddDependencyCaptureServices` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/ServiceCollectionExtensions.cs` registers `AddSingleton<ICapture, DependencyCapture>()` (not as IModule); verify `AddSimulatedDependencyAnalysis` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` registers `AddSingleton<IDependencyDiscoveryServiceFactory, SimulatedDependencyDiscoveryServiceFactory>()`; verify `AddDependencyCaptureServices` is called from the MigrationAgent host startup; verify `AddSimulatedDependencyAnalysis` is called for Simulated-connector host path; no orphaned registrations (depends on T021, T022)
- [x] T031 [P] [US2] **Test O-1** — Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Capture/DependencyCaptureTests.cs`: inject `TestActivityListener`; call `DependencyCapture.CaptureAsync`; assert `ActivitySource.StartActivity("dependency.capture")` was called with tags `job.id`, `org.url`, `project.name`, `capture.handler="dependencies"`; assert child spans `"dependency.capture.create_service"`, `"dependency.capture.execute"`, `"dependency.capture.write_csv"` were started (depends on T023)
- [x] T032 [P] [US2] **Test O-2** — Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Capture/DependencyCaptureTests.cs`: inject `Mock<IPlatformMetrics>(MockBehavior.Strict)`; call `CaptureAsync` (success path); assert `DependenciesCaptureInFlightIncrement`, `DependenciesCaptureStarted`, `DependenciesCaptureCompleted`, `RecordDependenciesCaptureDuration` called with correct `MetricsTagList` containing `job.id`, `org.url`, `project.name`; call again with simulated exception; assert `DependenciesCaptureFailed` called (not Completed) and `RecordDependenciesCaptureDuration` still called; assert in-flight is decremented in both paths (depends on T023)
- [x] T033 [P] [US2] **Test O-4** — Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Capture/DependencyCaptureTests.cs`: inject `Mock<IProgressSink>(MockBehavior.Strict)`; call `CaptureAsync` (success path); assert `Emit` called with `Stage = "Capturing"` at start; assert `Emit` called with `Stage = "Captured"` and non-null `Metrics.Discovery.Dependencies` (with `WorkItemsAnalysed ≥ 0`, `ExternalLinksFound ≥ 0`) on completion; call again with simulated exception; assert `Emit` called with `Stage = "Failed"` (depends on T023)

- [x] T044 [US2] **Test O-3** — Unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Capture/DependencyCaptureTests.cs`: inject `Mock<ILogger<DependencyCapture>>(MockBehavior.Strict)`; call `CaptureAsync` (success path); assert `LogInformation` called with structured template containing `{Org}`, `{Project}`, `{Handler}`, `{JobId}` at start; assert `LogInformation` called with `{DurationMs}` and `{OutputPath}` and `{JobId}` on completion; call again with simulated exception; assert `LogError` called with `{ErrorType}`, `{ErrorMessage}`, and `{JobId}` fields; verify no string interpolation or concatenation in any log template (depends on T023)

- [x] T045 [US2] **TFS Connector Verification** — Confirm `AddDependencyCaptureServices` is NOT called from TFS agent host startup (`src/DevOpsMigrationPlatform.TfsExportAgent/` and/or `TfsServiceCollectionExtensions.cs`); confirm the TFS plan builder emits no `capture.dependencies.*` tasks when `source.type = TeamFoundationServer`; verify the `captureHandlersByName` missing-handler log+skip path in `JobPlanExecutor` (T010) handles any erroneous TFS `capture.dependencies.*` task gracefully (no throw, no crash); document findings as a code comment in the TFS agent host startup file confirming the intentional absence (depends on T030)

- [x] T046 [US2] **Edge Case EC-5 — DependencyAnalyser graceful-miss** — In `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/DependencyAnalyser.cs` `AnalyseAsync`: handle missing per-project CSV files gracefully — for each expected project CSV path, if the file does not exist, log `_logger.LogError("Required dependency CSV not found for {Org}/{Project} at {Path}. Skipping project.", ...)` per missing file and continue processing remaining projects; do NOT throw `FileNotFoundException` or any unhandled exception; add unit test in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Analysis/DependencyAnalyserTests.cs` asserting that when one or more project CSV files are absent, `AnalyseAsync` completes without throwing and emits exactly one `LogError` per missing file (covers spec.md Edge Cases bullet 5)

**Checkpoint**: US2 complete. `capture.dependencies.*` tasks dispatch to `DependencyCapture` via the unified handler dictionary; `SimulatedDependencyDiscoveryServiceFactory` resolves for Simulated-sourced plans; all O-1 through O-4 signals implemented and verified with unit tests; `DependencyAnalyser` no longer needs `IProjectAnalyser`.

---

## Phase 4: User Story 3 — IProjectAnalyser removed (Priority: P2)

**Goal**: Delete `IProjectAnalyser.cs`. Strip `DependencyAnalyser` of its `IProjectAnalyser` implementation and the `CaptureProjectAsync` method body. Verify zero references survive in the solution. This phase closes the architectural debt introduced as a temporary workaround.

**Independent Test**: The solution builds with zero references to `IProjectAnalyser`. All existing tests pass. `DependencyAnalyser` compiles as `DependencyAnalyser : IOrganisationsAnalyser` only.

**Dependency note**: US3 must follow US2 — `IProjectAnalyser` is safe to delete only after `DependencyCapture` owns per-project dependency discovery (US2 complete) and `JobPlanExecutor` no longer references `IProjectAnalyser` (US1 complete).

### Gherkin Feature File for User Story 3 (mandatory — ATDD Phase 1 artifact)

- [x] T034 [US3] Create `features/platform/iproject-analyser-removal/US3-iproject-analyser-removed.feature` — translate spec.md US3 acceptance scenarios (2 scenarios) into conformant Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md`; commit before any step definitions or production code

### Implementation for User Story 3

- [x] T035 [US3] Delete `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IProjectAnalyser.cs` — remove the file; remove all `using` imports referencing this type from every `.cs` file in the solution; confirm `JobPlanExecutor.cs` has zero `IProjectAnalyser` references (already removed in T010) (depends on T034)
- [x] T036 [US3] Update `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/DependencyAnalyser.cs` — remove `: IProjectAnalyser` from class declaration; delete `CaptureProjectAsync` method body; confirm class declaration reads `DependencyAnalyser : IOrganisationsAnalyser` only; verify no other interface members are inadvertently removed (depends on T035)
- [x] T037 [P] [US3] Verify zero references: run `dotnet build` on `DevOpsMigrationPlatform.sln` and confirm zero errors and zero warnings related to `IProjectAnalyser`; search solution (`grep -r "IProjectAnalyser" src/ tests/`) — must return no results; confirm `DependencyAnalyser` still passes all existing analyser tests

> **No Observability sub-section**: US3 is a pure deletion. No production code is added or modified for new operations. No new span/metric/progress signal is introduced. O-1 through O-4 obligations do not apply.

**Checkpoint**: US3 complete. `IProjectAnalyser` does not exist anywhere in the solution. The solution builds cleanly. All tests pass.

---

## Phase 5: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect the ICapture interface introduction, `IModule : ICapture` change, `IJobPlanExecutor` signature update, `DependencyCapture` and `SimulatedDependencyDiscoveryServiceFactory` additions, and `IProjectAnalyser` deletion.

- [x] T038 Update `.agents/30-context/` files that reference `IModule.InventoryAsync`, `modulesByName`, or `IJobPlanExecutor.ExecuteTasksAsync` — replace with `CaptureAsync`, `captureHandlersByName`, and the updated signature; add entry for `DependencyCapture` and `ICapture` under the agent modules context file; document `SimulatedDependencyDiscoveryServiceFactory` in the Simulated connector context file
- [x] T039 Update `/docs/` files that describe module lifecycle, capture dispatch, or dependency discovery — align with `ICapture` unified dispatch; remove any references to `IProjectAnalyser`; add `DependencyCapture` to the module/capture handler reference
- [x] T040 Review `analysis/pending-actions.md` and remove any items resolved by this spec (dependency discovery Simulated connector gap FR-016, IProjectAnalyser architectural debt)
- [x] T041 Run `dotnet clean && dotnet build --no-incremental` on `DevOpsMigrationPlatform.sln` — MUST pass with zero warnings and zero errors
- [x] T042 Run `dotnet test` — ALL 984+ existing tests MUST pass; all new `DependencyCaptureTests` and `SimulatedDependencyDiscoveryServiceFactoryTests` MUST pass
- [x] T043 Run at least one Simulated Dependencies job plan scenario via a `.vscode/launch.json` debug profile — verify `DependencyCapture.CaptureAsync` is called, `SimulatedDependencyDiscoveryServiceFactory` resolves, `discovery/{org}/{project}/dependencies.csv` is written, `dependency.capture` span appears in local trace output, and CLI progress bar row for `DependencyCapture` is visible

---

## Dependencies & Execution Order

### Phase Dependencies

- **Foundational (Phase 1)**: No dependencies — start immediately
- **US1 (Phase 2)**: Depends on Foundational — BLOCKS US2 (dispatcher rewrite must precede DependencyCapture integration tests)
- **US2 (Phase 3)**: Depends on Foundational (ICapture.cs must exist); functionally depends on US1 (captureHandlersByName lookup must be live); US2 and US3 are sequential
- **US3 (Phase 4)**: Depends on US1 (JobPlanExecutor IProjectAnalyser branch removed) AND US2 (DependencyCapture owns per-project capture) — safe to delete IProjectAnalyser only after both are complete
- **Documentation (Phase 5)**: Depends on all user stories complete

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational only — no dependency on US2 or US3
- **US2 (P1)**: Depends on Foundational (T001–T003) and US1 (T010, T011) — `DependencyCapture` registers as `ICapture` in the dictionary that US1 builds
- **US3 (P2)**: Depends on US1 (T010 removes IProjectAnalyser from JobPlanExecutor) and US2 (T019 replaces IProjectAnalyser's behaviour)

### Within Each User Story

- Gherkin feature file MUST be written and committed first (ATDD Phase 1)
- Metric constants (T017) and IPlatformMetrics methods (T018) before DependencyCapture.cs (T019)
- DependencyCapture.cs (T019) and SimulatedFactory (T020) are independent of each other [P]
- All [P]-marked tasks within a phase can execute in parallel
- Observability tasks (T025–T028) are implemented inside T019 — verify each O signal explicitly before marking complete
- Observability tests (T031–T033) depend on T019/T023 and can run in parallel once DependencyCapture.cs exists

### Parallel Opportunities

```bash
# Phase 1 — sequential (T001 → T002 → T003)

# Phase 2 — once T004 (feature file) is committed:
T005, T006, T007, T008, T009  # all module renames — different files, fully parallel
# then T010 (JobPlanExecutor), T011 (JobAgentWorker) — depend on T002+T003
T012                          # test call-sites — parallel with T010/T011 (different files)
# then T013 (JobPlanExecutorTests) — depends on T010
# then T014 (O-3 verify), T015 (DI Wiring verify)

# Phase 3 — once T016 (feature file) is committed:
T017                          # metric constants
T018                          # IPlatformMetrics (depends T017)
T019, T020                    # DependencyCapture + SimulatedFactory — parallel after T018
T021 (depends T019), T022 (depends T020)
T023, T024                    # test files — parallel after T019/T020
T025–T028, T030               # observability impl (within T019) + DI wiring
T031, T032, T033, T044        # observability tests (O-1, O-2, O-4, O-3) — parallel after T023
T045                          # TFS connector verification — after T030
T046                          # EC-5 graceful-miss (DependencyAnalyser) — parallel after T023/T024

# Phase 4 — once US1+US2 checkpoints are reached:
T034 (feature file), then T035, T036
T037                          # zero-reference verification — parallel after T035+T036
```

---

## Parallel Example: User Story 2

```bash
# Step 1: Commit feature file (T016)
# Step 2: Add metric constants and IPlatformMetrics (T017 → T018, sequential)
# Step 3: Launch in parallel:
Task T019: "Create DependencyCapture.cs in src/.../Capture/"
Task T020: "Create SimulatedDependencyDiscoveryServiceFactory.cs in src/.../DependencyDiscovery/"
# Step 4: After T019 completes, launch in parallel:
Task T021: "Update ServiceCollectionExtensions.cs AddDependencyCaptureServices"
Task T023: "Create DependencyCaptureTests.cs"
# Step 5: After T020 completes, launch in parallel:
Task T022: "Update SimulatedServiceCollectionExtensions.cs AddSimulatedDependencyAnalysis"
Task T024: "Create SimulatedDependencyDiscoveryServiceFactoryTests.cs"
# Step 6: After T021+T022+T023: run T029, T030 (CLI row + DI wiring), then T031, T032, T033 (observability tests) in parallel
```

---

## Implementation Strategy

### MVP First (US1 Only)

1. Complete Phase 1: Foundational (T001–T003)
2. Complete Phase 2: US1 (T004–T015)
3. **STOP and VALIDATE**: `capture.workitems.*`, `capture.identities.*`, `capture.nodes.*`, `capture.teams.*` all dispatch via `captureHandlersByName`; all 984+ tests pass
4. Deploy/demo if ready — no `IProjectAnalyser` branch in dispatch; architecture is clean for US2

### Incremental Delivery

1. Foundational → US1 checkpoint → validates unified capture dispatch (MVP)
2. US1 → US2 checkpoint → validates `DependencyCapture` end-to-end with Simulated connector
3. US2 → US3 checkpoint → `IProjectAnalyser` deleted; zero architectural debt
4. US3 → Documentation sync → full spec complete

### Parallel Team Strategy

With two developers:
- Developer A: Phase 1 (Foundational) → Phase 2 (US1) → Phase 4 (US3)
- Developer B: Can begin Phase 3 (US2) metric/interface work (T017–T018) after Phase 1 completes; DependencyCapture implementation (T019–T033) after T010/T011 (US1 dispatcher) is merged

---

## Notes

- [P] tasks operate on different files and have no dependency on incomplete tasks in the same phase
- [Story] label maps each task to a specific user story for ATDD traceability
- US1 and US2 are both P1 but sequentially dependent (US2 requires US1's dispatcher rewrite)
- US3 is P2 and depends on both US1 and US2 — do not attempt deletion before both P1 stories are at checkpoint
- `InventoryContext` is NOT renamed — it describes the data shape, not the method name
- TFS connector: no `DependencyCapture` registration; TFS plan builder emits no `capture.dependencies.*` tasks; graceful skip if erroneously present (research.md D-007)
- All new classes use constructor injection via `Microsoft.Extensions.DependencyInjection`; no `IConfiguration` access
- Commit after each checkpoint — do not bundle all three user stories into a single commit

## Observability Rules (Non-Negotiable)

These rules apply to this `tasks.md`. They are not suggestions.

1. **US2 (DependencyCapture) is the only user story with new telemetry obligations.** US1 is a rename (signals carry through). US3 is a deletion (no signals). US2 requires the full O-1 through O-4 task block — implemented above as T025–T033.
2. **The Operations Table in `plan.md ## Observability Contract` is the authoritative source** for `dependency.capture` span names, metric instruments, log events, and ProgressEvent stages. T025–T028 reference those exact names.
3. **O-4 CLI Visible (T029) is required.** The `DependencyCapture` progress bar row must be added to `QueueCommand.BuildProgressRenderable` before the US2 checkpoint is reached.
4. **DI wiring (T030) is a required task.** The class must be registered via `AddDependencyCaptureServices`; the extension method must be called from the host startup.
5. **Observability unit tests (T031–T033, T044) are mandatory.** One test each for O-1 (span emitted with correct name and tags), O-2 (all six `IPlatformMetrics` method variants called in both success and failure paths), O-4 (ProgressSink `Emit` called at start, completion, and failure with correct `Stage` and `Metrics`), and O-3 (structured `ILogger` calls with all required fields including `{JobId}` and `{Handler}`).

