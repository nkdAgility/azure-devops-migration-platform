# Feature Specification: IdentitiesModule, NodeStructureModule & TeamsModule — Identity Mapping, Node Structure, and Team Migration

**Feature Branch**: `024-teams-module`  
**Created**: 2026-04-27  
**Status**: Draft  
**Input**: User description: "M5: TeamsModule which also uses the Node tool as an expansion. This module should run before work items and be implemented in all three ADO, TFS, Simulated. Include IdentitiesModule as a prerequisite. Promote NodeStructure from tool-embedded data-owning code to a proper module — extract the export/import lifecycle from the existing tool code."

## Architecture References

| Document | Status |
|----------|--------|
| `docs/architecture.md` | ✅ Confirmed accurate — TeamsModule listed in Module Responsibilities table |
| `docs/modules.md` | ⚠️ Discrepancy logged — TeamsModule listed but has no detailed section; config schema not documented |
| `docs/configuration.md` | ⚠️ Discrepancy logged — no `Teams` module entry in the `modules` config schema example |
| `.agents/guardrails/system-architecture.md` | ✅ Confirmed accurate — all rules apply (streaming, IArtefactStore, IStateStore, cursor-based checkpoints, identity mapping via IIdentityMappingService) |
| `.agents/guardrails/module-template.md` | ✅ Confirmed accurate — full new module checklist applies |
| `.agents/context/package-format.md` | ⚠️ Discrepancy logged — `Teams/` folder listed in package structure but contents not documented |
| `analysis/proposed-features.md` (M5, T2) | ✅ Confirmed — TeamsModule uses NodeTranslationTool for iteration path operations |

## Concepts: Extension → Tool → Module Promotion

This spec introduces three modules. The following promotion model governs when a shared capability changes category:

| Level | What it is | Promotion trigger |
|---|---|---|
| **Extension** | Sub-data collector scoped to one module (e.g., `Attachments` inside `WorkItems`). Enabled/disabled per module config. | Stays here while only one module uses it. |
| **Tool** | Shared, stateless, cross-cutting transformation or lookup service used by 2+ modules (e.g., `NodeTranslationTool`, `FieldTransformTool`). Injected via DI. Has no package folder, no cursor, no lifecycle. | Promote an extension to a tool when **two or more modules** need the same capability. |
| **Module** | Domain-scoped unit owning export/import/validate for a concern (e.g., `IdentitiesModule`, `NodeStructureModule`, `TeamsModule`). Owns a package folder, a cursor, and runs as a discrete phase. | Promote to a module when the concern **owns data** (package folder + cursor + schema) and has an independent export/import lifecycle. |

**Identity mapping** is a **module** (`IdentitiesModule`), not a tool, because it:
- Owns the `Identities/` package folder (`descriptors.jsonl`, `mapping.json`, `unresolved.json`)
- Has its own export lifecycle (extracting descriptors from source)
- Maintains its own cursor for resumability
- Provides the `IIdentityMappingService` cross-cutting service to all downstream modules

**NodeStructure** is split into a **module** + **tool**:

- **`NodeStructureModule`** (new — extracted from existing code) owns the `Nodes/` package folder. During export, it captures the full source classification tree to `source-tree.json`. During import, it reads `source-tree.json` (when `ReplicateSourceTree: true`) and/or `referenced-paths.json` (when `AutoCreateNodes: true`) to create nodes on the target. The export/import code already exists in `ClassificationTreeCapture`, `NodeEnsurer`, and `NodeStructureValidator` — this is an extraction, not new development.
- **`NodeTranslationTool`** (existing, unchanged) remains a stateless `INodeTranslationTool` with `TranslatePath()`. It performs pure source-path → target-path transformation with no I/O. Used by WorkItemsModule extensions, TeamsModule extensions, and by `NodeStructureModule` itself during import.
- **`ReferencedPathTracker`** (existing service, shared) — accumulates the set of actually-referenced area/iteration paths discovered during export by other modules' extensions. Writes `Nodes/referenced-paths.json`. This is NOT part of `NodeStructureModule.ExportAsync` — it is fed by downstream module extensions during their export phase.

**The referenced-paths collection flow:**
- `NodeStructureModule.ExportAsync` captures the full source tree → `Nodes/source-tree.json`.
- During **WorkItems export**, a NodeStructure extension on WorkItemsModule scans every revision's `System.AreaPath` and `System.IterationPath` fields and calls `ReferencedPathTracker.RecordAreaPathAsync()`/`RecordIterationPathAsync()` to accumulate the unique set of actually-referenced paths → written to `Nodes/referenced-paths.json`.
- During **Teams export**, the Teams module similarly records team-assigned area/iteration paths to the same tracker.
- During **NodeStructureModule import** (which runs *after* all exports), when `AutoCreateNodes: true`, only the paths in `referenced-paths.json` are created on the target (not the entire tree). When `ReplicateSourceTree: true`, the full tree is replicated.
- The `NodeTranslationTool.TranslatePath()` is then used by all downstream modules during import to translate source paths to target paths.

**Why NodeStructure needed promotion from tool to module:**
- It already owns data (`Nodes/source-tree.json`, `Nodes/referenced-paths.json`)
- It already has an export lifecycle (`ClassificationTreeCapture.CaptureAsync`)
- It already has an import lifecycle (`NodeEnsurer` — replicate tree, auto-create nodes)
- It needs its own cursor for resumability
- Multiple modules depend on its exported data existing before they run
- `referenced-paths.json` is populated by extensions on other modules during export, then consumed by this module during import

**Execution order** (operator-controlled via configuration; all exports complete before any imports begin):

Export phase:
1. `IdentitiesModule.ExportAsync` — export identity descriptors
2. `NodeStructureModule.ExportAsync` — capture full source tree
3. `TeamsModule.ExportAsync` — export teams (Teams NodeStructure extension records team paths to `ReferencedPathTracker`)
4. `WorkItemsModule.ExportAsync` — export work items (WorkItems NodeStructure extension records revision paths to `ReferencedPathTracker`)

Import phase:
1. `IdentitiesModule.ImportAsync` — build identity mapping service
2. `NodeStructureModule.ImportAsync` — create nodes on target from `source-tree.json` and/or `referenced-paths.json`
3. `TeamsModule.ImportAsync` — import teams (Teams NodeStructure extension uses `NodeTranslationTool.TranslatePath()` for path resolution)
4. `WorkItemsModule.ImportAsync` — import work items (WorkItems NodeStructure extension uses `NodeTranslationTool.TranslatePath()` for path fields)

## User Scenarios & Testing *(mandatory)*

### User Story 0 — IdentitiesModule: Export Identity Descriptors and Provide Identity Mapping Service (Priority: P0)

As a migration operator, I want the platform to export all user and group identity descriptors from the source project and provide a cross-cutting identity mapping service, so that all downstream modules (Teams, WorkItems, etc.) can resolve source identities to target identities without implementing their own identity logic.

**Why this priority**: Identity mapping is a hard prerequisite for every module that references users or groups. Without `IdentitiesModule`, team member import, capacity import, and work item identity fields cannot be resolved. This is the foundational cross-cutting service.

**Independent Test**: Can be fully tested by exporting identities from a source, verifying the `Identities/` package folder contains `descriptors.jsonl` with one entry per source identity, then loading `mapping.json` and confirming `IIdentityMappingService.Resolve()` returns the correct target identity.

**Acceptance Scenarios**:

1. **Given** a source project with 50 users and 3 groups, **When** the operator runs an export with `IdentitiesModule` enabled, **Then** the `Identities/descriptors.jsonl` file contains 53 entries (one per identity descriptor).
2. **Given** a package with exported identity descriptors and an operator-provided `mapping.json` containing 10 explicit overrides, **When** the identity mapping service resolves a source identity that appears in `mapping.json`, **Then** the explicit override is used (not automatic matching).
3. **Given** a source identity that does not appear in `mapping.json`, **When** the identity mapping service resolves it, **Then** automatic resolution by UPN or display name is attempted against the target directory.
4. **Given** a source identity that cannot be resolved by mapping or automatic matching, **When** the identity mapping service resolves it, **Then** the configured default identity is returned and the unresolved identity is recorded in `Identities/unresolved.json`.
5. **Given** an export is interrupted mid-way, **When** the operator resumes, **Then** the module resumes from the last checkpoint and does not re-export already-captured descriptors.
6. **Given** the `IdentitiesModule` has not completed export, **When** a downstream module (e.g., TeamsModule) attempts identity resolution during import, **Then** the system fails fast with a clear error indicating IdentitiesModule must run first.

---

### User Story 0b — NodeStructureModule: Export and Import Classification Node Trees (Priority: P0)

As a migration operator, I want the platform to export the complete area path and iteration path trees from the source project and create/replicate them on the target, so that all downstream modules (Teams, WorkItems) can resolve paths without each implementing their own node export/import logic.

**Why this priority**: Area and iteration paths are a structural prerequisite for Teams (iteration assignments, area path assignments) and WorkItems (every work item references area/iteration paths). Without the node tree on the target, nothing else can be imported correctly. This is already coded — the implementation lives in `ClassificationTreeCapture`, `NodeEnsurer`, and `NodeStructureValidator` under the existing Tool namespace. This user story extracts that code into a proper `IModule`.

**Architecture note**: `NodeStructureModule.ExportAsync` only captures the full source tree → `Nodes/source-tree.json`. The `Nodes/referenced-paths.json` file is populated by extensions on *other* modules (WorkItems NodeStructure extension and Teams NodeStructure extension) via `ReferencedPathTracker` during their export phase. `NodeStructureModule.ImportAsync` then consumes both files to create nodes on the target.

**Independent Test**: Can be tested by exporting nodes from a source, verifying `Nodes/source-tree.json` contains the full area + iteration tree, then importing into a target and confirming the trees exist with correct structure.

**Acceptance Scenarios**:

1. **Given** a source project with 20 area paths and 15 iteration paths (some nested 4 levels deep), **When** the operator runs an export with `NodeStructureModule` enabled, **Then** `Nodes/source-tree.json` contains all 35 nodes with their full paths and hierarchy.
2. **Given** iteration nodes in the source have start/end dates, **When** the operator runs an export, **Then** the `source-tree.json` records those dates alongside the path.
3. **Given** a package with `source-tree.json` and `ReplicateSourceTree: true`, **When** the operator runs an import, **Then** the entire source tree is replicated on the target project, creating any missing nodes.
4. **Given** `AutoCreateNodes: true` and `Nodes/referenced-paths.json` has been populated by WorkItems/Teams export extensions, **When** the operator runs a NodeStructureModule import, **Then** only the referenced paths are created on the target (not the full tree).
5. **Given** a node path in the package uses a different project root name than the target, **When** `NodeStructureModule` imports using `NodeTranslationTool.TranslatePath()`, **Then** the project root is swapped correctly (e.g., `SourceProject\Sprint 1` → `TargetProject\Sprint 1`).
6. **Given** an import is interrupted after creating 10 of 20 area nodes, **When** the operator resumes, **Then** the module resumes from the cursor and creates only the remaining 10 nodes (idempotent — already-existing nodes are skipped).
7. **Given** the source tree contains localised root names (e.g., German `Bereich` instead of English `Area`), **When** the module exports, **Then** the root name is normalised to the canonical English form in the package.

### User Story 0c — WorkItems NodeStructure Extension: Referenced Path Collection (Priority: P0)

As a migration operator, I want the WorkItemsModule to have a NodeStructure extension that scans every exported work item revision's `System.AreaPath` and `System.IterationPath` fields and records them to `Nodes/referenced-paths.json`, so that the NodeStructureModule's import phase knows which paths are actually referenced and can create only those nodes when `AutoCreateNodes: true`.

**Why this priority**: Without this extension, the NodeStructureModule has no way to know which paths are actually used by work items. It would have to replicate the entire source tree. This extension enables the `AutoCreateNodes` mode which is the most common import strategy.

**Architecture note**: This extension runs during `WorkItemsModule.ExportAsync`. It uses `ReferencedPathTracker.RecordAreaPathAsync()`/`RecordIterationPathAsync()` to accumulate unique paths. The same `ReferencedPathTracker` instance is shared with the Teams NodeStructure extension. The output is `Nodes/referenced-paths.json`.

**Independent Test**: Can be tested by exporting work items with the NodeStructure extension enabled, then verifying `Nodes/referenced-paths.json` contains exactly the unique set of area/iteration paths found across all exported revisions.

**Acceptance Scenarios**:

1. **Given** 100 work items across 5 area paths and 3 iteration paths, **When** the operator runs a WorkItems export with the NodeStructure extension enabled, **Then** `Nodes/referenced-paths.json` contains exactly 5 area paths and 3 iteration paths (deduplicated).
2. **Given** a work item with 10 revisions that changed `System.IterationPath` three times across different sprints, **When** the extension processes that work item, **Then** all three unique iteration paths are recorded (historical paths matter).
3. **Given** the NodeStructure extension is disabled on WorkItemsModule, **When** the operator runs an export, **Then** `Nodes/referenced-paths.json` is not updated by this module (other extensions may still contribute).
4. **Given** both WorkItems and Teams modules export with their NodeStructure extensions enabled, **When** both complete, **Then** `referenced-paths.json` contains the union of all paths from both modules (no duplicates).

---

### User Story 1 — Export and Import Team Definitions with Settings (Priority: P1)

As a migration operator, I want the platform to export all teams from a source project (including backlog settings, working days, and default area path) and import them into the target project, so that the team organisational structure is faithfully reproduced after migration.

**Why this priority**: Teams are the foundational organisational unit in Azure DevOps. Without teams, iteration paths, capacity, and board configurations have no context. This is the minimum viable unit of team migration.

**Independent Test**: Can be fully tested by exporting teams from a source project, verifying the package `Teams/` folder contains team definition files, then importing into a target project and confirming each team exists with correct settings.

**Acceptance Scenarios**:

1. **Given** a source project with three teams configured with distinct backlog settings and working days, **When** the operator runs an export, **Then** the `Teams/` folder in the package contains one artefact per team with all settings captured.
2. **Given** a package with exported team definitions, **When** the operator runs an import, **Then** each team is created in the target project with matching backlog settings and working days.
3. **Given** a team that already exists in the target project, **When** the operator runs an import, **Then** the existing team's settings are updated to match the package (idempotent).
4. **Given** an export is interrupted mid-way, **When** the operator resumes, **Then** the module resumes from the last checkpoint and does not re-export already-captured teams.

---

### User Story 2 — Export and Import Team Iteration Path Assignments (Priority: P2)

As a migration operator, I want the platform to export the iteration paths assigned to each team and import those assignments into the target, so that teams' sprint boards are correctly configured after migration.

**Why this priority**: Iteration path assignments determine which sprints appear on each team's board. This depends on the NodeTranslationTool having already created/mapped the target iteration paths. Running TeamsModule before WorkItemsModule ensures that when work items are imported, the team board structure is already in place.

**Independent Test**: Can be tested by exporting a team with three iteration assignments, verifying the package records those paths, then importing and confirming the target team has the same three iterations assigned.

**Acceptance Scenarios**:

1. **Given** a source team with specific iteration paths assigned (including a default iteration), **When** the operator runs an export, **Then** the package records both the assigned iterations and the default iteration for that team.
2. **Given** a package with team iteration assignments and the NodeTranslationTool configured with iteration mappings, **When** the operator runs an import, **Then** the target team has the mapped iteration paths assigned, using the NodeTranslationTool to resolve source paths to target paths.
3. **Given** an iteration path in the package that cannot be resolved by the NodeTranslationTool, **When** the operator runs an import, **Then** the system logs a warning for that path and continues importing the remaining assignments.

---

### User Story 3 — Export and Import Team Members (Priority: P3)

As a migration operator, I want the platform to export team membership and import those members into the target team, so that team composition is preserved after migration.

**Why this priority**: Team membership determines who sees which boards and backlogs. It depends on the IdentitiesModule having already mapped source identities to target identities.

**Independent Test**: Can be tested by exporting a team with five members, verifying the package records those identities, then importing and confirming all five members are added to the target team.

**Acceptance Scenarios**:

1. **Given** a source team with multiple members (including administrators), **When** the operator runs an export, **Then** the package records each member's identity descriptor and their administrator flag.
2. **Given** a package with team members and a completed identity mapping, **When** the operator runs an import, **Then** the mapped identities are added to the target team with the correct administrator status.
3. **Given** a team member whose identity cannot be resolved via the identity mapping service, **When** the operator runs an import, **Then** the system logs a warning for that member and continues importing remaining members.

---

### User Story 4 — Export and Import Team Capacity (Priority: P4)

As a migration operator, I want the platform to export per-sprint capacity data for each team member and import it into the target, so that capacity planning information is preserved for historical and active sprints.

**Why this priority**: Capacity data is valuable for active sprint planning but is lower priority than team structure, iterations, and membership, which are prerequisites for capacity to be meaningful.

**Independent Test**: Can be tested by exporting capacity for a team's active sprint, verifying the package records per-member capacity values, then importing and confirming the target team shows matching capacity.

**Acceptance Scenarios**:

1. **Given** a source team with capacity set for two sprints, **When** the operator runs an export, **Then** the package records per-member capacity (activities and hours) and days off for each sprint.
2. **Given** a package with team capacity data and completed iteration/member imports, **When** the operator runs an import, **Then** the target team shows matching capacity values for each sprint.
3. **Given** capacity data referencing an iteration that was not assigned to the team, **When** the operator runs an import, **Then** the system logs a warning and skips that capacity entry.

---

### User Story 5 — NodeStructure Extension for TeamsModule: Path Collection and Resolution (Priority: P5)

As a migration operator, I want the TeamsModule to have a NodeStructure extension that (a) during export, records team-assigned area/iteration paths to `ReferencedPathTracker` and (b) during import, uses `NodeTranslationTool.TranslatePath()` to resolve source paths to target paths in team assignments.

**Why this priority**: This extension has two roles:
- **Export phase**: Feeds team-assigned paths to `ReferencedPathTracker` so `referenced-paths.json` includes paths used by teams (not just work items).
- **Import phase**: Uses the stateless `NodeTranslationTool.TranslatePath()` to translate source paths stored in team assignments to their target equivalents. Does NOT create nodes — that is `NodeStructureModule`'s job.

**Independent Test**: Export: verify `referenced-paths.json` includes team-assigned paths. Import: verify team assignments use translated paths.

**Acceptance Scenarios**:

1. **Given** a source team assigned to 3 area paths and 2 iteration paths, **When** the operator runs a TeamsModule export with the NodeStructure extension enabled, **Then** those 5 paths are recorded in `Nodes/referenced-paths.json` via `ReferencedPathTracker`.
2. **Given** a package with team area/iteration assignments and `NodeStructureModule` has already created the target nodes, **When** the operator runs a TeamsModule import with the NodeStructure extension enabled, **Then** the target team has the translated paths assigned with correct flags.
3. **Given** a path mapping that remaps `SourceProject\Release 1` to `TargetProject\v1.0`, **When** the NodeStructure extension resolves a team's area assignment during import, **Then** the mapped path is used.
4. **Given** the NodeStructure extension is disabled in the TeamsModule configuration, **When** the operator runs an import, **Then** source paths are used as-is and the module logs a warning for any path that does not exist in the target.
5. **Given** both Teams and WorkItems export with their NodeStructure extensions enabled, **When** both complete, **Then** `referenced-paths.json` contains the union of all paths from both modules (no duplicates).

---

### Edge Cases

**NodeStructureModule:**
- What happens when the source has thousands of nodes? The module captures the entire tree in a single `source-tree.json` (this is bounded — typical projects have <500 nodes). Import creates nodes one at a time idempotently.
- What happens when `ReplicateSourceTree` and `AutoCreateNodes` are both true? They are complementary: `ReplicateSourceTree` creates the current tree, `AutoCreateNodes` adds historical paths from revisions that no longer exist in the current tree. The union of both sets is created.
- What happens when `ReplicateSourceTree` and `AutoCreateNodes` are both false? The module exports the tree for reference but creates no nodes on import. Downstream modules must handle missing paths.
- What happens when the target already has some of the source nodes? Node creation is idempotent — existing nodes are skipped, not duplicated.
- What happens when a node creation fails mid-import? The cursor tracks which nodes have been created. On resume, only remaining nodes are attempted.

**IdentitiesModule:**
- What happens when the source has thousands of identities? The module exports descriptors one at a time via streaming enumeration through IArtefactStore. No in-memory collection of all descriptors.
- What happens when `mapping.json` is not provided? The module operates in automatic-only mode — resolution by UPN or display name. All unresolved identities are recorded in `unresolved.json`.
- What happens when `mapping.json` contains an entry for an identity that does not exist in the source? The entry is ignored (no error). Only identities encountered during migration are resolved.
- What happens when the target directory is unavailable during import? Resolution falls back to the default identity for all unresolvable identities. The module does not fail — it records every unresolved identity.
- What happens on a TFS source where the identity API returns different descriptor formats? The TFS connector normalises descriptors to the same schema as ADO before writing to the package.

**TeamsModule:**

- What happens when a team in the source has no members? The module exports the team definition and settings; the members extension produces an empty collection. Import creates the team with no members.
- What happens when a team's default iteration or area path does not exist in the target? The NodeTranslationTool must be configured to create missing nodes or the system logs a clear error identifying the missing path and skips that assignment.
- What happens when team names collide with existing teams in the target? The module treats this as an update — it applies settings, iterations, members, and areas to the existing team (idempotent behaviour).
- How does the system handle a source project with 100+ teams? The module processes teams one at a time via streaming enumeration through IArtefactStore. No in-memory collection of all team data.
- What happens when the source is TFS and a team API capability is not available? The TFS connector logs a structured warning identifying the unsupported capability and exports what the TFS OM API supports.
- What happens when the source project's default team has a different name from the target project? The module detects the default team and maps it to the target project's default team regardless of name differences.
- What happens when an import is interrupted after team settings are applied but before members are added? On resume, the cursor indicates that TeamSettings completed but TeamMembers did not, so the module retries member import for that team.

## Observability

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `identities.export` | `module` | `IdentitiesModule.ExportAsync` | Source Identity API (ADO REST / TFS OM / Simulated), `IArtefactStore`, `IStateStore` |
| `identities.import` | `module` | `IdentitiesModule.ImportAsync` | `IArtefactStore`, Target Directory API (for automatic UPN/display name matching) |
| `identities.validate` | `module` | `IdentitiesModule.ValidateAsync` | `IArtefactStore` (read-only) |
| `nodes.export` | `module` | `NodeStructureModule.ExportAsync` | Source Classification API (ADO REST / TFS OM / Simulated), `IArtefactStore`, `IStateStore` |
| `nodes.import` | `module` | `NodeStructureModule.ImportAsync` | Target Classification API, `IArtefactStore`, `IStateStore`, `INodeTranslationTool` |
| `nodes.validate` | `module` | `NodeStructureModule.ValidateAsync` | `IArtefactStore` (read-only) |
| `teams.export` | `module` | `TeamsModule.ExportAsync` | Source Teams API (ADO REST / TFS OM / Simulated), `IArtefactStore`, `IStateStore` |
| `teams.import` | `module` | `TeamsModule.ImportAsync` | Target Teams API, `IArtefactStore`, `IStateStore`, `IIdentityMappingService`, `INodeTranslationTool` |
| `teams.validate` | `module` | `TeamsModule.ValidateAsync` | `IArtefactStore` (read-only) |

### Operator Decisions

| Operation | Decision | Question |
|---|---|---|
| `identities.export` | Is it working? | Are identity descriptors being exported? |
| `identities.export` | Is it fast enough? | Is export completing within acceptable time? |
| `identities.export` | Is it overloaded? | How many descriptors are being processed concurrently? |
| `identities.export` | What failed? | Which identity export failed and why? |
| `identities.export` | Is it correct? | Does the exported descriptor count match the source? |
| `identities.import` | Is it working? | Is identity resolution loading and functioning? |
| `identities.import` | What failed? | Which identities could not be resolved? |
| `identities.import` | Is it correct? | How many identities were resolved vs unresolved? |
| `identities.validate` | Is it working? | Is validation completing? |
| `identities.validate` | What failed? | Which descriptor entries are malformed? |
| `nodes.export` | Is it working? | Are area/iteration nodes being exported? |
| `nodes.export` | Is it fast enough? | Is node tree capture completing promptly? |
| `nodes.export` | What failed? | Which node export failed and why? |
| `nodes.export` | Is it correct? | Does the node count match the source? |
| `nodes.import` | Is it working? | Are nodes being created on the target? |
| `nodes.import` | Is it fast enough? | Is node creation completing promptly? |
| `nodes.import` | What failed? | Which node creation failed and why? |
| `nodes.import` | Is it correct? | Were all required nodes created? |
| `nodes.validate` | Is it working? | Is validation completing? |
| `nodes.validate` | What failed? | Which entries in source-tree.json are malformed? |
| `teams.export` | Is it working? | Are teams being exported successfully? |
| `teams.export` | Is it fast enough? | Is export completing within acceptable time per team? |
| `teams.export` | Is it overloaded? | How many teams are being processed concurrently? |
| `teams.export` | What failed? | Which team export failed and why? |
| `teams.export` | Is it correct? | Does the exported team count match the source team count? |
| `teams.import` | Is it working? | Are teams being imported successfully? |
| `teams.import` | Is it fast enough? | Is import completing within acceptable time per team? |
| `teams.import` | Is it overloaded? | How many teams are being processed concurrently? |
| `teams.import` | What failed? | Which team or extension import failed and why? |
| `teams.import` | Is it correct? | Do imported team counts and member counts match the package? |
| `teams.import` | Where is it slow? | Which extension (settings, iterations, members, capacity) is the bottleneck? |
| `teams.validate` | Is it working? | Is validation completing without errors? |
| `teams.validate` | What failed? | Which team artefact failed validation and why? |

### Metrics

| Metric Name | Instrument | Unit | Operation | Decision |
|---|---|---|---|---|
| `migration.identities.export.count` | `Counter<long>` | `{descriptor}` | `identities.export` | Is it working? |
| `migration.identities.export.duration_ms` | `Histogram<double>` | `ms` | `identities.export` | Is it fast enough? |
| `migration.identities.export.errors` | `Counter<long>` | `{descriptor}` | `identities.export` | What failed? |
| `migration.identities.export.in_flight` | `UpDownCounter<long>` | `{descriptor}` | `identities.export` | Is it overloaded? |
| `migration.identities.import.resolved` | `Counter<long>` | `{identity}` | `identities.import` | Is it correct? |
| `migration.identities.import.unresolved` | `Counter<long>` | `{identity}` | `identities.import` | What failed? |
| `migration.identities.import.duration_ms` | `Histogram<double>` | `ms` | `identities.import` | Is it fast enough? |
| `migration.identities.import.errors` | `Counter<long>` | `{identity}` | `identities.import` | What failed? |
| `migration.identities.validate.count` | `Counter<long>` | `{descriptor}` | `identities.validate` | Is it working? |
| `migration.identities.validate.errors` | `Counter<long>` | `{descriptor}` | `identities.validate` | What failed? |
| `migration.nodes.export.count` | `Counter<long>` | `{node}` | `nodes.export` | Is it working? |
| `migration.nodes.export.duration_ms` | `Histogram<double>` | `ms` | `nodes.export` | Is it fast enough? |
| `migration.nodes.export.errors` | `Counter<long>` | `{node}` | `nodes.export` | What failed? |
| `migration.nodes.import.count` | `Counter<long>` | `{node}` | `nodes.import` | Is it working? |
| `migration.nodes.import.duration_ms` | `Histogram<double>` | `ms` | `nodes.import` | Is it fast enough? |
| `migration.nodes.import.errors` | `Counter<long>` | `{node}` | `nodes.import` | What failed? |
| `migration.nodes.import.skipped` | `Counter<long>` | `{node}` | `nodes.import` | Is it correct? (already existed on target) |
| `migration.nodes.import.created` | `Counter<long>` | `{node}` | `nodes.import` | Is it correct? |
| `migration.nodes.referenced_paths.count` | `Counter<long>` | `{path}` | `nodes.import` | Is it correct? (paths from referenced-paths.json) |
| `migration.nodes.validate.count` | `Counter<long>` | `{node}` | `nodes.validate` | Is it working? |
| `migration.nodes.validate.errors` | `Counter<long>` | `{node}` | `nodes.validate` | What failed? |
| `migration.teams.export.count` | `Counter<long>` | `{team}` | `teams.export` | Is it working? |
| `migration.teams.export.duration_ms` | `Histogram<double>` | `ms` | `teams.export` | Is it fast enough? |
| `migration.teams.export.errors` | `Counter<long>` | `{team}` | `teams.export` | What failed? |
| `migration.teams.export.in_flight` | `UpDownCounter<long>` | `{team}` | `teams.export` | Is it overloaded? |
| `migration.teams.import.count` | `Counter<long>` | `{team}` | `teams.import` | Is it working? |
| `migration.teams.import.duration_ms` | `Histogram<double>` | `ms` | `teams.import` | Is it fast enough? |
| `migration.teams.import.errors` | `Counter<long>` | `{team}` | `teams.import` | What failed? |
| `migration.teams.import.in_flight` | `UpDownCounter<long>` | `{team}` | `teams.import` | Is it overloaded? |
| `migration.teams.import.members.count` | `Counter<long>` | `{member}` | `teams.import` | Is it correct? |
| `migration.teams.import.members.unresolved` | `Counter<long>` | `{member}` | `teams.import` | What failed? |
| `migration.teams.import.iterations.count` | `Counter<long>` | `{iteration}` | `teams.import` | Is it correct? |
| `migration.teams.import.iterations.unresolvable` | `Counter<long>` | `{iteration}` | `teams.import` | What failed? |
| `migration.teams.import.capacity.count` | `Counter<long>` | `{capacity}` | `teams.import` | Is it correct? |
| `migration.teams.import.extension.duration_ms` | `Histogram<double>` | `ms` | `teams.import` | Where is it slow? |
| `migration.teams.validate.count` | `Counter<long>` | `{team}` | `teams.validate` | Is it working? |
| `migration.teams.validate.errors` | `Counter<long>` | `{team}` | `teams.validate` | What failed? |

### Traces

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| `IdentitiesModule` | `identities.export` | `job.id`, `operation=export`, `module=Identities` | Root | Is it working? |
| `IdentitiesModule` | `identities.export.descriptor` | `job.id`, `identity.descriptor` | `identities.export` | What failed? / Is it fast enough? |
| `IdentitiesModule` | `identities.import` | `job.id`, `operation=import`, `module=Identities` | Root | Is it working? |
| `IdentitiesModule` | `identities.import.resolve` | `job.id`, `identity.descriptor` | `identities.import` | What failed? |
| `IdentitiesModule` | `identities.validate` | `job.id`, `operation=validate`, `module=Identities` | Root | Is it working? |
| `NodeStructureModule` | `nodes.export` | `job.id`, `operation=export`, `module=Nodes` | Root | Is it working? |
| `NodeStructureModule` | `nodes.export.tree` | `job.id`, `node.type` (Area/Iteration) | `nodes.export` | Is it fast enough? |
| `NodeStructureModule` | `nodes.import` | `job.id`, `operation=import`, `module=Nodes` | Root | Is it working? |
| `NodeStructureModule` | `nodes.import.node` | `job.id`, `node.path`, `node.type` | `nodes.import` | What failed? / Is it fast enough? |
| `NodeStructureModule` | `nodes.validate` | `job.id`, `operation=validate`, `module=Nodes` | Root | Is it working? |
| `TeamsModule` | `teams.export` | `job.id`, `operation=export`, `module=Teams` | Root | Is it working? |
| `TeamsModule` | `teams.export.team` | `job.id`, `team.name` | `teams.export` | What failed? / Is it fast enough? |
| `TeamsModule` | `teams.export.team.settings` | `job.id`, `team.name` | `teams.export.team` | Where is it slow? |
| `TeamsModule` | `teams.export.team.iterations` | `job.id`, `team.name` | `teams.export.team` | Where is it slow? |
| `TeamsModule` | `teams.export.team.members` | `job.id`, `team.name` | `teams.export.team` | Where is it slow? |
| `TeamsModule` | `teams.export.team.capacity` | `job.id`, `team.name` | `teams.export.team` | Where is it slow? |
| `TeamsModule` | `teams.import` | `job.id`, `operation=import`, `module=Teams` | Root | Is it working? |
| `TeamsModule` | `teams.import.team` | `job.id`, `team.name` | `teams.import` | What failed? / Is it fast enough? |
| `TeamsModule` | `teams.import.team.settings` | `job.id`, `team.name` | `teams.import.team` | Where is it slow? |
| `TeamsModule` | `teams.import.team.nodestructure` | `job.id`, `team.name` | `teams.import.team` | Where is it slow? |
| `TeamsModule` | `teams.import.team.iterations` | `job.id`, `team.name` | `teams.import.team` | Where is it slow? |
| `TeamsModule` | `teams.import.team.members` | `job.id`, `team.name` | `teams.import.team` | Where is it slow? |
| `TeamsModule` | `teams.import.team.capacity` | `job.id`, `team.name` | `teams.import.team` | Where is it slow? |
| `TeamsModule` | `teams.validate` | `job.id`, `operation=validate`, `module=Teams` | Root | Is it working? |
| `TeamsModule` | `teams.validate.team` | `job.id`, `team.name` | `teams.validate` | What failed? |

**Context propagation:** Automatic via `System.Diagnostics.Activity` hierarchy. The `Migration` ActivitySource (`WellKnownActivitySourceNames.Migration`) is used for all spans. Parent context flows from the Job Engine root span.

### Logging

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| Identity export started | `Information` | `operationId`, `operation=identities.export`, `descriptorCount` | `identities.export` | Is it working? |
| Identity export completed | `Information` | `operationId`, `operation=identities.export`, `outcome`, `durationMs`, `descriptorsExported` | `identities.export` | Is it working? / Is it fast enough? |
| Identity export failed | `Error` | `operationId`, `operation=identities.export`, `errorType`, `errorMessage`, `durationMs` | `identities.export` | What failed? |
| Identity resolution loaded | `Information` | `operationId`, `operation=identities.import`, `mappingCount`, `descriptorCount` | `identities.import` | Is it working? |
| Identity unresolved | `Warning` | `operationId`, `identityDescriptor` | `identities.import` | What failed? |
| Identity validation error | `Error` | `operationId`, `operation=identities.validate`, `descriptor`, `errorType` | `identities.validate` | What failed? |
| Node export started | `Information` | `operationId`, `operation=nodes.export`, `areaCount`, `iterationCount` | `nodes.export` | Is it working? |
| Node export completed | `Information` | `operationId`, `operation=nodes.export`, `outcome`, `durationMs`, `totalNodes` | `nodes.export` | Is it working? / Is it fast enough? |
| Node export failed | `Error` | `operationId`, `operation=nodes.export`, `errorType`, `errorMessage` | `nodes.export` | What failed? |
| Node import started | `Information` | `operationId`, `operation=nodes.import`, `mode` (ReplicateSourceTree/AutoCreateNodes), `nodeCount` | `nodes.import` | Is it working? |
| Node import completed | `Information` | `operationId`, `operation=nodes.import`, `outcome`, `durationMs`, `nodesCreated`, `nodesSkipped` | `nodes.import` | Is it correct? |
| Node import failed | `Error` | `operationId`, `operation=nodes.import`, `errorType`, `errorMessage` | `nodes.import` | What failed? |
| Node creation skipped | `Debug` | `operationId`, `nodePath`, `reason=already_exists` | `nodes.import` | (diagnostic) |
| Referenced paths loaded | `Information` | `operationId`, `pathCount` | `nodes.import` | Is it correct? |
| Node validation error | `Error` | `operationId`, `operation=nodes.validate`, `errorType`, `errorMessage` | `nodes.validate` | What failed? |
| Team export started | `Information` | `operationId`, `operation=teams.export`, `teamCount` | `teams.export` | Is it working? |
| Team export completed | `Information` | `operationId`, `operation=teams.export`, `outcome`, `durationMs`, `teamsExported` | `teams.export` | Is it working? / Is it fast enough? |
| Team export failed | `Error` | `operationId`, `operation=teams.export`, `errorType`, `errorMessage`, `durationMs` | `teams.export` | What failed? |
| Single team exported | `Debug` | `operationId`, `teamName`, `extensionsProcessed` | `teams.export` | (diagnostic) |
| Team import started | `Information` | `operationId`, `operation=teams.import`, `teamCount` | `teams.import` | Is it working? |
| Team import completed | `Information` | `operationId`, `operation=teams.import`, `outcome`, `durationMs`, `teamsImported`, `membersImported`, `membersUnresolved` | `teams.import` | Is it working? / Is it correct? |
| Team import failed | `Error` | `operationId`, `operation=teams.import`, `errorType`, `errorMessage`, `durationMs` | `teams.import` | What failed? |
| Extension import slow | `Warning` | `operationId`, `teamName`, `extension`, `durationMs`, `threshold` | `teams.import` | Where is it slow? |
| Member identity unresolved | `Warning` | `operationId`, `teamName`, `memberDescriptor` | `teams.import` | What failed? |
| Iteration path unresolvable | `Warning` | `operationId`, `teamName`, `sourcePath` | `teams.import` | What failed? |
| Retry attempt | `Warning` | `operationId`, `operation`, `attempt`, `maxAttempts`, `delay` | Both | Is it overloaded? |
| Validation started | `Information` | `operationId`, `operation=teams.validate`, `teamCount` | `teams.validate` | Is it working? |
| Validation completed | `Information` | `operationId`, `operation=teams.validate`, `outcome`, `durationMs`, `validCount`, `invalidCount` | `teams.validate` | Is it working? |
| Validation error | `Error` | `operationId`, `operation=teams.validate`, `teamName`, `errorType`, `errorMessage` | `teams.validate` | What failed? |
| Per-team detail | `Debug` | `operationId`, `teamName`, `step`, `detail` | All | (diagnostic) |
| API wire detail | `Trace` | `operationId`, `endpoint`, `statusCode` | All | (diagnostic) |

> Debug and Trace levels are disabled by default.
>
> **Data classification:** `teamName` is logged at `Information` level as it is a structural identifier (like a project name). If team names contain customer-identifiable information in a specific deployment, operators should raise the agent log level threshold. Member identity descriptors are structural (AAD object IDs / TFS SIDs), not customer content.

### Correlation

| Field | Source | Scope |
|---|---|---|
| `operationId` / `traceId` | `Activity.Current.TraceId` | All telemetry |
| `parentId` | `Activity.Current.ParentSpanId` | Spans and logs within parent context |
| `job.id` | Job context from `ExportContext` / `ImportContext` | All telemetry within a job |
| `module` | `"Identities"`, `"Nodes"`, or `"Teams"` | All telemetry from the respective module |
| `identity.descriptor` | Current identity being processed | Per-identity spans and logs (IdentitiesModule) |
| `team.name` | Current team being processed | Per-team spans and logs (TeamsModule) |
| `extension` | Current extension name (e.g., `TeamSettings`, `TeamMembers`) | Per-extension spans and logs (TeamsModule) |

### Validation Queries

#### Failure Identification
```kql
// Identify which teams failed export or import and why
customMetrics
| where name startswith "migration.teams" and name endswith ".errors"
| where value > 0
| summarize TotalErrors=sum(value) by name, bin(timestamp, 1m)
| join kind=inner (
    traces | where message contains "teams" and severityLevel >= 3
) on $left.timestamp == $right.timestamp
| project timestamp, MetricName=name, TotalErrors, ErrorMessage=message
```

#### Latency Analysis
```kql
// P50/P95/P99 latency per team for export and import
customMetrics
| where name in ("migration.teams.export.duration_ms", "migration.teams.import.duration_ms")
| summarize P50=percentile(value, 50), P95=percentile(value, 95), P99=percentile(value, 99) by name, bin(timestamp, 5m)
```

#### Load Observation
```kql
// In-flight team processing concurrency
customMetrics
| where name in ("migration.teams.export.in_flight", "migration.teams.import.in_flight")
| summarize MaxConcurrency=max(value), AvgConcurrency=avg(value) by name, bin(timestamp, 1m)
```

#### End-to-End Trace
```kql
// Trace a single team through export, from root span to all extension spans
dependencies
| where name startswith "teams.export.team"
| where customDimensions["team.name"] == "<team-name>"
| order by timestamp asc
| project timestamp, name, duration, customDimensions
```

#### Error Diagnosis
```kql
// Join error logs with trace spans for root cause analysis
traces
| where severityLevel >= 3 and message contains "teams"
| join kind=inner (
    dependencies | where name startswith "teams."
) on operation_Id
| project timestamp, SpanName=name1, ErrorMessage=message, Duration=duration, TraceId=operation_Id
```

## Requirements *(mandatory)*

### Functional Requirements

#### IdentitiesModule

- **FR-I01**: System MUST implement `IdentitiesModule` conforming to the `IModule` contract (`ExportAsync`, `ImportAsync`, `ValidateAsync`).
- **FR-I02**: `IdentitiesModule` has no module dependencies. Module execution order is controlled entirely by the operator via configuration. There is no `DependsOn` property.
- **FR-I03**: `ExportAsync` MUST enumerate all user and group identity descriptors from the source and write them to `Identities/descriptors.jsonl` (one JSON object per line) via `IArtefactStore`.
- **FR-I04**: `ImportAsync` MUST load `Identities/mapping.json` (operator-provided overrides) and `Identities/descriptors.jsonl`, then build the `IIdentityMappingService` resolution index. Target identity discovery is performed during the **Prepare** phase (which queries the target Azure DevOps instance's identity APIs to find existing users/groups). If `prepare` has not been run before `import`, the import MUST run the prepare phase automatically and exit with a clear error if there are any contradictions or validation failures.
- **FR-I05**: `IIdentityMappingService.Resolve(sourceIdentity)` MUST apply resolution in this order: (1) explicit `mapping.json` entry, (2) automatic match by UPN or display name in target directory, (3) configured default identity. Unresolved identities MUST be recorded in `Identities/unresolved.json`.
- **FR-I06**: `IIdentityMappingService` MUST be registered as a singleton in DI — all downstream modules consume the same instance.
- **FR-I07**: `ValidateAsync` MUST verify that `Identities/descriptors.jsonl` exists, is valid JSONL, and each entry contains the required fields (descriptor, display name, source type).
- **FR-I08**: System MUST maintain a cursor file at `.migration/Checkpoints/identities.cursor.json` for resumable export.
- **FR-I09**: System MUST implement all three connectors: **Simulated** (deterministic test identities from seed), **AzureDevOpsServices** (Graph API / Identity API via REST), and **TeamFoundationServer** (TFS Identity Service via .NET 4.8 subprocess bridge).
- **FR-I10**: The Simulated connector MUST generate a configurable number of identities (default: 20) with deterministic descriptors, display names, and UPNs based on a seed value.
- **FR-I11**: Import MUST be idempotent — re-running import loads the same resolution index and produces the same mappings.
- **FR-I12**: If a downstream module calls `IIdentityMappingService.Resolve()` before `IdentitiesModule.ImportAsync` has completed, the service MUST throw `InvalidOperationException` with a clear message indicating the prerequisite was not met.

#### NodeStructureModule

- **FR-N01**: System MUST implement `NodeStructureModule` conforming to the `IModule` contract (`ExportAsync`, `ImportAsync`, `ValidateAsync`). This is an extraction of existing code from `ClassificationTreeCapture`, `NodeEnsurer`, and `NodeStructureValidator`.
- **FR-N02**: `NodeStructureModule` has no module dependencies. Module execution order is controlled entirely by the operator via configuration. There is no `DependsOn` property. However, its import MUST run after all exports complete (so `referenced-paths.json` is fully populated by downstream extensions).
- **FR-N03**: `ExportAsync` MUST use `IClassificationTreeReader` to enumerate all area and iteration nodes from the source and write them to `Nodes/source-tree.json` via `IArtefactStore`.
- **FR-N04**: `ImportAsync` MUST support two complementary modes controlled by configuration: `ReplicateSourceTree` (create the full current tree from `source-tree.json`) and `AutoCreateNodes` (create historical paths listed in `referenced-paths.json` that no longer exist in the current tree). When both are true, the module creates the union of the current tree and historical referenced paths. When both are false, no nodes are created.
- **FR-N05**: During import, the module MUST use `NodeTranslationTool.TranslatePath()` to translate source paths to target paths before creating nodes.
- **FR-N06**: Node creation MUST be idempotent — existing nodes on the target are skipped without error.
- **FR-N07**: `ValidateAsync` MUST verify that `Nodes/source-tree.json` exists, is valid JSON, and contains the expected schema fields (path, type, optional dates for iterations).
- **FR-N08**: System MUST maintain a cursor file at `.migration/Checkpoints/nodes.cursor.json` for resumable import (tracks which nodes have been created).
- **FR-N09**: System MUST implement all three connectors for node reading/creation: **Simulated** (deterministic tree from seed), **AzureDevOpsServices** (`IClassificationTreeReader`/`INodeCreator` via REST), **TeamFoundationServer** (`ICommonStructureService` via TFS OM subprocess bridge).
- **FR-N10**: The module MUST normalise localised root names (e.g., German `Bereich` → English `Area`) during export.
- **FR-N11**: `ReferencedPathTracker` is a shared singleton service (not part of `NodeStructureModule.ExportAsync`). It is populated by extensions on other modules (WorkItems, Teams) during their export phase. The module consumes `Nodes/referenced-paths.json` during its import phase.
- **FR-N12**: The Simulated connector MUST generate a configurable number of area paths (default: 10) and iteration paths (default: 8) with deterministic names and hierarchy based on a seed value.

#### WorkItems NodeStructure Extension

- **FR-W01**: WorkItemsModule MUST support a `NodeStructure` extension that, during export, scans every revision's `System.AreaPath` and `System.IterationPath` fields and records them to `ReferencedPathTracker`.
- **FR-W02**: The extension MUST process all revisions (not just the latest) to capture historical path references.
- **FR-W03**: The extension MUST be independently enabled/disabled in WorkItemsModule configuration.
- **FR-W04**: During import, the WorkItems NodeStructure extension MUST use `NodeTranslationTool.TranslatePath()` to translate `System.AreaPath` and `System.IterationPath` field values in each revision before writing to the target.

#### TeamsModule

- **FR-001**: System MUST implement `TeamsModule` conforming to the `IModule` contract (`ExportAsync`, `ImportAsync`, `ValidateAsync`).
- **FR-002**: `TeamsModule` has no `DependsOn` property. Module execution order is controlled entirely by the operator via configuration. The operator MUST configure TeamsModule to run after IdentitiesModule and NodeStructureModule (so identity mappings and node structures are available for team import) and before WorkItemsModule (so team-assigned paths are recorded to `ReferencedPathTracker` before work item export).
- **FR-003**: *(Merged into FR-002.)*
- **FR-004**: System MUST support five independently-enabled extensions: `TeamSettings`, `TeamIterations`, `TeamMembers`, `TeamCapacity`, and `NodeStructure`. The NodeStructure extension has two roles: (a) during export, records team-assigned area/iteration paths to `ReferencedPathTracker`; (b) during import, uses `NodeTranslationTool.TranslatePath()` to resolve paths.
- **FR-005**: The `NodeStructure` extension MUST use the shared `NodeTranslationTool` to resolve source area and iteration paths to target paths during import. During export, it records team-assigned paths to `ReferencedPathTracker`. This extension does NOT create nodes on the target — node creation is exclusively `NodeStructureModule.ImportAsync`'s responsibility. This extension operates on the team's assigned area paths, assigned iteration paths, default area path, and default iteration path.
- **FR-006**: The `TeamSettings` extension MUST capture and restore: backlog navigation levels, working days, default iteration path, default area path, and bug behaviour setting. Default paths are stored as source values; the `NodeStructure` extension resolves them to target values during import.
- **FR-007**: The `TeamMembers` extension MUST use `IIdentityMappingService` for resolving source member identities to target identities during import.
- **FR-008**: The `TeamCapacity` extension MUST export per-member capacity data (activities, hours per day, days off) for each iteration assigned to the team.
- **FR-009**: System MUST write all team artefacts to the `Teams/` folder in the package via `IArtefactStore`. Layout: `Teams/{team-slug}/team.json` — one JSON file per team containing all extension data as nested objects (settings, iterations, members, capacity, area paths). The team slug is generated by `TeamSlugGenerator` using this algorithm: (1) lowercase the team display name, (2) replace whitespace with hyphens, (3) strip all characters except `[a-z0-9\-]`, (4) collapse consecutive hyphens, (5) trim leading/trailing hyphens, (6) truncate to 100 characters, (7) if a collision exists (two teams produce the same slug), append `-2`, `-3`, etc. The original team name is preserved inside `team.json`.
- **FR-010**: System MUST maintain a cursor file at `.migration/Checkpoints/teams.cursor.json` for resumable export and import.
- **FR-011**: System MUST support two scope types: `"teams"` (with optional `filter` parameter for team name pattern matching) and `"all"` (all teams in the project).
- **FR-012**: System MUST implement all three connectors: **Simulated** (deterministic test data, no external connectivity), **AzureDevOpsServices** (REST API via .NET 10), and **TeamFoundationServer** (TFS Object Model via .NET 4.8 subprocess bridge).
- **FR-013**: `ValidateAsync` MUST verify that all team artefact files in the package contain required fields and match the declared schema version.
- **FR-014**: Import MUST be idempotent — re-running import against a target that already has the teams must produce the same result without errors or duplication.
- **FR-015**: Extensions are applied in a fixed, hardcoded order per team within the module: `TeamSettings` → `NodeStructure` → `TeamIterations` → `TeamMembers` → `TeamCapacity`. This order is controlled by the module implementation, not by operator configuration. This ordering ensures each extension's prerequisites exist before it executes (e.g., iterations must be assigned before capacity can be set).
- **FR-016**: The module MUST detect the source project's default team and map it to the target project's default team during import, regardless of name differences. Non-default teams are matched by name.
- **FR-017**: When the `NodeStructure` extension is disabled or the `NodeTranslationTool` is not configured, the module MUST use source paths as-is (identity mapping). If a source path does not exist in the target, the module MUST log a warning and skip that assignment.
- **FR-018**: All target API calls MUST use the platform's standard retry policy (exponential back-off, handling 429/5xx/408 responses).
- **FR-019**: The cursor MUST track per-team, per-extension progress. On resume, the module MUST re-apply remaining extensions for any team where not all extensions completed successfully.
- **FR-020**: The Simulated connector MUST generate a configurable number of teams (default: 5) with deterministic names, settings, members, and capacity data based on a seed value.

### Migration Lifecycle: Export → Prepare → Import

The migration follows a three-phase lifecycle:

1. **Export** — Capture source data into the package (Identities/, Nodes/, Teams/, WorkItems/).
2. **Prepare** — Run target-side discovery and validation: resolve target identities (build `mapping.json` candidates), verify target project exists, check node structure compatibility. The CLI `prepare` command sends a job to the Migration Agent that performs these checks. The operator reviews `unresolved.json` and makes decisions before proceeding.
   - **Output files**: `Identities/mapping.json` (auto-resolved candidate mappings), `Identities/unresolved.json` (identities that could not be matched).
   - **Detection**: Import detects whether Prepare has run by checking for the existence of `Identities/mapping.json` in the package via `IArtefactStore.ExistsAsync()`.
   - **Auto-prepare**: If `mapping.json` does not exist when `import` starts, the import MUST run the prepare phase automatically before proceeding.
   - **Failure mode**: If auto-prepare produces entries in `unresolved.json`, import MUST write a structured error to `IProgressSink` listing each unresolved identity and exit with `MigrationException("Prepare phase found {count} unresolved identities. Review Identities/unresolved.json and provide explicit mappings in mapping.json before re-running import.")`. Exit code: non-zero.
3. **Import** — Apply package data to the target.

This lifecycle ensures the operator has full visibility into potential issues (unresolved identities, missing paths) before any target writes occur.

### Key Entities

#### IdentitiesModule

- **IdentityDescriptor**: A source user or group identity. Key attributes: descriptor (AAD object ID, TFS SID, or Simulated ID), display name, UPN (if user), source type (user/group), origin (AAD/TFS/Simulated).
- **IdentityMapping**: An explicit source-to-target mapping provided by the operator. Key attributes: source descriptor, target descriptor.
- **UnresolvedIdentity**: An identity encountered during import that could not be resolved. Key attributes: source descriptor, display name, reason (no UPN match / no display name match / target directory unavailable).

#### NodeStructureModule

- **ClassificationNode**: An area or iteration path node. Key attributes: path (full path string), type (`Area` or `Iteration`), start date (iterations only), end date (iterations only), children (nested nodes).
- **SourceTree**: The complete classification tree from the source project, stored as `Nodes/source-tree.json`. Contains two root nodes (Area, Iteration) with all children.
- **ReferencedPath**: A unique area or iteration path actually used by work item revisions or team assignments. Accumulated in `Nodes/referenced-paths.json` by `ReferencedPathTracker` during export.

#### TeamsModule

- **Team**: A named organisational unit within a project. Key attributes: name, description, default iteration path, default area path, backlog settings, working days.
- **TeamIteration**: An association between a team and an iteration path. Attributes: team reference, iteration path, time frame (start/end dates if set).
- **TeamMember**: An association between a team and a user identity. Attributes: team reference, identity descriptor, isAdmin flag.
- **TeamCapacity**: Per-member capacity allocation for a specific iteration. Attributes: team reference, iteration reference, member identity, activities (name + capacity per day), days off (start/end ranges).
- **TeamAreaPath**: An association between a team and an area path. Attributes: team reference, area path, include-sub-areas flag, isDefault flag.

## Success Criteria *(mandatory)*

### Measurable Outcomes

#### IdentitiesModule

- **SC-I01**: All identity descriptors from a source project are exported to `Identities/descriptors.jsonl` with one entry per identity.
- **SC-I02**: Operator-provided `mapping.json` overrides are respected — explicit mappings take priority over automatic resolution.
- **SC-I03**: *(Outcome expectation — depends on customer data, not verifiable by automated tests.)* Automatic UPN/display name matching resolves at least 90% of identities in a typical enterprise migration.
- **SC-I04**: All unresolved identities are recorded in `Identities/unresolved.json` with clear reason codes.
- **SC-I05**: Export and import are fully resumable — interrupting and re-running produces the same resolution index.
- **SC-I06**: All three connectors (Simulated, AzureDevOpsServices, TeamFoundationServer) pass the same acceptance test scenarios.

#### NodeStructureModule

- **SC-N01**: The complete area and iteration tree from a source project is exported to `Nodes/source-tree.json` with correct hierarchy and dates.
- **SC-N02**: When `ReplicateSourceTree: true`, the entire current source tree is created on the target (missing nodes created, existing nodes skipped).
- **SC-N03**: When `AutoCreateNodes: true`, only paths in `referenced-paths.json` are created on the target (historical paths from revisions).
- **SC-N04**: When both modes are true, the union of current tree + historical referenced paths is created.
- **SC-N05**: Import is fully resumable — interrupting and re-running creates only remaining nodes.
- **SC-N06**: All three connectors (Simulated, AzureDevOpsServices, TeamFoundationServer) pass the same acceptance test scenarios.

#### TeamsModule

- **SC-001**: All teams from a source project with up to 50 teams are exported and imported within a single migration run with zero manual intervention.
- **SC-002**: 100% of team settings (backlog levels, working days, bug behaviour) are faithfully restored on the target after import.
- **SC-003**: Team iteration assignments are correctly mapped using the NodeTranslationTool — operators can verify by checking the target team's sprint board shows the expected iterations.
- **SC-004**: Team member import achieves at least 95% membership restoration (accounting for identities that may not exist in the target tenant).
- **SC-005**: Capacity data for active sprints is preserved — operators can verify by checking the target team's capacity view for current and upcoming iterations.
- **SC-006**: Export and import are fully resumable — interrupting and re-running produces the same end state as an uninterrupted run.
- **SC-007**: All three connectors (Simulated, AzureDevOpsServices, TeamFoundationServer) pass the same acceptance test scenarios.

## Clarifications

### Session 2026-04-27

- Q: When both `ReplicateSourceTree` and `AutoCreateNodes` are true, what should happen? → A: They are complementary, not conflicting. `ReplicateSourceTree` creates the current source tree on the target. `AutoCreateNodes` creates historical nodes from revisions (paths that no longer exist in the current tree but are referenced in work item history). Both should execute together — the union of current tree + historical referenced paths.

- Q: What file layout should the `Teams/` folder use in the package? → A: One JSON file per team: `Teams/{team-slug}/team.json` with extensions as nested objects (settings, iterations, members, capacity, area paths all within one file per team).

- Q: When does `ReferencedPathTracker` write `referenced-paths.json` to disk? → A: All exports complete before any imports begin (architectural constraint). The tracker flushes at the end of each module's export. By the time `NodeStructureModule.ImportAsync` runs, the file is guaranteed to contain all paths from all modules.

- Q: How should team names with special characters be handled in `Teams/{team-slug}/` folder names? → A: Sanitise to filesystem-safe slug: replace invalid chars, lowercase, truncate to 100 chars. Use "team slug" for filesystem paths only. Use "team name" (the display name from the API) in all other contexts (UI, logs, JSON payloads, documentation).

- Q: When importing team capacity for past (closed) iterations, what should happen if the target rejects? → A: All past iterations are exported and must be mapped for work item import. Past iteration paths are translated via `NodeTranslationTool`. If a path cannot be resolved, log a warning and skip that capacity entry. Capacity import is attempted for all iterations (current and past).

- Q: Should `NodeStructureTool` be renamed to better separate it from `NodeStructureModule`? → A: Rename to `NodeTranslationTool` / `INodeTranslationTool`. The tool only does path translation (regex mappings), not structure management. The rename makes the separation from `NodeStructureModule` (which owns data) immediately obvious.

## Assumptions

- The `IdentitiesModule` is included in this spec (see User Story 0 and FR-I01 through FR-I12). It is a hard prerequisite for TeamsModule and all downstream modules.
- `IIdentityMappingService` is an existing interface in the codebase (in `Abstractions.Agent`). The IdentitiesModule provides the concrete implementation that builds the resolution index from package data.
- The `Identities/` folder layout is defined by `.agents/context/identity-and-mapping.md` — this spec implements that specification.
- The `NodeTranslationTool` is already implemented and supports both area and iteration path resolution/creation — no new tool development is needed. It is integrated into TeamsModule as the `NodeStructure` extension.
- The `Teams/` folder in the package format is reserved (already listed in `package-format.md`) but its internal structure is to be defined by this module.
- Team capacity import for past (closed) iterations is attempted for all iterations. Past iteration paths are translated via `NodeTranslationTool.TranslatePath()`. If the target rejects a capacity update (e.g., for closed iterations), the module logs a warning and skips that entry. If a past iteration path cannot be resolved, the module logs a warning and skips that capacity entry.
- TFS Object Model supports team enumeration and settings retrieval via `TfsTeamService` and related APIs. If specific capabilities (e.g., capacity) are not available via the TFS OM, those extensions will log a structured warning and skip gracefully.
- The module does not migrate team project-level permissions — that is the responsibility of `PermissionsModule`.
- Module execution order is determined by configuration (modules are properties, not an ordered list). The operator controls which modules run and in what order.
- Every ADO project has a default team that cannot be deleted. The module handles this by detecting and mapping the default team by role rather than by name.
- Architecture documents read: `agents.md`, `docs/architecture.md`, `docs/modules.md`, `docs/configuration.md`, `.agents/guardrails/system-architecture.md`, `.agents/guardrails/module-template.md`, `.agents/context/package-format.md`, `analysis/proposed-features.md` (M5 and T2 sections), `.agents/context/identity-and-mapping.md`.

## Connector Coverage

### Features

| Feature | Type | Abstraction | Simulated | AzureDevOps | TFS |
|---|---|---|---|---|---|
| `identities.export` | `export` | `IModule.ExportAsync` | Required | Required | Required |
| `identities.import` | `import` | `IModule.ImportAsync` | Required | Required | Required |
| `identities.validate` | `validation` | `IModule.ValidateAsync` | Required | Required | Required |
| `nodes.export` | `export` | `IModule.ExportAsync` | Required | Required | Required |
| `nodes.import` | `import` | `IModule.ImportAsync` | Required | Required | Required |
| `nodes.validate` | `validation` | `IModule.ValidateAsync` | Required | Required | Required |
| `nodes.translate` | `tool` | `INodeTranslationTool.TranslatePath` | Required | Required | Required |
| `workitems.ext.nodestructure.export` | `export` | WorkItems `NodeStructure` extension | Required | Required | Required |
| `workitems.ext.nodestructure.import` | `import` | WorkItems `NodeStructure` extension | Required | Required | Required |
| `teams.export` | `export` | `IModule.ExportAsync` | Required | Required | Required |
| `teams.import` | `import` | `IModule.ImportAsync` | Required | Required | Required |
| `teams.validate` | `validation` | `IModule.ValidateAsync` | Required | Required | Required |
| `teams.export.settings` | `export` | `TeamSettings` extension | Required | Required | Required |
| `teams.export.iterations` | `export` | `TeamIterations` extension | Required | Required | Required |
| `teams.export.members` | `export` | `TeamMembers` extension | Required | Required | Required |
| `teams.export.capacity` | `export` | `TeamCapacity` extension | Required | Required | Exempt |
| `teams.ext.nodestructure.export` | `export` | Teams `NodeStructure` extension | Required | Required | Required |
| `teams.ext.nodestructure.import` | `import` | Teams `NodeStructure` extension | Required | Required | Required |

### Acceptance Scenario Mapping

| Feature | Connector | Scenario(s) |
|---|---|---|
| `identities.export` | Simulated | US0-S1: Export 50 users + 3 groups to `descriptors.jsonl` (Simulated generates deterministic identities) |
| `identities.export` | AzureDevOps | US0-S1: Export identity descriptors via ADO Graph/Identity REST API |
| `identities.export` | TFS | US0-S1: Export identity descriptors via TFS Identity Service (OM) through subprocess bridge |
| `identities.import` | Simulated | US0-S2/S3/S4: Load mapping, resolve identities (all deterministic in Simulated) |
| `identities.import` | AzureDevOps | US0-S2/S3/S4: Load mapping, resolve via AAD Graph/UPN matching |
| `identities.import` | TFS | US0-S2/S3/S4: Load mapping, resolve via TFS Identity Service (OM) |
| `identities.validate` | Simulated | US0: Validate `descriptors.jsonl` schema and required fields |
| `identities.validate` | AzureDevOps | US0: Validate `descriptors.jsonl` schema and required fields |
| `identities.validate` | TFS | US0: Validate `descriptors.jsonl` schema and required fields |
| `nodes.export` | Simulated | US0b-S1: Export deterministic tree (10 area + 8 iteration by default) to `source-tree.json` |
| `nodes.export` | AzureDevOps | US0b-S1: Export area/iteration tree via ADO Classification Nodes REST API |
| `nodes.export` | TFS | US0b-S1: Export area/iteration tree via TFS `ICommonStructureService` (OM) through subprocess bridge |
| `nodes.import` | Simulated | US0b-S2/S3: Create nodes on Simulated target (idempotent) |
| `nodes.import` | AzureDevOps | US0b-S2/S3: Create nodes via ADO Classification Nodes REST API (idempotent) |
| `nodes.import` | TFS | US0b-S2/S3: Create nodes via TFS `ICommonStructureService` (OM) through subprocess bridge (idempotent) |
| `nodes.validate` | Simulated | US0b: Validate `source-tree.json` schema |
| `nodes.validate` | AzureDevOps | US0b: Validate `source-tree.json` schema |
| `nodes.validate` | TFS | US0b: Validate `source-tree.json` schema |
| `nodes.translate` | All | US0b-S2: `NodeTranslationTool.TranslatePath()` is connector-agnostic (stateless, in-process) |
| `workitems.ext.nodestructure.export` | Simulated | US0c-S1: Scan simulated revisions for area/iteration paths, record to `ReferencedPathTracker` |
| `workitems.ext.nodestructure.export` | AzureDevOps | US0c-S1: Scan ADO revisions for area/iteration paths, record to `ReferencedPathTracker` |
| `workitems.ext.nodestructure.export` | TFS | US0c-S1: Scan TFS revisions for area/iteration paths, record to `ReferencedPathTracker` |
| `workitems.ext.nodestructure.import` | Simulated | US0c: Translate `System.AreaPath`/`System.IterationPath` in each revision via `NodeTranslationTool` |
| `workitems.ext.nodestructure.import` | AzureDevOps | US0c: Translate path fields in each revision via `NodeTranslationTool` |
| `workitems.ext.nodestructure.import` | TFS | US0c: Translate path fields in each revision via `NodeTranslationTool` |
| `teams.export` | Simulated | US1-S1: Export teams with settings from Simulated source |
| `teams.export` | AzureDevOps | US1-S1: Export teams via ADO Teams REST API |
| `teams.export` | TFS | US1-S1: Export teams via TFS `TfsTeamService` (OM) through subprocess bridge |
| `teams.import` | Simulated | US1-S2/S3: Import teams into Simulated target (idempotent) |
| `teams.import` | AzureDevOps | US1-S2/S3: Import teams via ADO Teams REST API (idempotent) |
| `teams.import` | TFS | US1-S2/S3: Import teams via TFS OM through subprocess bridge (idempotent) |
| `teams.export.settings` | Simulated | US1-S1: Backlog settings, working days, bug behaviour |
| `teams.export.settings` | AzureDevOps | US1-S1: REST API `_apis/work/teamsettings` |
| `teams.export.settings` | TFS | US1-S1: TFS OM team configuration service |
| `teams.export.iterations` | Simulated | US2-S1: Team iteration assignments |
| `teams.export.iterations` | AzureDevOps | US2-S1: REST API `_apis/work/teamsettings/iterations` |
| `teams.export.iterations` | TFS | US2-S1: TFS OM `ICommonStructureService` |
| `teams.export.members` | Simulated | US3-S1: Team membership with admin flags |
| `teams.export.members` | AzureDevOps | US3-S1: REST API `_apis/projects/{project}/teams/{team}/members` |
| `teams.export.members` | TFS | US3-S1: TFS OM `TfsTeamService.GetTeamMembers()` |
| `teams.export.capacity` | Simulated | US4-S1: Per-member capacity per iteration |
| `teams.export.capacity` | AzureDevOps | US4-S1: REST API `_apis/work/teamsettings/iterations/{iteration}/capacities` |
| `teams.export.capacity` | TFS | Exempt: TFS OM does not expose capacity API prior to TFS 2017 Update 2 REST API |
| `teams.ext.nodestructure.export` | Simulated | US5-S1: Record team area/iteration assignments to `ReferencedPathTracker` |
| `teams.ext.nodestructure.export` | AzureDevOps | US5-S1: Record team area/iteration assignments to `ReferencedPathTracker` |
| `teams.ext.nodestructure.export` | TFS | US5-S1: Record team area/iteration assignments to `ReferencedPathTracker` |
| `teams.ext.nodestructure.import` | Simulated | US5-S2/S3: Translate team area/iteration paths via `NodeTranslationTool` |
| `teams.ext.nodestructure.import` | AzureDevOps | US5-S2/S3: Translate team area/iteration paths via `NodeTranslationTool` |
| `teams.ext.nodestructure.import` | TFS | US5-S2/S3: Translate team area/iteration paths via `NodeTranslationTool` |

### TFS Exemptions

| Feature | Reason | Graceful Behaviour |
|---|---|---|
| `teams.export.capacity` | The TFS Object Model does not expose a capacity API for TFS versions prior to TFS 2017 Update 2. The REST API for capacity was introduced in TFS 2017.2 (`_apis/work/teamsettings/iterations/{id}/capacities`). For older TFS instances, capacity data is not programmatically accessible. | The TFS connector emits a `ProgressEvent` with `EventKind.Warning` explaining that team capacity export is not supported for this TFS version. A structured `Warning` log is emitted. The extension is skipped gracefully — no `NotImplementedException` is thrown. For TFS 2017.2+ the connector MAY implement capacity via REST if the API version supports it, but this is best-effort. |

### Gaps

None. All features have complete connector coverage in the specification.

### Verdict

**PASS** — All features have complete connector coverage in the specification. One TFS exemption is documented with specific API limitation rationale and graceful degradation behaviour.
