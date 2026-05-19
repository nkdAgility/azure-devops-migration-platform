# Implementation Plan: IdentitiesModule, NodeStructureModule & TeamsModule

**Branch**: `024-teams-module` | **Date**: 2026-04-27 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/024-teams-module/spec.md`

## Summary

Implement three prerequisite modules for the migration platform:

1. **IdentitiesModule** — Export identity descriptors from source, build cross-cutting `IIdentityMappingService` for all downstream modules. Owns `Identities/` folder.
2. **NodeStructureModule** — Extract existing export/import code (`ClassificationTreeCapture`, `NodeEnsurer`, `NodeStructureValidator`) into a proper `IModule`. Rename `INodeStructureTool` → `INodeTranslationTool`. Owns `Nodes/` folder.
3. **TeamsModule** — Export/import team definitions, settings, iterations, members, capacity, and area paths. Owns `Teams/` folder. Uses `IIdentityMappingService` (members) and `INodeTranslationTool` (path resolution).

Plus two module extensions:
- **WorkItems NodeStructure Extension** — Records `System.AreaPath`/`System.IterationPath` from every revision to `ReferencedPathTracker` during export; translates paths via `INodeTranslationTool` during import.
- **Teams NodeStructure Extension** — Records team-assigned area/iteration paths to `ReferencedPathTracker` during export; translates paths during import.

All three connectors (Simulated, AzureDevOpsServices, TeamFoundationServer) must be fully implemented.

## Reconciliation Status (2026-05-17)

- Task truth is now canonical in `tasks.md` with normalized statuses.
- Remaining incomplete execution evidence: `T084`, `T085`, `T096`.
- Part of this plan is historically superseded by later specs:
  - `specs/030-module-analiser-refactor` (Nodes naming and module lifecycle evolution)
  - `specs/032-icapture-interface` (capture contract terminology)
  - `specs/035-workitem-import-support` (prepare/import readiness orchestration details)
- Legacy path references to `DevOpsMigrationPlatform.CLI.TfsExport` are reconciled to `Infrastructure.TfsObjectModel` implementations in current code.

## Technical Context

**Language/Version**: C# 10+, .NET 10 (TFS subprocess: .NET 4.8)
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, `System.Diagnostics` (OTel), Azure DevOps REST SDK, TFS Object Model (subprocess bridge)
**Storage**: On-disk package via `IArtefactStore` + `IStateStore` (cursor-based checkpointing)
**Testing**: Reqnroll.MSTest + Moq (`MockBehavior.Strict`)
**Target Platform**: Windows/Linux (.NET 10 agent, .NET 4.8 TFS subprocess on Windows only)
**Project Type**: Library (modules + connectors) within modular monolith
**Performance Goals**: Streaming — no in-memory collection of all items; bounded memory
**Constraints**: <200ms per identity resolution; <500ms per node creation; <1s per team export
**Scale/Scope**: Up to 10,000 identities, 500 nodes, 100 teams per project

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** All files in `/.agents/20-guardrails/`, `/.agents/30-context/`, and relevant `/docs/` files have been read.

- [x] **Package-First (I):** All modules write exclusively through `IArtefactStore`. No direct source→target migration. IdentitiesModule writes to `Identities/`, NodeStructureModule writes to `Nodes/`, TeamsModule writes to `Teams/`.
- [x] **Streaming (II):** IdentitiesModule streams descriptors one at a time. TeamsModule processes teams one at a time. NodeStructure tree is bounded (<500 nodes typical). No in-memory sort of `EnumerateAsync`.
- [x] **WorkItems Layout (III):** This spec does not modify WorkItems folder structure. The WorkItems NodeStructure extension operates on existing revision data without altering layout.
- [x] **Checkpointing (IV):** Each module defines its own cursor: `identities.cursor.json`, `nodes.cursor.json`, `teams.cursor.json`. All under `.migration/Checkpoints/`. No watermark tables.
- [x] **Module Isolation (V):** All persistence through `IArtefactStore`/`IStateStore`. Identity mapping via `IIdentityMappingService`. No concrete store references in module code.
- [x] **Separation of Planes (VI):** All module logic in `Infrastructure.Agent`. TFS via subprocess bridge. No migration logic in control plane or TUI.
- [x] **Determinism (VII):** Same inputs → same package layout. Schema version `"1.0"` for all three modules. Team slugs are deterministic (same name → same slug).
- [x] **ATDD-First (VIII):** 8 user stories with 30+ acceptance scenarios in spec.md. Each scenario → one ATDD session → one commit.
- [x] **SOLID & DI (IX):** All modules receive dependencies via constructor injection. Options classes (`IdentitiesModuleOptions`, `NodeStructureModuleOptions`, `TeamsModuleOptions`) are sealed with `init`-only properties and `SectionName` constants. Interfaces in `Abstractions.Agent`. Registration via `Add*Module()` and `Add*Services()` extension methods.

## Project Structure

### Documentation (this feature)

```text
specs/024-teams-module/
├── plan.md              # This file
├── spec.md              # Feature specification
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output (interface contracts)
├── checklists/          # Quality checklists
│   └── requirements.md
├── discrepancies.md     # Architecture discrepancies
└── tasks.md             # Phase 2 output (/speckit.tasks command)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions.Agent/
│   ├── Identity/
│   │   └── IIdentityMappingService.cs              # Existing — no changes
│   ├── Modules/
│   │   ├── IModule.cs                               # Existing — no changes
│   │   ├── IdentitiesModuleOptions.cs               # NEW: sealed options class
│   │   ├── NodeStructureModuleOptions.cs             # NEW: sealed options class (wraps existing NodeStructureOptions)
│   │   └── TeamsModuleOptions.cs                     # NEW: sealed options class
│   └── Tools/
│       ├── INodeTranslationTool.cs                   # RENAMED from INodeStructureTool.cs
│       ├── INodeCreator.cs                           # Existing — no changes
│       ├── IClassificationTreeReader.cs              # Existing — no changes
│       ├── INodeStructureValidator.cs                # Existing — no changes
│       ├── ITeamSource.cs                            # NEW: connector abstraction for reading teams
│       ├── ITeamTarget.cs                            # NEW: connector abstraction for writing teams
│       └── IIdentitySource.cs                        # NEW: connector abstraction for reading identities
│
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   ├── Modules/
│   │   ├── WorkItemsModule.cs                        # Existing — add NodeStructure extension hook
│   │   ├── IdentitiesModule.cs                       # NEW: IModule implementation
│   │   ├── NodeStructureModule.cs                    # NEW: IModule (extracted from existing Tool code)
│   │   ├── TeamsModule.cs                            # NEW: IModule implementation
│   │   └── ModuleServiceCollectionExtensions.cs      # Existing — add new module registrations
│   ├── Tools/
│   │   └── NodeStructure/
│   │       ├── NodeStructureTool.cs                  # RENAMED class to NodeTranslationTool
│   │       ├── ClassificationTreeCapture.cs          # Existing — called by NodeStructureModule
│   │       ├── NodeEnsurer.cs                        # Existing — called by NodeStructureModule
│   │       ├── ReferencedPathTracker.cs              # Existing — shared singleton
│   │       ├── NodeStructureValidator.cs             # Existing — called by NodeStructureModule
│   │       └── NodeStructureToolServiceCollectionExtensions.cs  # Update registrations
│   ├── Identity/
│   │   ├── IdentityMappingService.cs                 # NEW: full implementation (replaces PassThrough)
│   │   └── IdentityServiceCollectionExtensions.cs    # NEW: DI registration
│   └── Teams/
│       ├── TeamExportOrchestrator.cs                 # NEW: export orchestration
│       ├── TeamImportOrchestrator.cs                 # NEW: import orchestration
│       ├── TeamSlugGenerator.cs                      # NEW: filesystem-safe name sanitisation
│       └── TeamsServiceCollectionExtensions.cs        # NEW: DI registration
│
├── DevOpsMigrationPlatform.Infrastructure.Simulated/
│   ├── SimulatedIdentitySource.cs                    # NEW: deterministic identity generation
│   ├── SimulatedTeamSource.cs                        # NEW: deterministic team generation
│   ├── SimulatedTeamTarget.cs                        # NEW: in-memory team target
│   └── SimulatedServiceCollectionExtensions.cs       # Update: add identity/team registrations
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   ├── AzureDevOpsIdentitySource.cs                  # NEW: Graph/Identity REST API
│   ├── AzureDevOpsTeamSource.cs                      # NEW: Teams REST API
│   ├── AzureDevOpsTeamTarget.cs                      # NEW: Teams REST API (write)
│   └── ExportServiceCollectionExtensions.cs          # Update: add identity/team registrations
│
├── DevOpsMigrationPlatform.TfsMigrationAgent/
│   └── (TFS subprocess bridge commands for identity + teams)
│
└── DevOpsMigrationPlatform.CLI.TfsExport/            # .NET 4.8
    ├── TfsIdentityExportHandler.cs                   # NEW: TFS Identity Service OM
    ├── TfsTeamExportHandler.cs                       # NEW: TFS TfsTeamService OM
    └── TfsNodeStructureExportHandler.cs              # Existing — verify/update

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
│   ├── Modules/
│   │   ├── IdentitiesModuleTests.cs                  # NEW
│   │   ├── NodeStructureModuleTests.cs               # NEW
│   │   └── TeamsModuleTests.cs                       # NEW
│   └── Tools/NodeStructure/
│       └── NodeTranslationToolTests.cs               # RENAMED from NodeStructureToolTests.cs
│
├── DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/
│   ├── SimulatedIdentitySourceTests.cs               # NEW
│   ├── SimulatedTeamSourceTests.cs                   # NEW
│   └── SimulatedTeamTargetTests.cs                   # NEW
│
└── features/
    ├── export/
    │   ├── identities/                               # NEW feature files
    │   ├── nodes/                                    # NEW feature files
    │   └── teams/                                    # NEW feature files
    └── import/
        ├── identities/                               # NEW feature files
        ├── nodes/                                    # NEW feature files
        └── teams/                                    # NEW feature files
```

**Structure Decision**: Follows the existing modular monolith pattern. New modules are added to `Infrastructure.Agent/Modules/`. Connector-specific implementations go in their respective `Infrastructure.*` projects. Abstractions (interfaces, DTOs, options) go in `Abstractions.Agent`. This matches the established `WorkItemsModule` pattern exactly.

## Design Decisions

### DD-1: INodeStructureTool → INodeTranslationTool Rename

The existing `INodeStructureTool` interface and `NodeStructureTool` class are renamed to `INodeTranslationTool` and `NodeTranslationTool` respectively. This is a breaking change for consumers but is scoped to this branch. The rename is necessary because the new `NodeStructureModule` owns the "node structure" domain — keeping the tool named `NodeStructureTool` creates confusion about which component does what.

**Impact**: All references to `INodeStructureTool` must be updated. The DI registration key changes. The configuration section remains `MigrationPlatform:Tools:NodeStructure` (the tool's config has not changed — only the code name).

### DD-2: NodeStructureModule Extraction Strategy

The module is a thin orchestrator that delegates to existing classes:
- `ExportAsync` → calls `ClassificationTreeCapture.CaptureAsync()`
- `ImportAsync` → calls `NodeEnsurer.ReplicateSourceTreeAsync()` and/or `NodeEnsurer.EnsureReferencedPathsAsync()`
- `ValidateAsync` → calls `NodeStructureValidator.ValidateAsync()`

No new node-processing logic is written. The module adds `IModule` lifecycle (cursor, telemetry) around existing code. There is no `DependsOn` property — module execution order is controlled entirely by the operator via configuration.

### DD-3: IdentitiesModule — Full vs Pass-Through

The existing `PassThroughIdentityMappingService` is replaced by a full `IdentityMappingService` that:
1. Loads `mapping.json` (explicit overrides)
2. Loads `descriptors.jsonl` (source identities)
3. Performs automatic UPN/display name matching against target directory
4. Falls back to configured default identity
5. Records unresolved identities to `unresolved.json`

The `PassThroughIdentityMappingService` is kept as the Simulated connector's identity service (returns source identity unchanged, with optional seed-based mapping).

### DD-4: TeamsModule Extension Architecture

Each extension (TeamSettings, TeamIterations, TeamMembers, TeamCapacity, NodeStructure) is a strategy object invoked by the module orchestrator. Extensions are independently enabled/disabled via `TeamsModuleOptions.Extensions`. The execution order is fixed and hardcoded in the module implementation: TeamSettings → NodeStructure → TeamIterations → TeamMembers → TeamCapacity. The operator does not configure extension order.

### DD-4b: No DependsOn Property

Modules do not declare `DependsOn`. Module execution order is controlled entirely by the operator via configuration. The `IModule` contract does not include a `DependsOn` property. This decision applies to all modules (IdentitiesModule, NodeStructureModule, TeamsModule). The operator is trusted to order modules correctly (e.g., Identities before Teams, all exports before any imports).

### DD-4c: Migration Lifecycle — Export → Prepare → Import

The migration follows a three-phase lifecycle:
1. **Export** — Capture source data into the package.
2. **Prepare** — Run target-side discovery and validation (resolve identities, verify target project, check node compatibility). The CLI `prepare` command sends a job to the Migration Agent. The operator reviews results (e.g., `unresolved.json`) before proceeding.
3. **Import** — Apply package data to the target. If `prepare` has not been run, `import` auto-runs prepare and exits on any validation failure.

### DD-5: Connector Abstractions

New connector-facing interfaces:
- `IIdentitySource` — enumerate identity descriptors from source (Simulated / ADO / TFS)
- `ITeamSource` — enumerate teams + settings + members + capacity from source
- `ITeamTarget` — create/update teams + settings + members + capacity on target

These follow the existing `IClassificationTreeReader`/`INodeCreator` composite-factory pattern using `CompositeTeamSource`/`CompositeTeamTarget` dispatchers keyed by endpoint `Type`.

### DD-6: ReferencedPathTracker Flush Lifecycle

`ReferencedPathTracker` is a shared singleton service. Each module's export extension calls `RecordAreaPathAsync()`/`RecordIterationPathAsync()` which immediately persists (append-style) to `Nodes/referenced-paths.json`. By architectural constraint, all exports complete before any imports begin — so by the time `NodeStructureModule.ImportAsync` reads `referenced-paths.json`, it contains all paths from all modules.

## Phases

### Phase Mapping (plan.md → tasks.md)

| Plan Phase | Tasks Phase | Description |
|------------|-------------|-------------|
| Phase 0 | Phase 0 (tasks T000a–T000f) | Research & Unknowns |
| Phase 1 | Phase 1 (tasks T001–T006) | Design & Contracts / Setup |
| Phase 1.5 | Phase 2 (tasks T007–T012) | Prerequisite Refactoring |
| Phase 2 — Implementation | Phases 3–10 (tasks T013–T085) | User Stories + Connectors |
| Phase 3 — Validation | Phases 11–12 (tasks T086–T100+) | Docs + Polish |

### Phase 0 — Research & Unknowns Resolution

Resolve any remaining unknowns before design:

1. **TFS Identity API surface** — Confirm which TFS OM APIs are available for identity enumeration. Verify `IIdentityManagementService2.ReadIdentities()` works for both users and groups.
2. **TFS Teams API surface** — Confirm `TfsTeamService` API availability: `QueryTeams()`, `GetTeamMembers()`, team settings access. Document any version-specific limitations.
3. **ADO Teams REST API endpoints** — Confirm the REST API surface for teams: `_apis/projects/{project}/teams`, `_apis/work/teamsettings`, `_apis/work/teamsettings/iterations`, `_apis/work/teamsettings/iterations/{id}/capacities`, `_apis/projects/{project}/teams/{team}/members`.
4. **ADO Identity REST API** — Confirm Graph API or Identity Picker API for enumerating all project identities.
5. **Default team detection** — Research how to programmatically identify the project's default team (ADO: `isTeamDefault` flag; TFS: `TfsTeamService` default team property).
6. **Team area paths API** — Confirm REST API for team area path assignments (`_apis/work/teamsettings/teamfieldvalues`).

**Output**: `specs/024-teams-module/research.md`

### Phase 1 — Design & Contracts

Design the data model, interfaces, and integration points:

1. **Data model** — Define JSON schemas for:
   - `Identities/descriptors.jsonl` entry format
   - `Identities/mapping.json` format
   - `Identities/unresolved.json` format
   - `Teams/{team-slug}/team.json` full schema (settings, iterations, members, capacity, area paths)
   - `Nodes/source-tree.json` (confirm existing schema)
   - `Nodes/referenced-paths.json` (confirm existing schema)
   - Cursor schemas: `identities.cursor.json`, `nodes.cursor.json`, `teams.cursor.json`

2. **Interface contracts** — Define signatures for:
   - `IIdentitySource` (export-side identity enumeration)
   - `ITeamSource` (export-side team enumeration with settings/iterations/members/capacity)
   - `ITeamTarget` (import-side team creation with settings/iterations/members/capacity)
   - `INodeTranslationTool` (rename from `INodeStructureTool` — confirm signature unchanged)
   - Module options: `IdentitiesModuleOptions`, `NodeStructureModuleOptions`, `TeamsModuleOptions`

3. **DI registration plan** — Map out all `Add*` extension methods:
   - `AddIdentitiesModule()` — module + options
   - `AddNodeStructureModule()` — module + options (wraps existing tool registration)
   - `AddTeamsModule()` — module + options
   - `AddSimulatedIdentityServices()`, `AddAzureDevOpsIdentityServices()`, `AddTfsIdentityServices()`
   - `AddSimulatedTeamServices()`, `AddAzureDevOpsTeamServices()`, `AddTfsTeamServices()`

4. **Configuration schema** — Define the JSON configuration sections:
   - `MigrationPlatform:Modules:Identities`
   - `MigrationPlatform:Modules:Nodes`
   - `MigrationPlatform:Modules:Teams`

**Output**: `specs/024-teams-module/data-model.md`, `specs/024-teams-module/contracts/`, `specs/024-teams-module/quickstart.md`

### Phase 1.5 — Prerequisite Refactoring

The architecture review (see `architecture-review.md`) identified missing abstractions that must exist before the new modules can be properly implemented. These are not new features — they extract interfaces from existing concrete types.

| # | Refactoring | Files Affected | Violations Resolved |
|---|-------------|----------------|---------------------|
| R1 | Create `IReferencedPathTracker` interface in Abstractions.Agent/Tools/ | ReferencedPathTracker.cs, WorkItemExportOrchestrator.cs, WorkItemsModule.cs, DI extensions | MM-C3, VS-C1, VS-H3, MM-H1 |
| R2 | Create `IClassificationTreeCapture` interface in Abstractions.Agent/Tools/ | ClassificationTreeCapture.cs, WorkItemsModule.cs, DI extensions | MM-C3, MM-H1 |
| R3 | Create `INodeEnsurer` interface in Abstractions.Agent/Tools/ | NodeEnsurer.cs, WorkItemsModule.cs, DI extensions | MM-C2, MM-M2, CA-M1 |
| R4 | Rename `INodeStructureTool` → `INodeTranslationTool` (all references) | ~32 files (already documented in spec) | SA clarity |
| R5 | Add `ProjectMapping` overload to `IRevisionFolderProcessorFactory` | IRevisionFolderProcessorFactory.cs, RevisionFolderProcessorFactory.cs, WorkItemsModule.cs | MM-C1, MM-M1 |
| R6 | Fix ReferencedPathTracker DI lifetime (Transient → Singleton) | NodeStructureToolServiceCollectionExtensions.cs | VS-H3 |

Each refactoring is a self-contained commit. All existing tests must pass after each step.

### Phase 2 — Implementation (ATDD Inner Loop)

Each acceptance scenario from the spec becomes one ATDD session (Specification → Test Gen → Implementation → Review → Commit). The scenarios are ordered by dependency:

**Batch A — IdentitiesModule (US 0)**

| # | Scenario | Dependencies |
|---|----------|-------------|
| A1 | US0-S1: Export identity descriptors (50 users + 3 groups → `descriptors.jsonl`) | None |
| A2 | US0-S2: Explicit mapping.json overrides take priority | A1 |
| A3 | US0-S3: Automatic UPN/display name resolution | A1 |
| A4 | US0-S4: Unresolved identities → `unresolved.json` + default identity | A1 |
| A5 | US0-S5: Resumable export (cursor-based) | A1 |
| A6 | US0-S6: Fail-fast when downstream module calls Resolve() before import | A1 |

**Batch B — NodeStructureModule (US 0b)**

| # | Scenario | Dependencies |
|---|----------|-------------|
| B1 | US0b-S1: Export full tree (20 area + 15 iteration) → `source-tree.json` | None (existing code extraction) |
| B2 | US0b-S2: Export iteration dates | B1 |
| B3 | US0b-S3: Import with `ReplicateSourceTree: true` | B1 |
| B4 | US0b-S4: Import with `AutoCreateNodes: true` + `referenced-paths.json` | B1 |
| B5 | US0b-S5: Import with `NodeTranslationTool` project root swap | B1 |
| B6 | US0b-S6: Resumable import (cursor-based) | B3 |
| B7 | US0b-S7: Localised root name normalisation | B1 |

**Batch C — WorkItems NodeStructure Extension (US 0c)**

| # | Scenario | Dependencies |
|---|----------|-------------|
| C1 | US0c-S1: Record unique area/iteration paths from 100 work items | B1 |
| C2 | US0c-S2: Historical paths across 10 revisions | C1 |
| C3 | US0c-S3: Extension disabled → no update to `referenced-paths.json` | C1 |
| C4 | US0c-S4: Union of WorkItems + Teams paths (no duplicates) | C1, E1 |

**Batch D — TeamsModule Core (US 1)**

| # | Scenario | Dependencies |
|---|----------|-------------|
| D1 | US1-S1: Export 3 teams with settings → `Teams/` folder | None |
| D2 | US1-S2: Import teams into target with matching settings | D1 |
| D3 | US1-S3: Idempotent update of existing team | D2 |
| D4 | US1-S4: Resumable export (cursor-based) | D1 |

**Batch E — TeamsModule Iterations & NodeStructure Extension (US 2, US 5)**

| # | Scenario | Dependencies |
|---|----------|-------------|
| E1 | US5-S1: Export — record team area/iteration paths to `ReferencedPathTracker` | D1 |
| E2 | US2-S1: Export team iteration assignments (including default) | D1 |
| E3 | US2-S2: Import iterations with `NodeTranslationTool` path resolution | D2, B3 |
| E4 | US2-S3: Unresolvable iteration path → warning + skip | E3 |
| E5 | US5-S2: Import — translate team area assignments via `NodeTranslationTool` | D2, B3 |
| E6 | US5-S3: Regex path mapping applied during import | E5 |
| E7 | US5-S4: NodeStructure extension disabled → use source paths + warn | E5 |
| E8 | US5-S5: Union of Teams + WorkItems referenced paths | E1, C1 |

**Batch F — TeamsModule Members (US 3)**

| # | Scenario | Dependencies |
|---|----------|-------------|
| F1 | US3-S1: Export team members with admin flags | D1 |
| F2 | US3-S2: Import members with identity mapping | D2, A1 |
| F3 | US3-S3: Unresolvable member identity → warning + skip | F2 |

**Batch G — TeamsModule Capacity (US 4)**

| # | Scenario | Dependencies |
|---|----------|-------------|
| G1 | US4-S1: Export capacity (2 sprints, per-member) | D1, E2 |
| G2 | US4-S2: Import capacity with iteration/member mapping | D2, E3, F2 |
| G3 | US4-S3: Capacity for unassigned iteration → warning + skip | G2 |

**Batch H — Cross-cutting & Connectors**

| # | Scenario | Dependencies |
|---|----------|-------------|
| H1 | All acceptance scenarios verified for Simulated connector | All above |
| H2 | All acceptance scenarios verified for AzureDevOps connector | All above |
| H3 | All acceptance scenarios verified for TFS connector (with capacity exemption) | All above |

### Phase 3 — Documentation Sync

| # | Task | Target File |
|---|------|------------|
| 1 | Add IdentitiesModule detailed section | `docs/module-development-guide.md` |
| 2 | Add NodeStructureModule detailed section | `docs/module-development-guide.md` |
| 3 | Add TeamsModule detailed section | `docs/module-development-guide.md` |
| 4 | Add module config schemas | `docs/configuration-reference.md` |
| 5 | Document `Teams/` folder internal structure | `.agents/30-context/domains/migration-package-concept.md` |
| 6 | Add NodeStructureModule cursor to checkpointing docs | `.agents/30-context/domains/checkpointing-summary.md` |
| 7 | Add `Identities` and `Nodes` to `includedTypes` example in manifest | `.agents/30-context/domains/migration-package-concept.md` |
| 8 | Resolve all discrepancies in `specs/024-teams-module/discrepancies.md` | `discrepancies.md` |
| 9 | Review and update `analysis/pending-actions.md` | `analysis/pending-actions.md` |

## Complexity Tracking

No Constitution Check violations. All principles satisfied as documented above.

