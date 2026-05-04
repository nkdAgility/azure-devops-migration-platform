# Research: Module IModule Phase Consolidation and IAnalyser Introduction

## Current State Findings

### IModule interface (current)

```csharp
// DevOpsMigrationPlatform.Abstractions.Agent/Modules/IModule.cs
public interface IModule
{
    string Name { get; }
    IReadOnlyList<ModuleDependency> DependsOn { get; }
    bool SupportsExport { get; }
    bool SupportsImport { get; }
    Task ExportAsync(ExportContext context, CancellationToken ct);
    Task ImportAsync(ImportContext context, CancellationToken ct);
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
```

**Gap**: No `InventoryAsync`, no `PrepareAsync`, no `SupportsInventory`, no `SupportsPrepare`.

### DependencyPhase enum (current)

```csharp
// DevOpsMigrationPlatform.Abstractions.Agent/Modules/DependencyPhase.cs
public enum DependencyPhase { Export = 1, Import = 2, Both = 3 }
```

**Gap**: Missing `Inventory = 0`, `Prepare = 4`, `Analyse = 5`.

### ModuleDependency record (current)

- Strips `"Module"` suffix to compute `ModuleName`.
- Has `AppliesToExport` and `AppliesToImport` computed properties.
- **Gap**: Does not strip `"Analyser"` suffix. No `AppliesToInventory`, `AppliesToPrepare`, `AppliesToAnalyse`.

### IInventoryOrchestrator (current)

```csharp
// DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IInventoryOrchestrator.cs
public interface IInventoryOrchestrator
{
    Task RunAsync(
        string moduleName,
        IAsyncEnumerable<InventoryProgressEvent> eventStream,
        ExportContext context,
        IReadOnlyList<ScopedOrganisationEndpoint> organisations,
        int checkpointIntervalSeconds = 300,
        CancellationToken ct = default);
}
```

**Note**: Takes `ExportContext` — will need to accept `InventoryContext` after this refactor, or the orchestrator is inlined into `WorkItemsModule.InventoryAsync` directly.

### Existing modules (7 classes → will reduce to 4 + 1 analyser)

| Class | Assembly | Fate |
|---|---|---|
| `InventoryModule` | Infrastructure.Agent | **Eliminated** — logic → `WorkItemsModule.InventoryAsync` |
| `InventoryDiscoveryModule` | Infrastructure.Agent | **Eliminated** — logic → `JobAgentWorker` multi-org loop |
| `DependencyDiscoveryModule` | Infrastructure.Agent | **Eliminated** → `DependencyAnalyser : IAnalyser` |
| `WorkItemsModule` | Infrastructure.Agent | **Extended** with `InventoryAsync`, `PrepareAsync` |
| `IdentitiesModule` | Infrastructure.Agent | **Extended** with `InventoryAsync`, `PrepareAsync` |
| `NodesModule` | Infrastructure.Agent | **Extended** with `InventoryAsync`, `PrepareAsync` |
| `TeamsModule` | Infrastructure.Agent | **Extended** with `InventoryAsync`, `PrepareAsync` |

### Key infrastructure types

| Type | Location |
|---|---|
| `JobAgentWorker` | `DevOpsMigrationPlatform.MigrationAgent` |
| `JobExecutionPlanBuilder` | `Infrastructure.Agent/Context/` |
| `WellKnownMetricNames` | `Abstractions/Telemetry/` |
| `WellKnownActivitySourceNames` | `Abstractions/Telemetry/` — has `Discovery` source for inventory ops |

---

## Decisions

### Decision 1: `InventoryContext` vs reusing `ExportContext`

- **Decision**: Introduce a new `InventoryContext` record.
- **Rationale**: `ExportContext` carries a target endpoint and export-specific state that is irrelevant (and misleading) for inventory. `InventoryContext` is scoped to a single source endpoint and carries only `Job`, `IArtefactStore`, `IStateStore`, `IProgressSink?`, and `OrganisationEndpoint`. Passing a source endpoint per call is what enables the multi-org loop in `JobAgentWorker`.
- **Alternatives considered**: Reusing `ExportContext` (no new type needed). Rejected — semantic pollution, confuses future module authors.

### Decision 2: `PrepareContext` vs reusing `ExportContext`/`ImportContext`

- **Decision**: Introduce a new `PrepareContext` record.
- **Rationale**: Prepare reads from the package (source artefacts) AND queries the target to validate/map. It needs both `IArtefactStore` (read) and `ITargetEndpointInfo`. Neither `ExportContext` (source-only) nor `ImportContext` (full write) captures this accurately.
- **Alternatives considered**: Overloading `ImportContext` with a `PrepareOnly` flag. Rejected — flag-based behaviour is an anti-pattern; contexts should describe intent.

### Decision 3: `IInventoryOrchestrator` — retain or inline?

- **Decision**: Retain `IInventoryOrchestrator` but update its signature to accept `InventoryContext` instead of `ExportContext`. It is injected into `WorkItemsModule` (the primary inventory producer).
- **Rationale**: The orchestrator owns checkpoint management, progress events, and metric emission for inventory — these are non-trivial concerns that should not be inlined. Retaining the interface keeps the Module → Orchestrator → Service pattern intact.
- **Alternatives considered**: Inline all orchestration into `WorkItemsModule.InventoryAsync`. Rejected — violates Module → Orchestrator → Service pattern (docs/modules.md).

### Decision 4: `IAnalyser` assembly placement

- **Decision**: `IAnalyser` and `AnalyseContext` go in `DevOpsMigrationPlatform.Abstractions.Agent` (same as `IModule`).
- **Rationale**: Guardrail 20a permits `MigrationAgent → Abstractions.Agent`. `IAnalyser` must be discoverable by `JobExecutionPlanBuilder` which lives in `Infrastructure.Agent`. Placing in `Abstractions.Agent` respects project reference boundaries without introducing a circular dependency.
- **Alternatives considered**: New `Abstractions.Analysis` assembly. Rejected — adds unjustified assembly complexity for a single interface.

### Decision 5: `ModuleDependency` — suffix stripping for analysers

- **Decision**: Extend `ModuleDependency.ModuleName` to also strip `"Analyser"` suffix (e.g., `DependencyAnalyser` → `"Dependencies"`).
- **Rationale**: Task IDs are `"analyse.dependencies"` — the stem must match the conventional key. Consistent with existing `"Module"` stripping.
- **Alternatives considered**: Separate `AnalyserDependency` record. Rejected — unnecessary type proliferation; one dependency declaration type is sufficient.

### Decision 6: Multi-org loop location

- **Decision**: Multi-org loop lives in `JobAgentWorker` (not a new `IInventoryPhaseExecutor`).
- **Rationale**: `JobAgentWorker` already handles job dispatch by `JobKind`. Adding a loop over source endpoints is a natural extension of the existing dispatch method. A new interface (`IInventoryPhaseExecutor`) would be used by only one caller — this fails the "≥2 modules" new-abstraction rule (guardrail 21).
- **Alternatives considered**: New `IInventoryPhaseExecutor`. Rejected — guardrail 21.

### Decision 7: Inventory artefact format — shared file vs per-module

- **Decision**: Single shared `inventory.json` (and `inventory.csv`). Each module appends/merges its domain counts into the shared artefact via the `IInventoryOrchestrator`.
- **Rationale**: Consistent with current `InventoryModule` output. Operators and downstream analysers have a single well-known path to read.
- **Alternatives considered**: Per-module files (`WorkItems/inventory.json`, `Nodes/inventory.json`). Rejected — would require aggregation logic in every consumer.

### Decision 8: `DependencyAnalyser` orchestrator

- **Decision**: `DependencyAnalyser` reuses `IDependencyOrchestrator` (retained, adapted to `AnalyseContext`).
- **Rationale**: `DependencyDiscoveryModule` already delegates to `IDependencyOrchestrator`. Moving to `IAnalyser` doesn't change the orchestration concern. Retaining the orchestrator keeps the Module/Analyser → Orchestrator → Service pattern consistent.
- **Alternatives considered**: Inline dependency logic in `DependencyAnalyser`. Rejected — Module → Orchestrator → Service pattern required (docs/modules.md).

### Decision 9: `ValidateAsync` — unchanged

- **Decision**: `ValidateAsync` signature remains `Task ValidateAsync(ValidationContext context, CancellationToken ct)`.
- **Rationale**: Spec FR-020 explicitly excludes it from scope.

### Decision 10: `JobKind.Dependencies` dispatch

- **Decision**: `JobKind.Dependencies` dispatches as `inventory (WorkItems only) → analyse (DependencyAnalyser only)` — same semantics as today but via the inventory + analyse phases rather than export phase.
- **Rationale**: Consistent with spec FR-013 and the design document.

---

## New Metric Names Required

| Metric | Name | Instrument |
|---|---|---|
| Inventory items counted (WorkItems) | `migration.inventory.workitems.count` | Counter |
| Inventory duration (WorkItems) | `migration.inventory.workitems.duration_ms` | Histogram |
| Inventory errors (WorkItems) | `migration.inventory.workitems.errors` | Counter |
| Inventory items counted (Identities) | `migration.inventory.identities.count` | Counter |
| Inventory items counted (Nodes) | `migration.inventory.nodes.count` | Counter |
| Inventory items counted (Teams) | `migration.inventory.teams.count` | Counter |
| Prepare resolved (WorkItems) | `migration.prepare.workitems.resolved` | Counter |
| Prepare unresolved (WorkItems) | `migration.prepare.workitems.unresolved` | Counter |
| Prepare duration (WorkItems) | `migration.prepare.workitems.duration_ms` | Histogram |
| Prepare resolved (Identities) | `migration.prepare.identities.resolved` | Counter |
| Prepare unresolved (Identities) | `migration.prepare.identities.unresolved` | Counter |
| Prepare resolved (Nodes) | `migration.prepare.nodes.resolved` | Counter |
| Prepare unresolved (Nodes) | `migration.prepare.nodes.unresolved` | Counter |
| Prepare resolved (Teams) | `migration.prepare.teams.resolved` | Counter |
| Prepare unresolved (Teams) | `migration.prepare.teams.unresolved` | Counter |
| Dependencies analysed | `migration.analyse.dependencies.count` | Counter |
| Dependencies duration | `migration.analyse.dependencies.duration_ms` | Histogram |
| Dependencies errors | `migration.analyse.dependencies.errors` | Counter |

All use activity source `WellKnownActivitySourceNames.Discovery` for inventory/analyse, and a new `WellKnownActivitySourceNames.Migration` span prefix for prepare.
