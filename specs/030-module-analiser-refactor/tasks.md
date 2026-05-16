# Tasks: Module IModule Phase Consolidation and IAnalyser Introduction

**Input**: Design documents from `/specs/030-module-analiser-refactor/`
**Branch**: `030-module-analiser-refactor`
**Prerequisites**: plan.md ✅ · spec.md ✅ · research.md ✅ · data-model.md ✅

**Tests**: Business logic tests are generated where the logic branches or transforms state. Observability tests (O-1 through O-4) are MANDATORY in every user story phase — always generated.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to
- Every task includes an exact file path

---

## Phase 1: Setup (Shared Abstractions and Metric Infrastructure)

**Purpose**: New types in `Abstractions.Agent` and `WellKnownMetricNames` that all subsequent user stories depend on. No user story work can begin until this phase is complete.

**⚠️ CRITICAL**: Blocks all user stories.

- [x] T001 Extend `DependencyPhase` enum — add `Inventory = 0`, `Prepare = 4`, `Analyse = 5` to `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/DependencyPhase.cs`
- [x] T002 Update `ModuleDependency` record — strip `"Analyser"` suffix in `ModuleName` getter; add `AppliesToInventory`, `AppliesToPrepare`, `AppliesToAnalyse` computed properties in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/ModuleDependency.cs`
- [x] T003 [P] Create `InventoryContext` record (`init`-only, `Job`, `IArtefactStore`, `IStateStore`, `IProgressSink?`, `SourceEndpoint: OrganisationEndpoint`, `Projects: IReadOnlyList<string>?`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/InventoryContext.cs`
- [x] T004 [P] Create `PrepareContext` record (`init`-only, `Job`, `IArtefactStore`, `IStateStore`, `IProgressSink?`, `TargetEndpoint: ITargetEndpointInfo`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareContext.cs`
- [x] T005 [P] Create `PrepareIssueSeverity` enum (`Warning = 0`, `Blocking = 1`) and `UnresolvedItem` record (`Key`, `Reason`, `Severity`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareReport.cs`
- [x] T006 [P] Create `PrepareReport` record (serialised to `{Module}/prepare-report.json` — `ModuleName`, `ResolvedCount`, `UnresolvedCount`, `UnresolvedItems: IReadOnlyList<UnresolvedItem>`, `GeneratedAt: DateTimeOffset`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareReport.cs` (same file as T005)
- [x] T007 [P] Create `AnalyseContext` record (`init`-only, `Job`, `IArtefactStore`, `IStateStore`, `IProgressSink?` — no source/target endpoint) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/AnalyseContext.cs`
- [x] T007a [P] Create `OrganisationsAnalyseContext` record — extends `AnalyseContext`; adds `Organisations: IReadOnlyList<OrganisationEndpoint>` (init-only); used by `IOrganisationsAnalyser` implementations that iterate over source orgs (FR-024) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/OrganisationsAnalyseContext.cs`
- [x] T007b [P] Create `EndpointPairAnalyseContext` record — extends `AnalyseContext`; adds `SourceEndpoint: ISourceEndpointInfo` and `TargetEndpoint: ITargetEndpointInfo` (both init-only); used by `IEndpointPairAnalyser` implementations that compare live source and target data (FR-025) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/EndpointPairAnalyseContext.cs`
- [x] T007c [P] Create `IOrganisationsAnalyser` interface — extends `IAnalyser`; overrides `AnalyseAsync` with `OrganisationsAnalyseContext` parameter; for analysers that iterate over source organisations (FR-023, FR-024) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IOrganisationsAnalyser.cs`
- [x] T007d [P] Create `IEndpointPairAnalyser` interface — extends `IAnalyser`; overrides `AnalyseAsync` with `EndpointPairAnalyseContext` parameter; for analysers that compare live source and target data (FR-023, FR-025) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IEndpointPairAnalyser.cs`
- [x] T008 [P] Create `IAnalyser` interface (`Name: string`, `DependsOn: IReadOnlyList<ModuleDependency>`, `AnalyseAsync(AnalyseContext, CancellationToken): Task`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IAnalyser.cs`
- [x] T009 Extend `IModule` interface — add `SupportsInventory: bool`, `SupportsPrepare: bool`, `InventoryAsync(InventoryContext, CancellationToken): Task`, `PrepareAsync(PrepareContext, CancellationToken): Task` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs`
- [x] T010 Update `ModuleBase` abstract class — add default no-op implementations for `InventoryAsync` and `PrepareAsync` (emit one structured `Warning` log then return `Task.CompletedTask`; MUST NOT throw); default `SupportsInventory = false`, `SupportsPrepare = false` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs` (or wherever the base class lives)
- [x] T011 Update `IInventoryOrchestrator` interface — replace `ExportContext context` with `InventoryContext context`; remove `IReadOnlyList<ScopedOrganisationEndpoint> organisations` parameter in `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IInventoryOrchestrator.cs`
- [x] T012 Add new metric name constants to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownDiscoveryMetricNames.cs`:
  - `discovery.inventory.workitems.duration_ms` (Histogram)
  - `discovery.inventory.workitems.errors` (Counter)
  - `discovery.inventory.workitems.in_flight` (UpDownCounter)
  - `discovery.inventory.identities` (Counter)
  - `discovery.inventory.identities.duration_ms` (Histogram)
  - `discovery.inventory.identities.errors` (Counter)
  - `discovery.inventory.identities.in_flight` (UpDownCounter)
  - `discovery.inventory.nodes` (Counter)
  - `discovery.inventory.nodes.duration_ms` (Histogram)
  - `discovery.inventory.nodes.errors` (Counter)
  - `discovery.inventory.nodes.in_flight` (UpDownCounter)
  - `discovery.inventory.teams` (Counter)
  - `discovery.inventory.teams.duration_ms` (Histogram)
  - `discovery.inventory.teams.errors` (Counter)
  - `discovery.inventory.teams.in_flight` (UpDownCounter)
  - `discovery.inventory.consolidated` (Counter)
  - `discovery.inventory.consolidated.duration_ms` (Histogram)
  - `discovery.inventory.consolidated.errors` (Counter)
  - `discovery.dependencies.analyse.duration_ms` (Histogram)
  - `discovery.dependencies.analyse.errors` (Counter)
- [x] T013 [P] Add new metric name constants to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`:
  - `migration.workitems.prepare.resolved` (Counter)
  - `migration.workitems.prepare.unresolved` (Counter)
  - `migration.workitems.prepare.errors` (Counter)
  - `migration.workitems.prepare.duration_ms` (Histogram)
  - `migration.workitems.prepare.in_flight` (UpDownCounter)
  - `migration.identities.prepare.resolved` / `.unresolved` / `.errors` / `.duration_ms` / `.in_flight`
  - `migration.nodes.prepare.resolved` / `.unresolved` / `.errors` / `.duration_ms` / `.in_flight`
  - `migration.teams.prepare.resolved` / `.unresolved` / `.errors` / `.duration_ms` / `.in_flight`
- [x] T014 [P] Add `Inventory` and `Prepare` sub-counter properties to `MigrationCounters` DTO (wherever it lives in `Abstractions` or `Infrastructure.Agent`); update `SnapshotMetricExporter.cs` to extract these into `JobMetrics`
- [x] T014a [P] **Rename `DiscoveryOptions` → `AnalyserOptions`** (FR-022): (1) rename class and file `src/DevOpsMigrationPlatform.Abstractions/Options/DiscoveryOptions.cs` → `AnalyserOptions.cs`; (2) rename `DiscoveryOptionsOrganisationsBinder` → `AnalyserOptionsOrganisationsBinder` in `src/DevOpsMigrationPlatform.Infrastructure/Config/DiscoveryOptionsOrganisationsBinder.cs`; (3) rename `AddDiscoveryOptionsOrganisationsBinder` → `AddAnalyserOptionsOrganisationsBinder` and all `DiscoveryOptions` references in `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformServiceExtensions.cs`; (4) update all `IOptions<DiscoveryOptions>`, `AddOptions<DiscoveryOptions>`, and `BuildDiscoveryOptions` usages across `src/`; (5) rename `DiscoveryOptionsValidationTests` → `AnalyserOptionsValidationTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/`; (6) update `docs/capabilities-guide.md` Known Limitations section to replace `DiscoveryOptions` with `AnalyserOptions`
- [x] T015 Run `dotnet clean && dotnet build --no-incremental` — MUST pass before any user story phase begins

**Checkpoint**: All new abstractions compile. `IModule` contract extended. Metric names defined. No user story work starts until T015 passes.

---

## Phase 2: Foundational (Plan Builder and Worker Updates)

**Purpose**: `JobExecutionPlanBuilder` and `JobAgentWorker` changes that wire all phases together. Blocks User Stories 2, 3, and 4.

**⚠️ CRITICAL**: US1 can begin after Phase 1. US2 (multi-org), US3 (Prepare dispatch), and US4 (IAnalyser dispatch) depend on this phase.

- [x] T016 Update `JobExecutionPlanBuilder` — discover `IAnalyser` registrations alongside `IModule`; produce `JobTask` entries with phase labels `"inventory"`, `"prepare"`, `"analyse"`; when building a plan that includes `prepare` tasks, inspect each prepare module's `DependsOn` for `DependencyPhase.Analyse` entries and hoist the referenced `IAnalyser` tasks before the `prepare` tasks (FR-021) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
- [x] T016a Implement plan builder context-type resolution (FR-026) — when constructing the `AnalyseContext` for an `IAnalyser`, check `analyser is IEndpointPairAnalyser` first (construct `EndpointPairAnalyseContext`), then `analyser is IOrganisationsAnalyser` (construct `OrganisationsAnalyseContext`), then fall back to base `AnalyseContext`; endpoint data sourced from job configuration; add unit tests asserting each branch produces the correct context type in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` (tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderContextResolutionTests.cs`)
- [x] T017 Update `JobAgentWorker`— add multi-org loop for `JobKind.Inventory`: iterate over configured source endpoints and call each enabled `IModule.InventoryAsync` per endpoint; add `JobKind.Prepare` dispatch: execute any hoisted `analyse` tasks first (per plan builder ordering), then call `PrepareAsync` on each enabled module in `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
- [x] T018 Update `InventoryOrchestrator` concrete implementation — adapt to accept `InventoryContext` instead of `ExportContext`; remove multi-org parameter (single-org per call) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs`
- [x] T019 Update `IDependencyOrchestrator` (if it takes `ExportContext`) to accept `AnalyseContext` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IDependencyOrchestrator.cs`
- [x] T020 Run `dotnet clean && dotnet build --no-incremental` — MUST pass

**Checkpoint**: Plan builder wires all five phases and hoists `analyse` before `prepare` when `DependsOn` requires it. Multi-org loop in place. Build passes.

---

## Phase 3: User Story 1 — Run Inventory Without a Separate Inventory Module (Priority: P1) 🎯 MVP

**Goal**: Each domain module contributes its own inventory counts when `JobKind.Inventory` runs — no `InventoryModule` or `InventoryDiscoveryModule` required.

**Independent Test**: Submit a `JobKind.Inventory` job with all four domain modules enabled and no discovery modules. Assert `WorkItems/inventory.json`, `Identities/inventory.json`, `Nodes/inventory.json`, and `Teams/inventory.json` are present with non-zero counts for each domain.

### Gherkin Feature Files for User Story 1 (mandatory)

> **ATDD Phase 1 artifacts — write these before any step definitions or production code.**

- [x] T021 [US1] Create `features/inventory/simulated/inventory-modules.feature` — translate spec.md US1 acceptance scenarios 1 and 2 into conformant Gherkin: `@inventory` feature, scenarios `Inventory_AllModulesEnabled_ProducesInventoryJson`, `Inventory_WithoutInventoryModule_ProducesIdenticalArtefacts` (use Simulated connector)
- [x] T022 [P] [US1] Create `features/inventory/ado/inventory-modules.feature` — same scenarios using AzureDevOpsServices connector (scenario tag: `@ado`)
- [x] T023 [P] [US1] Create `features/inventory/tfs/inventory-modules.feature` — same scenarios using TeamFoundationServer connector (scenario tag: `@tfs`)

### Implementation for User Story 1

- [x] T024 [US1] Implement `WorkItemsModule.InventoryAsync` — set `SupportsInventory = true`; delegate to `IInventoryOrchestrator`; write work item and revision counts to **`WorkItems/inventory.json`** via `IArtefactStore` (NOT to shared `inventory.json`); delete `InventoryModule.cs` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
- [x] T025 [P] [US1] Implement `IdentitiesModule.InventoryAsync` — set `SupportsInventory = true`; enumerate identities from source connector via existing service; write identity count to **`Identities/inventory.json`** via `IArtefactStore` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs`
- [x] T026 [P] [US1] Implement `NodesModule.InventoryAsync` — set `SupportsInventory = true`; enumerate area/iteration nodes; write node count to **`Nodes/inventory.json`** via `IArtefactStore` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs`
- [x] T027 [P] [US1] Implement `TeamsModule.InventoryAsync` — set `SupportsInventory = true`; enumerate teams; write team count to **`Teams/inventory.json`** via `IArtefactStore` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs`
- [x] T028 [US1] Implement TFS-compatible `TfsWorkItemsModule.InventoryAsync` — set `SupportsInventory = true`; delegate to `IInventoryOrchestrator` (same as WorkItemsModule); adapts to net481 in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Modules/TfsWorkItemsModule.cs`
- [x] T029 [US1] Delete `InventoryModule.cs` from `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/InventoryModule.cs` (replaced by WorkItemsModule.InventoryAsync)
- [x] T030 [US1] Add Simulated connector `InventoryAsync` implementations — `SimulatedWorkItemSource.InventoryAsync`, `SimulatedIdentitiesSource.InventoryAsync`, `SimulatedNodesSource.InventoryAsync`, `SimulatedTeamsSource.InventoryAsync` must return ≥2 items each in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`
- [x] T031 [US1] Register `InventoryAsync` capability in DI — confirm all four modules' `SupportsInventory` returns `true` in their respective `Add*Services` registrations; verify `AddInventoryOrchestratorServices()` wires updated `IInventoryOrchestrator`

### Observability for User Story 1 ⛔ MANDATORY

- [x] T032 [US1] **O-1 Traces** — Add `using var activity = WellKnownActivitySourceNames.Discovery.StartActivity("inventory.workitems")` with tags `job.id`, `module="WorkItems"`, `project` to `WorkItemsModule.InventoryAsync`; add `"inventory.identities"`, `"inventory.nodes"`, `"inventory.teams"` spans to the other three modules
- [x] T033 [P] [US1] **O-2 Metrics** — Call `IDiscoveryMetrics.RecordInventoryWorkItems(count, tags)`, `RecordInventoryWorkItemsDuration(elapsed, tags)`, and `RecordInventoryWorkItemsErrors(tags)` in `WorkItemsModule.InventoryAsync`; add corresponding calls for identities/nodes/teams in their modules
- [x] T034 [P] [US1] **O-3 Logs** — Add `LogInformation("Inventorying {Module} for {Project}", module, project)` at start; `LogInformation("Inventoried {Module}: {Count} items in {DurationMs}ms", ...)` at end; `LogWarning("Zero items inventoried for {Module} in {Project}", ...)` when count = 0; `LogDebug("Inventory window {WindowIndex}: {ItemCount} items", ...)` per window in all four `InventoryAsync` methods
- [x] T035 [US1] **O-4 ProgressEvents** — Inject `IProgressSink?` optional; call `EmitAsync(new ProgressEvent { Stage = "Inventorying", Module = Name, ... })` at start and `EmitAsync(... Stage = "Inventoried", Metrics = new JobMetrics { ... } )` at completion in all four `InventoryAsync` methods; add Inventory row to `QueueCommand.BuildProgressRenderable` in `src/DevOpsMigrationPlatform.CLI/Commands/QueueCommand.cs`
- [x] T036 [US1] **DI Wiring** — Verify `IDiscoveryMetrics` has new methods for inventory counters; verify `DiscoveryMetrics` implements them; verify all four modules are registered with `SupportsInventory = true`; run host startup to confirm no `InvalidOperationException`
- [x] T037 [P] [US1] **Test O-1** — Unit test: `TestActivityListener`; call `WorkItemsModule.InventoryAsync`; assert `StartActivity("inventory.workitems")` emitted with `job.id` and `module` tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleInventoryTests.cs`
- [x] T038 [P] [US1] **Test O-2** — Unit test: `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; call `InventoryAsync`; assert `RecordInventoryWorkItems` and `RecordInventoryWorkItemsDuration` called with correct tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleInventoryTests.cs`
- [x] T039 [P] [US1] **Test O-4** — Unit test: `Mock<IProgressSink>`; call `InventoryAsync`; assert `EmitAsync` called at start (Stage="Inventorying") and at completion (Stage="Inventoried"); assert completion event has non-null `Metrics` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleInventoryTests.cs`
- [x] T040 [P] [US1] **Test O-3 zero-count warning** — Unit test: Simulated source returns 0 items; call `InventoryAsync`; assert `ILogger.LogWarning` called with `"Zero items inventoried for {Module}"` structured param in `WorkItemsModuleInventoryTests.cs`
- [x] T040a [P] [US1] **Test O-1 Identities/Nodes/Teams** — Unit tests (one per module): `TestActivityListener`; call `IdentitiesModule.InventoryAsync`, `NodesModule.InventoryAsync`, `TeamsModule.InventoryAsync`; assert `StartActivity("inventory.identities")`, `StartActivity("inventory.nodes")`, `StartActivity("inventory.teams")` emitted with `job.id` and `module` tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/IdentitiesModuleInventoryTests.cs`, `NodesModuleInventoryTests.cs`, `TeamsModuleInventoryTests.cs`
- [x] T040b [P] [US1] **Test O-2 Identities/Nodes/Teams** — Unit tests: `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; call each module's `InventoryAsync`; assert `RecordInventory*` metric methods called with correct tags in respective test files
- [x] T040c [P] [US1] **Test O-4 Identities/Nodes/Teams** — Unit tests: `Mock<IProgressSink>`; call each module's `InventoryAsync`; assert `EmitAsync` called at start and completion with non-null `Metrics` in respective test files
- [x] T041 [US1] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [x] T042 [US1] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: `JobKind.Inventory` runs without `InventoryModule`. All four domain modules contribute counts. Per-module `{Module}/inventory.json` files written with non-zero counts (Simulated). Three connectors covered. Observability tests pass.

---

## Phase 4: User Story 2 — Multi-Organisation Inventory Without a Discovery Module (Priority: P2)

**Goal**: `JobKind.Inventory` with multiple source endpoints iterates over organisations in `JobAgentWorker`. `InventoryDiscoveryModule` is eliminated.

**Independent Test**: Submit a `JobKind.Inventory` job with two simulated source endpoints. Assert `inventory.json` contains entries from both organisations. Run without `InventoryDiscoveryModule` in config.

### Gherkin Feature Files for User Story 2 (mandatory)

- [x] T043 [US2] Create `features/inventory/simulated/inventory-multi-org.feature` — translate spec.md US2 acceptance scenarios 1, 2, and 3 into conformant Gherkin: `Inventory_TwoOrganisations_BothContributeToInventory`, `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`, `Inventory_OneOrgUnreachable_RemainingOrgsStillProcessed`
- [x] T043a [P] [US2] Create `features/inventory/ado/inventory-multi-org.feature` — same US2 acceptance scenarios 1, 2, and 3 using AzureDevOpsServices connector (scenario tag: `@ado`); ensures Constitution XI full-connector coverage for multi-org behaviour
- [x] T043b [P] [US2] Create `features/inventory/tfs/inventory-multi-org.feature` — same US2 acceptance scenarios using TeamFoundationServer connector (scenario tag: `@tfs`); TFS multi-org may use `inventory-graceful-skip.feature` naming if TFS does not support multi-endpoint — assert at minimum scenario 3 (unreachable org → warning + continue)

### Implementation for User Story 2

- [x] T044 [US2] Extend `JobAgentWorker` multi-org loop (built in T017) — confirm that per-endpoint `InventoryAsync` calls aggregate into shared `inventory.json`; add error handling for unreachable orgs (structured Warning log, continue remaining orgs) in `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
- [x] T045 [US2] Delete `InventoryDiscoveryModule.cs` from `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/InventoryDiscoveryModule.cs` (logic now in JobAgentWorker)
- [x] T046 [US2] Update Simulated connector to support multi-org — `SimulatedWorkItemSource` accepts a list of org endpoints and yields items per org; verify ≥2 items returned per org call in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`
- [x] T047 [P] [US2] Update ADO connector `InventoryAsync` to respect `InventoryContext.SourceEndpoint` (single-org per call) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/AzureDevOps/`
- [x] T048 [P] [US2] Update TFS connector `InventoryAsync` to respect `InventoryContext.SourceEndpoint` in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Modules/TfsWorkItemsModule.cs`

### Observability for User Story 2 ⛔ MANDATORY

- [x] T049 [US2] **O-1 Traces** — Add `"inventory.workitems"` child span per org in the multi-org loop in `JobAgentWorker`; tag with `org.url` (high-cardinality allowed in traces per telemetry-model.md)
- [x] T050 [P] [US2] **O-2 Metrics** — Verify existing inventory metric calls aggregate correctly across orgs (single counter incrementing across iterations); no per-org metric tag (high-cardinality risk)
- [x] T050a [P] [US2] **Test O-2** — Unit test: inject `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; invoke `JobAgentWorker` inventory dispatch with 2 simulated endpoints; assert `RecordInventoryWorkItems` called twice (once per org) in `tests/DevOpsMigrationPlatform.MigrationAgent.Tests/JobAgentWorkerInventoryTests.cs`
- [x] T051 [P] [US2] **O-3 Logs** — Add `LogInformation("Starting multi-org inventory: {OrgCount} organisations", count)` and `LogWarning("Organisation {OrgIndex}/{OrgCount} unreachable: {ErrorType}", ...)` in `JobAgentWorker`
- [x] T052 [US2] **O-4 ProgressEvents** — Verify `ProgressEvent` emitted at per-org completion updates aggregate `Metrics.Discovery.Inventory.WorkItems` counter correctly; CLI Inventory row shows cumulative count
- [x] T053 [US2] **DI Wiring** — Verify `InventoryDiscoveryModule` is unregistered (not in DI); verify multi-org loop picks up all enabled `IModule` registrations where `SupportsInventory = true`
- [x] T054 [P] [US2] **Test O-1/O-2/O-3** — Unit test: two-org `JobAgentWorker` invocation; assert `InventoryAsync` called twice; assert `LogInformation` with `OrgCount=2`; assert metrics incremented twice in `tests/DevOpsMigrationPlatform.MigrationAgent.Tests/JobAgentWorkerInventoryTests.cs`
- [x] T055 [P] [US2] **Test O-4 error path** — Unit test: second org returns connection error; assert `LogWarning` emitted; assert first org's inventory artefacts are present; assert job does not fail in `JobAgentWorkerInventoryTests.cs`
- [x] T056 [US2] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [x] T057 [US2] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: Multi-org inventory works end-to-end. `InventoryDiscoveryModule` deleted. Partial failure tolerant. Three connectors covered.

---

## Phase 5: User Story 3 — Run Prepare Phase to Validate Target Before Import (Priority: P2)

**Goal**: `JobKind.Prepare` dispatches `PrepareAsync` on all enabled modules. Each writes a `{Module}/prepare-report.json`. `blockOnUnresolved: true` aborts the job when blocking issues found.

**Independent Test**: Submit a `JobKind.Prepare` job against a Simulated package. Assert `WorkItems/prepare-report.json`, `Identities/prepare-report.json`, `Nodes/prepare-report.json`, `Teams/prepare-report.json` all exist with non-trivially non-empty content.

### Gherkin Feature Files for User Story 3 (mandatory)

- [x] T058 [US3] Create `features/prepare/simulated/prepare-modules.feature` — translate spec.md US3 acceptance scenarios 1, 2, and 3: `Prepare_AllModulesEnabled_WritesReportPerModule`, `Prepare_UnresolvedIdentities_CompletesWithWarning`, `Prepare_InMigratePipeline_RunsBeforeImport`
- [x] T058a [P] [US3] Add US3 Acceptance Scenario 4 to `features/prepare/simulated/prepare-modules.feature` — `Prepare_ModuleWithAnalyserDependsOn_HoistsAnalyseBeforePrepare`: Given a module declares `DependsOn` on `IAnalyser` with `DependencyPhase.Analyse`, When a `JobKind.Prepare` job runs, Then the plan builder hoists the analyse tasks before prepare tasks and the module's `PrepareAsync` can read the analyser's artefacts (FR-021)
- [x] T059 [P] [US3] Create `features/prepare/ado/prepare-modules.feature` — same scenarios using AzureDevOpsServices connector
- [x] T060 [P] [US3] Create `features/prepare/tfs/prepare-graceful-skip.feature` — TFS source-only no-op scenario: `Prepare_TfsSourceOnlyModule_SkipsGracefullyWithWarning` (verifies `SupportsPrepare = false` + no-op base implementation)

### Implementation for User Story 3

- [x] T061 [US3] Implement `WorkItemsModule.PrepareAsync` — set `SupportsPrepare = true`; read exported artefacts from `IArtefactStore`; validate field mappings against target via `IIdentityMappingService`; write `WorkItems/prepare-report.json` via `IArtefactStore`; respect `blockOnUnresolved` config flag in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
- [x] T062 [P] [US3] Implement `IdentitiesModule.PrepareAsync` — set `SupportsPrepare = true`; resolve identity descriptors against target; write `Identities/prepare-report.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs`
- [x] T063 [P] [US3] Implement `NodesModule.PrepareAsync` — set `SupportsPrepare = true`; validate area/iteration paths exist on target; write `Nodes/prepare-report.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs`
- [x] T064 [P] [US3] Implement `TeamsModule.PrepareAsync` — set `SupportsPrepare = true`; validate teams exist on target; write `Teams/prepare-report.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs`
- [x] T065 [US3] Confirm `TfsWorkItemsModule.SupportsPrepare = false` and `ModuleBase.PrepareAsync` default no-op is correct (Warning log + `Task.CompletedTask`) in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Modules/TfsWorkItemsModule.cs`
- [x] T066 [US3] Add Simulated target connector `PrepareAsync` stubs — `SimulatedIdentitiesTarget.ValidateIdentities`, `SimulatedNodesTarget.ValidateNodes`, `SimulatedTeamsTarget.ValidateTeams` each return ≥1 resolved and optionally 1 unresolved item; connector must yield ≥2 items per test in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`
- [x] T067 [US3] Add `PrepareOptions` sealed class with `public const string SectionName = "MigrationPlatform:Prepare"` and `BlockOnUnresolved: bool` property (init-only, validates via `[Required]`) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/PrepareOptions.cs`; register via `AddSchemaEntry<PrepareOptions>()`

### Observability for User Story 3 ⛔ MANDATORY

- [x] T068 [US3] **O-1 Traces** — Add `using var activity = WellKnownActivitySourceNames.Migration.StartActivity("prepare.workitems")` with tags `job.id`, `module="WorkItems"` to `WorkItemsModule.PrepareAsync`; add `"prepare.identities"`, `"prepare.nodes"`, `"prepare.teams"` spans to the other three modules
- [x] T069 [P] [US3] **O-2 Metrics** — Call `IMigrationMetrics.RecordPrepareResolved(count, tags)`, `RecordPrepareUnresolved(count, tags)`, `RecordPrepareDuration(elapsed, tags)` in each module's `PrepareAsync`; use new `WellKnownMetricNames` constants defined in T013
- [x] T070 [P] [US3] **O-3 Logs** — Add `LogInformation("Preparing {Module}", module)` at start; `LogInformation("Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms", ...)` at end; `LogWarning("{Unresolved} unresolved items in {Module} prepare", ...)` when unresolved > 0 in all four `PrepareAsync` methods; structured params only
- [x] T071 [US3] **O-4 ProgressEvents** — Inject `IProgressSink?` optional; `EmitAsync(Stage="Preparing")` at start; `EmitAsync(Stage="Prepared", Metrics=...)` at completion with `MigrationCounters.Prepare` sub-counter populated; add Prepare row to `QueueCommand.BuildProgressRenderable` in `src/DevOpsMigrationPlatform.CLI/Commands/QueueCommand.cs`
- [x] T072 [US3] **DI Wiring** — Verify `PrepareOptions` is registered via `AddSchemaEntry<PrepareOptions>()`; verify `IMigrationMetrics` has `RecordPrepareResolved/Unresolved/Duration` methods; verify all four modules registered with `SupportsPrepare = true` in DI extension methods
- [x] T073 [P] [US3] **Test O-1** — Unit test: `TestActivityListener`; call `WorkItemsModule.PrepareAsync`; assert `StartActivity("prepare.workitems")` emitted with `job.id` and `module` tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModulePrepareTests.cs`
- [x] T074 [P] [US3] **Test O-2** — Unit test: `Mock<IMigrationMetrics>(MockBehavior.Strict)`; call `PrepareAsync`; assert `RecordPrepareResolved`, `RecordPrepareUnresolved`, and `RecordPrepareDuration` called with correct `TagList` in `WorkItemsModulePrepareTests.cs`
- [x] T075 [P] [US3] **Test O-4** — Unit test: `Mock<IProgressSink>`; call `PrepareAsync`; assert `EmitAsync` called at start (Stage="Preparing") and at completion (Stage="Prepared") with non-null Metrics in `WorkItemsModulePrepareTests.cs`
- [x] T076 [P] [US3] **Test prepare-report content** — Simulated system test: `JobKind.Prepare` completes → assert `WorkItems/prepare-report.json` exists in `IArtefactStore` AND has byte count > 0 AND `ResolvedCount >= 0` AND `UnresolvedCount >= 0` in `WorkItemsModulePrepareTests.cs`
- [x] T077 [US3] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [x] T078 [US3] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: `JobKind.Prepare` runs end-to-end. Four `prepare-report.json` files written. TFS no-op confirmed. Three connectors covered. Prepare row visible in CLI.

---

## Phase 6: User Story 4 — Dependency Analysis as a Distinct IAnalyser Operation (Priority: P3)

**Goal**: `DependencyDiscoveryModule` replaced by `DependencyAnalyser : IAnalyser` and `InventoryAnalyser : IAnalyser`. `JobKind.Dependencies` dispatches `analyse (DependencyAnalyser only)` — no inventory prerequisite. `InventoryAnalyser` runs as part of `JobKind.Inventory` jobs (after all module `InventoryAsync` calls complete via `DependsOn`).

**Independent Test**: Submit a `JobKind.Dependencies` job (Simulated). Assert `analysis/dependencies.csv` exists with ≥1 row, and `analysis/dependencies.mmd` exists. Submit a `JobKind.Inventory` job (Simulated). Assert `inventory.json` and `inventory.csv` exist at package root.

### Gherkin Feature Files for User Story 4 (mandatory)

- [x] T079 [US4] Create `features/analysis/simulated/dependency-analysis.feature` — translate spec.md US4 acceptance scenarios 1, 2, and 3: `Dependencies_AnalyserRuns_ProducesDependenciesCsv`, `Dependencies_NoLinksFound_EmitsWarning`, `Dependencies_RegisteredWithInventoryJob_RunsAfterInventoryPhase`
- [x] T079aa [P] [US4] Create `features/analysis/ado/dependency-analysis.feature` — same US4 acceptance scenarios using AzureDevOpsServices connector (scenario tag: `@ado`)
- [x] T079ab [P] [US4] Create `features/analysis/tfs/dependency-analysis.feature` — same US4 acceptance scenarios using TeamFoundationServer connector (scenario tag: `@tfs`)
- [x] T079a [US4] Create `features/analysis/simulated/inventory-analysis.feature` — scenarios: `Inventory_AllModulesComplete_ConsolidatedInventoryJsonWritten`, `Inventory_AllModulesComplete_InventoryCsvWritten`, `Inventory_ZeroCountModule_EmitsWarning`

### Implementation for User Story 4

- [x] T079b [US4] Create `InventoryAnalyser : IAnalyser` — `Name = "Inventory"`, `DependsOn = [ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Inventory), ModuleDependency(typeof(IdentitiesModule), DependencyPhase.Inventory), ModuleDependency(typeof(NodesModule), DependencyPhase.Inventory), ModuleDependency(typeof(TeamsModule), DependencyPhase.Inventory)]`; `AnalyseAsync` reads the four per-module `{Module}/inventory.json` files, aggregates, and writes `inventory.json` + `inventory.csv` at package root via `IArtefactStore` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/InventoryAnalyser.cs`
- [x] T079c [US4] Create `AddInventoryAnalyserServices(this IServiceCollection)` extension method — registers `InventoryAnalyser` as `IAnalyser` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/InventoryAnalyserServiceCollectionExtensions.cs`
- [x] T079d [US4] Wire `AddInventoryAnalyserServices` into `MigrationAgent` host startup
- [x] T079e [US4] **O-1 Traces** — Add `WellKnownActivitySourceNames.Discovery.StartActivity("analyse.inventory")` with tag `job.id` to `InventoryAnalyser.AnalyseAsync`
- [x] T079f [US4] **O-2 Metrics** — Instrument `InventoryAnalyser.AnalyseAsync` with `IDiscoveryMetrics`: record `discovery.inventory.consolidated` (count), `discovery.inventory.consolidated.duration_ms` (elapsed), `discovery.inventory.consolidated.errors` on failure; use constants from T012 (NOT `IMigrationMetrics` — `InventoryAnalyser` is a discovery operation on the `Discovery` meter)
- [x] T079g [US4] **O-3 Logging** — Log `Information` on start/end, `Warning` if any per-module count file is missing; `Warning` if total consolidated count is zero
- [x] T079h [US4] **O-4 Progress** — Emit `ProgressEvent` at start and completion from `InventoryAnalyser.AnalyseAsync` with `Metrics.Migration.Inventory` populated
- [x] T079i [US4] **SystemTest_Simulated** for `InventoryAnalyser` — Assert `inventory.json` exists at package root with non-empty content AND `inventory.csv` exists with ≥1 data row
- [x] T079j [P] [US4] **Test O-1 InventoryAnalyser** — Unit test: `TestActivityListener`; call `InventoryAnalyser.AnalyseAsync`; assert `StartActivity("analyse.inventory")` emitted with `job.id` tag in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Analysis/InventoryAnalyserTests.cs`
- [x] T079k [P] [US4] **Test O-2 InventoryAnalyser** — Unit test: `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; call `AnalyseAsync`; assert `discovery.inventory.consolidated` counter incremented with correct tags in `InventoryAnalyserTests.cs`
- [x] T079l [P] [US4] **Test O-3 InventoryAnalyser** — Unit test: one per-module `{Module}/inventory.json` absent; call `AnalyseAsync`; assert `LogWarning` emitted with structured param indicating missing module file; also assert `LogWarning` when total consolidated count is zero in `InventoryAnalyserTests.cs`
- [x] T079m [P] [US4] **Test O-4 InventoryAnalyser** — Unit test: `Mock<IProgressSink>`; call `AnalyseAsync`; assert `EmitAsync` called at start (Stage="Analysing") and at completion (Stage="Analysed") in `InventoryAnalyserTests.cs`

- [x] T080 [US4] Create `DependencyAnalyser : IOrganisationsAnalyser` — `Name = "Dependencies"`, `DependsOn = []`, `AnalyseAsync(OrganisationsAnalyseContext)` delegates to `IDependencyDiscoveryServiceFactory` (injected) to stream cross-project work item links; writes `analysis/dependencies.csv` and `analysis/dependencies.mmd` via `IArtefactStore`; optionally reads `inventory.json` for progress UI counters (non-gating) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/DependencyAnalyser.cs`
- [x] T081 [US4] Update `IDependencyOrchestrator` concrete implementation — adapt to accept `OrganisationsAnalyseContext` (replacing `ExportContext`); remove `organisations` parameter (sourced from context); retain checkpointing and CSV-write logic in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/DependencyOrchestrator.cs`
- [x] T082 [US4] Delete `DependencyDiscoveryModule.cs` from `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/DependencyDiscoveryModule.cs` (replaced by DependencyAnalyser)
- [x] T083 [US4] Create `AddDependencyAnalyserServices(this IServiceCollection)` extension method — registers `DependencyAnalyser` as **`IOrganisationsAnalyser`** (and via `IAnalyser`); registers `IDependencyOrchestrator` and `IDependencyDiscoveryServiceFactory` if not already registered in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/DependencyAnalyserServiceCollectionExtensions.cs`
- [x] T084 [US4] Wire `AddDependencyAnalyserServices` into `MigrationAgent` host startup
- [x] T085 [US4] Update `JobExecutionPlanBuilder` (T016) — confirm `IOrganisationsAnalyser` tasks appear in `JobTaskList` with phase label `"analyse"`; confirm plan builder constructs `OrganisationsAnalyseContext` (not base `AnalyseContext`) when `analyser is IOrganisationsAnalyser`; confirm `DependencyAnalyser` has no unsatisfied dependency gates
- [x] T086 [US4] Simulated `DependencyAnalyser` test data — create `SimulatedDependencyDiscoveryService` (or stub `IDependencyDiscoveryServiceFactory`) that yields ≥2 cross-project `DependencyRecord` items so `analysis/dependencies.csv` has ≥1 data row in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`

### Observability for User Story 4 ⛔ MANDATORY

- [x] T087 [US4] **O-1 Traces** — Add `using var activity = WellKnownActivitySourceNames.Discovery.StartActivity("analyse.dependencies")` with tags `job.id`, `module="Dependencies"` to `DependencyAnalyser.AnalyseAsync`; add child span `"analyse.dependencies.workitem"` with `wi.id` tag per work item processed
- [x] T088 [P] [US4] **O-2 Metrics** — Call `IDiscoveryMetrics.RecordDependencyLinks(count, tags)`, `RecordDependencyWorkitemsAnalysed(count, tags)`, `RecordDependenciesAnalyseDuration(elapsed, tags)`, `RecordDependenciesAnalyseErrors(tags)` in `DependencyAnalyser.AnalyseAsync`; use existing `discovery.dependencies.links` and `discovery.dependencies.workitems_analysed` constants plus new `analyse.*` constants from T012
- [x] T089 [P] [US4] **O-3 Logs** — Add `LogInformation("Starting dependency analysis for {JobId}", jobId)` at start; `LogInformation("Dependency analysis complete: {Links} cross-project links found across {WorkItems} work items in {DurationMs}ms", ...)` at end; `LogWarning("Zero cross-project dependency links written for {JobId} — verify source organisations are reachable and contain linked work items", ...)` when output is empty in `DependencyAnalyser.AnalyseAsync`; structured params only
- [x] T090 [US4] **O-4 ProgressEvents** — Inject `IProgressSink?` optional; `EmitAsync(Stage="Analysing")` at start; `EmitAsync(Stage="Analysed", Metrics=...)` at completion; add Dependencies row to `QueueCommand.BuildProgressRenderable` in `src/DevOpsMigrationPlatform.CLI/Commands/QueueCommand.cs`
- [x] T091 [US4] **DI Wiring** — Verify `AddDependencyAnalyserServices` registered; verify `IAnalyser` resolved correctly from DI; verify `JobExecutionPlanBuilder` picks up `IAnalyser` registrations; run smoke test
- [x] T092 [P] [US4] **Test O-1** — Unit test: `TestActivityListener`; call `DependencyAnalyser.AnalyseAsync`; assert `StartActivity("analyse.dependencies")` emitted with correct tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Analysis/DependencyAnalyserTests.cs`
- [x] T093 [P] [US4] **Test O-2** — Unit test: `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; call `AnalyseAsync`; assert `RecordDependencyLinks`, `RecordDependencyWorkitemsAnalysed`, `RecordDependenciesAnalyseDuration` called in `DependencyAnalyserTests.cs`
- [x] T094 [P] [US4] **Test O-4** — Unit test: `Mock<IProgressSink>`; call `AnalyseAsync`; assert `EmitAsync` called at start (Stage="Analysing") and completion (Stage="Analysed") in `DependencyAnalyserTests.cs`
- [x] T095 [P] [US4] **Test O-3 zero-output warning** — Unit test: simulated `IDependencyDiscoveryService` yields zero records; call `AnalyseAsync`; assert `LogWarning` emitted with `"Zero cross-project dependency links written"` in `DependencyAnalyserTests.cs`
- [x] T096 [P] [US4] **Test artefact content** — System test: `JobKind.Dependencies` (Simulated, `SimulatedDependencyDiscoveryService` yields ≥2 cross-project records); assert `analysis/dependencies.csv` exists in `IArtefactStore` AND line count > 1 (header + ≥1 data row) in `DependencyAnalyserTests.cs`
- [x] T097 [US4] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [x] T098 [US4] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: `DependencyAnalyser` replaces `DependencyDiscoveryModule`. `JobKind.Dependencies` dispatches correctly. `analysis/dependencies.csv` written. Dependencies row visible in CLI. All observability tests pass.

---

## Phase 7: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect the implementation. Blocking gate — no spec is complete without it.

- [x] T099 Update `docs/module-development-guide.md` — (1) extend IModule contract code block with `InventoryAsync`, `PrepareAsync`, `SupportsInventory`, `SupportsPrepare`; (2) replace "Discovery Modules" section with "Analysers" section documenting `IAnalyser`, `AnalyseContext`, `DependencyAnalyser`; (3) remove `InventoryModule`, `InventoryDiscoveryModule`, `DependencyDiscoveryModule` from module table; (4) update Module ↔ Orchestrator mapping table; (5) add Module Phase Support Matrix; (6) add "Module Dependencies and DependsOn" subsection with `DependencyPhase` enum values. See `discrepancies.md` for complete list.
- [x] T100 [P] Update `docs/architecture.md` — add `JobKind → Phase dispatch table`; add `IAnalyser` to extension-point list; update Migrate pipeline sequence (`inventory → export → prepare → import → validate`)
- [x] T101 [P] Update `.agents/20-guardrails/core/architecture-boundaries.md` Rule 24 — add `{Stem}Analyser` naming convention (`Name = "{Stem}"`, config = `"MigrationPlatform:Analysers:{Stem}"`, DI = `Add{Stem}AnalyserServices`, interface = `IAnalyser`, file = `{Stem}Analyser.cs`). See `discrepancies.md` entry for exact wording.
- [x] T102 [P] Update `.agents/20-guardrails/domains/module-rules.md` — add `InventoryAsync` and `PrepareAsync` implementation checklist items; add `SupportsInventory`/`SupportsPrepare` property declarations; add `prepare-report.json` output contract
- [x] T103 Mark all 8 items in `specs/030-module-analiser-refactor/discrepancies.md` as `Resolved` or `N/A`
- [x] T104 Review `analysis/pending-actions.md` — remove or update any items resolved by this spec
- [x] T105 Update `analysis/draftspec-Module-refactor-consolidation.md` — mark as `Superseded by spec 030` (or archive)
- [x] T106 Run `dotnet clean && dotnet build --no-incremental` — MUST pass (final clean build)
- [x] T107 Run `dotnet test` — ALL tests MUST pass (final full test run)
- [x] T108 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output in terminal (inventory phase must emit progress events)

**Checkpoint**: All discrepancies resolved. All canonical docs updated. Clean build. All tests pass. Scenario verified.

---

## Phase 8: Polish & Cross-Cutting Concerns (OPTIONAL)

- [x] T109 [P] Add `[TestCategory("SystemTest")]` attribute to Simulated system tests for inventory, prepare, and dependency analysis to ensure CLI-observable output is asserted
- [x] T110 [P] Consider adding `ConnectorCoverageTests` asserting all three connector implementations of `InventoryAsync` return ≥2 items per operation
- [x] T111 [P] Performance: verify `InventoryAsync` streaming — add a test asserting `IArtefactStore.WriteJsonAsync` is called incrementally (not after materialising all items)

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup) → no deps — start immediately
Phase 2 (Foundational) → depends on Phase 1
Phase 3 (US1: Inventory) → depends on Phase 1 only (can start before Phase 2 completes)
Phase 4 (US2: Multi-org) → depends on Phase 2 (multi-org loop in JobAgentWorker)
Phase 5 (US3: Prepare) → depends on Phase 2 (JobKind.Prepare dispatch)
Phase 6 (US4: IAnalyser) → depends on Phase 2 (IAnalyser wiring in plan builder)
Phase 7 (Docs) → depends on Phases 3–6 all complete
Phase 8 (Polish) → optional, any time after Phase 7
```

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 1. No dependency on other user stories. ← **MVP start here**
- **US2 (P2)**: Depends on Phase 2 (JobAgentWorker multi-org loop). Integrates with US1 artefacts.
- **US3 (P2)**: Depends on Phase 2 (JobKind.Prepare dispatch). Independent of US2.
- **US4 (P3)**: Depends on Phase 2 (IAnalyser plan builder wiring). Independent of US2/US3.

### Parallel Opportunities Within Each Story

- T003, T004, T005/T006, T007/T007a/T007b/T007c/T007d, T008 — all Phase 1 new type files (different files, parallel)
- T012, T013 — different metric constants files (parallel)
- T024–T027 — four modules' `InventoryAsync` (different files, parallel)
- T025, T026, T027 — Identities/Nodes/Teams `InventoryAsync` (all parallel; each writes its own per-module file, no shared-write dependency)
- T062, T063, T064 — Identities/Nodes/Teams `PrepareAsync` (parallel after T061 defines `PrepareReport` write pattern)
- T037, T038, T039, T040, T040a, T040b, T040c — observability unit tests for US1 (all parallel)

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001–T015)
2. Complete US1 tasks: T021–T042
3. **STOP and VALIDATE**: `JobKind.Inventory` runs without `InventoryModule`. `inventory.json` present. All observability tests pass.
4. Demo/review inventory phase working end-to-end.

### Incremental Delivery

1. Phase 1 + US1 → inventory phase working (MVP)
2. Phase 2 + US2 → multi-org inventory (no `InventoryDiscoveryModule`)
3. US3 → prepare phase working (`JobKind.Prepare`)
4. US4 → dependency analysis as first-class `IAnalyser`
5. Phase 7 → all docs updated, discrepancies resolved

Each story adds value without breaking previous stories. Build must pass after each story.

---

## Notes

- `[P]` = different files, no unfinished dependencies — safe to run in parallel
- `[Story]` maps task to a specific user story for traceability
- All SPDX headers required on every new `.cs` file: `// SPDX-License-Identifier: AGPL-3.0-only` / `// Copyright (c) Naked Agility Limited`
- `Assert.Inconclusive()` is forbidden — all test stubs must be either implemented or deleted
- No `throw new NotImplementedException()` may remain in any reachable code path at the end of each phase
- The Simulated connector MUST yield ≥2 items per operation — zero-item sources make every downstream test vacuously pass


