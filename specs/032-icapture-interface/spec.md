# Feature Specification: ICapture Interface — Unified Capture Contract

**Feature Branch**: `032-icapture-interface`
**Created**: 2026-05-06
**Status**: Draft

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Modules dispatch via ICapture (Priority: P1)

A developer adding a new migration module today implements `IModule` and overrides `InventoryAsync`. After this change they implement `ICapture.CaptureAsync` instead. The executor dispatches all `capture.*` tasks through a single `ICapture` lookup — no special-case branching on module vs. analyser.

**Why this priority**: This is the load-bearing rename. Everything else depends on it.

**Independent Test**: A simulated `capture.workitems.{org}.{project}` task plan executes and calls `CaptureAsync` on the registered `WorkItemsModule`, writing the expected artefact.

**Acceptance Scenarios**:

1. **Given** a Dependencies job plan with `capture.workitems.*` tasks, **When** the plan executor runs, **Then** `WorkItemsModule.CaptureAsync` is called for each task and the expected inventory artefact is written.
2. **Given** `IModule : ICapture`, **When** `SupportsInventory` is checked by the plan builder, **Then** modules that previously returned `true` still produce `capture.*` tasks in the plan.
3. **Given** a module registered as `ICapture`, **When** a `capture.*` task references its name, **Then** the executor resolves it from a single `captureHandlersByName` dictionary without branching on module type.

---

### User Story 2 — Pure capture handlers (DependencyCapture) (Priority: P1)

A developer adds `DependencyCapture : ICapture` — a class with no `ExportAsync`, `ImportAsync`, or module concerns. It is registered alongside modules in the DI container as `ICapture`. The plan executor resolves `capture.dependencies.*` tasks to it exactly as it resolves `capture.workitems.*` tasks to `WorkItemsModule`.

**Why this priority**: This is the immediate motivation — unblocks the dependencies fan-out without an architectural hack.

**Independent Test**: A Dependencies job plan includes `capture.dependencies.{org}.{project}` tasks; each executes `DependencyCapture.CaptureAsync` and writes `discovery/{org}/{project}/dependencies.csv`.

**Acceptance Scenarios**:

1. **Given** a Dependencies job plan, **When** the executor runs, **Then** one `capture.dependencies.*` task is created per org+project, each resolved to `DependencyCapture`.
2. **Given** `DependencyCapture` registered as `ICapture` only (not `IModule`), **When** the plan builder enumerates capture handlers, **Then** it includes `DependencyCapture` alongside modules.
3. **Given** `capture.dependencies.*` tasks execute via `DependencyCapture` (not `DependencyAnalyser`), **When** `analyse.dependencies` runs, **Then** `DependencyAnalyser.AnalyseAsync` consumes the same per-project CSV paths it previously received, requiring no changes to the analyser itself.
4. **Given** a Simulated-sourced Dependencies job plan (`source.type = Simulated`), **When** the executor runs `DependencyCapture.CaptureAsync`, **Then** it resolves to `SimulatedDependencyDiscoveryServiceFactory`, completes without external connectivity, and writes the expected per-project CSV.

---

### User Story 3 — IProjectAnalyser removed (Priority: P2)

`IProjectAnalyser` is deleted. `DependencyAnalyser` no longer implements it. No code references it. The executor has no `IProjectAnalyser` branch.

**Why this priority**: Removes the architectural debt introduced as a temporary workaround.

**Independent Test**: The solution builds with zero references to `IProjectAnalyser`. All existing tests pass.

**Acceptance Scenarios**:

1. **Given** the refactor is complete, **When** the solution is built, **Then** `IProjectAnalyser.cs` does not exist and no file references the type.
2. **Given** `DependencyAnalyser : IOrganisationsAnalyser`, **When** its interface list is inspected, **Then** it does not implement `IProjectAnalyser` or any per-project capture interface.

---

### Edge Cases

- A module that previously returned `SupportsInventory = false` must NOT appear in the capture handler registry and must NOT be dispatched for `capture.*` tasks.
- A pure `ICapture` (not `IModule`) must NOT be offered for export, import, or any other phase.
- If a `capture.*` task references a name not present in `captureHandlersByName`, the executor logs an error and skips the task — same behaviour as the current missing-module path.
- `InventoryContext` is **not** renamed — the context type name describes the data shape (inventory data), not the method that calls it.
- If one or more `capture.dependencies.*` tasks fail, `analyse.dependencies` MUST still execute and MUST fail gracefully if required input CSVs are absent (log error per missing file, do not throw unhandled exceptions).

## Observability

> This is an internal architectural refactor. Existing module-capture operations (`workitems`, `identities`, `nodes`, `teams`) retain their current `platform.*.inventory.*` metrics, `DevOpsMigrationPlatform.Discovery` spans, and structured logging unchanged — the rename `InventoryAsync` → `CaptureAsync` does not alter any signal. New observability obligations arise only from the **one new operation** introduced by this feature: `DependencyCapture.CaptureAsync`.

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `dependency.capture` | `module` | `DependencyCapture.CaptureAsync(InventoryContext, CancellationToken)` | `IDependencyDiscoveryServiceFactory.CreateForProject`, `IDependencyOrchestrator.CaptureProjectAsync`, CSV file write (`discovery/{org}/{project}/dependencies.csv`) |
| `capture.dispatch` | `workflow` | `JobPlanExecutor.ExecuteTasksAsync` — `TaskKind.Capture` branch | `captureHandlersByName` dictionary lookup (routing only; no new telemetry required) |

### Operator Decisions

| Operation | Decision | Question |
|---|---|---|
| `dependency.capture` | Is it working? | Are per-project dependency captures succeeding for all org+project combinations? |
| `dependency.capture` | Is it fast enough? | Is per-project capture latency within acceptable bounds? |
| `dependency.capture` | Is it overloaded? | How many captures are in flight concurrently? |
| `dependency.capture` | What failed? | Which org+project failed and with what error? |
| `dependency.capture` | Is it correct? | Did the CSV file get written for every expected project? |
| `capture.dispatch` | What failed? | When a `capture.*` task has no handler, is the missing name logged with the task ID? |

### Metrics

All new metrics use meter `WellKnownMeterNames.Agent` (`DevOpsMigrationPlatform.Agent`) and follow `platform.<domain>.<phase>.<measure>`.

| Metric Name | Constant | Instrument | Unit | Operation | Decision |
|---|---|---|---|---|---|
| `platform.dependencies.capture.count` | `WellKnownAgentMetricNames.DependenciesCaptureCount` *(new)* | `Counter<long>` | `{project}` | `dependency.capture` | Is it working? |
| `platform.dependencies.capture.duration_ms` | `WellKnownAgentMetricNames.DependenciesCaptureDurationMs` *(new)* | `Histogram<double>` | `ms` | `dependency.capture` | Is it fast enough? |
| `platform.dependencies.capture.errors` | `WellKnownAgentMetricNames.DependenciesCaptureErrors` *(new)* | `Counter<long>` | `{project}` | `dependency.capture` | What failed? |
| `platform.dependencies.capture.in_flight` | `WellKnownAgentMetricNames.DependenciesCaptureInFlight` *(new)* | `UpDownCounter<long>` | `{project}` | `dependency.capture` | Is it overloaded? |

> **No new constants required for existing modules.** `platform.workitems.inventory.*`, `platform.identities.inventory.*`, `platform.nodes.inventory.*`, and `platform.teams.inventory.*` continue unchanged.

> **CLI/TUI**: `DependencyCapture.CaptureAsync` MUST also emit `ProgressEvent.Metrics` (via `IProgressSink`) for every completed project so the CLI displays live capture progress. OTel metrics alone do not feed the CLI/TUI counter display.

### Traces

ActivitySource: `WellKnownActivitySourceNames.Discovery` (`DevOpsMigrationPlatform.Discovery`)

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| `DependencyCapture` | `dependency.capture` | `job.id`, `org.url`, `project.name`, `capture.handler="dependencies"` | Root | Is it working? / Is it fast enough? |
| `DependencyCapture` → factory | `dependency.capture.create_service` | `org.url`, `project.name` | `dependency.capture` | Where is it slow? |
| `DependencyCapture` → orchestrator | `dependency.capture.execute` | `org.url`, `project.name` | `dependency.capture` | Where is it slow? / What failed? |
| `DependencyCapture` → CSV write | `dependency.capture.write_csv` | `org.url`, `project.name`, `output.path` | `dependency.capture` | Is it correct? |

**Context propagation:** Automatic via `Activity` parent–child hierarchy using `ActivitySource.StartActivity`. The existing `JobPlanExecutor` span (if present) acts as the parent for `dependency.capture`. `W3C TraceContext` headers are not propagated to external systems — all calls are in-process.

### Logging

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| Capture started | `Information` | `jobId`, `org`, `project`, `handler="dependencies"` | `dependency.capture` | Is it working? |
| Capture completed | `Information` | `jobId`, `org`, `project`, `durationMs`, `outputPath` | `dependency.capture` | Is it working? / Is it fast enough? |
| Capture failed | `Error` | `jobId`, `org`, `project`, `errorType`, `errorMessage`, `durationMs` | `dependency.capture` | What failed? |
| Dependency slow | `Warning` | `jobId`, `org`, `project`, `dependency`, `durationMs`, `thresholdMs` | `dependency.capture` | Where is it slow? |
| No handler found | `Error` | `taskId`, `handlerName` | `capture.dispatch` | What failed? |
| CSV already exists, overwriting | `Debug` | `jobId`, `org`, `project`, `outputPath` | `dependency.capture` | Is it correct? |

> Debug and Trace levels are disabled by default.

> **Data classification**: `org` and `project` values are system identifiers (URLs and project names), not end-user personal data. They may appear in logs. Raw work item field values MUST NOT appear in any log event.

### Correlation

| Field | Source | Scope |
|---|---|---|
| `traceId` / `operationId` | `Activity.Current.TraceId` (set by `ActivitySource.StartActivity`) | All telemetry within `DependencyCapture.CaptureAsync` |
| `job.id` | `InventoryContext.Job.JobId` | All metrics tags, span tags, and log fields |
| `org.url` | `InventoryContext.SourceEndpoint.ResolvedUrl` | Span tags and log fields for `dependency.capture` |
| `project.name` | `InventoryContext.Project` | Span tags and log fields for `dependency.capture` |
| `capture.handler` | Constant `"dependencies"` | Root span tag; allows filtering all dependency-capture spans |

### Validation Queries

#### Failure Identification

```kql
// Identifies which org+project dependency captures failed and the error type
customMetrics
| where name == "platform.dependencies.capture.errors"
| summarize totalFailures = sum(value) by tostring(customDimensions["org.url"]),
             tostring(customDimensions["project.name"]),
             tostring(customDimensions["job.id"])
| where totalFailures > 0
| order by totalFailures desc
```

#### Latency Analysis

```kql
// P50 / P95 / P99 capture latency per org
customMetrics
| where name == "platform.dependencies.capture.duration_ms"
| summarize
    p50 = percentile(value, 50),
    p95 = percentile(value, 95),
    p99 = percentile(value, 99)
  by tostring(customDimensions["org.url"]), bin(timestamp, 5m)
| order by p99 desc
```

#### Load Observation

```kql
// In-flight concurrent dependency captures over time
customMetrics
| where name == "platform.dependencies.capture.in_flight"
| summarize maxInFlight = max(value) by tostring(customDimensions["job.id"]), bin(timestamp, 1m)
| order by timestamp desc
```

#### End-to-End Trace

```kql
// Trace a single job's full capture-to-analyse-dependencies path
dependencies
| where operation_Name == "dependency.capture"
    and customDimensions["job.id"] == "<job-id>"
| project timestamp, operation_Name, duration, success,
           org = tostring(customDimensions["org.url"]),
           project = tostring(customDimensions["project.name"]),
           operation_Id, id
| order by timestamp asc
```

#### Error Diagnosis

```kql
// Correlate Error-level log events with failed capture spans for root-cause analysis
union
  (exceptions
   | where customDimensions["capture.handler"] == "dependencies"
   | project timestamp, type, outerMessage, operation_Id,
             org = tostring(customDimensions["org"]),
             project = tostring(customDimensions["project"])),
  (dependencies
   | where operation_Name == "dependency.capture" and success == false
   | project timestamp, operation_Name, resultCode, operation_Id,
             org = tostring(customDimensions["org.url"]),
             project = tostring(customDimensions["project.name"]))
| order by timestamp desc
```

## Connector Coverage

### Features

| Feature | Type | Abstraction | Simulated | AzureDevOps | TFS |
|---|---|---|---|---|---|
| `icapture.rename` | `discovery` | `ICapture.CaptureAsync` (replaces `IModule.InventoryAsync`) | **Carry-over** | **Carry-over** | **Carry-over** |
| `capture.dispatch` | `workflow` | `IJobPlanExecutor.ExecuteTasksAsync` — `TaskKind.Capture` branch | N/A | N/A | N/A |
| `dependency.capture` | `discovery` | `DependencyCapture.CaptureAsync` → `IDependencyDiscoveryServiceFactory` | **Required** *(gap — see below)* | **Required** ✅ | **Exempt** *(see below)* |

> **`icapture.rename`** and **`capture.dispatch`** are internal refactors. All module connector implementations (`SimulatedWorkItemRevisionSourceFactory`, `AzureDevOps*`, `TfsWorkItemsModule`, etc.) remain unchanged — the rename from `InventoryAsync` to `CaptureAsync` carries through without any connector-specific logic changes. No new connector scenarios are required for these items.

### Acceptance Scenario Mapping

| Feature | Connector | Scenario(s) |
|---|---|---|
| `icapture.rename` | Simulated | US1 Scenario 1: simulated `capture.workitems.*` task plan calls `WorkItemsModule.CaptureAsync` ✅ |
| `icapture.rename` | AzureDevOps | US1 Scenarios 1–3 cover the unified dispatch path ✅ |
| `icapture.rename` | TFS | Carry-over — TFS module implementations rename `InventoryAsync` → `CaptureAsync` identically ✅ |
| `dependency.capture` | Simulated | **MISSING** — No scenario exercises `DependencyCapture.CaptureAsync` with a Simulated source ⚠️ |
| `dependency.capture` | AzureDevOps | US2 Scenario 1: `capture.dependencies.{org}.{project}` tasks resolve to `DependencyCapture` ✅ |
| `dependency.capture` | TFS | Exempt ✅ |

### TFS Exemptions

| Feature | Reason | Graceful Behaviour |
|---|---|---|
| `dependency.capture` | `IDependencyDiscoveryServiceFactory` uses Azure DevOps REST API patterns (cross-project work item link analysis via REST endpoints). The TFS Object Model (`WorkItemStore`) provides work item querying but does not expose the same cross-project dependency analysis API surface that `IDependencyDiscoveryService` abstracts. A TFS-native `IDependencyDiscoveryServiceFactory` is a separate work item. | `DependencyCapture.CaptureAsync` MUST NOT be registered in the TFS agent's `ICapture` registry. The TFS agent's plan builder MUST emit no `capture.dependencies.*` tasks when `source.type = TeamFoundationServer`. If a `capture.dependencies.*` task is erroneously present in a TFS plan, the executor logs a structured `Warning` and skips it (no `NotImplementedException`, no crash). |

### Gaps

| Feature | Connector | Gap | Required Action |
|---|---|---|---|
| `dependency.capture` | Simulated | No `IDependencyDiscoveryServiceFactory` implementation for Simulated connector exists. `AddSimulatedDependencyAnalysis` registers `IWorkItemLinkAnalysisService` (keyed "Simulated") but not the factory. `DependencyCapture` cannot be exercised by Simulated-sourced test plans. | Add FR-016: Create `SimulatedDependencyDiscoveryServiceFactory : IDependencyDiscoveryServiceFactory` in `Infrastructure.Simulated`, backed by `SimulatedWorkItemLinkAnalysisService`. Register via `AddSimulatedDependencyAnalysis`. Add US2 Scenario 4 covering Simulated end-to-end. |
| `dependency.capture` | Simulated | No acceptance scenario in the spec exercises `DependencyCapture` with `source.type = Simulated`. | Add acceptance scenario to US2 (see FR-016 action above). |

### Verdict

**PASS** — All gaps closed within this spec. FR-016 adds `SimulatedDependencyDiscoveryServiceFactory` and US2 Scenario 4 adds the missing Simulated acceptance scenario. The `icapture.rename` and `capture.dispatch` changes carry through all existing connector implementations unchanged.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: `ICapture` MUST define `string Name` and `Task CaptureAsync(InventoryContext context, CancellationToken ct)`.
- **FR-002**: `IModule` MUST extend `ICapture`, replacing `InventoryAsync` with `CaptureAsync` inherited from `ICapture`.
- **FR-003**: `ModuleBase.CaptureAsync` MUST provide the same no-op default that `InventoryAsync` previously provided.
- **FR-004**: All concrete modules (`WorkItemsModule`, `IdentitiesModule`, `NodesModule`, `TeamsModule`) MUST rename `InventoryAsync` → `CaptureAsync`.
- **FR-005**: `JobPlanExecutor` MUST dispatch `TaskKind.Capture` tasks against a single `captureHandlersByName` dictionary populated from all registered `ICapture` instances.
- **FR-006**: `JobAgentWorker` MUST assemble `captureHandlersByName` before calling `IJobPlanExecutor.ExecuteTasksAsync`, merging `IModule` registrations (where `SupportsInventory = true`, cast to `ICapture`) with all `ICapture`-only registrations resolved from the DI scope. This replaces the current `modulesByName` dictionary construction in both `OnMigrationJobAsync` and `OnDiscoveryJobAsync`.
- **FR-007**: `DependencyCapture : ICapture` MUST be created; it calls `IDependencyDiscoveryServiceFactory.CreateForProject` and `IDependencyOrchestrator.CaptureProjectAsync`, writing `discovery/{org}/{project}/dependencies.csv`. If the output file already exists (resume scenario), it MUST be overwritten.
- **FR-008**: `DependencyAnalyser` MUST NOT implement `IProjectAnalyser`; it MUST remain a pure `IAnalyser` / `IOrganisationsAnalyser` fan-in.
- **FR-009**: `IProjectAnalyser` MUST be deleted from `DevOpsMigrationPlatform.Abstractions.Agent`.
- **FR-010**: All existing tests referencing `InventoryAsync` MUST be updated to reference `CaptureAsync`. Tests exercising `IJobPlanExecutor.ExecuteTasksAsync` MUST update the `modulesByName` parameter to `captureHandlersByName` (type `IReadOnlyDictionary<string, ICapture>`).
- **FR-011**: `InventoryContext` MUST retain its current name — the rename applies only to the method and interface, not the context record.
- **FR-012**: `IJobPlanExecutor.ExecuteTasksAsync` MUST replace the `IReadOnlyDictionary<string, IModule> modulesByName` parameter with `IReadOnlyDictionary<string, ICapture> captureHandlersByName`. All other parameters (`analysersByName`, `baseInventoryContext`, `baseExportContext`, `importContext`, `endpointsByUrl`, `stateStore`, `ct`) are unchanged.
- **FR-013**: `ICapture`-only registrations (e.g. `DependencyCapture`) MUST be registered with the DI container as `ICapture` only — NOT as `IModule` — to prevent double-registration when `JobAgentWorker` merges the two sources into `captureHandlersByName`.
- **FR-014**: `SupportsInventory` MUST be retained on `IModule` unchanged. It controls whether the plan builder emits `capture.*` tasks for that module. It MUST NOT move to `ICapture`; pure `ICapture` implementors have no equivalent flag and are always included in the capture handler registry.
- **FR-015**: `ICapture.Name` MUST match the second dot-separated segment of the task ID for all `capture.*` tasks dispatched to that handler (e.g. `Name = "workitems"` for `capture.workitems.{org}.{project}` tasks). The executor MUST extract the handler name by splitting the task ID on `'.'` and reading index `[1]`.

- **FR-016**: `SimulatedDependencyDiscoveryServiceFactory : IDependencyDiscoveryServiceFactory` MUST be created in `DevOpsMigrationPlatform.Infrastructure.Simulated`, backed by the already-registered `SimulatedWorkItemLinkAnalysisService`. It MUST be registered via `AddSimulatedDependencyAnalysis` so Simulated-sourced dependency job plans can exercise `DependencyCapture` without external connectivity.

### Key Entities

- **`ICapture`**: New base interface. Owns `Name` and `CaptureAsync`. Lives in `DevOpsMigrationPlatform.Abstractions.Agent.Modules`. `ICapture.Name` MUST match the second dot-separated segment of the corresponding `capture.*` task ID.
- **`IModule : ICapture`**: Existing migration module contract, extended to inherit `ICapture`. `InventoryAsync` removed; `CaptureAsync` inherited.
- **`DependencyCapture`**: New class. Implements `ICapture` only. Per-project dependency discovery pass. Registered in DI as `ICapture`, never as `IModule`.
- **`JobAgentWorker`**: Updated to resolve `ICapture` registrations from the DI scope and assemble `captureHandlersByName` (merging `IModule` where `SupportsInventory=true` + pure `ICapture` instances) before calling `ExecuteTasksAsync`.
- **`IJobPlanExecutor`**: `ExecuteTasksAsync` signature updated — `modulesByName` parameter replaced with `captureHandlersByName: IReadOnlyDictionary<string, ICapture>`.
- **`DependencyAnalyser`**: Unchanged contract. Pure fan-in `IAnalyser` / `IOrganisationsAnalyser`.
- **`IProjectAnalyser`**: Deleted.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Solution builds with zero errors and zero new warnings after the rename.
- **SC-002**: All 984+ existing tests pass. Changes are limited to: method renames (`InventoryAsync` → `CaptureAsync`), the `modulesByName` parameter type change (`IModule` → `ICapture`) at `ExecuteTasksAsync` call sites, and deletion of any tests that exclusively tested the removed `IProjectAnalyser` interface.
- **SC-003**: A Dependencies job plan for N organisations × M projects produces exactly `(N×M×K) + 1 + (N×M) + 1` tasks: inventory captures + analyse.inventory + dependency captures + analyse.dependencies. At the current codebase baseline K = 4 (WorkItems, Identities, Nodes, Teams — all `SupportsInventory = true` and all config-enabled).
- **SC-004**: `IProjectAnalyser` has zero references anywhere in the solution after the change.
- **SC-005**: The `JobPlanExecutor` `TaskKind.Capture` branch contains no type-check branching on `IModule` vs. other capture types — one unified lookup against `captureHandlersByName`.

## Assumptions

- `InventoryContext` retains its name; only the calling method is renamed.
- `SupportsInventory` on `IModule` is retained as-is — it controls whether the plan builder emits `capture.*` tasks for a module. It does NOT move to `ICapture` (pure `ICapture` implementors are always included).
- Simulated connectors (`SimulatedWorkItemSource` etc.) do not directly implement `ICapture` — they are called by modules and are unaffected by this rename.
- `DependencyCapture` replaces the `IProjectAnalyser` workaround on `DependencyAnalyser` introduced in the prior session. The `CaptureProjectAsync` method on `IDependencyOrchestrator` and `CreateForProject` on `IDependencyDiscoveryServiceFactory` added in the prior session are retained and used by `DependencyCapture`.
- No CLI commands, configuration keys, or user-facing behaviour changes as a result of this refactor.
- The Abstractions package is consumed only within this repository. No external plugin or connector assemblies reference `IProjectAnalyser`, `IJobPlanExecutor`, or `ICapture` outside the solution boundary. If external consumers exist, this refactor requires a coordinated release.
- `ICapture.Name` values are unique across all registrations. Startup-time duplicate detection follows the existing `ToDictionary` pattern (throws `ArgumentException` on conflict).
