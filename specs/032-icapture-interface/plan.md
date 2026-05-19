# Implementation Plan: ICapture Interface — Unified Capture Contract

**Branch**: `032-icapture-interface` | **Date**: 2026-05-06 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/032-icapture-interface/spec.md`

## Summary

This is a pure internal architectural refactor that introduces `ICapture` as the unified
dispatch contract for all `capture.*` job tasks. `IModule` inherits `ICapture`, replacing
`InventoryAsync` with `CaptureAsync`. A new `DependencyCapture : ICapture` (pure class, not
a module) handles per-project dependency discovery, removing the `IProjectAnalyser` workaround
from `DependencyAnalyser`. `IJobPlanExecutor.ExecuteTasksAsync` is updated to receive
`captureHandlersByName: IReadOnlyDictionary<string, ICapture>` instead of `modulesByName`.
`JobAgentWorker` assembles this dictionary by merging modules where `SupportsInventory=true`
with all `ICapture`-only registrations. A `SimulatedDependencyDiscoveryServiceFactory` is
added to close the Simulated connector coverage gap. No CLI, configuration, or user-facing
changes.

## Reconciliation Status (2026-05-17)

- **Class**: Class A documentation/status reconciliation (no runtime surface changes in this pass).
- **Implemented**: unified `ICapture` dispatch, `BuildCaptureHandlers`, `DependencyCapture`, and `IProjectAnalyser` removal are present in source.
- **Superseded path/name drift**:
  - `DependencyCapture` implemented at `Infrastructure.Agent/Analysis/DependencyCapture.cs`.
  - Simulated factory implemented at `Infrastructure.Simulated/Factories/SimulatedDependencyDiscoveryServiceFactory.cs`.
  - DI extension implemented as `AddDependencyCapture` in `DependencyAnalyserServiceCollectionExtensions.cs`.
  - CLI progress row change implemented in `CLI.Migration/Commands/QueueCommand.cs`.
- **Outstanding verification gaps**:
  - Clean build gate with zero warnings is not currently true (build succeeds with warnings).
  - No fresh full-solution `dotnet test` evidence in this reconciliation run.
  - No fresh `.vscode/launch.json` simulated dependency-capture run evidence in this reconciliation run.

## Technical Context

**Language/Version**: C# 10+, .NET 10  
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.Logging`, OpenTelemetry (`System.Diagnostics.ActivitySource`, `System.Diagnostics.Metrics`)  
**Storage**: `IArtefactStore` (package-local). `DependencyCapture` writes `discovery/{org}/{project}/dependencies.csv`. No new storage backends.  
**Testing**: Reqnroll.MSTest + Moq (`MockBehavior.Strict`). All new and updated tests live under `tests/`.  
**Target Platform**: .NET 10 (Linux container / Windows host). `DevOpsMigrationPlatform.Abstractions` multi-targets `net481;net10.0` — the `ICapture` interface must be compatible with both TFMs.  
**Project Type**: Internal library refactor (no CLI, no deployable host changes)  
**Performance Goals**: No throughput change. All existing latency SLOs preserved. The capture handler dictionary lookup is O(1).  
**Constraints**: Zero new warnings on build. All 984+ existing tests must continue to pass. `IProjectAnalyser` must have zero references after the change.  
**Scale/Scope**: 4 existing modules renamed; 1 new class (`DependencyCapture`); 1 new class (`SimulatedDependencyDiscoveryServiceFactory`); 1 interface deleted (`IProjectAnalyser`); 2 interface signatures updated (`IModule`, `IJobPlanExecutor`); all test call-sites updated.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** ALL files in `/.agents/20-guardrails/`, ALL files in
> `/.agents/30-context/`, and relevant `/docs/` files have been read prior to this plan.

- [x] **Package-First (I):** `DependencyCapture` writes only to `IArtefactStore` via `discovery/{org}/{project}/dependencies.csv`. No direct source-to-target migration. No module may write files directly. ✅
- [x] **Streaming (II):** This refactor does not alter any streaming import path. `DependencyCapture.CaptureAsync` is a single-project per-call operation with no in-memory accumulation of revisions. ✅
- [x] **WorkItems Layout (III):** No WorkItems folder structure changes. Refactor is entirely in the dispatch and capture layer. ✅
- [x] **Checkpointing (IV):** Existing module cursor files are unaffected. `DependencyCapture` is a stateless per-project call; resume is handled at the task level by the plan executor (tasks already marked `Completed` are skipped). ✅
- [x] **Module Isolation (V):** `DependencyCapture` receives `IDependencyDiscoveryServiceFactory` and `IDependencyOrchestrator` via constructor injection. No direct filesystem access. All artefact writes flow through `IArtefactStore` (via orchestrator). ✅
- [x] **Separation of Planes (VI):** All changes are within the Job Engine boundary (`Infrastructure.Agent`, `MigrationAgent`). No control-plane coupling introduced. ✅
- [x] **Determinism (VII):** `DependencyCapture` overwrites on resume (FR-007). CSV output is deterministic given the same source data. No schema breaking changes (no new package format fields). ✅
- [x] **ATDD-First (VIII):** All three user stories have Given/When/Then scenarios. US1 has 3 scenarios, US2 has 4, US3 has 2. Feature files live at `features/inventory/icapture-rename/US1-modules-dispatch-via-icapture.feature`, `features/inventory/dependency-capture/US2-pure-capture-handlers.feature`, and `features/platform/iproject-analyser-removal/US3-iproject-analyser-removed.feature`. Each will be implemented one scenario per session per commit via the ATDD inner loop. ✅
- [x] **SOLID & DI (IX):** `ICapture` defined in `DevOpsMigrationPlatform.Abstractions.Agent.Modules`. `DependencyCapture` uses constructor injection. `SimulatedDependencyDiscoveryServiceFactory` registered via `AddSimulatedDependencyAnalysis`. No raw `IConfiguration` access. ✅
- [x] **Full Connector Coverage (XI):** `icapture.rename` carries through all three connectors unchanged. `dependency.capture`: AzureDevOps ✅, Simulated ✅ (FR-016 adds `SimulatedDependencyDiscoveryServiceFactory`), TFS exempt (TFS OM does not expose the required cross-project dependency API — documented in spec Connector Coverage section). ✅

## Observability Contract

*GATE: Must be completed before task generation. Every operation enumerated here MUST appear as explicit tasks in `tasks.md`.*

> Files consulted: `.agents/30-context/telemetry-architecture.md`, `.agents/30-context/domains/telemetry-model.md`, `WellKnownActivitySourceNames.cs`, `WellKnownAgentMetricNames.cs`, `WellKnownMeterNames.cs`, `WellKnownTagNames.cs`, `IPlatformMetrics.cs`.

For this feature, the only **new** operation is `DependencyCapture.CaptureAsync`. Existing module operations (`workitems`, `identities`, `nodes`, `teams`) are renamed method-only (`InventoryAsync` → `CaptureAsync`) and retain all current signals unchanged.

**No operations — pure refactor** applies to: `ICapture` interface introduction, `IModule : ICapture` rename, `IJobPlanExecutor` signature update, `IProjectAnalyser` deletion.

### Operations Table

| Operation | Class / Method | Span Name (O-1) | Metrics Instruments (O-2) | Log Events (O-3) | ProgressEvent Stage (O-4) |
|-----------|---------------|-----------------|--------------------------|-----------------|---------------------------|
| `dependency.capture` | `DependencyCapture.CaptureAsync` | `dependency.capture` (root), `dependency.capture.create_service` (child), `dependency.capture.execute` (child), `dependency.capture.write_csv` (child) | `platform.dependencies.capture.count` (`Counter<long>`), `platform.dependencies.capture.duration_ms` (`Histogram<double>`), `platform.dependencies.capture.errors` (`Counter<long>`), `platform.dependencies.capture.in_flight` (`UpDownCounter<long>`) | `Information`: "Capture started for {Org}/{Project}"; `Information`: "Capture completed for {Org}/{Project} in {DurationMs}ms → {OutputPath}"; `Error`: "Capture failed for {Org}/{Project}: {ErrorType} {ErrorMessage}"; `Warning`: "Dependency slow: {Dependency} took {DurationMs}ms > {ThresholdMs}ms"; `Debug`: "CSV already exists at {OutputPath}, overwriting" | `Capturing` (start), `Captured` (success per-project), `Failed` (error per-project) |
| `capture.dispatch` (no-handler path) | `JobPlanExecutor.ExecuteTasksAsync` — `TaskKind.Capture` missing handler | N/A (no new span) | N/A | `Error`: "Task {TaskId} references capture handler '{HandlerName}', but it is not registered. Skipping." | N/A |

### New Metric Constants (additions to `WellKnownAgentMetricNames`)

```csharp
// --- Dependencies Capture ---
public const string DependenciesCaptureCount       = "platform.dependencies.capture.count";
public const string DependenciesCaptureDurationMs  = "platform.dependencies.capture.duration_ms";
public const string DependenciesCaptureErrors      = "platform.dependencies.capture.errors";
public const string DependenciesCaptureInFlight    = "platform.dependencies.capture.in_flight";
```

### New IPlatformMetrics Methods (additions to `IPlatformMetrics`)

```csharp
void DependenciesCaptureStarted(MetricsTagList tags);
void DependenciesCaptureCompleted(MetricsTagList tags);
void DependenciesCaptureFailed(MetricsTagList tags);
void RecordDependenciesCaptureDuration(double milliseconds, MetricsTagList tags);
void DependenciesCaptureInFlightIncrement(MetricsTagList tags);
void DependenciesCaptureInFlightDecrement(MetricsTagList tags);
```

### Wiring Checklist

- [x] **O-1 ActivitySource:** `dependency.capture` span uses `WellKnownActivitySourceNames.Discovery` (`DevOpsMigrationPlatform.Discovery`) — already registered. Sub-spans `create_service`, `execute`, `write_csv` are children of the root span. All span tags MUST use `WellKnownTagNames` constants — no string literals. Tags applied to the root span: `WellKnownTagNames.JobId` (`job.id`), `WellKnownTagNames.OrgUrl` (`org.url`), `WellKnownTagNames.ProjectName` (`project.name`), `WellKnownTagNames.CaptureHandler` (`capture.handler`, value `"dependencies"`).
- [x] **O-2 Metric instruments:** New constants `DependenciesCaptureCount`, `DependenciesCaptureDurationMs`, `DependenciesCaptureErrors`, `DependenciesCaptureInFlight` added to `WellKnownAgentMetricNames`. Corresponding methods added to `IPlatformMetrics` and implemented in `PlatformMetrics`.
- [x] **O-2 Meter registration:** No new meter. All instruments live under existing `WellKnownMeterNames.Agent` (`DevOpsMigrationPlatform.Agent`) — already registered in MigrationAgent and TfsMigrationAgent hosts.
- [x] **O-3 Log structured params:** All log calls use structured params (`{Org}`, `{Project}`, `{DurationMs}`, `{OutputPath}`, `{ErrorType}`, `{ErrorMessage}`, `{HandlerName}`, `{TaskId}`). No string interpolation.
- [x] **O-4 IProgressSink wiring:** `IProgressSink?` injected as optional in `DependencyCapture`. `EmitAsync` (sync `Emit`) called at capture start and per-project completion/failure.
- [x] **O-4 ModuleCounters property:** `DependencyCapture` emits `ProgressEvent.Metrics` with `JobMetrics.Discovery.Dependencies` (a `DependencyCounters` instance) populated per-project completion. Fields set: `DependencyCounters.WorkItemsAnalysed` and `DependencyCounters.ExternalLinksFound`. Note: `SnapshotMetricExporter.cs` does NOT require changes — `JobMetrics.Discovery` is already handled.
- [x] **O-4 CLI row:** `QueueCommand.BuildProgressRenderable` — a progress bar row for `DependencyCapture` is added to display live per-project capture progress. Order: after inventory modules, before `analyse.dependencies`.
- [x] **DI wiring verified:** `DependencyCapture` registered via `services.AddSingleton<ICapture, DependencyCapture>()` in a new `AddDependencyCaptureServices` extension method in `Infrastructure.Agent`. `SimulatedDependencyDiscoveryServiceFactory` registered in `AddSimulatedDependencyAnalysis`.

### Correlation

| Field | Source | Applied To |
|-------|--------|------------|
| `traceId` / `operationId` | `Activity.Current.TraceId` (set by `ActivitySource.StartActivity`) | All telemetry within `DependencyCapture.CaptureAsync` — links metrics, spans, and logs for a single operation |
| `job.id` | `InventoryContext.Job.JobId` | All `MetricsTagList` instances (O-2) and all span tags via `WellKnownTagNames.JobId` (O-1) |
| `org.url` | `InventoryContext.SourceEndpoint.ResolvedUrl` | Span tags via `WellKnownTagNames.OrgUrl` and log structured field `{Org}` (O-3) |
| `project.name` | `InventoryContext.Project` | Span tags via `WellKnownTagNames.ProjectName` and log structured field `{Project}` (O-3) |
| `capture.handler` | Constant `"dependencies"` | Root `dependency.capture` span tag via `WellKnownTagNames.CaptureHandler`; enables cross-span filtering in Application Insights |

> **Data classification:** `org` (URL), `project` (name), and `{OutputPath}` are customer-identifiable data per coding-standards.md O-3 and MUST use `DataClassification.Customer` scope in all structured log fields. Span tags `org.url` and `project.name` MUST carry `DataClassification.Customer` and MUST NOT be forwarded to Application Insights (enforced via `AddDataClassificationFilter()` on the host). Raw work item field values MUST NOT appear in any log event.

> **Context propagation:** All calls are in-process. `W3C TraceContext` headers are NOT propagated to external systems. The `Activity` parent–child hierarchy (`ActivitySource.StartActivity`) is sufficient.

### Tests Required for Observability

- [x] Unit test: `DependencyCapture` — verify `ActivitySource.StartActivity("dependency.capture")` is called; verify child spans `create_service`, `execute`, `write_csv` are started.
- [x] Unit test: `DependencyCapture` — verify `IPlatformMetrics.DependenciesCaptureStarted`, `DependenciesCaptureCompleted`, `RecordDependenciesCaptureDuration` are called on success; `DependenciesCaptureFailed` on exception.
- [x] Unit test: `DependencyCapture` — verify `IProgressSink.Emit` is called with `Stage = "Capturing"` at start and `Stage = "Captured"` (with `DiscoveryCounters`) at completion.
- [x] Unit test: `DependencyCapture` — verify `ILogger` receives `Information` log at start and completion with `{Org}` and `{Project}` structured fields.
- [x] Unit test: `JobPlanExecutor` — verify that when a `capture.*` task ID has no matching entry in `captureHandlersByName`, an `Error` log is emitted with `{TaskId}` and `{HandlerName}`, and the task is skipped.
- [x] Simulated system test: Dependencies job plan with `source.type = Simulated` → `DependencyCapture.CaptureAsync` called → `SimulatedDependencyDiscoveryServiceFactory` resolves → CSV written without external connectivity.

## Project Structure

### Documentation (this feature)

```text
specs/032-icapture-interface/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   ├── ICapture.md           # New interface contract
│   └── IJobPlanExecutor.md   # Updated signature contract
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions.Agent/
│   ├── Modules/
│   │   ├── ICapture.cs                          # NEW — string Name + Task CaptureAsync(...)
│   │   └── IModule.cs                           # UPDATED — extends ICapture; removes InventoryAsync
│   ├── Analysis/
│   │   └── IProjectAnalyser.cs                  # DELETED
│   └── Context/
│       └── IJobPlanExecutor.cs                  # UPDATED — modulesByName → captureHandlersByName: IReadOnlyDictionary<string, ICapture>
│
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   ├── Modules/
│   │   ├── ModuleBase.cs                        # UPDATED — InventoryAsync → CaptureAsync
│   │   ├── WorkItemsModule.cs                   # UPDATED — InventoryAsync → CaptureAsync
│   │   ├── IdentitiesModule.cs                  # UPDATED — InventoryAsync → CaptureAsync
│   │   ├── NodesModule.cs                       # UPDATED — InventoryAsync → CaptureAsync
│   │   └── TeamsModule.cs                       # UPDATED — InventoryAsync → CaptureAsync
│   ├── Capture/
│   │   └── DependencyCapture.cs                 # NEW — ICapture only; per-project dep discovery
│   ├── Context/
│   │   └── JobPlanExecutor.cs                   # UPDATED — single captureHandlersByName lookup; no IProjectAnalyser branch
│   ├── Analysis/
│   │   └── DependencyAnalyser.cs                # UPDATED — removes IProjectAnalyser implementation; removes CaptureProjectAsync
│   └── ServiceCollectionExtensions.cs           # UPDATED — AddSingleton<ICapture, DependencyCapture>() in new AddDependencyCaptureServices()
│
├── DevOpsMigrationPlatform.Infrastructure.Simulated/
│   └── DependencyDiscovery/
│       └── SimulatedDependencyDiscoveryServiceFactory.cs  # NEW — IDependencyDiscoveryServiceFactory backed by SimulatedWorkItemLinkAnalysisService
│   └── SimulatedServiceCollectionExtensions.cs            # UPDATED — AddSimulatedDependencyAnalysis registers SimulatedDependencyDiscoveryServiceFactory
│
├── DevOpsMigrationPlatform.MigrationAgent/
│   └── JobAgentWorker.cs                        # UPDATED — assembles captureHandlersByName; passes to ExecuteTasksAsync
│
└── DevOpsMigrationPlatform.Abstractions/
    ├── WellKnownAgentMetricNames.cs              # UPDATED — 4 new DependenciesCapture* constants
    └── (WellKnownActivitySourceNames.cs — no changes needed; Discovery already registered)

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
│   ├── Modules/
│   │   └── *ModuleTests.cs                      # UPDATED — InventoryAsync → CaptureAsync
│   ├── Capture/
│   │   └── DependencyCaptureTests.cs            # NEW — unit tests for DependencyCapture
│   └── Context/
│       └── JobPlanExecutorTests.cs              # UPDATED — modulesByName → captureHandlersByName
│
├── DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/
│   └── DependencyDiscovery/
│       └── SimulatedDependencyDiscoveryServiceFactoryTests.cs  # NEW
│
└── DevOpsMigrationPlatform.MigrationAgent.Tests/
    └── (unit + system test .cs files only — no .feature files here)

features/
├── inventory/
│   ├── icapture-rename/
│   │   └── US1-modules-dispatch-via-icapture.feature    # NEW Gherkin
│   └── dependency-capture/
│       └── US2-pure-capture-handlers.feature            # NEW Gherkin
└── platform/
    └── iproject-analyser-removal/
        └── US3-iproject-analyser-removed.feature        # NEW Gherkin
```

**Structure Decision**: Single-project refactor across 5 existing projects. `Capture/` subfolder is introduced in `Infrastructure.Agent` to parallel `Analysis/` and give `DependencyCapture` a clear home separate from modules. `DependencyDiscovery/` subfolder in `Infrastructure.Simulated` keeps Simulated discovery implementations co-located. No new projects are added.

## Complexity Tracking

No constitution violations. All principles satisfied without architectural workarounds.

