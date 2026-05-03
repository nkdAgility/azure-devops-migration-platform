# Draft Spec: Module Refactor — Consolidate Phases into IModule

**Status**: Draft  
**Date**: 2026-05-03  
**Author**: MartinHinshelwoodNKD + Copilot  

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

### 5. Dependencies Module — Stays as-is or Becomes a Phase

`DependencyDiscoveryModule` analyses cross-project links. This is a cross-cutting analysis, not a per-module concern. Two options:

**Option A: Keep as a separate module** — `DependencyDiscoveryModule` implements `SupportsExport => true` and runs its analysis during the Export phase. This is the current behaviour and works fine.

**Option B: Make it a pre-export analysis step** — Run before modules, not as a module. This is cleaner semantically but adds another dispatch mechanism.

**Recommendation: Option A** — keep it as a module. It already works, follows the pattern, and the `DependsOn` graph handles ordering.

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
| `DependencyDiscoveryModule` | Unchanged (continues as Export-phase module) |

### Module Phase Support Matrix (Post-Refactor)

| Module | Inventory | Export | Prepare | Import | Validate |
|---|---|---|---|---|---|
| `IdentitiesModule` | ○ | ✓ | ✓ | ✓ | ✓ |
| `NodesModule` | ○ | ✓ | ✓ | ✓ | ✓ |
| `TeamsModule` | ○ | ✓ | ✓ | ✓ | ✓ |
| `WorkItemsModule` | ✓ | ✓ | ✓ | ✓ | ✓ |
| `DependencyDiscoveryModule` | — | ✓ | — | — | — |

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

3. **Should `DependencyDiscoveryModule` get `SupportsInventory => true` and move its logic to `InventoryAsync`?** Dependency analysis is conceptually an inventory/discovery operation. Moving it would mean `JobKind.Dependencies` dispatches via the inventory phase.

4. **Base class vs interface-only?** An abstract `ModuleBase` class could provide default `NotSupportedException` implementations for unsupported phases. This avoids forcing every module to implement all five methods. However, the current design is interface-only — introducing a base class is a style decision.

5. **What about `ValidateAsync` — should it be phase-specific?** Currently there's one `ValidateAsync`. Should there be `ValidateExportAsync` and `ValidateImportAsync`? Or is one generic validation sufficient?

---

## Summary

| Concept | Before | After |
|---|---|---|
| Inventory | Separate module (`InventoryModule`) abusing `ExportAsync` | Action on modules: `InventoryAsync` |
| Multi-org inventory | Separate module (`InventoryDiscoveryModule`) | Orchestrator loop calling `InventoryAsync` per org |
| Prepare | Documented but not implemented on `IModule` | `PrepareAsync` on each module |
| Phase dispatch | Export + Import only | Inventory → Export → Prepare → Import |
| Module count | 7 (including 2 inventory modules) | 5 (each module handles its own inventory) |

The core insight: **Inventory, Export, Prepare, and Import are actions performed against modules — not module types.** This aligns the `IModule` contract with the `JobKind` enum and eliminates the semantic mismatch where inventory modules pretend to be export modules.
