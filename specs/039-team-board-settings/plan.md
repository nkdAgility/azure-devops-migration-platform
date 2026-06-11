# Implementation Plan: Team Board Configuration Export/Import

**Branch**: `039-team-board-settings` | **Date**: 2026-06-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/039-team-board-settings/spec.md`

## Summary

Export and import per-team board configuration (Kanban columns, swimlanes, card rules, backlog
metadata, sprint taskboard columns) delivered as a formal `ITeamExtension` — a new per-entity
extension contract that mirrors `IModule` (capabilities declared, phases implemented). Board config
is the first extension built against this pattern; the same pattern replaces the existing
boolean-flag if-blocks for `TeamSettings`, `TeamIterations`, etc. as they are touched.

The `TeamsModule` resolves all registered `ITeamExtension` implementations from DI, filters to
those enabled by options, and passes the ordered list to `TeamsOrchestrator`. The orchestrator
owns the per-team loop and invokes each extension per team — it does not know what any extension
does. Board config artefacts are written to `Teams/{slug}/board-config.json`. TFS connectors
dead-end via a new `ConnectorCapability` runtime flag mechanism rather than `#if NET481` guards.

## Technical Context

**Language/Version**: C# 13 / .NET 10 (net10.0) for main path; .NET 4.8.1 (net481) for TFS dead-ends  
**Primary Dependencies**: `Microsoft.TeamFoundation.WorkItemTracking.Client` (TFS), Azure DevOps .NET Client Libraries (`Microsoft.TeamFoundationServer.Client`)  
**Storage**: `IArtefactStore` / `IStateStore` via `IPackageAccess` — no direct filesystem access  
**Testing**: MSTest + Moq (MockBehavior.Strict) — test-first; `[TestMethod]` tests written before production code; existing `.feature` files are legacy and must not be modified  
**Target Platform**: net10.0 (AzureDevOpsServices, Simulated connectors); net481 stub (TFS)  
**Project Type**: Library — module extension inside `DevOpsMigrationPlatform.Infrastructure.Agent`  
**Performance Goals**: Board config export adds minimal per-team overhead (5–6 API calls per team); no bulk requirement  
**Constraints**: Package-first (no direct migration); cursor-based checkpointing; `IArtefactStore`/`IStateStore` only  
**Scale/Scope**: Per-team, per-project; realistic scale 100–1000 teams per project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design.*

| Principle | Check | Status |
|-----------|-------|--------|
| I – Package-First | Board config written to `Teams/{slug}/board-config.json` via `IPackageAccess`. No direct migration. | ✅ |
| II – Deterministic Pipeline | Export → package file → import. No side-effects across stages. | ✅ |
| III – Resumable / Idempotent | Per-team artefact existence check (same as `AlwaysExport` pattern). Import modes (Replace/Merge/Skip). | ✅ |
| IV – Cursor-Based Checkpointing | Board config export happens inside the existing per-team checkpoint loop in `TeamsOrchestrator`. | ✅ |
| V – Observability | ActivitySource tags on all new operations; structured log at export and import per team. | ✅ NEEDS IMPLEMENTATION |
| VI – No Coupling Between Modules | Board config lives entirely within `TeamsModule`. No cross-module reads. | ✅ |
| VII – Options via IOptions&lt;T&gt; | `BoardConfigExtensionsOptions` nested inside `TeamsModuleExtensionsOptions`. `IOptions<TeamsModuleOptions>` pattern. | ✅ |
| VIII – Sealed Options Classes | New options class is `sealed` with `SectionName` inherited from parent. | ✅ |
| IX – Abstractions Boundary | New source/target interfaces in `DevOpsMigrationPlatform.Abstractions.Agent`. Connectors implement in their own projects. | ✅ |
| X – No Direct DB / HTTP | All data via injected source/target interfaces (same pattern as `ITeamSource`). | ✅ |
| XI – Full Connector Coverage | AzureDevOpsServices, Simulated, and TFS (dead-end via `ConnectorCapability` flag). | ✅ NEEDS IMPLEMENTATION |

**Gate**: No violations. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```text
specs/039-team-board-settings/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/           ← Phase 1 output
│   └── ITeamBoardAdapter.md
└── tasks.md             ← Phase 2 output (/speckit-tasks — NOT created here)
```

### Source Code (repository root)

```text
# Extension architecture — cross-cutting marker and Teams-specific contract
src/DevOpsMigrationPlatform.Abstractions.Agent/
├── IModuleExtension.cs                   # NEW — cross-cutting marker (Module, Name, SupportsExport, SupportsImport)
└── Teams/
    ├── ITeamExtension.cs                 # NEW — per-entity extension contract (IsEnabled, ExportAsync, ImportAsync)
    └── TeamExtensionContext.cs           # NEW — context passed to every extension per team

# Abstractions — board config data records
src/DevOpsMigrationPlatform.Abstractions.Agent/
└── Teams/
    ├── BoardColumn.cs                    # NEW — Kanban column record
    ├── BoardColumnStateMappings.cs       # NEW — column→state mapping record
    ├── BoardSwimLane.cs                  # NEW — swimlane (row) record
    ├── BoardCardRule.cs                  # NEW — card rule (colour-coding) record
    ├── BacklogMetadata.cs                # NEW — backlog display name + WIT category
    ├── TaskboardColumn.cs                # NEW — sprint taskboard column record
    ├── TeamBoardConfig.cs                # NEW — top-level package model (per team)
    └── ITeamBoardAdapter.cs              # NEW — single adapter contract (export + import)

# Options — extend existing TeamsModuleExtensionsOptions
src/DevOpsMigrationPlatform.Abstractions.Agent/
└── Modules/
    └── TeamsModuleOptions.cs             # EXTEND — add BoardConfig sub-options

# Infrastructure — extension implementation, orchestrator wiring, DI registration
src/DevOpsMigrationPlatform.Infrastructure.Agent/
├── Teams/
│   └── Extensions/
│       └── BoardConfigTeamExtension.cs   # NEW — implements ITeamExtension; owns export+import board config logic
└── Modules/
    ├── TeamsModule.cs                    # EXTEND — resolve IEnumerable<ITeamExtension> from DI, filter enabled, pass to orchestrator
    ├── TeamsOrchestrator.cs              # EXTEND — accept IReadOnlyList<ITeamExtension>, invoke per team in loop
    └── TeamsServiceCollectionExtensions.cs   # EXTEND — register BoardConfigTeamExtension as ITeamExtension

# Connector — AzureDevOpsServices implementation
src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
└── Teams/
    └── AzureDevOpsBoardAdapter.cs        # NEW — implements ITeamBoardAdapter (export + import)

# Connector — Simulated implementation
src/DevOpsMigrationPlatform.Infrastructure.Simulated/
└── Teams/
    └── SimulatedBoardAdapter.cs          # NEW — implements ITeamBoardAdapter (export + import)

# Connector — TFS (explicit capability declaration — no null-guards in extension)
# TFS registers IConnectorCapabilityProvider with ConnectorCapability.None explicitly.
# This satisfies runtime-compatibility-net10-net481 Rule 7:
# "DI registration must not be used to hide capability gaps."
# BoardConfigTeamExtension checks Has(ConnectorCapability.BoardConfig) == false → returns Skipped.
# No null-guard (if _boardSource is null) appears anywhere in extension code.
src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
└── Teams/
    ├── TfsConnectorCapabilityProvider.cs     # NEW — IConnectorCapabilityProvider returning ConnectorCapability.None
    └── TfsNullBoardAdapter.cs                # NEW — ITeamBoardAdapter; all methods throw NotSupportedException; registered so DI can construct BoardConfigTeamExtension; capability check fires first so never reached

# Tests
tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
└── Teams/
    ├── BoardConfigTeamExtensionTests.cs       # MSTest — export + import scenarios
    ├── ConnectorCapabilityTests.cs
    └── TeamExtensionDispatchTests.cs

tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/      # NEW project
└── Teams/
    └── AzureDevOpsBoardAdapterTests.cs

tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/
└── Teams/
    ├── SimulatedBoardAdapterExportTests.cs         # Full round-trip: export → board-config.json
    └── SimulatedBoardAdapterImportTests.cs         # Import modes: Replace / Merge / Skip
```

**Structure Decision**: Board config is delivered as a formal `ITeamExtension`. The `TeamsModule`
owns extension discovery and enablement; `TeamsOrchestrator` owns the per-team loop and invokes
extensions without knowing their content. `BoardConfigTeamExtension` encapsulates both export and
import in one class — export and import are capabilities on an extension, not different types.
All new abstractions land in `DevOpsMigrationPlatform.Abstractions.Agent`; connector
implementations in their respective projects; TFS registers capability explicitly.

## Extension Architecture

### Design Decision

The existing `TeamsModule` uses boolean flags (`if (extensions.TeamSettings)` etc.) checked
inline in per-team orchestrators. This feature introduces a formal `ITeamExtension` contract
that replaces that pattern going forward. Board config is the first extension built against it.

### Contracts

```csharp
// DevOpsMigrationPlatform.Abstractions.Agent — cross-cutting marker
public interface IModuleExtension
{
    string Module { get; }           // "Teams", "WorkItems", …
    string Name   { get; }           // "BoardConfig", "TeamSettings", …
    bool SupportsExport { get; }
    bool SupportsImport { get; }
}

// DevOpsMigrationPlatform.Abstractions.Agent.Teams — Teams-specific extension contract
public interface ITeamExtension : IModuleExtension
{
    string IModuleExtension.Module => "Teams";
    bool IsEnabled(TeamsModuleExtensionsOptions options);
    Task ExportAsync(TeamExtensionContext context, CancellationToken ct);
    Task ImportAsync(TeamExtensionContext context, CancellationToken ct);
}

// Context passed to every ITeamExtension per team — export and import share the same record
public sealed record TeamExtensionContext(
    string Organisation,
    string Project,
    string SourceProject,
    TeamDefinition Team,
    string Slug,
    IPackageAccess Package,
    TeamsModuleExtensionsOptions Extensions,
    IProgressSink? ProgressSink);
```

### Module → Orchestrator Seam

`TeamsModule` owns **what** extensions are active. It resolves `IEnumerable<ITeamExtension>`
from DI, filters by `IsEnabled` and `SupportsExport`/`SupportsImport`, and passes an ordered
`IReadOnlyList<ITeamExtension>` to the orchestrator.

`TeamsOrchestrator` owns **when** extensions run. It drives the per-team loop, checkpointing,
metrics, and progress events — invoking each extension per team without knowing its content.

```
TeamsModule.ExportAsync()
  │  resolves IEnumerable<ITeamExtension> from DI
  │  filters:  e.SupportsExport && e.IsEnabled(options.Extensions)
  │  orders:   by declared Order (e.g. BoardConfig after TeamSettings)
  ↓
TeamsOrchestrator.ExportAsync(teamSource, context, extensions, options, ct)
  │  foreach team:
  │    foreach extension in extensions:
  │      await extension.ExportAsync(ctx, ct)
  ↓
BoardConfigTeamExtension.ExportAsync(ctx, ct)
  │  checks ConnectorCapability.BoardConfig
  │  calls ITeamBoardAdapter.*
  │  persists Teams/{slug}/board-config.json
```

### Why One Extension Class, Not Two

`BoardConfigTeamExtension` implements both `ExportAsync` and `ImportAsync`. Export and import
are capabilities on an extension — `SupportsExport` and `SupportsImport` declare which phases
it participates in. Splitting into `BoardConfigExportExtension` and `BoardConfigImportExtension`
would be the same mistake as `ITeamExportExtension` vs `ITeamImportExtension`: it fragments a
cohesive concern and forces every consumer to reason about two registrations for one feature.

### Ordering

Extension ordering is declared on `ITeamExtension` via an `int Order` property (lower = earlier).
`TeamsModule` sorts before passing to the orchestrator. For this feature, `BoardConfig` runs last
among export extensions (after team definition, settings, iterations, members, area paths) so
that team identity is confirmed in the package before board config is written.

---

## Complexity Tracking

> No Constitution Check violations requiring justification.

---

## Observability

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `teams.boardconfig.export` | `module` | `BoardConfigTeamExtension.ExportAsync` | `ITeamBoardAdapter.GetBoardsAsync`, `GetCardRuleSettingsAsync`, `GetBacklogsAsync`, `GetTaskboardColumnsAsync`, `IPackageAccess.PersistContentAsync` |
| `teams.boardconfig.import` | `module` | `BoardConfigTeamExtension.ImportAsync` | `ITeamBoardAdapter.UpdateBoardColumnsAsync`, `UpdateSwimLanesAsync`, `UpdateCardRuleSettingsAsync`, `UpdateTaskboardColumnsAsync`, `IPackageAccess` (read) |

---

### Operator Decisions

| Operation | Decision | Question |
|---|---|---|
| `teams.boardconfig.export` | Is it working? | Are board configs being exported without errors? |
| `teams.boardconfig.export` | Is it fast enough? | Is per-team board config export completing within acceptable time? |
| `teams.boardconfig.export` | Is it overloaded? | Are exports backing up across many teams in parallel? |
| `teams.boardconfig.export` | What failed? | Which team's board config export failed and why? |
| `teams.boardconfig.export` | Is it correct? | Does the exported artefact contain boards, columns, and backlogs? |
| `teams.boardconfig.import` | Is it working? | Are board configs being applied to the target successfully? |
| `teams.boardconfig.import` | Is it fast enough? | Is per-team board config import completing within acceptable time? |
| `teams.boardconfig.import` | Is it overloaded? | Are import API calls being throttled or queuing? |
| `teams.boardconfig.import` | What failed? | Which team's board config import failed and why? |
| `teams.boardconfig.import` | Is it correct? | Do the target board columns match the package values after import? |

---

### Metrics

All metrics are recorded on the `DevOpsMigrationPlatform.Agent` meter (see `WellKnownMeterNames.Agent`).

New constants to add to `WellKnownAgentMetricNames`:

```
// --- Teams Board Config Export ---
platform.teams.export.boardconfig.count        Counter<long>         {team}
platform.teams.export.boardconfig.duration_ms  Histogram<double>     ms
platform.teams.export.boardconfig.errors       Counter<long>         {team}
platform.teams.export.boardconfig.in_flight    UpDownCounter<long>   {team}

// --- Teams Board Config Import ---
platform.teams.import.boardconfig.count        Counter<long>         {team}
platform.teams.import.boardconfig.duration_ms  Histogram<double>     ms
platform.teams.import.boardconfig.errors       Counter<long>         {team}
platform.teams.import.boardconfig.in_flight    UpDownCounter<long>   {team}
platform.teams.import.boardconfig.skipped      Counter<long>         {team}
```

| Metric Name | Instrument | Unit | Operation | Decision |
|---|---|---|---|---|
| `platform.teams.export.boardconfig.count` | `Counter<long>` | `{team}` | export | Is it working? |
| `platform.teams.export.boardconfig.duration_ms` | `Histogram<double>` | `ms` | export | Is it fast enough? |
| `platform.teams.export.boardconfig.errors` | `Counter<long>` | `{team}` | export | What failed? |
| `platform.teams.export.boardconfig.in_flight` | `UpDownCounter<long>` | `{team}` | export | Is it overloaded? |
| `platform.teams.import.boardconfig.count` | `Counter<long>` | `{team}` | import | Is it working? |
| `platform.teams.import.boardconfig.duration_ms` | `Histogram<double>` | `ms` | import | Is it fast enough? |
| `platform.teams.import.boardconfig.errors` | `Counter<long>` | `{team}` | import | What failed? |
| `platform.teams.import.boardconfig.in_flight` | `UpDownCounter<long>` | `{team}` | import | Is it overloaded? |
| `platform.teams.import.boardconfig.skipped` | `Counter<long>` | `{team}` | import | Is it working? (Skip mode count) |

---

### Traces

Activity sources: `WellKnownActivitySourceNames.Migration` (export/import), `WellKnownActivitySourceNames.Discovery` (inventory).

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| `BoardConfigTeamExtension` | `teams.boardconfig.export` | `job.id`, `team.name`, `team.slug`, `module=Teams` | Root | Is it working? / Is it fast enough? |
| `ITeamBoardAdapter.GetBoardsAsync` | `teams.boardconfig.export.boards` | `job.id`, `team.id`, `board.count` | `teams.boardconfig.export` | Where is it slow? |
| `ITeamBoardAdapter.GetCardRuleSettingsAsync` | `teams.boardconfig.export.cardrules` | `job.id`, `team.id`, `board.name` | `teams.boardconfig.export` | Where is it slow? |
| `ITeamBoardAdapter.GetBacklogsAsync` | `teams.boardconfig.export.backlogs` | `job.id`, `team.id` | `teams.boardconfig.export` | Where is it slow? |
| `ITeamBoardAdapter.GetTaskboardColumnsAsync` | `teams.boardconfig.export.taskboard` | `job.id`, `team.id` | `teams.boardconfig.export` | Where is it slow? |
| `IPackageAccess.PersistContentAsync` | `teams.boardconfig.export.persist` | `job.id`, `team.slug`, `path` | `teams.boardconfig.export` | Is it correct? |
| `BoardConfigTeamExtension` | `teams.boardconfig.import` | `job.id`, `team.name`, `team.slug`, `import_mode`, `module=Teams` | Root | Is it working? / Is it fast enough? |
| `ITeamBoardAdapter.UpdateBoardColumnsAsync` | `teams.boardconfig.import.columns` | `job.id`, `team.id`, `board.name`, `column.count` | `teams.boardconfig.import` | Is it correct? |
| `ITeamBoardAdapter.UpdateSwimLanesAsync` | `teams.boardconfig.import.swimlanes` | `job.id`, `team.id`, `board.name` | `teams.boardconfig.import` | Is it correct? |
| `ITeamBoardAdapter.UpdateCardRuleSettingsAsync` | `teams.boardconfig.import.cardrules` | `job.id`, `team.id`, `board.name` | `teams.boardconfig.import` | Is it correct? |
| `ITeamBoardAdapter.UpdateTaskboardColumnsAsync` | `teams.boardconfig.import.taskboard` | `job.id`, `team.id` | `teams.boardconfig.import` | Is it correct? |

**Context propagation:** Automatic via `Activity` parent-child hierarchy. Root spans are created by the orchestrator using `WellKnownActivitySourceNames.Migration`. Child spans are created inside connector implementations using the same source. W3C TraceContext headers used for any outbound HTTP calls within connector implementations.

---

### Logging

All log events use `ILogger<T>` with structured templates (no string concatenation). Customer data
(team names, board names) uses `DataClassification.Customer` scope where applicable.

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| Board config export started | `Information` | `teamName`, `teamSlug`, `boardCount` | export | Is it working? |
| Board config export completed | `Information` | `teamName`, `teamSlug`, `boardCount`, `durationMs` | export | Is it working? / Is it fast enough? |
| Board config export skipped (no source) | `Warning` | `teamSlug`, `reason` | export | What failed? |
| Board config export failed | `Error` | `teamSlug`, `errorType`, `durationMs` | export | What failed? |
| Board config import started | `Information` | `teamName`, `teamSlug`, `importMode` | import | Is it working? |
| Board config import completed | `Information` | `teamName`, `teamSlug`, `importMode`, `durationMs` | import | Is it working? / Is it fast enough? |
| Board config import skipped (Skip mode) | `Information` | `teamSlug`, `importMode=Skip` | import | Is it working? |
| Board config import skipped (no capability) | `Information` | `teamSlug`, `capability=BoardConfig` | import | What failed? |
| Board config import failed | `Error` | `teamSlug`, `boardName`, `errorType`, `durationMs` | import | What failed? |
| Board not found on target | `Warning` | `teamSlug`, `boardName` | import | What failed? (RT-H4) |
| Per-board columns API call slow | `Warning` | `teamSlug`, `boardName`, `durationMs`, `thresholdMs` | export | Where is it slow? |

> Debug and Trace levels (per-item column enumeration details, raw API payloads) are disabled by default.

---

### Correlation

| Field | Source | Scope |
|---|---|---|
| `traceId` | `Activity.Current.TraceId` | All telemetry within a board config export/import call |
| `parentId` | `Activity.Current.ParentSpanId` | Child spans within an export/import root |
| `job.id` | `ExportContext.Job.JobId` / `ImportContext.Job.JobId` | All telemetry within a job |
| `team.name` | `TeamDefinition.Name` (classified) | Spans and logs for a specific team |
| `team.slug` | `slug` parameter | Spans, logs, artefact paths |
| `board.name` | Board name from API | Child span tags |
| `import_mode` | `BoardConfigExtensionsOptions.ImportMode` | Import root span + logs |

---

### Validation Queries

#### Failure Identification
```kql
// Identify board config export failures by team
customMetrics
| where name == "platform.teams.export.boardconfig.errors"
| summarize totalErrors = sum(value) by tostring(customDimensions["team.slug"]), bin(timestamp, 5m)
| where totalErrors > 0
| order by timestamp desc
```

#### Latency Analysis
```kql
// P50/P95/P99 board config export latency per team
customMetrics
| where name == "platform.teams.export.boardconfig.duration_ms"
| summarize
    p50 = percentile(value, 50),
    p95 = percentile(value, 95),
    p99 = percentile(value, 99)
  by bin(timestamp, 15m)
```

#### Load Observation
```kql
// In-flight board config exports (concurrency / overload detection)
customMetrics
| where name == "platform.teams.export.boardconfig.in_flight"
| summarize maxInFlight = max(value) by bin(timestamp, 1m)
| order by timestamp desc
```

#### End-to-End Trace
```kql
// Trace a single team's board config export from root span to all dependency spans
dependencies
| where operation_Name == "teams.boardconfig.export"
| where customDimensions["team.slug"] == "<slug>"
| project timestamp, operation_Id, name, duration, success
| order by timestamp asc
```

#### Error Diagnosis
```kql
// Join error logs with failing traces to diagnose root cause
exceptions
| join kind=inner (
    dependencies
    | where operation_Name startswith "teams.boardconfig"
    | where success == false
) on operation_Id
| project timestamp, operation_Id, outerMessage, type, name, duration
| order by timestamp desc
```
