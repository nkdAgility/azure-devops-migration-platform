# ADR 0027 — Real Teams/Nodes Prepare Validation and Module-Only Dependency Targets

## Status

Accepted

Executes architecture-audit items **MC-L1** and **MC-L2** as one Class C change under explicit operator consent. For MC-L1 the operator ruled: **implement real Prepare validation** (not `SupportsPrepare = false`).

## Context

1. **MC-L1** — `TeamsModule` and `NodesModule` declared `SupportsPrepare = true`, but their Prepare path (delegated to `TeamsOrchestrator.PrepareAsync` / `NodesOrchestrator.PrepareAsync`) wrote a hardcoded empty `PrepareReport` (`ResolvedCount = 0`, no findings). `module-model.md` defines `PrepareAsync` as "validates target"; a fixed zero-count report satisfied the phase in name only, and consumers of `prepare-report.json` received a report that always passed.
2. **MC-L2** — `WorkItemsModule.DependsOn` declared `new ModuleDependency(typeof(InventoryAnalyser), DependencyPhase.Import)`. `InventoryAnalyser` is an `IAnalyser`, not an `IModule`; `IModule.DependsOn` is documented as "Modules this one depends on". Encoding an analyser as an Import-phase module dependency blurred the extension-point taxonomy: the plan builder silently dropped it from import task dependencies (not a module), while the export-prerequisite pass happened to pick it up phase-agnostically.

## Decision

### MC-L1 — Prepare validates the package, and the package is connector-neutral

Prepare-phase validation is **evidence-based validation of the exported package artefacts** (the filesystem package is the source of truth, ADR-0002). Because Simulated, AzureDevOpsServices, and TFS connectors all write the same package format, validating the package format covers all three connectors without connector probes. Findings use the same shapes and severities as the WorkItems Prepare path: `UnresolvedItem` (`Warning`/`Blocking`) and `ArtefactFinding` in the module's `prepare-report.json`; a new `ArtefactFindingType.ModuleArtefact` value (additive) identifies module-level artefacts.

**Nodes** (`NodesOrchestrator.BuildPrepareReportAsync`) validates `Nodes/source-tree.json`:

- artefact present (missing → Blocking + `Missing` artefact finding) and parseable (malformed → Blocking + `Invalid`)
- node paths well-formed — empty/whitespace area or iteration path → Blocking
- duplicate area/iteration paths (case-insensitive) → Warning
- iteration date sanity — `StartDate > FinishDate` where both present → Warning
- `ResolvedCount` = number of well-formed, distinct nodes

**Teams** (`TeamsOrchestrator.BuildPrepareReportAsync`) validates the per-team artefacts under `Teams/{slug}/`:

- `team.json` parseable with a required `definition` object (malformed/missing definition → Blocking + `Invalid`)
- duplicate team `definition.id` / `definition.name` across slugs (case-insensitive) → Warning
- split artefacts (`settings.json`, `iterations.json`, `members.json`, `capacity.json`, `area-paths.json`, `board-config.json`) parseable where present (malformed → Blocking + `Invalid`)
- `board-config.json` boards declaring no column states → Warning
- cross-module reference check: member descriptors in `members.json` are matched against the Identities export (`Identities/descriptors.jsonl`); unmatched descriptors → Warning; the check is skipped when the Identities export is absent
- no `team.json` found at all → Warning
- `ResolvedCount` = number of valid teams

Prepare remains report-producing, not gating: the task completes and findings land in `prepare-report.json` (and the existing `RecordPrepare{Nodes,Teams}{Resolved,Unresolved}` metrics now carry real counts). The module wrappers, progress-event shapes, and console output are unchanged.

### MC-L2 — ModuleDependency targets are constrained by phase

`ModuleDependency` now validates its target type at construction:

- Module phases (`Inventory`, `Export`, `Import`, `Both`, `Prepare`) must target `IModule` implementations.
- `Analyse` is the dedicated analyser-ordering mechanism and must target `IAnalyser` implementations (this is how `DependencyAnalyser → InventoryAnalyser` was already expressed, and how the plan builder queues `analyse.*` tasks ahead of Prepare).

`WorkItemsModule` re-expresses its `InventoryAnalyser` ordering through that mechanism: `DependencyPhase.Import` → `DependencyPhase.Analyse`. Effects:

- The export-prerequisite pass still queues `analyse.inventory` before `export.workitems` (it matches analyser dependencies phase-agnostically) — the existing plan contract holds.
- Import task dependencies are now module-only by construction (previously the analyser entry was silently dropped with a warning).
- Prepare plans now include `analyse.inventory`, and `prepare.workitems` depends on it — the WorkItems Prepare readiness checks read consolidated inventory, so running the analyser first is the intended ordering that the Import-phase declaration failed to express.

## Contract Tests

- `Prepare/NodesPrepareValidationTests` — missing/malformed source-tree, empty node path, duplicate paths, inverted iteration dates, and the valid-package happy path (RED against the stub, GREEN against the implementation).
- `Prepare/TeamsPrepareValidationTests` — malformed `team.json`, missing `definition`, duplicate team names, malformed split artefact, empty board columns, member-descriptor cross-check against the Identities export, and the valid-package happy path.
- `Modules/ModuleDependencyContractTests` — phase-target constraint (module phases require `IModule`, Analyse requires `IAnalyser`) and the `WorkItemsModule` dependency shape (module-only imports; `Inventory` via Analyse).

## Alternatives Considered

- **`SupportsPrepare = false` for Teams/Nodes** — rejected by operator ruling; it narrows the declared phase surface and removes the phase instead of making it honest.
- **Connectivity/target probes in Prepare** — rejected: Prepare validates the package (source of truth), keeping the phase deterministic and connector-neutral; live-target checks belong to connector capabilities and the Validate phase.
- **Constraining `ModuleDependency` to `IModule` only (including Analyse)** — rejected: analyser ordering (`DependencyAnalyser → InventoryAnalyser`, module → analyser prepare prerequisites) is a legitimate, already-mechanised use; phase-aware constraint keeps one dependency record while restoring the taxonomy.
- **A separate `AnalyserDependency` type and new `IModule`/`IAnalyser` members** — rejected: `IModule` is consumed on net481 where default interface members are unavailable, so every implementation and test fake would churn for no additional safety over the phase-aware constraint.
