# Implementation Plan: Module IModule Phase Consolidation and IAnalyser Introduction

**Branch**: `030-module-analiser-refactor` | **Date**: 2026-05-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/030-module-analiser-refactor/spec.md`

## Summary

The current `IModule` contract supports only `ExportAsync` and `ImportAsync`. Two additional operational phases — `Inventory` and `Prepare` — are abused via `ExportAsync` in standalone discovery modules (`InventoryModule`, `InventoryDiscoveryModule`, `DependencyDiscoveryModule`) that are semantically misaligned. Additionally, `PrepareAsync` is documented in `docs/modules.md` but absent from the actual interface.

This plan delivers five outcomes:

1. **Extend `IModule`** with `InventoryAsync`, `PrepareAsync`, and four `Supports*` flags.
2. **Eliminate three classes**: `InventoryModule` (→ `WorkItemsModule.InventoryAsync`), `InventoryDiscoveryModule` (→ `JobAgentWorker` multi-org loop), `DependencyDiscoveryModule` (→ `DependencyAnalyser`).
3. **Introduce `IAnalyser`** as a first-class interface for cross-cutting analysis operations.
4. **Wire `JobKind.Prepare`** end-to-end for the first time.
5. **Extend `DependencyPhase`** with `Inventory`, `Prepare`, and `Analyse` values.

Research findings are in [research.md](research.md). Data model is in [data-model.md](data-model.md).

## Technical Context

**Language/Version**: C# 12, .NET 10 (all new code); net481 carve-out for `TfsMigrationAgent` only  
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`, OpenTelemetry SDK, Reqnroll.MSTest  
**Storage**: `IArtefactStore` (filesystem or Azure Blob) for artefacts; `IStateStore` for cursor state  
**Testing**: Reqnroll.MSTest + Moq (`MockBehavior.Strict`); MSTest runner  
**Target Platform**: MigrationAgent (.NET 10 Linux/Windows), TfsMigrationAgent (.NET 4.8 Windows)  
**Project Type**: Internal platform library — extension to existing module abstraction layer  
**Performance Goals**: `InventoryAsync` must not load all work items into memory — streaming via `IWorkItemFetchService`. `PrepareAsync` must complete in O(N) API calls where N = distinct domain items.  
**Constraints**: All new abstractions in `Abstractions.Agent`. No new assembly. `IAnalyser` and context records are multi-targeted (net481 + net10.0) for TFS compatibility.  
**Scale/Scope**: 7 classes → 4 modules + 1 analyser. 5 new types in Abstractions.Agent. 3 types deleted.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Before completing this gate, confirm that ALL files in
> `/.agents/guardrails/`, ALL files in `/.agents/context/`, and relevant `/docs/` files
> have been read. Skipping either `.agents/` subdirectory is a constitution violation.

- [x] **Package-First (I):** `InventoryAsync` and `PrepareAsync` write only via `IArtefactStore`. `AnalyseAsync` reads and writes artefacts only. No source-to-target migration. Verified.
- [x] **Streaming (II):** `InventoryAsync` delegates to `IInventoryOrchestrator` which uses `IWorkItemFetchService` for streaming. No in-memory list of all revisions. Verified.
- [x] **WorkItems Layout (III):** This refactor does not change the WorkItems folder layout. Verified.
- [x] **Checkpointing (IV):** `InventoryAsync` and `PrepareAsync` use `IInventoryOrchestrator` / dedicated orchestrators that write cursors under `Checkpoints/`. `AnalyseAsync` uses `IStateStore` for checkpoint. Verified.
- [x] **Module Isolation (V):** All new phase methods go through `IArtefactStore`/`IStateStore`. No concrete store references in module/analyser code. Verified.
- [x] **Separation of Planes (VI):** `JobAgentWorker` (MigrationAgent) handles multi-org loop — not the control plane. No UI coupling. TFS exporter unchanged. Verified.
- [x] **Determinism (VII):** Same inputs → same inventory artefacts. `PrepareAsync` is idempotent (overwrites prepare-report.json). `AnalyseAsync` is idempotent (rewrites analysis artefacts). No schema breaking changes in this refactor — `DependencyPhase` enum extends with new values (additive, backward-compatible). Verified.
- [x] **ATDD-First (VIII):** All 4 user stories have Given/When/Then scenarios. Each will be delivered via ATDD inner loop. Verified.
- [x] **SOLID & DI (IX):** `IAnalyser`, `InventoryContext`, `PrepareContext`, `AnalyseContext` in `Abstractions.Agent`. Constructor injection. `IOptions<T>` for config. `AddDependencyAnalyserServices()` and `AddInventoryAnalyserServices()` extension methods. Verified.
- [x] **Full Connector Coverage (XI):** All three connectors (Simulated, AzureDevOps, TFS) require `InventoryAsync` and `PrepareAsync`. TFS is source-only; `PrepareAsync` and `ImportAsync` are no-ops on `TfsWorkItemsModule` — graceful skip with Warning log (not throw). `DependencyAnalyser` implements `IOrganisationsAnalyser` and calls `IDependencyDiscoveryServiceFactory` against live ADO — all three connector feature files are required (`features/analysis/{simulated,ado,tfs}/dependency-analysis.feature`). Verified.

**No constitution violations. All gates pass.**

## Observability Contract

*GATE: Must be completed before task generation. Every operation enumerated here MUST appear as explicit tasks in `tasks.md`. This is not optional — a feature without a complete observability contract will not reach done.*

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `inventory.workitems` | module | `WorkItemsModule.InventoryAsync` | `IInventoryOrchestrator`, `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
| `inventory.identities` | module | `IdentitiesModule.InventoryAsync` | ADO/TFS client (via connector), `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
| `inventory.nodes` | module | `NodesModule.InventoryAsync` | ADO/TFS client (via connector), `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
| `inventory.teams` | module | `TeamsModule.InventoryAsync` | ADO/TFS client (via connector), `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
| `prepare.workitems` | module | `WorkItemsModule.PrepareAsync` | `IArtefactStore`, `IIdentityMappingService`, `IMigrationMetrics`, `IProgressSink?` |
| `prepare.identities` | module | `IdentitiesModule.PrepareAsync` | `IArtefactStore`, `IMigrationMetrics`, `IProgressSink?` |
| `prepare.nodes` | module | `NodesModule.PrepareAsync` | `IArtefactStore`, `IMigrationMetrics`, `IProgressSink?` |
| `prepare.teams` | module | `TeamsModule.PrepareAsync` | `IArtefactStore`, `IMigrationMetrics`, `IProgressSink?` |
| `analyse.inventory` | analyser | `InventoryAnalyser.AnalyseAsync` | `IArtefactStore` (read per-module `{Module}/inventory.json`; write `inventory.json` + `inventory.csv`), `IDiscoveryMetrics`, `IProgressSink?` |
| `analyse.dependencies` | analyser | `DependencyAnalyser.AnalyseAsync` | `IDependencyDiscoveryServiceFactory`, `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |

### Operator Decisions

| Operation | Decision | Question |
|---|---|---|
| `inventory.*` | Is it working? | Did all four modules complete without errors? |
| `inventory.*` | Is it correct? | Do inventory counts match known source data? |
| `inventory.*` | What failed? | Which module failed and why? |
| `inventory.*` | Is it fast enough? | Did inventory complete within expected duration? |
| `prepare.*` | Is it working? | Did prepare report write for every module? |
| `prepare.*` | Is it correct? | How many items are unresolved (blocking)? |
| `prepare.*` | What failed? | Which module had unresolved items and which are blocking? |
| `prepare.*` | Is it fast enough? | Did prepare phase complete within expected duration? |
| `analyse.dependencies` | Is it working? | Did dependency analysis write artefacts? |
| `analyse.dependencies` | Is it correct? | How many dependency links were found? Zero is suspicious. |
| `analyse.dependencies` | What failed? | Did analysis fail mid-way? Which work item caused failure? |
| `analyse.dependencies` | Is it fast enough? | Did analysis complete within expected duration? |
| `analyse.inventory` | Is it working? | Did `InventoryAnalyser` produce consolidated `inventory.json` and `inventory.csv`? |
| `analyse.inventory` | Is it correct? | Do consolidated counts match sum of per-module files? |
| `analyse.inventory` | What failed? | Which per-module `{Module}/inventory.json` file was missing or zero? |

### Metrics

> **Naming note:** Inventory and analyse metrics use the `discovery.*` meter (`WellKnownMeterNames.Discovery`) and `WellKnownDiscoveryMetricNames`. Prepare metrics use the `migration.*` meter (`WellKnownMeterNames.Migration`) and `WellKnownMetricNames`. Metric `discovery.inventory.workitems` and `discovery.dependencies.links` / `discovery.dependencies.workitems_analysed` **already exist** in `WellKnownDiscoveryMetricNames` and MUST be reused — do not create duplicates.

| Metric Name | Instrument | Unit | Operation | Decision | Status |
|---|---|---|---|---|---|
| `discovery.inventory.workitems` | `Counter<long>` | `{workitem}` | `inventory.workitems` | Is it working? / Is it correct? | **REUSE** existing |
| `discovery.inventory.workitems.duration_ms` | `Histogram<double>` | `ms` | `inventory.workitems` | Is it fast enough? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.workitems.errors` | `Counter<long>` | `{error}` | `inventory.workitems` | What failed? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.identities` | `Counter<long>` | `{identity}` | `inventory.identities` | Is it working? / Is it correct? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.identities.errors` | `Counter<long>` | `{error}` | `inventory.identities` | What failed? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.nodes` | `Counter<long>` | `{node}` | `inventory.nodes` | Is it working? / Is it correct? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.nodes.errors` | `Counter<long>` | `{error}` | `inventory.nodes` | What failed? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.teams` | `Counter<long>` | `{team}` | `inventory.teams` | Is it working? / Is it correct? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.teams.errors` | `Counter<long>` | `{error}` | `inventory.teams` | What failed? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `migration.workitems.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.workitems` | Is it correct? | NEW → add to `WellKnownMetricNames` |
| `migration.workitems.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.workitems` | What failed? | NEW → add to `WellKnownMetricNames` |
| `migration.workitems.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.workitems` | Is it fast enough? | NEW → add to `WellKnownMetricNames` |
| `migration.identities.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.identities` | Is it correct? | NEW → add to `WellKnownMetricNames` |
| `migration.identities.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.identities` | What failed? | NEW → add to `WellKnownMetricNames` |
| `migration.identities.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.identities` | Is it fast enough? | NEW → add to `WellKnownMetricNames` |
| `migration.nodes.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.nodes` | Is it correct? | NEW → add to `WellKnownMetricNames` |
| `migration.nodes.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.nodes` | What failed? | NEW → add to `WellKnownMetricNames` |
| `migration.nodes.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.nodes` | Is it fast enough? | NEW → add to `WellKnownMetricNames` |
| `migration.teams.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.teams` | Is it correct? | NEW → add to `WellKnownMetricNames` |
| `migration.teams.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.teams` | What failed? | NEW → add to `WellKnownMetricNames` |
| `migration.teams.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.teams` | Is it fast enough? | NEW → add to `WellKnownMetricNames` |
| `discovery.inventory.consolidated` | `Counter<long>` | `{module}` | `analyse.inventory` | Is it correct? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.consolidated.duration_ms` | `Histogram<double>` | `ms` | `analyse.inventory` | Is it fast enough? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.inventory.consolidated.errors` | `Counter<long>` | `{error}` | `analyse.inventory` | What failed? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.dependencies.links` | `Counter<long>` | `{link}` | `analyse.dependencies` | Is it correct? | **REUSE** existing |
| `discovery.dependencies.workitems_analysed` | `Counter<long>` | `{workitem}` | `analyse.dependencies` | Is it working? | **REUSE** existing |
| `discovery.dependencies.analyse.duration_ms` | `Histogram<double>` | `ms` | `analyse.dependencies` | Is it fast enough? | NEW → add to `WellKnownDiscoveryMetricNames` |
| `discovery.dependencies.analyse.errors` | `Counter<long>` | `{error}` | `analyse.dependencies` | What failed? | NEW → add to `WellKnownDiscoveryMetricNames` |

### Traces

> `WellKnownActivitySourceNames.Discovery` = `"DevOpsMigrationPlatform.Discovery"` (inventory + analyse spans).  
> `WellKnownActivitySourceNames.Migration` = `"DevOpsMigrationPlatform.Migration"` (prepare spans).

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| `WorkItemsModule` | `inventory.workitems` | `job.id`, `module=WorkItems`, `project` | Root | Is it working? / Is it fast enough? |
| `IInventoryOrchestrator` (child) | `inventory.workitems.window` | `job.id`, `window_index`, `project` | `inventory.workitems` | Where is it slow? |
| `IdentitiesModule` | `inventory.identities` | `job.id`, `module=Identities`, `project` | Root | Is it working? |
| `NodesModule` | `inventory.nodes` | `job.id`, `module=Nodes`, `project` | Root | Is it working? |
| `TeamsModule` | `inventory.teams` | `job.id`, `module=Teams`, `project` | Root | Is it working? |
| `WorkItemsModule` | `prepare.workitems` | `job.id`, `module=WorkItems` | Root | Is it working? |
| `IdentitiesModule` | `prepare.identities` | `job.id`, `module=Identities` | Root | Is it working? |
| `NodesModule` | `prepare.nodes` | `job.id`, `module=Nodes` | Root | Is it working? |
| `TeamsModule` | `prepare.teams` | `job.id`, `module=Teams` | Root | Is it working? |
| `DependencyAnalyser` | `analyse.dependencies` | `job.id`, `module=Dependencies` | Root | Is it working? |
| `DependencyAnalyser` (child) | `analyse.dependencies.workitem` | `job.id`, `wi.id` | `analyse.dependencies` | What failed? |
| `InventoryAnalyser` | `analyse.inventory` | `job.id`, `module=Inventory` | Root | Is it working? |

**Context propagation:** Automatic via `Activity` hierarchy. `Activity.Current` is ambient in all async operations. `job.id` added as tag via `ActivityTagsCollection` at start of each root span. No W3C header injection needed (in-process).

### Logging

> No raw customer data (project names, org URLs, field values, attachment paths) without `DataClassification.Customer` scope. Work item IDs are integer identifiers — not customer data.

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| Inventory started | `Information` | `job.id`, `module`, `project` | `inventory.*` | Is it working? |
| Inventory completed | `Information` | `job.id`, `module`, `count`, `durationMs` | `inventory.*` | Is it working? / Is it fast enough? |
| Inventory zero warning | `Warning` | `job.id`, `module`, `project` | `inventory.*` | What failed? |
| Inventory failed | `Error` | `job.id`, `module`, `errorType`, `errorMessage`, `durationMs` | `inventory.*` | What failed? |
| Inventory window detail | `Debug` | `job.id`, `module`, `windowIndex`, `itemCount` | `inventory.workitems` | (diagnostic) |
| Prepare started | `Information` | `job.id`, `module` | `prepare.*` | Is it working? |
| Prepare completed | `Information` | `job.id`, `module`, `resolved`, `unresolved`, `durationMs` | `prepare.*` | Is it working? / Is it correct? |
| Prepare unresolved warning | `Warning` | `job.id`, `module`, `unresolved` | `prepare.*` | What failed? |
| Prepare failed | `Error` | `job.id`, `module`, `errorType`, `errorMessage`, `durationMs` | `prepare.*` | What failed? |
| Prepare item detail | `Debug` | `job.id`, `module`, `itemId`, `resolution` | `prepare.*` | (diagnostic) |
| Analyse started | `Information` | `job.id` | `analyse.dependencies` | Is it working? |
| Analyse completed | `Information` | `job.id`, `links`, `workitemsAnalysed`, `durationMs` | `analyse.dependencies` | Is it working? / Is it correct? |
| Analyse zero warning | `Warning` | `job.id` | `analyse.dependencies` | What failed? |
| Analyse failed | `Error` | `job.id`, `errorType`, `errorMessage`, `durationMs` | `analyse.dependencies` | What failed? |
| Analyse item detail | `Debug` | `job.id`, `wi.id` | `analyse.dependencies` | (diagnostic) |
| Inventory analyse started | `Information` | `job.id` | `analyse.inventory` | Is it working? |
| Inventory analyse completed | `Information` | `job.id`, `moduleCount`, `durationMs` | `analyse.inventory` | Is it working? / Is it correct? |
| Inventory analyse missing source | `Warning` | `job.id`, `module` | `analyse.inventory` | What failed? |
| Inventory analyse zero warning | `Warning` | `job.id` | `analyse.inventory` | What failed? |
| Inventory analyse failed | `Error` | `job.id`, `errorType`, `errorMessage`, `durationMs` | `analyse.inventory` | What failed? |

> Debug and Trace levels are disabled by default. Enabled via `Logging:LogLevel:DevOpsMigrationPlatform=Debug` in appsettings.

### Correlation

| Field | Source | Scope |
|---|---|---|
| `traceId` / `operationId` | `Activity.Current.TraceId` (established by `StartActivity`) | All telemetry for an operation |
| `parentId` | `Activity.Current.ParentSpanId` | Child spans and logs within a parent context |
| `job.id` | `InventoryContext.JobId` / `PrepareContext.JobId` / `AnalyseContext.JobId` | All telemetry within a job |
| `module` | Module name constant (e.g. `"WorkItems"`, `"Teams"`) | Operation-scoped spans and logs |
| `project` | `InventoryContext.SourceProject` | Inventory spans and logs |
| `wi.id` | Work item integer ID | Child spans in analyse |

### Validation Queries

#### Failure Identification
```kql
// Identify which inventory/prepare/analyse operations failed and why
customMetrics
| where name in ("discovery.inventory.workitems.errors","discovery.inventory.identities.errors",
                 "discovery.inventory.nodes.errors","discovery.inventory.teams.errors",
                 "discovery.dependencies.analyse.errors")
    or name in ("migration.workitems.prepare.unresolved","migration.identities.prepare.unresolved",
                "migration.nodes.prepare.unresolved","migration.teams.prepare.unresolved")
| summarize errorCount=sum(value) by name, bin(timestamp, 5m)
| where errorCount > 0
| order by timestamp desc
```

#### Latency Analysis
```kql
// P50/P95/P99 latency per operation phase
customMetrics
| where name in ("discovery.inventory.workitems.duration_ms",
                 "migration.workitems.prepare.duration_ms",
                 "migration.identities.prepare.duration_ms",
                 "migration.nodes.prepare.duration_ms",
                 "migration.teams.prepare.duration_ms",
                 "discovery.dependencies.analyse.duration_ms")
| summarize p50=percentile(value, 50), p95=percentile(value, 95), p99=percentile(value, 99)
          by name, bin(timestamp, 1h)
| order by timestamp desc
```

#### Load Observation
```kql
// In-flight / concurrency — inventory and analyse use Discovery meter;
// no concurrent in-flight counter defined (inventory is sequential per module).
// Monitor via active job count from control plane:
customMetrics
| where name == "discovery.jobs.active"
| summarize avg(value) by bin(timestamp, 1m)
```

#### End-to-End Trace
```kql
// Trace a single inventory job from start to all module completions
dependencies
| where type == "InProc" and name startswith "inventory."
| where customDimensions["job.id"] == "<job_id>"
| project timestamp, name, duration, success, customDimensions["module"]
| order by timestamp asc
```

#### Error Diagnosis
```kql
// Join error logs with traces to determine root cause
traces
| where severityLevel >= 3  // Error
| where customDimensions["job.id"] == "<job_id>"
| project timestamp, message, customDimensions["module"], customDimensions["errorType"], operation_Id
| join kind=leftouter (
    dependencies
    | where customDimensions["job.id"] == "<job_id>"
    | where success == false
    | project operation_Id, spanName=name, spanDuration=duration
) on operation_Id
| order by timestamp desc
```

### Wiring Checklist

- [x] **O-1 ActivitySource:** `WellKnownActivitySourceNames.Discovery` used for inventory and analyse spans. `WellKnownActivitySourceNames.Migration` used for prepare spans. No new sources needed.
- [x] **O-2 Metric instruments (reuse):** `discovery.inventory.workitems`, `discovery.dependencies.links`, `discovery.dependencies.workitems_analysed` already exist in `WellKnownDiscoveryMetricNames` — REUSE, do not duplicate.
- [x] **O-2 Metric instruments (new):** All new names follow `discovery.*` or `migration.<module>.prepare.*` convention. Added to `WellKnownDiscoveryMetricNames` and `WellKnownMetricNames` respectively.
- [x] **O-2 Meter registration:** New instruments attach to existing `Discovery` and `Migration` meters. No new `.AddMeter()` calls required.
- [x] **O-3 Log structured params:** All log calls use structured params — no string interpolation. `DataClassification.Customer` scope on any field containing project name or org URL.
- [x] **O-4 IProgressSink wiring:** `IProgressSink?` injected as optional into all modules and `DependencyAnalyser`. `EmitAsync` called at start, per-batch (≤50), and completion.
- [x] **O-4 ModuleCounters property:** `MigrationCounters` gains `Inventory` and `Prepare` sub-counters. `SnapshotMetricExporter.cs` maps these into `JobMetrics` for CLI/TUI.
- [x] **O-4 CLI row:** `QueueCommand.BuildProgressRenderable` gains rows for Inventory and Prepare phases in correct order. Dependencies analysis row added.
- [x] **DI wiring verified:** `DependencyAnalyser` registered via `AddDependencyAnalyserServices()`. `InventoryAnalyser` registered via `AddInventoryAnalyserServices()`. `IInventoryOrchestrator` updated to accept `InventoryContext`.

### Tests Required for Observability

- [ ] Unit test: verify `ActivitySource.StartActivity("inventory.workitems")` called in `WorkItemsModule.InventoryAsync`
- [ ] Unit test: verify `ActivitySource.StartActivity("prepare.workitems")` called in `WorkItemsModule.PrepareAsync`
- [ ] Unit test: verify `ActivitySource.StartActivity("analyse.dependencies")` called in `DependencyAnalyser.AnalyseAsync`
- [ ] Unit test: verify `IDiscoveryMetrics` receives `discovery.inventory.workitems` counter increment (inject mock)
- [ ] Unit test: verify `IMigrationMetrics` receives `migration.workitems.prepare.resolved` / `.unresolved` increments (inject mock)
- [ ] Unit test: verify `IProgressSink.EmitAsync` called at start and completion for each new phase method
- [ ] Unit test: verify `ILogger` receives `Warning` when inventory count = 0
- [ ] Unit test: verify `ILogger` receives `Warning` when analyser writes zero artefacts
- [ ] Unit test: verify plan builder hoists `analyse` task before `prepare` task when a module's `DependsOn` contains a `DependencyPhase.Analyse` reference (FR-021)
- [ ] Unit test: verify plan builder fails at plan-build time when an `IAnalyser` required by a prepare module's `DependsOn` is not registered
- [ ] Simulated system test: `JobKind.Prepare` with a module that declares `DependsOn` on `DependencyAnalyser` completes → `analyse` task runs first, `prepare-report.json` reads analyser artefact from `IArtefactStore`
- [ ] Simulated system test: `JobKind.Inventory` completes → `inventory.json` exists with non-zero counts
- [ ] Simulated system test: `JobKind.Prepare` completes → `{Module}/prepare-report.json` written for each module
- [ ] Simulated system test: `JobKind.Dependencies` completes → `analysis/dependencies.csv` written with ≥1 row

## Project Structure

### Documentation (this feature)

```text
specs/030-module-analiser-refactor/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output ✅
├── data-model.md        # Phase 1 output ✅
├── checklists/
│   └── requirements.md  # Spec quality checklist ✅
├── discrepancies.md     # Architecture gaps ✅
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created here)
```

### Source Code (repository root)

```text
src/DevOpsMigrationPlatform.Abstractions.Agent/
├── Modules/
│   ├── IModule.cs                        ← MODIFY: add InventoryAsync, PrepareAsync, Supports* flags
│   ├── DependencyPhase.cs                ← MODIFY: add Inventory=0, Prepare=4, Analyse=5
│   ├── ModuleDependency.cs               ← MODIFY: strip "Analyser" suffix; add AppliesToInventory/Prepare/Analyse
│   ├── InventoryContext.cs               ← NEW
│   ├── PrepareContext.cs                 ← NEW
│   └── PrepareReport.cs                  ← NEW (produced by PrepareAsync — belongs with Modules)
├── Analysis/
│   ├── IAnalyser.cs                      ← NEW
│   ├── IOrganisationsAnalyser.cs         ← NEW
│   ├── IEndpointPairAnalyser.cs          ← NEW
│   ├── AnalyseContext.cs                 ← NEW
│   ├── OrganisationsAnalyseContext.cs    ← NEW
│   └── EndpointPairAnalyseContext.cs     ← NEW
└── Discovery/
    └── IInventoryOrchestrator.cs         ← MODIFY: ExportContext → InventoryContext; remove organisations param

src/DevOpsMigrationPlatform.Infrastructure.Agent/
├── Modules/
│   ├── WorkItemsModule.cs                ← MODIFY: add InventoryAsync, PrepareAsync; SupportsInventory/SupportsPrepare = true
│   ├── IdentitiesModule.cs               ← MODIFY: add InventoryAsync, PrepareAsync
│   ├── NodesModule.cs                    ← MODIFY: add InventoryAsync, PrepareAsync
│   ├── TeamsModule.cs                    ← MODIFY: add InventoryAsync, PrepareAsync
│   ├── InventoryModule.cs                ← DELETE
│   └── InventoryDiscoveryModule.cs       ← DELETE
├── Analysis/
│   ├── DependencyAnalyser.cs             ← NEW (replaces DependencyDiscoveryModule)
│   ├── DependencyAnalyserServiceCollectionExtensions.cs ← NEW
│   ├── InventoryAnalyser.cs              ← NEW
│   └── InventoryAnalyserServiceCollectionExtensions.cs ← NEW
├── Discovery/
│   ├── DependencyDiscoveryModule.cs      ← DELETE
│   └── DependencyOrchestrator.cs        ← MODIFY (adapt to OrganisationsAnalyseContext)
│   └── InventoryOrchestrator.cs          ← MODIFY: adapt to InventoryContext
└── Context/
    └── JobExecutionPlanBuilder.cs        ← MODIFY: discover IAnalyser; build inventory/prepare/analyse tasks

src/DevOpsMigrationPlatform.MigrationAgent/
└── JobAgentWorker.cs                     ← MODIFY: multi-org loop for inventory; JobKind.Prepare dispatch

src/DevOpsMigrationPlatform.Abstractions/
└── Telemetry/
    └── WellKnownMetricNames.cs           ← MODIFY: add inventory/prepare/analyse metric name constants

src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
└── Modules/
    └── TfsWorkItemsModule.cs             ← MODIFY: add InventoryAsync (delegate to IInventoryOrchestrator),
                                                      PrepareAsync (graceful skip + Warning log)

features/
├── inventory/
│   ├── simulated/
│   │   └── inventory-workitems.feature       ← NEW
│   ├── ado/
│   │   └── inventory-workitems.feature       ← NEW
│   └── tfs/
│       └── inventory-workitems.feature       ← NEW
├── prepare/
│   ├── simulated/
│   │   └── prepare-workitems.feature         ← NEW
│   ├── ado/
│   │   └── prepare-workitems.feature         ← NEW
│   └── tfs/
│       └── prepare-graceful-skip.feature     ← NEW (TFS source-only no-op scenario)
└── analysis/
    ├── simulated/
    │   ├── dependency-analysis.feature   ← NEW
    │   └── inventory-analysis.feature    ← NEW
    ├── ado/
    │   └── dependency-analysis.feature   ← NEW
    └── tfs/
        └── dependency-analysis.feature   ← NEW

tests/
└── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
    ├── Modules/
    │   ├── WorkItemsModuleInventoryTests.cs   ← NEW
    │   ├── WorkItemsModulePrepareTests.cs     ← NEW
    │   ├── IdentitiesModuleInventoryTests.cs  ← NEW
    │   ├── NodesModuleInventoryTests.cs       ← NEW
    │   └── TeamsModuleInventoryTests.cs       ← NEW
    └── Analysis/
        ├── DependencyAnalyserTests.cs     ← NEW
        └── InventoryAnalyserTests.cs      ← NEW
```

**Structure Decision**: Single repo, modifying existing assemblies. No new assemblies. New types in `Abstractions.Agent` (interfaces and context records) and `Infrastructure.Agent` (implementations and analyser).

## Complexity Tracking

No constitution violations requiring justification.
