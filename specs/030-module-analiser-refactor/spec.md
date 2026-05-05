# Feature Specification: Module IModule Phase Consolidation and IAnalyser Introduction

**Feature Branch**: `030-module-analiser-refactor`  
**Created**: 2026-05-03  
**Status**: Draft  
**Input**: Refactor `IModule` to expose all five migration phases (`Inventory`, `Export`, `Prepare`, `Import`, `Validate`), eliminate the standalone `InventoryModule`, `InventoryDiscoveryModule`, and `DependencyDiscoveryModule`, and introduce a new `IAnalyser` interface for cross-cutting analysis participants.

---

## Architecture References

| Document | Status |
|---|---|
| `docs/modules.md` | ⚠️ Has discrepancies — `IModule` contract, discovery module descriptions, phase dispatch table all require update |
| `docs/architecture.md` | ✓ Confirmed accurate — phase gate rules already reference `Inventory` and `Prepare` phases |
| `.agents/guardrails/system-architecture.md` | ✓ Confirmed — rule 10 (phase gates), rule 15 (job unit), rule 17 (execution plan) all apply |
| `.agents/guardrails/module-template.md` | ⚠️ Has discrepancies — template describes only `ExportAsync`/`ImportAsync`; must be extended for `InventoryAsync`/`PrepareAsync` |
| `analysis/draftspec-Module-refactor-consolidation.md` | Source design document — grounded this spec |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Run Inventory Without a Separate Inventory Module (Priority: P1)

A migration operator running `queue inventory` against a project wants a complete count of work items, revisions, teams, nodes, and identities — without needing a dedicated `InventoryModule` enabled in their config. Each domain module should contribute its own counts automatically when inventory is run.

**Why this priority**: The current `InventoryModule` abuses `ExportAsync` to run inventory logic. Every operator who runs `queue inventory` is affected by this semantic mismatch. Correcting it is the most visible observable change.

**Independent Test**: Can be fully tested by submitting a `JobKind.Inventory` job with `WorkItemsModule` enabled and no `InventoryModule` configured, and asserting that `WorkItems/inventory.json` is written with non-zero work item and revision counts.

**Acceptance Scenarios**:

1. **Given** a job of kind `Inventory` with `WorkItemsModule`, `TeamsModule`, `NodesModule`, and `IdentitiesModule` enabled, **When** the job executes, **Then** each module's inventory contribution is written to the package as `{Module}/inventory.json` with non-zero counts for each domain.
2. **Given** `InventoryModule` is not referenced in the config, **When** a `JobKind.Inventory` job runs, **Then** the job succeeds and produces inventory artefacts identical to those previously produced by `InventoryModule`.
3. **Given** a `JobKind.Export` job, **When** the job executes, **Then** the inventory phase runs automatically before the export phase (phase gate rule 10), and the package contains both inventory and export artefacts.

---

### User Story 2 — Multi-Organisation Inventory Without a Discovery Module (Priority: P2)

An operator with multiple Azure DevOps organisations configured wants to run cross-org inventory. Today they rely on `InventoryDiscoveryModule`, which is a separate module class that abuses the export phase. After this change, the orchestrator loops over organisations and calls each module's `InventoryAsync` per org — the operator configures multiple source endpoints, not a special module.

**Why this priority**: Eliminates the highest-complexity module in the codebase (multi-org loop inside a module class). Simplifies operator config and removes the semantic confusion.

**Independent Test**: Can be tested by submitting a `JobKind.Inventory` job with two simulated source endpoints and asserting that `inventory.json` contains entries from both organisations.

**Acceptance Scenarios**:

1. **Given** a job config with two `sourceEndpoints`, **When** a `JobKind.Inventory` job runs, **Then** `InventoryAsync` is called once per module per organisation, and the aggregated artefacts contain data from all organisations.
2. **Given** `InventoryDiscoveryModule` is not in the config, **When** a multi-org inventory job runs, **Then** the job succeeds and produces the same artefacts that `InventoryDiscoveryModule` previously produced.
3. **Given** one of the configured organisations is unreachable, **When** the inventory job runs, **Then** the failing organisation is recorded as an error in the job log and the remaining organisations are still processed.

---

### User Story 3 — Run Prepare Phase to Validate Target Before Import (Priority: P2)

An operator who has completed an export wants to run `queue prepare` to check whether all referenced area/iteration paths, teams, and identities already exist on the target — and get a prepare report — before committing to a full import.

**Why this priority**: `PrepareAsync` is documented in `docs/modules.md` but not implemented on `IModule`. Adding it closes the gap between documentation and reality and unlocks the `JobKind.Prepare` job kind.

**Independent Test**: Can be tested by submitting a `JobKind.Prepare` job against a package produced by export and asserting that `{Module}/prepare-report.json` files are written by each enabled module.

**Acceptance Scenarios**:

1. **Given** a package produced by a successful export and a configured target, **When** a `JobKind.Prepare` job runs, **Then** each domain module writes a `prepare-report.json` to its package folder containing resolved and unresolved mappings.
2. **Given** a `PrepareAsync` run finds unresolved identity mappings, **When** the prepare job completes, **Then** unresolved items appear in `Identities/prepare-report.json` and the job completes with a warning (not an error) unless the operator has configured `blockOnUnresolved: true`.
3. **Given** a `JobKind.Migrate` job, **When** the pipeline executes, **Then** `PrepareAsync` runs automatically between the Export and Import phases as per the phase gate rules.
4. **Given** a module declares a `DependsOn` entry on an `IAnalyser` with `DependencyPhase.Analyse`, **When** a `JobKind.Prepare` job runs, **Then** the plan builder hoists the required `analyse` tasks before the `prepare` tasks and the module's `PrepareAsync` can read the analyser's artefacts from `IArtefactStore`.

---

### User Story 4 — Run Dependency Analysis as a Distinct Analysis Operation (Priority: P3)

An operator planning a cross-project migration wants to run `queue dependencies` to produce a dependency map (`dependencies.csv`) of linked work items across projects and organisations — without that operation being treated as an export. The result is a planning artefact, never imported.

**Why this priority**: `DependencyDiscoveryModule` currently abuses `ExportAsync`. Promoting it to a first-class `IAnalyser` makes its purpose explicit and opens the door to future analysis operations (process diff, blast radius, compliance).

**Independent Test**: Can be tested by submitting a `JobKind.Dependencies` job and asserting that `analysis/dependencies.csv` is written with at least one row.

**Acceptance Scenarios**:

1. **Given** a `JobKind.Dependencies` job with `DependencyAnalyser` registered, **When** the job executes, **Then** `DependencyAnalyser.AnalyseAsync` runs directly (no inventory prerequisite), producing `analysis/dependencies.csv` and `analysis/dependencies.mmd`.
2. **Given** the inventory artefacts are already present in the package, **When** a `JobKind.Dependencies` job runs, **Then** the analyse phase runs directly — `DependencyAnalyser.AnalyseAsync` may optionally read existing `inventory.json` artefacts from `IArtefactStore` for progress-display purposes, but no inventory phase is triggered.
3. **Given** a `JobKind.Inventory` job with `DependencyAnalyser` registered, **When** the job executes, **Then** `AnalyseAsync` runs after all `InventoryAsync` calls complete, and the analysis artefacts are written.

---

### Edge Cases

- What happens when a module reports `SupportsInventory = false` but a `JobKind.Inventory` job is submitted with that module enabled? → Module is skipped for the inventory phase without error; its `InventoryAsync` is not called.
- What happens when `PrepareAsync` finds a blocking issue (e.g., required field mapping is entirely absent)? → If `blockOnUnresolved` is `true`, the job fails with a structured error and the Import phase does not start.
- What happens when a multi-org inventory job has one org with zero work items? → The module MUST emit a structured `Warning` log (silent zero-count completion is forbidden) and the aggregate inventory still completes.
- What happens when an `IAnalyser` declares a `DependsOn` on a module phase that was not executed in the current job plan? → The plan builder detects the unsatisfied dependency and fails the job at plan-build time with a descriptive error.
- What happens when the `IModule` contract is extended but an existing module does not implement the new methods? → The abstract base class (`ModuleBase`) provides default **no-op implementations** that emit one structured `Warning` log (`"Module {Name} does not support phase {Phase} — skipping"`) and return `Task.CompletedTask`. They MUST NOT throw `NotSupportedException` or `NotImplementedException`. The `Supports*` guard in the plan builder ensures unsupported phases are never called, but the default implementation must be safe even if the guard misfires.
- What happens when `TfsWorkItemsModule.PrepareAsync` is called? → `TfsWorkItemsModule` is source-only. `SupportsPrepare` returns `false` and the plan builder will not call `PrepareAsync`. If called directly (e.g., in a test), the `ModuleBase` default no-op fires: one structured `Warning` log (`"TfsWorkItemsModule does not support Prepare — skipping"`) and returns `Task.CompletedTask`. This behaviour is asserted by `features/prepare/tfs/prepare-graceful-skip.feature`.
- What happens when a `PrepareAsync` module declares `DependsOn` an `IAnalyser`, but the analyser's own `DependsOn` (e.g., on `WorkItemsModule` inventory output) is not satisfiable in the current job plan? → The plan builder detects the transitive unsatisfied dependency and fails the job at plan-build time with a descriptive error, the same as for any other unsatisfied `DependsOn`.
- What happens when a module's `PrepareAsync` reads analyser artefacts via `IArtefactStore` but the analyser has not yet been registered? → The plan builder enforces the `DependsOn` declaration; if no registered `IAnalyser` satisfies the dependency, plan-build fails. Modules MUST NOT read analyser artefacts without a corresponding `DependsOn` — silent reads on absent artefacts are forbidden.

---

## Observability

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `inventory.workitems` | module | `WorkItemsModule.InventoryAsync` | `IInventoryOrchestrator`, `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
| `inventory.identities` | module | `IdentitiesModule.InventoryAsync` | ADO/TFS connector, `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
| `inventory.nodes` | module | `NodesModule.InventoryAsync` | ADO/TFS connector, `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
| `inventory.teams` | module | `TeamsModule.InventoryAsync` | ADO/TFS connector, `IArtefactStore`, `IDiscoveryMetrics`, `IProgressSink?` |
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
| `inventory.*` | Is it fast enough? | Did inventory complete within expected duration? |
| `inventory.*` | Is it overloaded? | Are inventory jobs queued faster than they complete? |
| `inventory.*` | What failed? | Which module failed and why? |
| `inventory.*` | Is it correct? | Do inventory counts match known source data? |
| `prepare.*` | Is it working? | Did prepare report write for every module? |
| `prepare.*` | Is it fast enough? | Did prepare phase complete within expected duration? |
| `prepare.*` | Is it overloaded? | Are prepare jobs stacking up? |
| `prepare.*` | What failed? | Which module had unresolved items and are any blocking? |
| `prepare.*` | Is it correct? | How many items are unresolved vs resolved per module? |
| `analyse.inventory` | Is it working? | Was `inventory.json` written with non-zero consolidated counts? |
| `analyse.inventory` | Is it fast enough? | Did consolidation complete within expected duration? |
| `analyse.inventory` | Is it overloaded? | Are there too many concurrent inventory jobs? |
| `analyse.inventory` | What failed? | Which per-module count file was missing or caused consolidation to fail? |
| `analyse.dependencies` | Is it working? | Did dependency analysis write artefacts? |
| `analyse.dependencies` | Is it fast enough? | Did analysis complete within expected duration? |
| `analyse.dependencies` | Is it overloaded? | Are dependency analysis jobs blocked or queued? |
| `analyse.dependencies` | What failed? | Which work item caused the failure? |
| `analyse.dependencies` | Is it correct? | How many dependency links were found? Zero is suspicious. |

### Metrics

> **Naming:** Inventory and analyse metrics use the `discovery.*` meter (`WellKnownMeterNames.Discovery`) via `IDiscoveryMetrics`. Prepare metrics use the `migration.*` meter (`WellKnownMeterNames.Migration`) via `IMigrationMetrics`. Constants marked **REUSE** already exist in `WellKnownDiscoveryMetricNames`; all others are **NEW** and must be added to the corresponding `WellKnown*MetricNames` class before use.

| Metric Name | Instrument | Unit | Operation | Decision |
|---|---|---|---|---|
| `discovery.inventory.workitems` | `Counter<long>` | `{workitem}` | `inventory.workitems` | Is it working? / Is it correct? — **REUSE** |
| `discovery.inventory.workitems.duration_ms` | `Histogram<double>` | `ms` | `inventory.workitems` | Is it fast enough? — **NEW** |
| `discovery.inventory.workitems.errors` | `Counter<long>` | `{error}` | `inventory.workitems` | What failed? — **NEW** |
| `discovery.inventory.workitems.in_flight` | `UpDownCounter<long>` | `{operation}` | `inventory.workitems` | Is it overloaded? — **NEW** |
| `discovery.inventory.identities` | `Counter<long>` | `{identity}` | `inventory.identities` | Is it working? / Is it correct? — **NEW** |
| `discovery.inventory.identities.duration_ms` | `Histogram<double>` | `ms` | `inventory.identities` | Is it fast enough? — **NEW** |
| `discovery.inventory.identities.errors` | `Counter<long>` | `{error}` | `inventory.identities` | What failed? — **NEW** |
| `discovery.inventory.identities.in_flight` | `UpDownCounter<long>` | `{operation}` | `inventory.identities` | Is it overloaded? — **NEW** |
| `discovery.inventory.nodes` | `Counter<long>` | `{node}` | `inventory.nodes` | Is it working? / Is it correct? — **NEW** |
| `discovery.inventory.nodes.duration_ms` | `Histogram<double>` | `ms` | `inventory.nodes` | Is it fast enough? — **NEW** |
| `discovery.inventory.nodes.errors` | `Counter<long>` | `{error}` | `inventory.nodes` | What failed? — **NEW** |
| `discovery.inventory.nodes.in_flight` | `UpDownCounter<long>` | `{operation}` | `inventory.nodes` | Is it overloaded? — **NEW** |
| `discovery.inventory.teams` | `Counter<long>` | `{team}` | `inventory.teams` | Is it working? / Is it correct? — **NEW** |
| `discovery.inventory.teams.duration_ms` | `Histogram<double>` | `ms` | `inventory.teams` | Is it fast enough? — **NEW** |
| `discovery.inventory.teams.errors` | `Counter<long>` | `{error}` | `inventory.teams` | What failed? — **NEW** |
| `discovery.inventory.teams.in_flight` | `UpDownCounter<long>` | `{operation}` | `inventory.teams` | Is it overloaded? — **NEW** |
| `migration.workitems.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.workitems` | Is it working? / Is it correct? — **NEW** |
| `migration.workitems.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.workitems` | What failed? / Is it correct? — **NEW** |
| `migration.workitems.prepare.errors` | `Counter<long>` | `{error}` | `prepare.workitems` | What failed? — **NEW** |
| `migration.workitems.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.workitems` | Is it fast enough? — **NEW** |
| `migration.workitems.prepare.in_flight` | `UpDownCounter<long>` | `{operation}` | `prepare.workitems` | Is it overloaded? — **NEW** |
| `migration.identities.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.identities` | Is it working? / Is it correct? — **NEW** |
| `migration.identities.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.identities` | What failed? — **NEW** |
| `migration.identities.prepare.errors` | `Counter<long>` | `{error}` | `prepare.identities` | What failed? — **NEW** |
| `migration.identities.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.identities` | Is it fast enough? — **NEW** |
| `migration.identities.prepare.in_flight` | `UpDownCounter<long>` | `{operation}` | `prepare.identities` | Is it overloaded? — **NEW** |
| `migration.nodes.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.nodes` | Is it working? / Is it correct? — **NEW** |
| `migration.nodes.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.nodes` | What failed? — **NEW** |
| `migration.nodes.prepare.errors` | `Counter<long>` | `{error}` | `prepare.nodes` | What failed? — **NEW** |
| `migration.nodes.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.nodes` | Is it fast enough? — **NEW** |
| `migration.nodes.prepare.in_flight` | `UpDownCounter<long>` | `{operation}` | `prepare.nodes` | Is it overloaded? — **NEW** |
| `migration.teams.prepare.resolved` | `Counter<long>` | `{item}` | `prepare.teams` | Is it working? / Is it correct? — **NEW** |
| `migration.teams.prepare.unresolved` | `Counter<long>` | `{item}` | `prepare.teams` | What failed? — **NEW** |
| `migration.teams.prepare.errors` | `Counter<long>` | `{error}` | `prepare.teams` | What failed? — **NEW** |
| `migration.teams.prepare.duration_ms` | `Histogram<double>` | `ms` | `prepare.teams` | Is it fast enough? — **NEW** |
| `migration.teams.prepare.in_flight` | `UpDownCounter<long>` | `{operation}` | `prepare.teams` | Is it overloaded? — **NEW** |
| `discovery.inventory.consolidated` | `Counter<long>` | `{item}` | `analyse.inventory` | Is it working? — **NEW** |
| `discovery.inventory.consolidated.duration_ms` | `Histogram<double>` | `ms` | `analyse.inventory` | Is it fast enough? — **NEW** |
| `discovery.inventory.consolidated.errors` | `Counter<long>` | `{error}` | `analyse.inventory` | What failed? — **NEW** |
| `discovery.dependencies.links` | `Counter<long>` | `{link}` | `analyse.dependencies` | Is it correct? — **REUSE** |
| `discovery.dependencies.workitems_analysed` | `Counter<long>` | `{workitem}` | `analyse.dependencies` | Is it working? — **REUSE** |
| `discovery.dependencies.analyse.duration_ms` | `Histogram<double>` | `ms` | `analyse.dependencies` | Is it fast enough? — **NEW** |
| `discovery.dependencies.analyse.errors` | `Counter<long>` | `{error}` | `analyse.dependencies` | What failed? — **NEW** |

### Traces

> `WellKnownActivitySourceNames.Discovery` = `"DevOpsMigrationPlatform.Discovery"` for `inventory.*` and `analyse.*` spans.
> `WellKnownActivitySourceNames.Migration` = `"DevOpsMigrationPlatform.Migration"` for `prepare.*` spans.

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| `WorkItemsModule` | `inventory.workitems` | `job.id`, `module=WorkItems`, `project` | Root | Is it working? / Is it fast enough? |
| `IInventoryOrchestrator` | `inventory.workitems.window` | `job.id`, `window_index`, `project` | `inventory.workitems` | Where is it slow? |
| `IdentitiesModule` | `inventory.identities` | `job.id`, `module=Identities`, `project` | Root | Is it working? |
| `NodesModule` | `inventory.nodes` | `job.id`, `module=Nodes`, `project` | Root | Is it working? |
| `TeamsModule` | `inventory.teams` | `job.id`, `module=Teams`, `project` | Root | Is it working? |
| `WorkItemsModule` | `prepare.workitems` | `job.id`, `module=WorkItems` | Root | Is it working? |
| `IdentitiesModule` | `prepare.identities` | `job.id`, `module=Identities` | Root | Is it working? |
| `NodesModule` | `prepare.nodes` | `job.id`, `module=Nodes` | Root | Is it working? |
| `TeamsModule` | `prepare.teams` | `job.id`, `module=Teams` | Root | Is it working? |
| `InventoryAnalyser` | `analyse.inventory` | `job.id`, `module=Inventory` | Root | Is it working? |
| `DependencyAnalyser` | `analyse.dependencies` | `job.id`, `module=Dependencies` | Root | Is it working? / Is it fast enough? |
| `DependencyAnalyser` | `analyse.dependencies.workitem` | `job.id`, `wi.id` | `analyse.dependencies` | What failed? |

**Context propagation:** Automatic via `Activity` hierarchy. `Activity.Current` is ambient in all async operations. `job.id` is added as a tag via `ActivityTagsCollection` at the start of each root span. No W3C header injection required (all in-process).

### Logging

> No raw customer data (project names, org URLs, field values, attachment paths) without `DataClassification.Customer` scope. Work item IDs are integer identifiers — not customer data.

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| Inventory started | `Information` | `job.id`, `module`, `project` | `inventory.*` | Is it working? |
| Inventory completed | `Information` | `job.id`, `module`, `count`, `durationMs` | `inventory.*` | Is it working? / Is it fast enough? |
| Inventory zero items | `Warning` | `job.id`, `module`, `project` | `inventory.*` | What failed? |
| Inventory failed | `Error` | `job.id`, `module`, `errorType`, `errorMessage`, `durationMs` | `inventory.*` | What failed? |
| Inventory window detail | `Debug` | `job.id`, `module`, `windowIndex`, `itemCount` | `inventory.workitems` | (diagnostic only) |
| Prepare started | `Information` | `job.id`, `module` | `prepare.*` | Is it working? |
| Prepare completed | `Information` | `job.id`, `module`, `resolved`, `unresolved`, `durationMs` | `prepare.*` | Is it working? / Is it correct? |
| Prepare unresolved items | `Warning` | `job.id`, `module`, `unresolved` | `prepare.*` | What failed? |
| Prepare failed | `Error` | `job.id`, `module`, `errorType`, `errorMessage`, `durationMs` | `prepare.*` | What failed? |
| Prepare item detail | `Debug` | `job.id`, `module`, `itemId`, `resolution` | `prepare.*` | (diagnostic only) |
| Inventory analyse started | `Information` | `job.id` | `analyse.inventory` | Is it working? |
| Inventory analyse completed | `Information` | `job.id`, `consolidatedCount`, `durationMs` | `analyse.inventory` | Is it working? / Is it fast enough? |
| Inventory analyse missing source | `Warning` | `job.id`, `module`, `file` | `analyse.inventory` | What failed? |
| Inventory analyse zero count | `Warning` | `job.id` | `analyse.inventory` | What failed? |
| Inventory analyse failed | `Error` | `job.id`, `errorType`, `errorMessage`, `durationMs` | `analyse.inventory` | What failed? |
| Dependency analyse started | `Information` | `job.id` | `analyse.dependencies` | Is it working? |
| Dependency analyse completed | `Information` | `job.id`, `links`, `workitemsAnalysed`, `durationMs` | `analyse.dependencies` | Is it working? / Is it correct? |
| Dependency analyse zero output | `Warning` | `job.id` | `analyse.dependencies` | What failed? |
| Dependency analyse failed | `Error` | `job.id`, `errorType`, `errorMessage`, `durationMs` | `analyse.dependencies` | What failed? |
| Dependency analyse item detail | `Debug` | `job.id`, `wi.id` | `analyse.dependencies` | (diagnostic only) |

> Debug and Trace levels are disabled by default. Enabled via `Logging:LogLevel:DevOpsMigrationPlatform=Debug` in appsettings.

### Correlation

| Field | Source | Scope |
|---|---|---|
| `traceId` / `operationId` | `Activity.Current.TraceId` (established by `StartActivity`) | All telemetry for an operation |
| `parentId` | `Activity.Current.ParentSpanId` | Child spans and logs within a parent context |
| `job.id` | `InventoryContext.JobId` / `PrepareContext.JobId` / `AnalyseContext.JobId` | All telemetry within a job |
| `module` | Module name constant (e.g. `"WorkItems"`, `"Teams"`, `"Inventory"`, `"Dependencies"`) | Operation-scoped spans and logs |
| `project` | `InventoryContext.SourceProject` | Inventory spans and logs only |
| `wi.id` | Work item integer ID | Child spans in `analyse.dependencies` only |

### Validation Queries

#### Failure Identification
```kql
// Identify which inventory/prepare/analyse operations failed and why
customMetrics
| where name in ("discovery.inventory.workitems.errors","discovery.inventory.identities.errors",
                 "discovery.inventory.nodes.errors","discovery.inventory.teams.errors",
                 "discovery.inventory.consolidated.errors","discovery.dependencies.analyse.errors")
    or name in ("migration.workitems.prepare.errors","migration.workitems.prepare.unresolved",
                "migration.identities.prepare.errors","migration.identities.prepare.unresolved",
                "migration.nodes.prepare.errors","migration.nodes.prepare.unresolved",
                "migration.teams.prepare.errors","migration.teams.prepare.unresolved")
| summarize errorCount=sum(value) by name, bin(timestamp, 5m)
| where errorCount > 0
| order by timestamp desc
```

#### Latency Analysis
```kql
// P50/P95/P99 latency per operation phase
customMetrics
| where name in ("discovery.inventory.workitems.duration_ms",
                 "discovery.inventory.identities.duration_ms",
                 "discovery.inventory.nodes.duration_ms",
                 "discovery.inventory.teams.duration_ms",
                 "discovery.inventory.consolidated.duration_ms",
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
// Active discovery/inventory jobs and in-flight operations
customMetrics
| where name in ("discovery.jobs.active",
                 "discovery.inventory.workitems.in_flight",
                 "discovery.inventory.identities.in_flight",
                 "discovery.inventory.nodes.in_flight",
                 "discovery.inventory.teams.in_flight",
                 "migration.workitems.prepare.in_flight")
| summarize avg(value) by name, bin(timestamp, 1m)
```

#### End-to-End Trace
```kql
// Trace a single inventory job from start through all modules to consolidation
dependencies
| where type == "InProc"
    and (name startswith "inventory." or name startswith "analyse.inventory")
| where customDimensions["job.id"] == "<job_id>"
| project timestamp, name, duration, success, customDimensions["module"]
| order by timestamp asc
```

#### Error Diagnosis
```kql
// Join error logs with traces to determine root cause per job
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

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST dispatch `JobKind.Inventory` to an `InventoryAsync` method on each enabled `IModule` where `SupportsInventory` is `true`, NOT to `ExportAsync`.
- **FR-002**: The platform MUST dispatch `JobKind.Prepare` to a `PrepareAsync` method on each enabled `IModule` where `SupportsPrepare` is `true`.
- **FR-003**: Each `IModule` implementation MUST declare whether it supports each phase via `SupportsInventory`, `SupportsExport`, `SupportsPrepare`, and `SupportsImport` boolean properties.
- **FR-004**: `WorkItemsModule` MUST implement `InventoryAsync` and write its domain counts to `WorkItems/inventory.json` via `IArtefactStore`. It MUST NOT write the consolidated `inventory.json` or `inventory.csv` directly — those are produced by `InventoryAnalyser` (FR-027).
- **FR-005**: `IdentitiesModule`, `NodesModule`, and `TeamsModule` MUST implement `InventoryAsync` and each write their domain counts to a per-module file (`Identities/inventory.json`, `Nodes/inventory.json`, `Teams/inventory.json`) via `IArtefactStore`; each may produce a count of zero if no domain data is in scope, but MUST emit a structured `Warning` log when count is zero.
- **FR-006**: Multi-organisation inventory MUST be handled by the job orchestrator looping over configured source endpoints and calling each module's `InventoryAsync` per endpoint — NOT by a dedicated `InventoryDiscoveryModule`.
- **FR-007**: `InventoryModule` and `InventoryDiscoveryModule` MUST be eliminated as standalone classes; their behaviour MUST be absorbed into `WorkItemsModule.InventoryAsync` and the job orchestrator respectively.
- **FR-008**: A new `IAnalyser` interface MUST be introduced in `DevOpsMigrationPlatform.Abstractions.Agent` for participants that read artefacts and produce analysis outputs but never write to a target system.
- **FR-009**: `DependencyDiscoveryModule` MUST be eliminated and replaced by `DependencyAnalyser` which implements `IAnalyser`, not `IModule`.
- **FR-010**: `DependencyAnalyser` is a self-contained `IOrganisationsAnalyser`. It has an empty `DependsOn` list — it receives its scope via `OrganisationsAnalyseContext.Organisations` and calls `IDependencyDiscoveryServiceFactory` directly to stream link data from live ADO. It MUST NOT declare a dependency on `InventoryAnalyser` or any module inventory phase. Reading `inventory.json` from `IArtefactStore` for progress-display purposes is permitted but optional.
- **FR-011**: The job execution plan builder MUST discover both `IModule` and `IAnalyser` registrations and include both in the `JobTaskList` with correct phase labels (`inventory`, `export`, `prepare`, `import`, `analyse`).
- **FR-012**: `DependencyPhase` MUST be extended to include `Inventory`, `Prepare`, and `Analyse` values so cross-type dependencies can be declared.
- **FR-013**: `JobKind.Dependencies` MUST dispatch via the `analyse` phase only (`DependencyAnalyser`). No `inventory` phase runs as a prerequisite. `DependencyAnalyser` is self-sufficient — it sources org scope from job config and streams link data from live ADO. It MUST NOT run via the export phase.
- **FR-014**: The `JobKind.Migrate` pipeline MUST execute phases in order: `inventory` → `export` → `prepare` → `import` → `validate`.
- **FR-015**: Each module's `PrepareAsync` MUST write a `{Module}/prepare-report.json` to `IArtefactStore` containing at minimum: resolved items count, unresolved items count, and a list of unresolved items.
- **FR-016**: All new phase methods (`InventoryAsync`, `PrepareAsync`) MUST be observable: O-1 activity spans, O-2 metrics, O-3 structured logging, and O-4 progress events (per guardrail 25).
- **FR-017**: The `IAnalyser` contract MUST enforce that `AnalyseAsync` never writes to a target system — only reads and writes artefacts via `IArtefactStore`.
- **FR-018**: An `IAnalyser` that completes with zero artefacts written MUST emit a structured `Warning` log (same rule as modules — silent zero-output is forbidden).
- **FR-019**: Both `IModule` and `IAnalyser` MUST be registered in DI and participate in the same `JobTaskList`; `DependsOn` references MUST work across the `IModule`/`IAnalyser` boundary.
- **FR-020**: The existing `ValidateAsync` signature on `IModule` MUST remain unchanged.
- **FR-021**: An `IModule` MAY declare a `DependsOn` entry referencing an `IAnalyser` with `DependencyPhase.Analyse` to express that its `PrepareAsync` reads that analyser's artefacts from `IArtefactStore`. When such a dependency is present, the plan builder MUST hoist the required `analyse` tasks before the `prepare` tasks in the `JobTaskList`, regardless of the `JobKind` being executed. Modules MUST NOT read analyser artefacts without a corresponding `DependsOn` declaration.
- **FR-022**: `DiscoveryOptions` MUST be renamed to `AnalyserOptions` throughout the codebase to align with the `IAnalyser` vocabulary introduced in FR-008. All cascading renames MUST be applied: `DiscoveryOptionsOrganisationsBinder` → `AnalyserOptionsOrganisationsBinder`, `AddDiscoveryOptionsOrganisationsBinder` → `AddAnalyserOptionsOrganisationsBinder`, `BuildDiscoveryOptions` helper methods in factories → `BuildAnalyserOptions`, and `DiscoveryOptionsValidationTests` → `AnalyserOptionsValidationTests`. The reference in `docs/source-types.md` (Known Limitations) MUST also be updated.
- **FR-023**: The `IAnalyser` base interface MUST accept only `AnalyseContext` (artefact-only, no live endpoints). Analysers that require live endpoint access MUST extend one of two sub-interfaces — `IOrganisationsAnalyser` (receives a list of source organisations) or `IEndpointPairAnalyser` (receives a paired source + target endpoint) — rather than adding nullable endpoint fields to `AnalyseContext`. The plan builder MUST construct the appropriate context subtype based on the interface the analyser implements.
- **FR-024**: `IOrganisationsAnalyser` MUST extend `IAnalyser` and override `AnalyseAsync` with `OrganisationsAnalyseContext`, which carries `Organisations: IReadOnlyList<OrganisationEndpoint>` in addition to all base `AnalyseContext` fields. This shape is used by analysers that iterate over source organisations (same pattern as the multi-org inventory loop).
- **FR-025**: `IEndpointPairAnalyser` MUST extend `IAnalyser` and override `AnalyseAsync` with `EndpointPairAnalyseContext`, which carries `SourceEndpoint: ISourceEndpointInfo` and `TargetEndpoint: ITargetEndpointInfo` in addition to all base `AnalyseContext` fields. This shape is used by analysers that compare live source and target data (e.g., field mapping compatibility analysis).
- **FR-026**: The plan builder MUST resolve the correct context type by checking `analyser is IEndpointPairAnalyser` before `analyser is IOrganisationsAnalyser` before falling back to `AnalyseContext`. Endpoint data MUST be sourced from the same job configuration used by the module orchestrator — no separate endpoint configuration is required.
- **FR-027**: An `InventoryAnalyser : IAnalyser` MUST be introduced. It MUST declare `DependsOn` on all four domain modules' `Inventory` phase (`WorkItemsModule`, `IdentitiesModule`, `NodesModule`, `TeamsModule`). Its `AnalyseAsync` MUST read the four per-module `{Module}/inventory.json` files and write the consolidated `inventory.json` and `inventory.csv` at the package root via `IArtefactStore`. This is the ONLY component permitted to write `inventory.json` and `inventory.csv`.

### Key Entities

- **`IModule`**: The domain-data-owning participant in a migration job. After this change it exposes five phase methods: `InventoryAsync`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`. Four boolean capability flags indicate which phases the module supports.
- **`IAnalyser`**: A new cross-cutting analysis participant. Exposes one method — `AnalyseAsync(AnalyseContext, CancellationToken)` — and a `DependsOn` list. Never writes to a target. Registered in DI alongside `IModule`. Extended by `IOrganisationsAnalyser` and `IEndpointPairAnalyser` for analysers that need live endpoint access.
- **`IOrganisationsAnalyser`**: Sub-interface of `IAnalyser` for analysers that iterate over source organisations (e.g., a cross-org dependency analyser). Receives `OrganisationsAnalyseContext`.
- **`IEndpointPairAnalyser`**: Sub-interface of `IAnalyser` for analysers that compare live source and target data (e.g., field mapping compatibility). Receives `EndpointPairAnalyseContext`.
- **`InventoryContext`**: Context record passed to `InventoryAsync`. Carries the scoped source endpoint, artefact store, state store, progress sink, and job reference.
- **`PrepareContext`**: Context record passed to `PrepareAsync`. Carries the package (source), target endpoint, artefact store, state store, progress sink, and job reference.
- **`AnalyseContext`**: Base context record passed to `IAnalyser.AnalyseAsync`. Carries artefact store (read/write), state store, progress sink, and job reference. No source or target endpoint.
- **`OrganisationsAnalyseContext`**: Extends `AnalyseContext` with `Organisations: IReadOnlyList<OrganisationEndpoint>`. Passed to `IOrganisationsAnalyser` implementations.
- **`EndpointPairAnalyseContext`**: Extends `AnalyseContext` with `SourceEndpoint: ISourceEndpointInfo` and `TargetEndpoint: ITargetEndpointInfo`. Passed to `IEndpointPairAnalyser` implementations.
- **`DependencyPhase`**: Enum extended with `Inventory` (0), `Prepare` (4), and `Analyse` (5) values.
- **`JobTaskList`**: Unchanged structure, but now includes tasks with phase labels `"inventory"`, `"prepare"`, and `"analyse"` alongside existing `"export"` and `"import"`.
- **`DependencyAnalyser`**: First domain-specific `IOrganisationsAnalyser` implementation. Replaces `DependencyDiscoveryModule`. Receives `OrganisationsAnalyseContext` from the plan builder. Calls `IDependencyDiscoveryServiceFactory` to stream cross-project work item links from live ADO. Writes `analysis/dependencies.csv` and `analysis/dependencies.mmd` via `IArtefactStore`. `DependsOn = []` — no artefact prerequisites. May optionally read `inventory.json` for UI progress counters but MUST NOT gate on its presence.
- **`InventoryAnalyser`**: Second `IAnalyser` implementation. Declares `DependsOn` on all four domain modules' `Inventory` phase. Reads per-module `{Module}/inventory.json` files and consolidates them into the canonical `inventory.json` and `inventory.csv` at the package root. Always runs as part of any `JobKind.Inventory` job plan.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All existing `JobKind.Export`, `JobKind.Import`, `JobKind.Migrate`, and `JobKind.Dependencies` jobs produce identical artefact output before and after the refactor — zero regression in artefact content.
- **SC-002**: `JobKind.Inventory` jobs complete successfully without `InventoryModule` or `InventoryDiscoveryModule` present in the config — the module list is reduced by 2 classes.
- **SC-003**: `JobKind.Prepare` jobs complete end-to-end and produce a `prepare-report.json` in each enabled module's package folder.
- **SC-004**: Multi-organisation inventory (2+ source endpoints) completes with a single config change (no special module required) and produces the same aggregate inventory artefacts as the previous `InventoryDiscoveryModule`-based approach.
- **SC-005**: The codebase module count is reduced from 7 (`InventoryModule`, `InventoryDiscoveryModule`, `DependencyDiscoveryModule`, `WorkItemsModule`, `IdentitiesModule`, `NodesModule`, `TeamsModule`) to 4 domain modules plus N analysers.
- **SC-006**: All simulated-connector system tests for inventory, export, prepare, import, and dependency analysis pass with assertions on artefact content (non-empty, correct structure) — not just "no exception thrown".
- **SC-007**: The `DependencyAnalyser` produces `analysis/dependencies.csv` with at least one row when at least one linked work item pair exists in the source organisations' work item links.
- **SC-008**: Every new `InventoryAsync` and `PrepareAsync` phase emits observable telemetry: at least one activity span, one metric increment, and one structured log entry per module invocation.

---

## Assumptions

- The spec is grounded in `docs/modules.md`, `docs/architecture.md`, and `analysis/draftspec-Module-refactor-consolidation.md`. The draft spec was authored by the project owner and represents a finalised design decision.
- `docs/modules.md` describes `PrepareAsync` as part of the `IModule` contract (line 17) but the actual interface does not implement it — this spec closes that gap.
- The abstract base class (`ModuleBase`) pattern is preferred over pure interface-only to avoid forcing every module to implement all five methods; `Supports*` properties default to `false`.
- Incremental migration is assumed: the refactor proceeds in five phases (add methods with defaults → move WorkItems inventory → multi-org orchestrator → implement PrepareAsync → clean up), maintaining backward compatibility at each step.
- The `ValidateAsync` method signature remains unchanged; no `ValidateInventoryAsync` or `ValidateExportAsync` variants are introduced.
- Each `IModule.InventoryAsync` writes its domain counts to a per-module file (`{Module}/inventory.json`); `InventoryAnalyser` reads all per-module files and produces the consolidated `inventory.json` and `inventory.csv` at the package root. No module writes to `inventory.json` directly.
- The `IAnalyser` interface is placed in `DevOpsMigrationPlatform.Abstractions.Agent` (same assembly as `IModule`) per the project reference boundary rules (guardrail 20a).
- `JobKind.Dependencies` remains a valid `JobKind` value and dispatches as `analyse (DependencyAnalyser only)`. No inventory phase runs as a prerequisite.
- Docs read: `docs/modules.md`, `docs/architecture.md`, `.agents/guardrails/system-architecture.md`, `.agents/guardrails/module-template.md`. Gaps found — see `discrepancies.md`.
