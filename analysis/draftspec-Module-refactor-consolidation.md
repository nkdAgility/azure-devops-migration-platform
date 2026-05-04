# Spec: Module Refactor — IModule Phase Consolidation + IAnalyser

**Status**: Superseded by spec 030 (`specs/030-module-analiser-refactor`)  
**Date**: 2026-05-03  
**Author**: MartinHinshelwoodNKD + Copilot  
**Supersedes**: Current `IModule` contract in `DevOpsMigrationPlatform.Abstractions.Agent`

---

## Problem Statement

The current `IModule` interface defines three methods: `ExportAsync`, `ImportAsync`, and `ValidateAsync`. However, the platform supports five operational modes (`Inventory`, `Export`, `Prepare`, `Import`, `Migrate`), and two of these — **Inventory** and **Prepare** — are handled outside the standard module contract:

1. **Inventory** is implemented as two separate `IModule` implementations (`InventoryModule` and `InventoryDiscoveryModule`) that abuse `ExportAsync` to run inventory logic, with `SupportsImport => false`. This is semantically misleading — inventory is not export.

2. **Prepare** is documented in `docs/modules.md` as part of the `IModule` contract (line 17: `PrepareAsync(PrepareContext context, CancellationToken ct)`) but **does not exist on the actual `IModule` interface**. The `IPackagePreparer` abstraction exists but only handles zip extraction — not per-module prepare logic.

3. **Multi-org inventory** (`InventoryDiscoveryModule`) is really just "run inventory N times across N organisations". It adds complexity by being a separate module class when it could be the orchestrator looping over organisations, calling each module's `InventoryAsync` per org.

This creates confusion:
- `JobKind.Inventory` dispatches to `InventoryModule`/`InventoryDiscoveryModule` via the Export phase — a semantic mismatch.
- `JobKind.Prepare` exists in the enum but has no per-module dispatch mechanism.
- The `InventoryDiscoveryModule` duplicates the Module → Orchestrator → Service pattern for what is essentially a loop over endpoints.

---

## Proposed Design

### 1. Expand IModule to Five Phase Methods

```csharp
public interface IModule
{
    string Name { get; }
    IReadOnlyList<ModuleDependency> DependsOn { get; }

    bool SupportsInventory { get; }
    bool SupportsExport { get; }
    bool SupportsPrepare { get; }
    bool SupportsImport { get; }

    Task InventoryAsync(InventoryContext context, CancellationToken ct);
    Task ExportAsync(ExportContext context, CancellationToken ct);
    Task PrepareAsync(PrepareContext context, CancellationToken ct);
    Task ImportAsync(ImportContext context, CancellationToken ct);
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
```

Each `JobKind` maps cleanly to one or more phase methods:

| JobKind | Phases executed (in order) |
|---|---|
| `Inventory` | `InventoryAsync` on all modules where `SupportsInventory` |
| `Export` | `InventoryAsync` → `ExportAsync` |
| `Prepare` | `PrepareAsync` on all modules where `SupportsPrepare` |
| `Import` | `ImportAsync` on all modules where `SupportsImport` |
| `Migrate` | `InventoryAsync` → `ExportAsync` → `PrepareAsync` → `ImportAsync` |

### 2. Each Module Knows How to Inventory Itself

Instead of `InventoryModule` being a separate module, each domain module implements its own `InventoryAsync`:

| Module | InventoryAsync behaviour |
|---|---|
| `WorkItemsModule` | Counts work items and revisions for the source project. Writes to `inventory.csv` / `inventory.json`. |
| `TeamsModule` | Counts teams in the source project. Adds team counts to inventory. |
| `NodesModule` | Counts area/iteration nodes. Adds node counts to inventory. |
| `IdentitiesModule` | Counts identity descriptors. Adds identity counts to inventory. |

This eliminates `InventoryModule` as a standalone class. The `WorkItemsModule` is the primary inventory producer (work item counts), and other modules can optionally contribute counts for their domain.

### 3. Multi-Org Inventory = Multiple Inventories (Simplification)

The key insight: **multi-org inventory is just running inventory N times with different source endpoints**.

Currently `InventoryDiscoveryModule` resolves a list of `ScopedOrganisationEndpoint` and calls `IInventoryService.RunInventoryAsync()` across all of them. This is orchestration-level logic, not module logic.

**Proposed simplification:**

```
JobKind.Inventory + multi-org config
  → For each organisation:
      → Create a scoped DI scope with ISourceEndpointInfo pointing to that org
      → Run InventoryAsync on each module
      → Aggregate results into the shared inventory artefacts
```

This means:
- **`InventoryDiscoveryModule` is eliminated entirely.**
- The `JobAgentWorker` (or a new `InventoryPhaseExecutor`) handles the multi-org loop.
- Each module's `InventoryAsync` is endpoint-scoped and simple — it inventories one source, one project.
- The orchestrator aggregates results across all org invocations.

The `InventoryContext` would carry the current endpoint:

```csharp
public record InventoryContext
{
    public Job Job { get; init; }
    public IArtefactStore ArtefactStore { get; init; }
    public IStateStore StateStore { get; init; }
    public IProgressSink? ProgressSink { get; init; }
    public OrganisationEndpoint SourceEndpoint { get; init; }
    public IReadOnlyList<string>? Projects { get; init; }
}
```

### 4. PrepareAsync — Per-Module Target Validation

`PrepareAsync` fills the gap between export and import. It reads the package, queries the target, and writes validation/mapping artefacts:

| Module | PrepareAsync behaviour |
|---|---|
| `IdentitiesModule` | Reads `descriptors.jsonl`, queries target for matching identities, writes `Identities/prepare-report.json`. |
| `NodesModule` | Reads `source-tree.json`, queries target for existing nodes, writes `Nodes/prepare-report.json`. |
| `TeamsModule` | Reads team files, queries target for existing teams/groups, writes `Teams/prepare-report.json`. |
| `WorkItemsModule` | Cross-references field names with `FieldTranslations`, validates area/iteration paths on target, writes `WorkItems/prepare-report.json`. |

This is already documented in `docs/modules.md` — we're just implementing the contract that was always intended.

### 5. Dependencies Module — The Unique Case

`DependencyDiscoveryModule` is fundamentally different from every other module:

- **Cross-cutting**: it analyses relationships *between* work items across projects/orgs — not a single domain concern.
- **Multi-org by nature**: like inventory, it operates across multiple endpoints.
- **Analysis-only**: `dependencies.csv` is never imported — it's a planning artefact.
- **Has its own `JobKind.Dependencies`**: it's a standalone operation.
- **No domain data**: unlike `WorkItemsModule` (work items), `TeamsModule` (teams), etc., it doesn't own a data domain.

#### Options Considered

| Option | Description | Pros | Cons |
|---|---|---|---|
| **A: Keep as standalone IModule, SupportsExport** | Status quo — runs during Export phase | Minimal change | Semantic mismatch: not really "exporting" |
| **B: Fold into WorkItemsModule.InventoryAsync** | Dependency analysis as deeper inventory | Eliminates a module | Makes InventoryAsync do two things; expensive optional step |
| **C: Keep as standalone IModule, SupportsInventory** | Use `InventoryAsync` instead of `ExportAsync` | Semantically correct — it IS discovery | Still a separate module for cross-cutting concern |
| **D: Promote to a platform-level analysis step** | Not a module at all — runs between Inventory and Export | Clean separation: analysis ≠ module | New dispatch mechanism; breaks the "everything is a module" pattern |

#### Decision: Option D — Promote to IAnalyser

`DependencyDiscoveryModule` is **eliminated** and replaced by `DependencyAnalyser : IAnalyser`.

This was initially considered as Option C (inventory-phase `IModule`), but the subsequent introduction of the `IAnalyser` interface makes the correct answer clear: dependency analysis is **not** a module at all — it owns no data domain and never participates in Export, Prepare, or Import. It reads inventory artefacts and writes analysis outputs. That is exactly what `IAnalyser` is for.

See the **Design Decision: Two Distinct Interfaces** section below for the full `IAnalyser` contract and `DependencyAnalyser` implementation sketch.

#### Alternative: Could Dependencies Be a Scope on WorkItemsModule?

One could argue that dependency analysis is "inventory with a `dependencies` scope on `WorkItemsModule`". This would mean:

```json
{
  "name": "WorkItems",
  "scopes": [
    { "type": "wiql", "parameters": { "query": "..." } },
    { "type": "dependencies", "parameters": { "depth": "transitive" } }
  ]
}
```

**Pros:** Eliminates the module entirely; dependencies are about work item links.  
**Cons:** Scopes currently filter *which* items to process — dependency analysis is a different *operation*, not a different *scope*. The semantics don't match. Dependencies produces `dependencies.csv`, not work item revisions.

**Verdict:** Keep as a separate module. Dependencies isn't a scope — it's a different kind of discovery.

---

## Impact Analysis

### Types Eliminated

| Type | Replaced by |
|---|---|
| `InventoryModule` | `WorkItemsModule.InventoryAsync` (+ other modules contributing counts) |
| `InventoryDiscoveryModule` | Multi-org loop in `JobAgentWorker` / `InventoryPhaseExecutor` |

### New Types Required

| Type | Purpose |
|---|---|
| `InventoryContext` | Context object for `InventoryAsync`, carries endpoint + project scope |
| `PrepareContext` | Context object for `PrepareAsync`, carries package + target endpoint |
| `IInventoryPhaseExecutor` | Handles multi-org loop, calling `InventoryAsync` per module per org |

### Modified Types

| Type | Change |
|---|---|
| `IModule` | Add `InventoryAsync`, `PrepareAsync`, `SupportsInventory`, `SupportsPrepare` |
| `IJobPlanExecutor` | Add `ExecuteInventoryPhaseAsync`, `ExecutePreparePhaseAsync` |
| `JobExecutionPlanBuilder` | Build plans that include Inventory and Prepare phases |
| `JobAgentWorker` | Dispatch `JobKind.Inventory` → inventory phase, `JobKind.Prepare` → prepare phase |
| `WorkItemsModule` | Move inventory logic from `InventoryModule.ExportAsync` into `InventoryAsync` |
| `TeamsModule`, `NodesModule`, `IdentitiesModule` | Add `InventoryAsync` (optional contribution), `PrepareAsync` |
| `DependencyDiscoveryModule` | `DependencyAnalyser : IAnalyser` (see IAnalyser section) |

### Module Phase Support Matrix (Post-Refactor)

| Module | Inventory | Export | Prepare | Import | Validate |
|---|---|---|---|---|---|
| `IdentitiesModule` | ○ | ✓ | ✓ | ✓ | ✓ |
| `NodesModule` | ○ | ✓ | ✓ | ✓ | ✓ |
| `TeamsModule` | ○ | ✓ | ✓ | ✓ | ✓ |
| `WorkItemsModule` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `DependencyDiscoveryModule` | ✓ | — | — | — | — |

✓ = primary implementation, ○ = optional contribution, — = not supported

---

## Migration Path (Incremental)

This refactor can be done incrementally without breaking existing behaviour:

### Phase 1: Add methods to IModule with default implementations
- Add `InventoryAsync` and `PrepareAsync` to `IModule` with `throw new NotSupportedException()` defaults (or use an abstract base class).
- Add `SupportsInventory` and `SupportsPrepare` properties (default `false`).
- All existing modules continue to work unchanged.

### Phase 2: Move inventory logic into WorkItemsModule
- Implement `WorkItemsModule.InventoryAsync` by extracting logic from `InventoryModule.ExportAsync`.
- Set `SupportsInventory => true` on `WorkItemsModule`.
- Wire `JobKind.Inventory` to call the inventory phase instead of export phase.
- Keep `InventoryModule` temporarily as a fallback.

### Phase 3: Implement multi-org loop in the orchestrator
- Create `IInventoryPhaseExecutor` that loops over configured organisations.
- For each org, create a scoped context and call `InventoryAsync` on relevant modules.
- Eliminate `InventoryDiscoveryModule`.

### Phase 4: Implement PrepareAsync on modules
- Add `PrepareAsync` to each module (Identities, Nodes, Teams, WorkItems).
- Wire `JobKind.Prepare` to call the prepare phase.
- This is net-new functionality — no existing behaviour to migrate.

### Phase 5: Clean up
- Remove `InventoryModule` and `InventoryDiscoveryModule`.
- Update `JobExecutionPlanBuilder` to produce plans with all five phase types.
- Update `docs/modules.md` to reflect the new contract (it's already partially documented).

---

## Open Questions

1. **Should `InventoryAsync` write to a shared `inventory.csv/json`, or should each module write to its own `{Module}/inventory.json`?** Current design uses a shared file. Per-module files are more modular but require aggregation.

2. **Should the multi-org loop live in `JobAgentWorker`, in `IJobPlanExecutor`, or in a new `IInventoryPhaseExecutor`?** The plan executor already handles tier-based dispatch — extending it is natural.

3. **`DependencyDiscoveryModule` moves to inventory phase.** See section 5 — recommended approach is Option C: `SupportsInventory => true`, `SupportsExport => false`. `JobKind.Dependencies` dispatches via the inventory phase with only the dependency module enabled. Multi-org is handled by the same orchestrator loop as inventory.

4. **Base class vs interface-only?** An abstract `ModuleBase` class could provide default `NotSupportedException` implementations for unsupported phases. This avoids forcing every module to implement all five methods. However, the current design is interface-only — introducing a base class is a style decision.

5. **What about `ValidateAsync` — should it be phase-specific?** Currently there's one `ValidateAsync`. Should there be `ValidateExportAsync` and `ValidateImportAsync`? Or is one generic validation sufficient?

---

## Design Decision: Two Distinct Interfaces — IModule and IAnalyser

The architecture uses **two interfaces** for participants in a migration job:

---

### Interface 1: IModule (data-owning domain modules)

```csharp
public interface IModule
{
    string Name { get; }
    IReadOnlyList<ModuleDependency> DependsOn { get; }

    bool SupportsInventory { get; }
    bool SupportsExport { get; }
    bool SupportsPrepare { get; }
    bool SupportsImport { get; }

    Task InventoryAsync(InventoryContext context, CancellationToken ct);
    Task ExportAsync(ExportContext context, CancellationToken ct);
    Task PrepareAsync(PrepareContext context, CancellationToken ct);
    Task ImportAsync(ImportContext context, CancellationToken ct);
    Task ValidateAsync(ValidationContext context, CancellationToken ct);
}
```

`IModule` is for anything that **owns a data domain** and participates in the data lifecycle (Inventory → Export → Prepare → Import). An `IModule` reads from a source, writes artefacts, and imports those artefacts to a target.

| Module | Data domain | Inventory | Export | Prepare | Import |
|---|---|---|---|---|---|
| `WorkItemsModule` | Work items + revisions | ✓ | ✓ | ✓ | ✓ |
| `IdentitiesModule` | User/group descriptors | ○ | ✓ | ✓ | ✓ |
| `NodesModule` | Area/iteration trees | ○ | ✓ | ✓ | ✓ |
| `TeamsModule` | Team membership/settings | ○ | ✓ | ✓ | ✓ |
| _(future)_ `GitModule` | Git repositories | ○ | ✓ | ✓ | ✓ |
| _(future)_ `TestManagementModule` | Test plans/suites/cases | ○ | ✓ | ✓ | ✓ |

✓ = implements this phase, ○ = optional count contribution

---

### Interface 2: IAnalyser (cross-cutting analysis)

```csharp
public interface IAnalyser
{
    string Name { get; }
    IReadOnlyList<ModuleDependency> DependsOn { get; }

    Task AnalyseAsync(AnalyseContext context, CancellationToken ct);
}
```

`IAnalyser` is for anything that **reads existing artefacts** (from `IArtefactStore`) and produces **analysis outputs** — reports, dependency maps, diff summaries. An `IAnalyser` never imports data to a target. It is read-and-write against the artefact store only.

```csharp
public record AnalyseContext
{
    public Job Job { get; init; }
    public IArtefactStore ArtefactStore { get; init; }  // read inventory/export artefacts; write analysis artefacts
    public IStateStore StateStore { get; init; }
    public IProgressSink? ProgressSink { get; init; }
}
```

| Analyser | Purpose | Reads | Writes |
|---|---|---|---|
| `DependencyAnalyser` | Cross-project/org link analysis | `inventory.json` (work items) | `dependencies.csv`, Mermaid diagrams |
| _(future)_ `ProcessDiffAnalyser` | Source vs target process template diff | `export/workitems/fields.json` | `process-diff.json` |
| _(future)_ `ImpactAnalyser` | Blast-radius for a migration scope | `inventory.json` | `impact-report.json` |
| _(future)_ `ComplianceAnalyser` | Pre-migration governance validation | `inventory.json`, `prepare/` artefacts | `compliance-report.json` |

**Contract invariants for `IAnalyser`:**
1. **Never writes to a target system** — only reads/writes artefacts in `IArtefactStore`.
2. **Declares all data dependencies via `DependsOn`** — the plan executor ensures those phases are complete before calling `AnalyseAsync`.
3. **Can depend on any `IModule` phase or another `IAnalyser`**.
4. **Registered in DI alongside `IModule`** — the plan builder discovers both.
5. **Produces at least one non-empty artefact** — a silent zero-output analyser is forbidden (same rule as modules).

---

### DependencyDiscoveryModule → DependencyAnalyser : IAnalyser

**This is the definitive answer:** `DependencyDiscoveryModule` is **eliminated** and replaced by `DependencyAnalyser` which implements `IAnalyser`, not `IModule`.

**Why `IAnalyser` and not `IModule`?**

| Criterion | IModule | IAnalyser | Dependencies verdict |
|---|---|---|---|
| Owns a data domain (work items, identities, …)? | ✓ | ✗ | ✗ — no data domain |
| Writes importable package artefacts? | ✓ | ✗ | ✗ — CSV/diagrams only |
| Imports data to a target? | ✓ | ✗ | ✗ — planning artefact, never imported |
| Reads existing artefacts for analysis? | ✗ | ✓ | ✓ — reads inventory outputs |
| Cross-cutting concern? | ✗ | ✓ | ✓ — links across projects/orgs |

`DependencyAnalyser` declares a dependency on `WorkItemsModule` inventory output:

```csharp
public class DependencyAnalyser : IAnalyser
{
    public string Name => "Dependencies";

    public IReadOnlyList<ModuleDependency> DependsOn =>
    [
        new ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Inventory)
    ];

    public async Task AnalyseAsync(AnalyseContext context, CancellationToken ct)
    {
        // reads context.ArtefactStore → "inventory.json" (produced by WorkItemsModule.InventoryAsync)
        // writes context.ArtefactStore → "analysis/dependencies.csv", "analysis/dependencies.mmd"
    }
}
```

The task ID convention for analysers is: `analyse.dependencies`, `analyse.processdiff`, etc.

---

### DependencyPhase — Extended for IAnalyser

`DependencyPhase` needs new values to cover all phase types, including the new `Analyse` phase used by `IAnalyser → IAnalyser` dependencies:

```csharp
public enum DependencyPhase
{
    Inventory = 0,  // NEW — IModule.InventoryAsync
    Export    = 1,  // existing
    Import    = 2,  // existing
    Both      = 3,  // existing (Export + Import)
    Prepare   = 4,  // NEW — IModule.PrepareAsync
    Analyse   = 5,  // NEW — IAnalyser.AnalyseAsync
}
```

**Cross-type dependency examples:**

```csharp
// IAnalyser depending on an IModule's Inventory output (most common)
new ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Inventory)
// → task ID: "inventory.workitems"

// IAnalyser depending on an IModule's Export output (e.g., ProcessDiffAnalyser needs actual revision data)
new ModuleDependency(typeof(WorkItemsModule), DependencyPhase.Export)
// → task ID: "export.workitems"

// IAnalyser depending on another IAnalyser
new ModuleDependency(typeof(DependencyAnalyser), DependencyPhase.Analyse)
// → task ID: "analyse.dependencies"
```

The name resolution in `ModuleDependency` strips the suffix to get the module key:
- `WorkItemsModule` → `"workitems"` (strip "Module")
- `DependencyAnalyser` → `"dependencies"` (strip "Analyser")

---

### Unified Task List — JobTaskList Extended

Both `IModule` phases and `IAnalyser` phases live in the **same `JobTaskList`**. The `Phase` field (already a string) is extended with new values:

| Phase string | Produced by | Example task IDs |
|---|---|---|
| `"inventory"` | `IModule.InventoryAsync` | `inventory.workitems`, `inventory.nodes` |
| `"export"` | `IModule.ExportAsync` | `export.workitems`, `export.identities` |
| `"prepare"` | `IModule.PrepareAsync` | `prepare.workitems`, `prepare.identities` |
| `"import"` | `IModule.ImportAsync` | `import.workitems`, `import.identities` |
| `"analyse"` | `IAnalyser.AnalyseAsync` | `analyse.dependencies`, `analyse.processdiff` |

`DependsOn` in `JobTask` works across phases and across types — no special handling required. The plan executor already resolves `DependsOn` task IDs to completion status; it doesn't care whether the producing participant was an `IModule` or `IAnalyser`.

---

### JobKind Dispatch — Updated

| JobKind | Phases executed (in order) |
|---|---|
| `Inventory` | `inventory` (all IModules) → `analyse` (IAnalysers whose DependsOn are all inventory) |
| `Export` | `inventory` → `export` |
| `Prepare` | `prepare` |
| `Import` | `import` |
| `Migrate` | `inventory` → `export` → `prepare` → `import` |
| `Dependencies` | `inventory` (WorkItems only) → `analyse` (DependencyAnalyser only) |

---

### InventoryDiscoveryModule — Folded Into the Orchestrator

`InventoryDiscoveryModule` is eliminated entirely. Multi-org inventory is an orchestration concern: the `JobAgentWorker` loops over configured organisations, calling each module's `InventoryAsync` per org. Analysers run once after all org inventory passes complete, reading the aggregated artefacts.

```
JobKind.Inventory + multi-org config:
  for each organisation in config:
    create scoped InventoryContext with this org's endpoint
    for each IModule where SupportsInventory:
      call module.InventoryAsync(context, ct)
  // all inventory tasks complete
  for each IAnalyser whose DependsOn are satisfied:
    call analyser.AnalyseAsync(context, ct)
```

`InventoryModule` (single-source) is also eliminated — its logic moves into `WorkItemsModule.InventoryAsync`.

---

## Updated Impact Analysis

### Types Eliminated

| Type | Replaced by |
|---|---|
| `InventoryModule` | `WorkItemsModule.InventoryAsync` |
| `InventoryDiscoveryModule` | Multi-org loop in `JobAgentWorker` |
| `DependencyDiscoveryModule` | `DependencyAnalyser : IAnalyser` |

### New Types Required

| Type | Assembly | Purpose |
|---|---|---|
| `IAnalyser` | `Abstractions.Agent` | New top-level interface for analysis participants |
| `AnalyseContext` | `Abstractions.Agent` | Context for `AnalyseAsync` — artefact store + state |
| `InventoryContext` | `Abstractions.Agent` | Context for `InventoryAsync` — endpoint + project scope |
| `PrepareContext` | `Abstractions.Agent` | Context for `PrepareAsync` — package + target endpoint |
| `DependencyAnalyser` | `Infrastructure.Agent` | Replaces `DependencyDiscoveryModule`, implements `IAnalyser` |

### Modified Types

| Type | Change |
|---|---|
| `IModule` | Add `InventoryAsync`, `PrepareAsync`, `SupportsInventory`, `SupportsPrepare` |
| `DependencyPhase` | Add `Inventory = 0`, `Prepare = 4`, `Analyse = 5` |
| `ModuleDependency` | Strip "Analyser" suffix in name resolution; add `AppliesToAnalyse` property |
| `JobExecutionPlanBuilder` | Build `inventory`, `prepare`, `analyse` phase tasks; discover `IAnalyser` registrations |
| `JobPlanExecutor` / `IJobPlanExecutor` | Add `ExecuteInventoryPhaseAsync`, `ExecutePreparePhaseAsync`, `ExecuteAnalysePhaseAsync` |
| `JobAgentWorker` | Multi-org loop replaces `InventoryDiscoveryModule`; dispatch to unified phase executor |
| `WorkItemsModule` | Add `InventoryAsync` (from `InventoryModule`), `PrepareAsync` (new), `SupportsPrepare => true` |
| `IdentitiesModule`, `NodesModule`, `TeamsModule` | Add `InventoryAsync` (optional count), `PrepareAsync` |

---

## Summary

| Concept | Before | After |
|---|---|---|
| Inventory | Separate `InventoryModule` abusing `ExportAsync` | `InventoryAsync` action on each `IModule` |
| Multi-org inventory | `InventoryDiscoveryModule` | Orchestrator loop in `JobAgentWorker` |
| Prepare | Documented but missing from `IModule` | `PrepareAsync` on each `IModule` |
| Dependencies | `DependencyDiscoveryModule : IModule` using `ExportAsync` | `DependencyAnalyser : IAnalyser` using `AnalyseAsync` |
| Cross-cutting analysis | Abused module phases | `IAnalyser` interface — clean, purpose-built |
| Phase dispatch | Export + Import only | `inventory` → `export` → `prepare` → `import` → `analyse` |
| Cross-type dependencies | Not supported | `IAnalyser.DependsOn` can reference any `IModule` phase |
| Interface count | 1 (`IModule`) | 2 (`IModule` + `IAnalyser`) |
| Class count | 7 modules (2 inventory abuses) | 4 domain modules + N analysers (clean separation) |

The core principle: **`IModule` owns data. `IAnalyser` reads data and produces insight.** They are first-class, distinct participants in the job execution plan. Both register in DI, both appear in `JobTaskList`, and `DependsOn` works across the boundary.
