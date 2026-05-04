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

- [ ] T001 Extend `DependencyPhase` enum — add `Inventory = 0`, `Prepare = 4`, `Analyse = 5` to `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/DependencyPhase.cs`
- [ ] T002 Update `ModuleDependency` record — strip `"Analyser"` suffix in `ModuleName` getter; add `AppliesToInventory`, `AppliesToPrepare`, `AppliesToAnalyse` computed properties in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/ModuleDependency.cs`
- [ ] T003 [P] Create `InventoryContext` record (`init`-only, `Job`, `IArtefactStore`, `IStateStore`, `IProgressSink?`, `SourceEndpoint: OrganisationEndpoint`, `Projects: IReadOnlyList<string>?`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/InventoryContext.cs`
- [ ] T004 [P] Create `PrepareContext` record (`init`-only, `Job`, `IArtefactStore`, `IStateStore`, `IProgressSink?`, `TargetEndpoint: ITargetEndpointInfo`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareContext.cs`
- [ ] T005 [P] Create `PrepareIssueSeverity` enum (`Warning = 0`, `Blocking = 1`) and `UnresolvedItem` record (`Key`, `Reason`, `Severity`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareReport.cs`
- [ ] T006 [P] Create `PrepareReport` record (serialised to `{Module}/prepare-report.json` — `ModuleName`, `ResolvedCount`, `UnresolvedCount`, `UnresolvedItems: IReadOnlyList<UnresolvedItem>`, `GeneratedAt: DateTimeOffset`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/PrepareReport.cs` (same file as T005)
- [ ] T007 [P] Create `AnalyseContext` record (`init`-only, `Job`, `IArtefactStore`, `IStateStore`, `IProgressSink?` — no source/target endpoint) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/AnalyseContext.cs`
- [ ] T007a [P] Create `OrganisationsAnalyseContext` record — extends `AnalyseContext`; adds `Organisations: IReadOnlyList<OrganisationEndpoint>` (init-only); used by `IOrganisationsAnalyser` implementations that iterate over source orgs (FR-024) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/OrganisationsAnalyseContext.cs`
- [ ] T007b [P] Create `EndpointPairAnalyseContext` record — extends `AnalyseContext`; adds `SourceEndpoint: ISourceEndpointInfo` and `TargetEndpoint: ITargetEndpointInfo` (both init-only); used by `IEndpointPairAnalyser` implementations that compare live source and target data (FR-025) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/EndpointPairAnalyseContext.cs`
- [ ] T007c [P] Create `IOrganisationsAnalyser` interface — extends `IAnalyser`; overrides `AnalyseAsync` with `OrganisationsAnalyseContext` parameter; for analysers that iterate over source organisations (FR-023, FR-024) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IOrganisationsAnalyser.cs`
- [ ] T007d [P] Create `IEndpointPairAnalyser` interface — extends `IAnalyser`; overrides `AnalyseAsync` with `EndpointPairAnalyseContext` parameter; for analysers that compare live source and target data (FR-023, FR-025) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IEndpointPairAnalyser.cs`
- [ ] T008 [P] Create `IAnalyser` interface (`Name: string`, `DependsOn: IReadOnlyList<ModuleDependency>`, `AnalyseAsync(AnalyseContext, CancellationToken): Task`) in `src/DevOpsMigrationPlatform.Abstractions.Agent/Analysis/IAnalyser.cs`
- [ ] T009 Extend `IModule` interface — add `SupportsInventory: bool`, `SupportsPrepare: bool`, `InventoryAsync(InventoryContext, CancellationToken): Task`, `PrepareAsync(PrepareContext, CancellationToken): Task` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs`
- [ ] T010 Update `ModuleBase` abstract class — add default no-op implementations for `InventoryAsync` and `PrepareAsync` (emit one structured `Warning` log then return `Task.CompletedTask`; MUST NOT throw); default `SupportsInventory = false`, `SupportsPrepare = false` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleBase.cs` (or wherever the base class lives)
- [ ] T011 Update `IInventoryOrchestrator` interface — replace `ExportContext context` with `InventoryContext context`; remove `IReadOnlyList<ScopedOrganisationEndpoint> organisations` parameter in `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IInventoryOrchestrator.cs`
- [ ] T012 Add new metric name constants to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownDiscoveryMetricNames.cs`:
  - `discovery.inventory.workitems.duration_ms` (Histogram)
  - `discovery.inventory.workitems.errors` (Counter)
  - `discovery.inventory.identities` (Counter)
  - `discovery.inventory.identities.errors` (Counter)
  - `discovery.inventory.nodes` (Counter)
  - `discovery.inventory.nodes.errors` (Counter)
  - `discovery.inventory.teams` (Counter)
  - `discovery.inventory.teams.errors` (Counter)
  - `discovery.dependencies.analyse.duration_ms` (Histogram)
  - `discovery.dependencies.analyse.errors` (Counter)
- [ ] T013 [P] Add new metric name constants to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`:
  - `migration.workitems.prepare.resolved` (Counter)
  - `migration.workitems.prepare.unresolved` (Counter)
  - `migration.workitems.prepare.duration_ms` (Histogram)
  - `migration.identities.prepare.resolved` / `.unresolved` / `.duration_ms`
  - `migration.nodes.prepare.resolved` / `.unresolved` / `.duration_ms`
  - `migration.teams.prepare.resolved` / `.unresolved` / `.duration_ms`
- [ ] T014 [P] Add `Inventory` and `Prepare` sub-counter properties to `MigrationCounters` DTO (wherever it lives in `Abstractions` or `Infrastructure.Agent`); update `SnapshotMetricExporter.cs` to extract these into `JobMetrics`
- [ ] T014a [P] **Rename `DiscoveryOptions` → `AnalyserOptions`** (FR-022): (1) rename class and file `src/DevOpsMigrationPlatform.Abstractions/Options/DiscoveryOptions.cs` → `AnalyserOptions.cs`; (2) rename `DiscoveryOptionsOrganisationsBinder` → `AnalyserOptionsOrganisationsBinder` in `src/DevOpsMigrationPlatform.Infrastructure/Config/DiscoveryOptionsOrganisationsBinder.cs`; (3) rename `AddDiscoveryOptionsOrganisationsBinder` → `AddAnalyserOptionsOrganisationsBinder` and all `DiscoveryOptions` references in `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformServiceExtensions.cs`; (4) update all `IOptions<DiscoveryOptions>`, `AddOptions<DiscoveryOptions>`, and `BuildDiscoveryOptions` usages across `src/`; (5) rename `DiscoveryOptionsValidationTests` → `AnalyserOptionsValidationTests` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/`; (6) update `docs/source-types.md` Known Limitations section to replace `DiscoveryOptions` with `AnalyserOptions`
- [ ] T015 Run `dotnet clean && dotnet build --no-incremental` — MUST pass before any user story phase begins

**Checkpoint**: All new abstractions compile. `IModule` contract extended. Metric names defined. No user story work starts until T015 passes.

---

## Phase 2: Foundational (Plan Builder and Worker Updates)

**Purpose**: `JobExecutionPlanBuilder` and `JobAgentWorker` changes that wire all phases together. Blocks User Stories 2, 3, and 4.

**⚠️ CRITICAL**: US1 can begin after Phase 1. US2 (multi-org), US3 (Prepare dispatch), and US4 (IAnalyser dispatch) depend on this phase.

- [ ] T016 Update `JobExecutionPlanBuilder` — discover `IAnalyser` registrations alongside `IModule`; produce `JobTask` entries with phase labels `"inventory"`, `"prepare"`, `"analyse"`; when building a plan that includes `prepare` tasks, inspect each prepare module's `DependsOn` for `DependencyPhase.Analyse` entries and hoist the referenced `IAnalyser` tasks before the `prepare` tasks (FR-021) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs`
- [ ] T016a Implement plan builder context-type resolution (FR-026) — when constructing the `AnalyseContext` for an `IAnalyser`, check `analyser is IEndpointPairAnalyser` first (construct `EndpointPairAnalyseContext`), then `analyser is IOrganisationsAnalyser` (construct `OrganisationsAnalyseContext`), then fall back to base `AnalyseContext`; endpoint data sourced from job configuration; add unit tests asserting each branch produces the correct context type in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Context/JobExecutionPlanBuilder.cs` (tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobExecutionPlanBuilderContextResolutionTests.cs`)
- [ ] T017 Update `JobAgentWorker`— add multi-org loop for `JobKind.Inventory`: iterate over configured source endpoints and call each enabled `IModule.InventoryAsync` per endpoint; add `JobKind.Prepare` dispatch: execute any hoisted `analyse` tasks first (per plan builder ordering), then call `PrepareAsync` on each enabled module in `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
- [ ] T018 Update `InventoryOrchestrator` concrete implementation — adapt to accept `InventoryContext` instead of `ExportContext`; remove multi-org parameter (single-org per call) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs`
- [ ] T019 Update `IDependencyOrchestrator` (if it takes `ExportContext`) to accept `AnalyseContext` in `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IDependencyOrchestrator.cs`
- [ ] T020 Run `dotnet clean && dotnet build --no-incremental` — MUST pass

**Checkpoint**: Plan builder wires all five phases and hoists `analyse` before `prepare` when `DependsOn` requires it. Multi-org loop in place. Build passes.

---

## Phase 3: User Story 1 — Run Inventory Without a Separate Inventory Module (Priority: P1) 🎯 MVP

**Goal**: Each domain module contributes its own inventory counts when `JobKind.Inventory` runs — no `InventoryModule` or `InventoryDiscoveryModule` required.

**Independent Test**: Submit a `JobKind.Inventory` job with all four domain modules enabled and no discovery modules. Assert `inventory.json` is present with non-zero counts for each domain.

### Gherkin Feature Files for User Story 1 (mandatory)

> **ATDD Phase 1 artifacts — write these before any step definitions or production code.**

- [ ] T021 [US1] Create `features/inventory/simulated/inventory-modules.feature` — translate spec.md US1 acceptance scenarios 1 and 2 into conformant Gherkin: `@inventory` feature, scenarios `Inventory_AllModulesEnabled_ProducesInventoryJson`, `Inventory_WithoutInventoryModule_ProducesIdenticalArtefacts` (use Simulated connector)
- [ ] T022 [P] [US1] Create `features/inventory/ado/inventory-modules.feature` — same scenarios using AzureDevOpsServices connector (scenario tag: `@ado`)
- [ ] T023 [P] [US1] Create `features/inventory/tfs/inventory-modules.feature` — same scenarios using TeamFoundationServer connector (scenario tag: `@tfs`)

### Implementation for User Story 1

- [ ] T024 [US1] Implement `WorkItemsModule.InventoryAsync` — set `SupportsInventory = true`; delegate to `IInventoryOrchestrator`; write work item and revision counts to shared `inventory.json` via `IArtefactStore`; delete `InventoryModule.cs` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
- [ ] T025 [P] [US1] Implement `IdentitiesModule.InventoryAsync` — set `SupportsInventory = true`; enumerate identities from source connector via existing service; append identity count to `inventory.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs`
- [ ] T026 [P] [US1] Implement `NodesModule.InventoryAsync` — set `SupportsInventory = true`; enumerate area/iteration nodes; append node count to `inventory.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs`
- [ ] T027 [P] [US1] Implement `TeamsModule.InventoryAsync` — set `SupportsInventory = true`; enumerate teams; append team count to `inventory.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs`
- [ ] T028 [US1] Implement TFS-compatible `TfsWorkItemsModule.InventoryAsync` — set `SupportsInventory = true`; delegate to `IInventoryOrchestrator` (same as WorkItemsModule); adapts to net481 in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Modules/TfsWorkItemsModule.cs`
- [ ] T029 [US1] Delete `InventoryModule.cs` from `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/InventoryModule.cs` (replaced by WorkItemsModule.InventoryAsync)
- [ ] T030 [US1] Add Simulated connector `InventoryAsync` implementations — `SimulatedWorkItemSource.InventoryAsync`, `SimulatedIdentitiesSource.InventoryAsync`, `SimulatedNodesSource.InventoryAsync`, `SimulatedTeamsSource.InventoryAsync` must return ≥2 items each in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`
- [ ] T031 [US1] Register `InventoryAsync` capability in DI — confirm all four modules' `SupportsInventory` returns `true` in their respective `Add*Services` registrations; verify `AddInventoryOrchestratorServices()` wires updated `IInventoryOrchestrator`

### Observability for User Story 1 ⛔ MANDATORY

- [ ] T032 [US1] **O-1 Traces** — Add `using var activity = WellKnownActivitySourceNames.Discovery.StartActivity("inventory.workitems")` with tags `job.id`, `module="WorkItems"`, `project` to `WorkItemsModule.InventoryAsync`; add `"inventory.identities"`, `"inventory.nodes"`, `"inventory.teams"` spans to the other three modules
- [ ] T033 [P] [US1] **O-2 Metrics** — Call `IDiscoveryMetrics.RecordInventoryWorkItems(count, tags)`, `RecordInventoryWorkItemsDuration(elapsed, tags)`, and `RecordInventoryWorkItemsErrors(tags)` in `WorkItemsModule.InventoryAsync`; add corresponding calls for identities/nodes/teams in their modules
- [ ] T034 [P] [US1] **O-3 Logs** — Add `LogInformation("Inventorying {Module} for {Project}", module, project)` at start; `LogInformation("Inventoried {Module}: {Count} items in {DurationMs}ms", ...)` at end; `LogWarning("Zero items inventoried for {Module} in {Project}", ...)` when count = 0; `LogDebug("Inventory window {WindowIndex}: {ItemCount} items", ...)` per window in all four `InventoryAsync` methods
- [ ] T035 [US1] **O-4 ProgressEvents** — Inject `IProgressSink?` optional; call `EmitAsync(new ProgressEvent { Stage = "Inventorying", Module = Name, ... })` at start and `EmitAsync(... Stage = "Inventoried", Metrics = new JobMetrics { ... } )` at completion in all four `InventoryAsync` methods; add Inventory row to `QueueCommand.BuildProgressRenderable` in `src/DevOpsMigrationPlatform.CLI/Commands/QueueCommand.cs`
- [ ] T036 [US1] **DI Wiring** — Verify `IDiscoveryMetrics` has new methods for inventory counters; verify `DiscoveryMetrics` implements them; verify all four modules are registered with `SupportsInventory = true`; run host startup to confirm no `InvalidOperationException`
- [ ] T037 [P] [US1] **Test O-1** — Unit test: `TestActivityListener`; call `WorkItemsModule.InventoryAsync`; assert `StartActivity("inventory.workitems")` emitted with `job.id` and `module` tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleInventoryTests.cs`
- [ ] T038 [P] [US1] **Test O-2** — Unit test: `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; call `InventoryAsync`; assert `RecordInventoryWorkItems` and `RecordInventoryWorkItemsDuration` called with correct tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleInventoryTests.cs`
- [ ] T039 [P] [US1] **Test O-4** — Unit test: `Mock<IProgressSink>`; call `InventoryAsync`; assert `EmitAsync` called at start (Stage="Inventorying") and at completion (Stage="Inventoried"); assert completion event has non-null `Metrics` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModuleInventoryTests.cs`
- [ ] T040 [P] [US1] **Test O-3 zero-count warning** — Unit test: Simulated source returns 0 items; call `InventoryAsync`; assert `ILogger.LogWarning` called with `"Zero items inventoried for {Module}"` structured param in `WorkItemsModuleInventoryTests.cs`
- [ ] T040a [P] [US1] **Test O-1 Identities/Nodes/Teams** — Unit tests (one per module): `TestActivityListener`; call `IdentitiesModule.InventoryAsync`, `NodesModule.InventoryAsync`, `TeamsModule.InventoryAsync`; assert `StartActivity("inventory.identities")`, `StartActivity("inventory.nodes")`, `StartActivity("inventory.teams")` emitted with `job.id` and `module` tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/IdentitiesModuleInventoryTests.cs`, `NodesModuleInventoryTests.cs`, `TeamsModuleInventoryTests.cs`
- [ ] T040b [P] [US1] **Test O-2 Identities/Nodes/Teams** — Unit tests: `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; call each module's `InventoryAsync`; assert `RecordInventory*` metric methods called with correct tags in respective test files
- [ ] T040c [P] [US1] **Test O-4 Identities/Nodes/Teams** — Unit tests: `Mock<IProgressSink>`; call each module's `InventoryAsync`; assert `EmitAsync` called at start and completion with non-null `Metrics` in respective test files
- [ ] T041 [US1] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T042 [US1] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: `JobKind.Inventory` runs without `InventoryModule`. All four domain modules contribute counts. `inventory.json` written with non-zero counts (Simulated). Three connectors covered. Observability tests pass.

---

## Phase 4: User Story 2 — Multi-Organisation Inventory Without a Discovery Module (Priority: P2)

**Goal**: `JobKind.Inventory` with multiple source endpoints iterates over organisations in `JobAgentWorker`. `InventoryDiscoveryModule` is eliminated.

**Independent Test**: Submit a `JobKind.Inventory` job with two simulated source endpoints. Assert `inventory.json` contains entries from both organisations. Run without `InventoryDiscoveryModule` in config.

### Gherkin Feature Files for User Story 2 (mandatory)

- [ ] T043 [US2] Create `features/inventory/simulated/inventory-multi-org.feature` — translate spec.md US2 acceptance scenarios 1, 2, and 3 into conformant Gherkin: `Inventory_TwoOrganisations_BothContributeToInventory`, `Inventory_WithoutInventoryDiscoveryModule_ProducesSameArtefacts`, `Inventory_OneOrgUnreachable_RemainingOrgsStillProcessed`
- [ ] T043a [P] [US2] Create `features/inventory/ado/inventory-multi-org.feature` — same US2 acceptance scenarios 1, 2, and 3 using AzureDevOpsServices connector (scenario tag: `@ado`); ensures Constitution XI full-connector coverage for multi-org behaviour
- [ ] T043b [P] [US2] Create `features/inventory/tfs/inventory-multi-org.feature` — same US2 acceptance scenarios using TeamFoundationServer connector (scenario tag: `@tfs`); TFS multi-org may use `inventory-graceful-skip.feature` naming if TFS does not support multi-endpoint — assert at minimum scenario 3 (unreachable org → warning + continue)

### Implementation for User Story 2

- [ ] T044 [US2] Extend `JobAgentWorker` multi-org loop (built in T017) — confirm that per-endpoint `InventoryAsync` calls aggregate into shared `inventory.json`; add error handling for unreachable orgs (structured Warning log, continue remaining orgs) in `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs`
- [ ] T045 [US2] Delete `InventoryDiscoveryModule.cs` from `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/InventoryDiscoveryModule.cs` (logic now in JobAgentWorker)
- [ ] T046 [US2] Update Simulated connector to support multi-org — `SimulatedWorkItemSource` accepts a list of org endpoints and yields items per org; verify ≥2 items returned per org call in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`
- [ ] T047 [P] [US2] Update ADO connector `InventoryAsync` to respect `InventoryContext.SourceEndpoint` (single-org per call) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/AzureDevOps/`
- [ ] T048 [P] [US2] Update TFS connector `InventoryAsync` to respect `InventoryContext.SourceEndpoint` in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Modules/TfsWorkItemsModule.cs`

### Observability for User Story 2 ⛔ MANDATORY

- [ ] T049 [US2] **O-1 Traces** — Add `"inventory.workitems"` child span per org in the multi-org loop in `JobAgentWorker`; tag with `org.url` (high-cardinality allowed in traces per telemetry-architecture.md)
- [ ] T050 [P] [US2] **O-2 Metrics** — Verify existing inventory metric calls aggregate correctly across orgs (single counter incrementing across iterations); no per-org metric tag (high-cardinality risk)
- [ ] T050a [P] [US2] **Test O-2** — Unit test: inject `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; invoke `JobAgentWorker` inventory dispatch with 2 simulated endpoints; assert `RecordInventoryWorkItems` called twice (once per org) in `tests/DevOpsMigrationPlatform.MigrationAgent.Tests/JobAgentWorkerInventoryTests.cs`
- [ ] T051 [P] [US2] **O-3 Logs** — Add `LogInformation("Starting multi-org inventory: {OrgCount} organisations", count)` and `LogWarning("Organisation {OrgIndex}/{OrgCount} unreachable: {ErrorType}", ...)` in `JobAgentWorker`
- [ ] T052 [US2] **O-4 ProgressEvents** — Verify `ProgressEvent` emitted at per-org completion updates aggregate `Metrics.Discovery.Inventory.WorkItems` counter correctly; CLI Inventory row shows cumulative count
- [ ] T053 [US2] **DI Wiring** — Verify `InventoryDiscoveryModule` is unregistered (not in DI); verify multi-org loop picks up all enabled `IModule` registrations where `SupportsInventory = true`
- [ ] T054 [P] [US2] **Test O-1/O-2/O-3** — Unit test: two-org `JobAgentWorker` invocation; assert `InventoryAsync` called twice; assert `LogInformation` with `OrgCount=2`; assert metrics incremented twice in `tests/DevOpsMigrationPlatform.MigrationAgent.Tests/JobAgentWorkerInventoryTests.cs`
- [ ] T055 [P] [US2] **Test O-4 error path** — Unit test: second org returns connection error; assert `LogWarning` emitted; assert first org's inventory artefacts are present; assert job does not fail in `JobAgentWorkerInventoryTests.cs`
- [ ] T056 [US2] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T057 [US2] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: Multi-org inventory works end-to-end. `InventoryDiscoveryModule` deleted. Partial failure tolerant. Three connectors covered.

---

## Phase 5: User Story 3 — Run Prepare Phase to Validate Target Before Import (Priority: P2)

**Goal**: `JobKind.Prepare` dispatches `PrepareAsync` on all enabled modules. Each writes a `{Module}/prepare-report.json`. `blockOnUnresolved: true` aborts the job when blocking issues found.

**Independent Test**: Submit a `JobKind.Prepare` job against a Simulated package. Assert `WorkItems/prepare-report.json`, `Identities/prepare-report.json`, `Nodes/prepare-report.json`, `Teams/prepare-report.json` all exist with non-trivially non-empty content.

### Gherkin Feature Files for User Story 3 (mandatory)

- [ ] T058 [US3] Create `features/prepare/simulated/prepare-modules.feature` — translate spec.md US3 acceptance scenarios 1, 2, and 3: `Prepare_AllModulesEnabled_WritesReportPerModule`, `Prepare_UnresolvedIdentities_CompletesWithWarning`, `Prepare_InMigratePipeline_RunsBeforeImport`
- [ ] T058a [P] [US3] Add US3 Acceptance Scenario 4 to `features/prepare/simulated/prepare-modules.feature` — `Prepare_ModuleWithAnalyserDependsOn_HoistsAnalyseBeforePrepare`: Given a module declares `DependsOn` on `IAnalyser` with `DependencyPhase.Analyse`, When a `JobKind.Prepare` job runs, Then the plan builder hoists the analyse tasks before prepare tasks and the module's `PrepareAsync` can read the analyser's artefacts (FR-021)
- [ ] T059 [P] [US3] Create `features/prepare/ado/prepare-modules.feature` — same scenarios using AzureDevOpsServices connector
- [ ] T060 [P] [US3] Create `features/prepare/tfs/prepare-graceful-skip.feature` — TFS source-only no-op scenario: `Prepare_TfsSourceOnlyModule_SkipsGracefullyWithWarning` (verifies `SupportsPrepare = false` + no-op base implementation)

### Implementation for User Story 3

- [ ] T061 [US3] Implement `WorkItemsModule.PrepareAsync` — set `SupportsPrepare = true`; read exported artefacts from `IArtefactStore`; validate field mappings against target via `IIdentityMappingService`; write `WorkItems/prepare-report.json` via `IArtefactStore`; respect `blockOnUnresolved` config flag in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs`
- [ ] T062 [P] [US3] Implement `IdentitiesModule.PrepareAsync` — set `SupportsPrepare = true`; resolve identity descriptors against target; write `Identities/prepare-report.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs`
- [ ] T063 [P] [US3] Implement `NodesModule.PrepareAsync` — set `SupportsPrepare = true`; validate area/iteration paths exist on target; write `Nodes/prepare-report.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodesModule.cs`
- [ ] T064 [P] [US3] Implement `TeamsModule.PrepareAsync` — set `SupportsPrepare = true`; validate teams exist on target; write `Teams/prepare-report.json` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs`
- [ ] T065 [US3] Confirm `TfsWorkItemsModule.SupportsPrepare = false` and `ModuleBase.PrepareAsync` default no-op is correct (Warning log + `Task.CompletedTask`) in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Modules/TfsWorkItemsModule.cs`
- [ ] T066 [US3] Add Simulated target connector `PrepareAsync` stubs — `SimulatedIdentitiesTarget.ValidateIdentities`, `SimulatedNodesTarget.ValidateNodes`, `SimulatedTeamsTarget.ValidateTeams` each return ≥1 resolved and optionally 1 unresolved item; connector must yield ≥2 items per test in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`
- [ ] T067 [US3] Add `PrepareOptions` sealed class with `public const string SectionName = "MigrationPlatform:Prepare"` and `BlockOnUnresolved: bool` property (init-only, validates via `[Required]`) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/PrepareOptions.cs`; register via `AddSchemaEntry<PrepareOptions>()`

### Observability for User Story 3 ⛔ MANDATORY

- [ ] T068 [US3] **O-1 Traces** — Add `using var activity = WellKnownActivitySourceNames.Migration.StartActivity("prepare.workitems")` with tags `job.id`, `module="WorkItems"` to `WorkItemsModule.PrepareAsync`; add `"prepare.identities"`, `"prepare.nodes"`, `"prepare.teams"` spans to the other three modules
- [ ] T069 [P] [US3] **O-2 Metrics** — Call `IMigrationMetrics.RecordPrepareResolved(count, tags)`, `RecordPrepareUnresolved(count, tags)`, `RecordPrepareDuration(elapsed, tags)` in each module's `PrepareAsync`; use new `WellKnownMetricNames` constants defined in T013
- [ ] T070 [P] [US3] **O-3 Logs** — Add `LogInformation("Preparing {Module}", module)` at start; `LogInformation("Prepared {Module}: {Resolved} resolved, {Unresolved} unresolved in {DurationMs}ms", ...)` at end; `LogWarning("{Unresolved} unresolved items in {Module} prepare", ...)` when unresolved > 0 in all four `PrepareAsync` methods; structured params only
- [ ] T071 [US3] **O-4 ProgressEvents** — Inject `IProgressSink?` optional; `EmitAsync(Stage="Preparing")` at start; `EmitAsync(Stage="Prepared", Metrics=...)` at completion with `MigrationCounters.Prepare` sub-counter populated; add Prepare row to `QueueCommand.BuildProgressRenderable` in `src/DevOpsMigrationPlatform.CLI/Commands/QueueCommand.cs`
- [ ] T072 [US3] **DI Wiring** — Verify `PrepareOptions` is registered via `AddSchemaEntry<PrepareOptions>()`; verify `IMigrationMetrics` has `RecordPrepareResolved/Unresolved/Duration` methods; verify all four modules registered with `SupportsPrepare = true` in DI extension methods
- [ ] T073 [P] [US3] **Test O-1** — Unit test: `TestActivityListener`; call `WorkItemsModule.PrepareAsync`; assert `StartActivity("prepare.workitems")` emitted with `job.id` and `module` tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/WorkItemsModulePrepareTests.cs`
- [ ] T074 [P] [US3] **Test O-2** — Unit test: `Mock<IMigrationMetrics>(MockBehavior.Strict)`; call `PrepareAsync`; assert `RecordPrepareResolved`, `RecordPrepareUnresolved`, and `RecordPrepareDuration` called with correct `TagList` in `WorkItemsModulePrepareTests.cs`
- [ ] T075 [P] [US3] **Test O-4** — Unit test: `Mock<IProgressSink>`; call `PrepareAsync`; assert `EmitAsync` called at start (Stage="Preparing") and at completion (Stage="Prepared") with non-null Metrics in `WorkItemsModulePrepareTests.cs`
- [ ] T076 [P] [US3] **Test prepare-report content** — Simulated system test: `JobKind.Prepare` completes → assert `WorkItems/prepare-report.json` exists in `IArtefactStore` AND has byte count > 0 AND `ResolvedCount >= 0` AND `UnresolvedCount >= 0` in `WorkItemsModulePrepareTests.cs`
- [ ] T077 [US3] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T078 [US3] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: `JobKind.Prepare` runs end-to-end. Four `prepare-report.json` files written. TFS no-op confirmed. Three connectors covered. Prepare row visible in CLI.

---

## Phase 6: User Story 4 — Dependency Analysis as a Distinct IAnalyser Operation (Priority: P3)

**Goal**: `DependencyDiscoveryModule` replaced by `DependencyAnalyser : IAnalyser`. `JobKind.Dependencies` dispatches `inventory (WorkItems only) → analyse (DependencyAnalyser only)`.

**Independent Test**: Submit a `JobKind.Dependencies` job (Simulated). Assert `analysis/dependencies.csv` exists with ≥1 row and `analysis/dependencies.mmd` exists.

### Gherkin Feature Files for User Story 4 (mandatory)

- [ ] T079 [US4] Create `features/analysis/simulated/dependency-analysis.feature` — translate spec.md US4 acceptance scenarios 1, 2, and 3: `Dependencies_WorkItemsInventoriedFirst_ProducesDependenciesCsv`, `Dependencies_InventoryAlreadyPresent_SkipsInventoryPhase`, `Dependencies_RunWithInventoryJob_AnalyserRunsAfterAllModules`

### Implementation for User Story 4

- [ ] T080 [US4] Create `DependencyAnalyser : IAnalyser` — `Name = "Dependencies"`, `DependsOn = [ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Inventory)]`, `AnalyseAsync` delegates to updated `IDependencyOrchestrator`; reads `inventory.json`; writes `analysis/dependencies.csv` and `analysis/dependencies.mmd` via `IArtefactStore` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/DependencyAnalyser.cs`
- [ ] T081 [US4] Update `IDependencyOrchestrator` concrete implementation — adapt to accept `AnalyseContext` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/DependencyOrchestrator.cs`
- [ ] T082 [US4] Delete `DependencyDiscoveryModule.cs` from `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/DependencyDiscoveryModule.cs` (replaced by DependencyAnalyser)
- [ ] T083 [US4] Create `AddDependencyAnalyserServices(this IServiceCollection)` extension method — registers `DependencyAnalyser` as `IAnalyser`; registers `IDependencyOrchestrator` if not already registered in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Analysis/DependencyAnalyserServiceCollectionExtensions.cs`
- [ ] T084 [US4] Wire `AddDependencyAnalyserServices` into `MigrationAgent` host startup
- [ ] T085 [US4] Update `JobExecutionPlanBuilder` (T016) — confirm `IAnalyser` tasks appear in `JobTaskList` with phase label `"analyse"` and correct topological ordering after `WorkItems.inventory`
- [ ] T086 [US4] Simulated `DependencyAnalyser` test data — `SimulatedWorkItemSource` must yield ≥2 linked work items so `analysis/dependencies.csv` has ≥1 row in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Connectors/Simulated/`

### Observability for User Story 4 ⛔ MANDATORY

- [ ] T087 [US4] **O-1 Traces** — Add `using var activity = WellKnownActivitySourceNames.Discovery.StartActivity("analyse.dependencies")` with tags `job.id`, `module="Dependencies"` to `DependencyAnalyser.AnalyseAsync`; add child span `"analyse.dependencies.workitem"` with `wi.id` tag per work item processed
- [ ] T088 [P] [US4] **O-2 Metrics** — Call `IDiscoveryMetrics.RecordDependencyLinks(count, tags)`, `RecordDependencyWorkitemsAnalysed(count, tags)`, `RecordDependenciesAnalyseDuration(elapsed, tags)`, `RecordDependenciesAnalyseErrors(tags)` in `DependencyAnalyser.AnalyseAsync`; use existing `discovery.dependencies.links` and `discovery.dependencies.workitems_analysed` constants plus new `analyse.*` constants from T012
- [ ] T089 [P] [US4] **O-3 Logs** — Add `LogInformation("Starting dependency analysis for {JobId}", jobId)` at start; `LogInformation("Dependency analysis complete: {Links} links found, {WorkItems} work items analysed in {DurationMs}ms", ...)` at end; `LogWarning("Zero dependencies written — verify inventory artefacts are present for {JobId}", ...)` when output is empty in `DependencyAnalyser.AnalyseAsync`; structured params only
- [ ] T090 [US4] **O-4 ProgressEvents** — Inject `IProgressSink?` optional; `EmitAsync(Stage="Analysing")` at start; `EmitAsync(Stage="Analysed", Metrics=...)` at completion; add Dependencies row to `QueueCommand.BuildProgressRenderable` in `src/DevOpsMigrationPlatform.CLI/Commands/QueueCommand.cs`
- [ ] T091 [US4] **DI Wiring** — Verify `AddDependencyAnalyserServices` registered; verify `IAnalyser` resolved correctly from DI; verify `JobExecutionPlanBuilder` picks up `IAnalyser` registrations; run smoke test
- [ ] T092 [P] [US4] **Test O-1** — Unit test: `TestActivityListener`; call `DependencyAnalyser.AnalyseAsync`; assert `StartActivity("analyse.dependencies")` emitted with correct tags in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Analysis/DependencyAnalyserTests.cs`
- [ ] T093 [P] [US4] **Test O-2** — Unit test: `Mock<IDiscoveryMetrics>(MockBehavior.Strict)`; call `AnalyseAsync`; assert `RecordDependencyLinks`, `RecordDependencyWorkitemsAnalysed`, `RecordDependenciesAnalyseDuration` called in `DependencyAnalyserTests.cs`
- [ ] T094 [P] [US4] **Test O-4** — Unit test: `Mock<IProgressSink>`; call `AnalyseAsync`; assert `EmitAsync` called at start (Stage="Analysing") and completion (Stage="Analysed") in `DependencyAnalyserTests.cs`
- [ ] T095 [P] [US4] **Test O-3 zero-output warning** — Unit test: inventory has no linked work items; call `AnalyseAsync`; assert `LogWarning` emitted with `"Zero dependencies written"` in `DependencyAnalyserTests.cs`
- [ ] T096 [P] [US4] **Test artefact content** — System test: `JobKind.Dependencies` (Simulated, 2 linked work items); assert `analysis/dependencies.csv` exists in `IArtefactStore` AND line count > 1 (header + ≥1 data row) in `DependencyAnalyserTests.cs`
- [ ] T097 [US4] Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T098 [US4] Run `dotnet test` — ALL tests MUST pass

**Checkpoint**: `DependencyAnalyser` replaces `DependencyDiscoveryModule`. `JobKind.Dependencies` dispatches correctly. `analysis/dependencies.csv` written. Dependencies row visible in CLI. All observability tests pass.

---

## Phase 7: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect the implementation. Blocking gate — no spec is complete without it.

- [ ] T099 Update `docs/modules.md` — (1) extend IModule contract code block with `InventoryAsync`, `PrepareAsync`, `SupportsInventory`, `SupportsPrepare`; (2) replace "Discovery Modules" section with "Analysers" section documenting `IAnalyser`, `AnalyseContext`, `DependencyAnalyser`; (3) remove `InventoryModule`, `InventoryDiscoveryModule`, `DependencyDiscoveryModule` from module table; (4) update Module ↔ Orchestrator mapping table; (5) add Module Phase Support Matrix; (6) add "Module Dependencies and DependsOn" subsection with `DependencyPhase` enum values. See `discrepancies.md` for complete list.
- [ ] T100 [P] Update `docs/architecture.md` — add `JobKind → Phase dispatch table`; add `IAnalyser` to extension-point list; update Migrate pipeline sequence (`inventory → export → prepare → import → validate`)
- [ ] T101 [P] Update `.agents/guardrails/system-architecture.md` Rule 24 — add `{Stem}Analyser` naming convention (`Name = "{Stem}"`, config = `"MigrationPlatform:Analysers:{Stem}"`, DI = `Add{Stem}AnalyserServices`, interface = `IAnalyser`, file = `{Stem}Analyser.cs`). See `discrepancies.md` entry for exact wording.
- [ ] T102 [P] Update `.agents/guardrails/module-template.md` — add `InventoryAsync` and `PrepareAsync` implementation checklist items; add `SupportsInventory`/`SupportsPrepare` property declarations; add `prepare-report.json` output contract
- [ ] T103 Mark all 8 items in `specs/030-module-analiser-refactor/discrepancies.md` as `Resolved` or `N/A`
- [ ] T104 Review `analysis/pending-actions.md` — remove or update any items resolved by this spec
- [ ] T105 Update `analysis/draftspec-Module-refactor-consolidation.md` — mark as `Superseded by spec 030` (or archive)
- [ ] T106 Run `dotnet clean && dotnet build --no-incremental` — MUST pass (final clean build)
- [ ] T107 Run `dotnet test` — ALL tests MUST pass (final full test run)
- [ ] T108 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output in terminal (inventory phase must emit progress events)

**Checkpoint**: All discrepancies resolved. All canonical docs updated. Clean build. All tests pass. Scenario verified.

---

## Phase 8: Polish & Cross-Cutting Concerns (OPTIONAL)

- [ ] T109 [P] Add `[TestCategory("SystemTest")]` attribute to Simulated system tests for inventory, prepare, and dependency analysis to ensure CLI-observable output is asserted
- [ ] T110 [P] Consider adding `ConnectorCoverageTests` asserting all three connector implementations of `InventoryAsync` return ≥2 items per operation
- [ ] T111 [P] Performance: verify `InventoryAsync` streaming — add a test asserting `IArtefactStore.WriteJsonAsync` is called incrementally (not after materialising all items)

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
- T025, T026, T027 — Identities/Nodes/Teams `InventoryAsync` (parallel after T024 defines shared `inventory.json` write pattern)
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
