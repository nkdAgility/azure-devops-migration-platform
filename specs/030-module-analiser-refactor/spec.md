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

**Independent Test**: Can be fully tested by submitting a `JobKind.Inventory` job with `WorkItemsModule` enabled and no `InventoryModule` configured, and asserting that `inventory.json` is written with non-zero work item and revision counts.

**Acceptance Scenarios**:

1. **Given** a job of kind `Inventory` with `WorkItemsModule`, `TeamsModule`, `NodesModule`, and `IdentitiesModule` enabled, **When** the job executes, **Then** each module's inventory contribution is written to the package and a complete `inventory.json` is present with non-zero counts for each domain.
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

---

### User Story 4 — Run Dependency Analysis as a Distinct Analysis Operation (Priority: P3)

An operator planning a cross-project migration wants to run `queue dependencies` to produce a dependency map (`dependencies.csv`) of linked work items across projects and organisations — without that operation being treated as an export. The result is a planning artefact, never imported.

**Why this priority**: `DependencyDiscoveryModule` currently abuses `ExportAsync`. Promoting it to a first-class `IAnalyser` makes its purpose explicit and opens the door to future analysis operations (process diff, blast radius, compliance).

**Independent Test**: Can be tested by submitting a `JobKind.Dependencies` job and asserting that `analysis/dependencies.csv` is written with at least one row.

**Acceptance Scenarios**:

1. **Given** a `JobKind.Dependencies` job with `WorkItemsModule` enabled, **When** the job executes, **Then** `WorkItemsModule.InventoryAsync` runs first and `DependencyAnalyser.AnalyseAsync` runs after, producing `analysis/dependencies.csv` and `analysis/dependencies.mmd`.
2. **Given** the inventory artefacts are already present in the package, **When** a `JobKind.Dependencies` job runs, **Then** the inventory phase is skipped (checkpoint present) and analysis runs directly against existing artefacts.
3. **Given** a `JobKind.Inventory` job with `DependencyAnalyser` registered, **When** the job executes, **Then** `AnalyseAsync` runs after all `InventoryAsync` calls complete, and the analysis artefacts are written.

---

### Edge Cases

- What happens when a module reports `SupportsInventory = false` but a `JobKind.Inventory` job is submitted with that module enabled? → Module is skipped for the inventory phase without error; its `InventoryAsync` is not called.
- What happens when `PrepareAsync` finds a blocking issue (e.g., required field mapping is entirely absent)? → If `blockOnUnresolved` is `true`, the job fails with a structured error and the Import phase does not start.
- What happens when a multi-org inventory job has one org with zero work items? → The module MUST emit a structured `Warning` log (silent zero-count completion is forbidden) and the aggregate inventory still completes.
- What happens when an `IAnalyser` declares a `DependsOn` on a module phase that was not executed in the current job plan? → The plan builder detects the unsatisfied dependency and fails the job at plan-build time with a descriptive error.
- What happens when the `IModule` contract is extended but an existing module does not implement the new methods? → The abstract base class (`ModuleBase`) provides default `throw new NotSupportedException()` implementations; the `Supports*` properties default to `false` so unsupported phases are never called.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST dispatch `JobKind.Inventory` to an `InventoryAsync` method on each enabled `IModule` where `SupportsInventory` is `true`, NOT to `ExportAsync`.
- **FR-002**: The platform MUST dispatch `JobKind.Prepare` to a `PrepareAsync` method on each enabled `IModule` where `SupportsPrepare` is `true`.
- **FR-003**: Each `IModule` implementation MUST declare whether it supports each phase via `SupportsInventory`, `SupportsExport`, `SupportsPrepare`, and `SupportsImport` boolean properties.
- **FR-004**: `WorkItemsModule` MUST implement `InventoryAsync` and produce the same `inventory.csv` and `inventory.json` artefacts that `InventoryModule.ExportAsync` currently produces.
- **FR-005**: `IdentitiesModule`, `NodesModule`, and `TeamsModule` MUST implement `InventoryAsync` with optional count contributions to the shared inventory artefact; each may produce a count of zero if no domain data is in scope.
- **FR-006**: Multi-organisation inventory MUST be handled by the job orchestrator looping over configured source endpoints and calling each module's `InventoryAsync` per endpoint — NOT by a dedicated `InventoryDiscoveryModule`.
- **FR-007**: `InventoryModule` and `InventoryDiscoveryModule` MUST be eliminated as standalone classes; their behaviour MUST be absorbed into `WorkItemsModule.InventoryAsync` and the job orchestrator respectively.
- **FR-008**: A new `IAnalyser` interface MUST be introduced in `DevOpsMigrationPlatform.Abstractions.Agent` for participants that read artefacts and produce analysis outputs but never write to a target system.
- **FR-009**: `DependencyDiscoveryModule` MUST be eliminated and replaced by `DependencyAnalyser` which implements `IAnalyser`, not `IModule`.
- **FR-010**: `DependencyAnalyser.AnalyseAsync` MUST declare a dependency on `WorkItemsModule` inventory output via a `DependsOn` entry so the plan builder can enforce ordering.
- **FR-011**: The job execution plan builder MUST discover both `IModule` and `IAnalyser` registrations and include both in the `JobTaskList` with correct phase labels (`inventory`, `export`, `prepare`, `import`, `analyse`).
- **FR-012**: `DependencyPhase` MUST be extended to include `Inventory`, `Prepare`, and `Analyse` values so cross-type dependencies can be declared.
- **FR-013**: `JobKind.Dependencies` MUST dispatch via `inventory` (WorkItems only) → `analyse` (DependencyAnalyser only), not via the export phase.
- **FR-014**: The `JobKind.Migrate` pipeline MUST execute phases in order: `inventory` → `export` → `prepare` → `import` → `validate`.
- **FR-015**: Each module's `PrepareAsync` MUST write a `{Module}/prepare-report.json` to `IArtefactStore` containing at minimum: resolved items count, unresolved items count, and a list of unresolved items.
- **FR-016**: All new phase methods (`InventoryAsync`, `PrepareAsync`) MUST be observable: O-1 activity spans, O-2 metrics, O-3 structured logging, and O-4 progress events (per guardrail 25).
- **FR-017**: The `IAnalyser` contract MUST enforce that `AnalyseAsync` never writes to a target system — only reads and writes artefacts via `IArtefactStore`.
- **FR-018**: An `IAnalyser` that completes with zero artefacts written MUST emit a structured `Warning` log (same rule as modules — silent zero-output is forbidden).
- **FR-019**: Both `IModule` and `IAnalyser` MUST be registered in DI and participate in the same `JobTaskList`; `DependsOn` references MUST work across the `IModule`/`IAnalyser` boundary.
- **FR-020**: The existing `ValidateAsync` signature on `IModule` MUST remain unchanged.

### Key Entities

- **`IModule`**: The domain-data-owning participant in a migration job. After this change it exposes five phase methods: `InventoryAsync`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`. Four boolean capability flags indicate which phases the module supports.
- **`IAnalyser`**: A new cross-cutting analysis participant. Exposes one method — `AnalyseAsync` — and a `DependsOn` list. Never writes to a target. Registered in DI alongside `IModule`.
- **`InventoryContext`**: Context record passed to `InventoryAsync`. Carries the scoped source endpoint, artefact store, state store, progress sink, and job reference.
- **`PrepareContext`**: Context record passed to `PrepareAsync`. Carries the package (source), target endpoint, artefact store, state store, progress sink, and job reference.
- **`AnalyseContext`**: Context record passed to `IAnalyser.AnalyseAsync`. Carries artefact store (read/write), state store, progress sink, and job reference. No source or target endpoint.
- **`DependencyPhase`**: Enum extended with `Inventory` (0), `Prepare` (4), and `Analyse` (5) values.
- **`JobTaskList`**: Unchanged structure, but now includes tasks with phase labels `"inventory"`, `"prepare"`, and `"analyse"` alongside existing `"export"` and `"import"`.
- **`DependencyAnalyser`**: First `IAnalyser` implementation. Replaces `DependencyDiscoveryModule`. Reads `inventory.json` and writes `analysis/dependencies.csv` and `analysis/dependencies.mmd`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: All existing `JobKind.Export`, `JobKind.Import`, `JobKind.Migrate`, and `JobKind.Dependencies` jobs produce identical artefact output before and after the refactor — zero regression in artefact content.
- **SC-002**: `JobKind.Inventory` jobs complete successfully without `InventoryModule` or `InventoryDiscoveryModule` present in the config — the module list is reduced by 2 classes.
- **SC-003**: `JobKind.Prepare` jobs complete end-to-end and produce a `prepare-report.json` in each enabled module's package folder.
- **SC-004**: Multi-organisation inventory (2+ source endpoints) completes with a single config change (no special module required) and produces the same aggregate inventory artefacts as the previous `InventoryDiscoveryModule`-based approach.
- **SC-005**: The codebase module count is reduced from 7 (`InventoryModule`, `InventoryDiscoveryModule`, `DependencyDiscoveryModule`, `WorkItemsModule`, `IdentitiesModule`, `NodesModule`, `TeamsModule`) to 4 domain modules plus N analysers.
- **SC-006**: All simulated-connector system tests for inventory, export, prepare, import, and dependency analysis pass with assertions on artefact content (non-empty, correct structure) — not just "no exception thrown".
- **SC-007**: The `DependencyAnalyser` produces `analysis/dependencies.csv` with at least one row when at least one linked work item pair exists in the inventory artefacts.
- **SC-008**: Every new `InventoryAsync` and `PrepareAsync` phase emits observable telemetry: at least one activity span, one metric increment, and one structured log entry per module invocation.

---

## Assumptions

- The spec is grounded in `docs/modules.md`, `docs/architecture.md`, and `analysis/draftspec-Module-refactor-consolidation.md`. The draft spec was authored by the project owner and represents a finalised design decision.
- `docs/modules.md` describes `PrepareAsync` as part of the `IModule` contract (line 17) but the actual interface does not implement it — this spec closes that gap.
- The abstract base class (`ModuleBase`) pattern is preferred over pure interface-only to avoid forcing every module to implement all five methods; `Supports*` properties default to `false`.
- Incremental migration is assumed: the refactor proceeds in five phases (add methods with defaults → move WorkItems inventory → multi-org orchestrator → implement PrepareAsync → clean up), maintaining backward compatibility at each step.
- The `ValidateAsync` method signature remains unchanged; no `ValidateInventoryAsync` or `ValidateExportAsync` variants are introduced.
- `InventoryAsync` per-module writes count contributions to a shared `inventory.json`; each module appends or merges its domain counts rather than owning a separate file.
- The `IAnalyser` interface is placed in `DevOpsMigrationPlatform.Abstractions.Agent` (same assembly as `IModule`) per the project reference boundary rules (guardrail 20a).
- `JobKind.Dependencies` remains a valid `JobKind` value and continues to dispatch as `inventory (WorkItems only) → analyse (DependencyAnalyser only)`.
- Docs read: `docs/modules.md`, `docs/architecture.md`, `.agents/guardrails/system-architecture.md`, `.agents/guardrails/module-template.md`. Gaps found — see `discrepancies.md`.
