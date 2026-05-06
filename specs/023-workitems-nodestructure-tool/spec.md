# Feature Specification: WorkItemsModule — NodeStructure Tool

**Feature Branch**: `023-workitems-nodestructure-tool`  
**Created**: 2026-05-13  
**Status**: Draft  
**Input**: `analysis/proposed-features.md` — M2: WorkItemsModule — NodeStructure Tool

---

## Architecture References

The following canonical documents were read before drafting this specification:

| Document | Status |
|---|---|
| `agents.md` | Confirmed accurate |
| `docs/module-development-guide.md` | Confirmed accurate — Tool Resolution section referenced; `NodeStructureTool` not yet described → **discrepancy logged** |
| `docs/configuration-reference.md` | Confirmed accurate — `Tools` section documents `FieldTransform`; `NodeStructure` tool not yet documented → **discrepancy logged** |
| `.agents/guardrails/architecture-boundaries.md` | Confirmed accurate |
| `.agents/guardrails/workitems-rules.md` | Confirmed accurate |
| `.agents/guardrails/migration-rules.md` | Confirmed accurate |
| `.agents/context/workitems-format-summary.md` | Confirmed accurate |
| `azure-devops-migration-tools` — `TfsNodeStructureTool.cs` | Cross-referenced for behavioural completeness — iteration dates, auto project-name swap, unanchored paths, bulk pre-collection |

Discrepancies logged in: `specs/023-workitems-nodestructure-tool/discrepancies.md`

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Map Area and Iteration Paths Across Projects (Priority: P1)

A migration operator is moving work items from one Azure DevOps organisation (or project) to another where the area and iteration path structure has been renamed or restructured. Without path remapping, the import stage fails or silently places every work item at the root node, losing organisational structure.

The operator declares an ordered list of regex-based mapping rules (each with a `Match` pattern and a `Replacement` string) so that `System.AreaPath` and `System.IterationPath` values in `revision.json` are translated to the correct target path before the API call is made.

**Why this priority**: Path resolution is a prerequisite for a successful cross-project import. Without it, nearly every cross-project or cross-organisation migration will produce incorrect data. All other tool features build on this core capability.

**Independent Test**: Configure a single source → target area path mapping, run an import of work items whose `revision.json` contains the source path, and verify that the created work items carry the mapped target path value.

**Acceptance Scenarios**:

1. **Given** a `revision.json` contains `System.AreaPath: "OldOrg\\OldProject\\Team A"` and the tool has an `AreaPathMappings` entry `{ Match: "^OldOrg\\\\OldProject\\\\Team A$", Replacement: "NewOrg\\NewProject\\Team A - Migrated" }`, **When** the import stage applies the tool, **Then** the work item is written to the target with `System.AreaPath: "NewOrg\\NewProject\\Team A - Migrated"`.

2. **Given** a `revision.json` contains `System.IterationPath: "OldOrg\\OldProject\\Sprint 1"` and the tool has an `IterationPathMappings` entry `{ Match: "^OldOrg\\\\OldProject\\\\Sprint 1$", Replacement: "NewOrg\\NewProject\\Sprint 1" }`, **When** the import stage applies the tool, **Then** the work item is written with `System.IterationPath: "NewOrg\\NewProject\\Sprint 1"`.

3. **Given** a `revision.json` contains a path that no `AreaPathMappings` or `IterationPathMappings` rule matches, and the path begins with the source project name, **When** the import stage applies the tool, **Then** the source project name prefix is automatically substituted with the target project name (auto-swap). If the path does not begin with the source project name it is passed through unchanged.

4. **Given** a `revision.json` contains a path that no `AreaPathMappings` or `IterationPathMappings` rule matches, and the path does not begin with the source project name, **When** the import stage applies the tool, **Then** the original path value is preserved without modification (pass-through — the path is not anchored in the source project).

5. **Given** the `NodeStructure` tool is not declared under `MigrationPlatform.Tools`, **When** a module import runs, **Then** no path translation is applied and behaviour is identical to the pre-tool state.

---

### User Story 2 — Auto-Create Missing Nodes in the Target (Priority: P2)

An operator has declared correct path mappings but the target project does not yet contain all required area or iteration nodes. The import currently fails with an ADO API error when a work item is patched with a path that does not exist in the target classification tree.

The operator wants the tool to automatically call the ADO Classification Nodes API to create any missing node before it is referenced, removing the need to manually pre-configure the target tree.

**Why this priority**: Auto-create removes the most common manual remediation step before import. Without it, operators must pre-configure every target node — a fragile, error-prone step on large projects.

**Independent Test**: Run an import against a target project with no area or iteration nodes (beyond the root), with `AutoCreateNodes: true`, and confirm that all required nodes are created and work items are placed under them.

**Acceptance Scenarios**:

1. **Given** `AutoCreateNodes: true` and the target does not have the node `"NewProject\\Team A"`, **When** the import encounters a revision whose translated area path is `"NewProject\\Team A"`, **Then** the node is created via the ADO Classification Nodes API before the work item patch is attempted.

2. **Given** `AutoCreateNodes: true` and the node already exists, **When** the import encounters a revision referencing that node, **Then** no duplicate creation is attempted (the creation call is idempotent — check before POST).

3. **Given** `AutoCreateNodes: false` and a translated path does not exist in the target, **When** the import encounters that revision, **Then** the tool does not attempt creation, and the import proceeds according to the `SkipOnUnresolvableArea` / `SkipOnUnresolvableIteration` settings.

4. **Given** `AutoCreateNodes: true` and a nested path `"Project\\Area\\Sub-Area"` is required, **When** the tool resolves the path, **Then** all intermediate nodes are created in order (parent before child).

---

### User Story 3 — Skip or Fail Gracefully on Unresolvable Paths (Priority: P3)

When a path cannot be resolved (no mapping entry, node creation disabled or failed, and node does not exist in target), the import currently fails hard. Operators running large migrations need a way to configure whether the tool skips the affected revision or fails the import.

**Why this priority**: Graceful degradation on bad paths is important for large migrations where a small percentage of items may have unusual paths. It allows the bulk of the migration to succeed while flagging items for manual review.

**Independent Test**: Configure `SkipOnUnresolvableArea: true`, present a revision whose area path cannot be resolved, and verify the revision is skipped with a warning emitted while the import continues.

**Acceptance Scenarios**:

1. **Given** `SkipOnUnresolvableArea: true` and a revision whose translated area path cannot be resolved or created, **When** the import applies the tool, **Then** the revision is skipped (not written), a warning progress event is emitted, and import continues to the next revision.

2. **Given** `SkipOnUnresolvableIteration: true` and a revision whose translated iteration path cannot be resolved, **When** the import applies the tool, **Then** the revision is skipped, a warning is emitted, and import continues.

3. **Given** both skip flags are `false` and a path cannot be resolved, **When** the import applies the tool, **Then** the import emits a descriptive error identifying the unresolvable path and affected revision, and fails.

---

### User Story 4 — Replicate All Source Nodes to the Target (Priority: P4)

Operators performing full project migrations want the option to pre-populate the target classification tree with all nodes from the source project, regardless of whether any work item currently references them. This enables a like-for-like node tree migration before or alongside work item migration.

**Why this priority**: Useful for complete project structure migration but not required for incremental or revision-only scenarios. The `AutoCreateNodes` flag already handles the common on-demand case.

> **Architecture note**: To comply with the Source → Package → Target model (import must read only from the package, not from the live source), the **export** phase always captures the full source classification tree into `Nodes/source-tree.json` in the package. The import phase reads from this artifact — never from the live source. `ReplicateSourceTree` is therefore a two-phase capability: export captures the tree; import replicates it.

**Independent Test**: Configure a scenario with `ReplicateSourceTree: true`, run a full export (which captures the classification tree artifact), then run import against a target with no pre-existing nodes, and verify all source nodes appear in the target before any work item is written.

**Acceptance Scenarios**:

1. **Given** `ReplicateSourceTree: true` and an exported package containing `Nodes/source-tree.json`, **When** the import phase initialises before processing any revision folder, **Then** all area and iteration nodes recorded in `source-tree.json` are created (or confirmed present) in the target project, processed one node at a time.

2. **Given** `ReplicateSourceTree: false`, **When** the module imports work items, **Then** `Nodes/source-tree.json` (if present) is ignored, and only nodes referenced by work item revisions are created on-demand via `AutoCreateNodes`.

3. **Given** `ReplicateSourceTree: true` and the export was run, **When** the import is interrupted after replicating 50 of 200 nodes and then resumed, **Then** the tool skips nodes already recorded as confirmed-present in the node creation checkpoint, and continues from node 51.

---

### User Story 5 — Override Localised Node Names (Priority: P5)

Azure DevOps installations in non-English locales name the root classification nodes differently (e.g. `"Área"` in Spanish, `"Bereich"` in German). Path matching fails because the source path begins with a localised root name that does not match the target.

The operator declares a language override so that localised root node names in both source and target paths are normalised before any mapping is applied.

**Why this priority**: Affects cross-locale migrations only — a minority scenario, but a hard blocker when encountered.

**Independent Test**: Configure `AreaLanguageOverride: "Area"`, present a source revision with `System.AreaPath: "Área\\ProjectX\\Team A"`, and verify the tool normalises the root segment before path lookup.

**Acceptance Scenarios**:

1. **Given** `AreaLanguageOverride: "Area"` and a revision with `System.AreaPath: "Área\\ProjectX\\Team A"`, **When** the tool processes the path, **Then** the root segment is normalised to `"Area"` before the mapping lookup proceeds.

2. **Given** `IterationLanguageOverride: "Iteration"` and a revision with `System.IterationPath: "Iteración\\ProjectX\\Sprint 1"`, **When** the tool processes the path, **Then** the root segment is normalised to `"Iteration"` before mapping rules are applied.

---

### User Story 6 — Preserve Iteration Node Start/Finish Dates (Priority: P4)

When an operator replicates the full iteration tree from source to target (`ReplicateSourceTree: true`), the sprint schedule dates (start and finish) defined on each iteration node in the source project must be preserved in the target. Without this, the target project has the correct iteration hierarchy but loses all sprint scheduling information.

**Why this priority**: Sprint date loss is silent data loss. It does not block migration but degrades the usefulness of the target for planning. Rated P4 (same as `ReplicateSourceTree`) because it only applies to that feature.

**Independent Test**: Export a source project whose iteration tree contains nodes with `StartDate` and `FinishDate` set. Import with `ReplicateSourceTree: true`. Verify that each iteration node in the target has the correct start and finish dates.

**Acceptance Scenarios**:

1. **Given** `ReplicateSourceTree: true` and `Nodes/source-tree.json` contains an iteration node with `startDate: "2024-01-15"` and `finishDate: "2024-01-28"`, **When** the import creates that iteration node in the target, **Then** the node's start and finish dates are set to the exported values.

2. **Given** `ReplicateSourceTree: true` and a source area node (not an iteration), **When** the import creates that area node, **Then** no date-setting API call is made (area nodes have no dates).

3. **Given** `ReplicateSourceTree: true` and an iteration node in `Nodes/source-tree.json` has no `startDate` or `finishDate` (they were null on the source), **When** the import creates that node, **Then** the node is created without dates and no error is emitted.

---

### User Story 7 — Export-Time Path Discovery (Priority: P1)

During work item export, the system must maintain a running set of all distinct `System.AreaPath` and `System.IterationPath` values encountered across all exported work item revisions. This eliminates the need for an import-time pre-collection scan of the package: the export already knows which paths are referenced.

The discovered paths are saved to the package at `Nodes/referenced-paths.json`. The set is maintained in memory and persisted incrementally — each time a previously-unseen path is encountered, the artifact is updated.

**Why this priority**: P1 because the referenced-paths artifact is consumed by the import-time node creation pass (FR-024). Without it, the import must perform an expensive scan of all revision folders to discover which paths need to exist in the target. Capturing this during export is both more efficient and more correct (it captures paths from revisions that may later be filtered).

**Independent Test**: Export a set of work items with known area and iteration paths. Verify that `Nodes/referenced-paths.json` in the package contains exactly the distinct paths referenced by all exported revisions.

**Acceptance Scenarios**:

1. **Given** a work item export is running, **When** a `revision.json` is written containing `System.AreaPath: "Project\\Team A"` and that path has not been seen before, **Then** the path is added to the in-memory set and `Nodes/referenced-paths.json` is updated in the package.

2. **Given** a work item export is running, **When** a `revision.json` is written containing `System.AreaPath: "Project\\Team A"` and that path has already been recorded, **Then** the in-memory set is unchanged and no redundant write occurs.

3. **Given** a work item export completes, **When** the operator inspects `Nodes/referenced-paths.json`, **Then** it contains the distinct union of all `System.AreaPath` and `System.IterationPath` values from all exported revisions, with no duplicates.

4. **Given** the export was interrupted and resumed, **When** the resumed export encounters a path already in `Nodes/referenced-paths.json`, **Then** the path is recognised as already discovered and no duplicate entry is created.

---



- What happens when a source path is an empty string or null? → Treated as unresolvable; skip/fail behaviour applies according to configuration.
- What happens when a path does not begin with the source project name (unanchored path — e.g. a cross-project link whose area/iteration belongs to a third project)? → The auto-swap does not apply. The path is passed through unchanged. If the pass-through value does not exist in the target, skip/fail behaviour applies. The warning MUST identify the path as an external path.
- What happens when the ADO Classification Nodes API returns a transient error (5xx, 408, 429) during node creation? → Retried with exponential back-off per FR-022. Persistent transient failures surface as import errors.
- What happens when the ADO Classification Nodes API returns 401 or 403? → Non-retryable; the import fails immediately with a message identifying the missing permission. The operator must fix service account permissions before retrying.
- What happens when an `AreaPathMappings` or `IterationPathMappings` `Match` pattern is invalid regex? → ValidateAsync (FR-004a/FR-021) flags this as a configuration error. At import time, the tool fails fast on initialisation.
- What happens when a mapping rule's `Replacement` value, after substitution, produces a target path that is empty or contains characters illegal in ADO node names (`\`, `/`, `$`, `?`, `*`, `"`, `:`, `>`, `<`, `|`, `#`, `%`, `+`, control characters)? → ValidateAsync (FR-021) flags this as a configuration error. At import time, the revision is treated as having an unresolvable path and skip/fail behaviour applies.
- What happens when a source path's translated target path (after regex replacement) exceeds the maximum classification node depth supported by the target ADO instance? → The node creation attempt fails; the revision is treated as having an unresolvable path and skip/fail behaviour applies. ValidateAsync should flag paths whose nesting depth exceeds the known ADO limit.
- What happens when two revisions in different work items reference the same previously-unresolved path concurrently (future parallel worker scenario)? → The node-creation checkpoint (FR-016a) is the source of truth; idempotent check-before-POST (FR-007) prevents duplicate creation.
- What happens when the tool is declared but not referenced by any extension? → The declaration is inert; no mapping, no node creation, no export artifact is produced.
- What happens when `ReplicateSourceTree: true` is configured but `Nodes/source-tree.json` is absent from the package (e.g., a legacy package exported before this feature)? → The import logs a warning and skips the bulk replication step; `AutoCreateNodes` (if enabled) handles on-demand creation during revision processing.
- What happens when `ValidateAsync` encounters a revision where `System.AreaPath` is absent (field was unchanged in that revision)? → ValidateAsync MUST collect AreaPath/IterationPath values across ALL revisions of ALL work items, not only the first or last revision. A path not present in the map is flagged regardless of which revision introduced it.
- What happens when `SetIterationDates` fails for a created node? → The failure is logged as a warning (non-blocking); the node is still present and the import continues. Date loss is surfaced in the progress log for operator review.
- What happens when `Nodes/referenced-paths.json` exists in the package? → The import-time pre-collection pass (FR-024) reads discovered paths from this artifact instead of scanning all `revision.json` files, significantly reducing import startup time.

---

## Observability

### Operations

| Name | Type | Entry Point | Dependencies |
|---|---|---|---|
| `nodes.export.tree` | module | `ClassificationTreeCapture` | `IClassificationTreeReader` (source ADO API), `IArtefactStore` (write `Nodes/source-tree.json`) |
| `nodes.export.discover` | module | `ReferencedPathTracker` | `IArtefactStore` (write `Nodes/referenced-paths.json`) |
| `nodes.import.replicate` | module | `NodeEnsurer` (replicate phase) | `IArtefactStore` (read `Nodes/source-tree.json`), `INodeCreator` (target ADO API), `IStateStore` (checkpoint) |
| `nodes.import.precollect` | module | `NodeEnsurer` (pre-collect phase) | `IArtefactStore` (read `Nodes/referenced-paths.json` or revision folders), `INodeCreator` (target ADO API) |
| `nodes.import.translate` | module | `NodeStructureTool.TranslatePath()` | None (pure, in-memory) |
| `nodes.validate` | module | `NodeStructureValidator.ValidateAsync()` | `IArtefactStore` (read `Nodes/referenced-paths.json` or revision folders) |

### Operator Decisions

| Operation | Decision | Question |
|---|---|---|
| `nodes.export.tree` | Is it working? | Did the source tree capture complete successfully? |
| `nodes.export.tree` | Is it fast enough? | How long did the source tree API enumeration take? |
| `nodes.export.tree` | Is it correct? | How many area and iteration nodes were captured? |
| `nodes.export.discover` | Is it working? | Are referenced paths being discovered during export? |
| `nodes.export.discover` | Is it correct? | How many distinct area/iteration paths were found? |
| `nodes.import.replicate` | Is it working? | Are source tree nodes being replicated to the target? |
| `nodes.import.replicate` | Is it fast enough? | How long does each node creation take? Is bulk replication within SLO? |
| `nodes.import.replicate` | Is it overloaded? | Are we hitting ADO API rate limits during replication? |
| `nodes.import.replicate` | What failed? | Which specific node creations failed and why? |
| `nodes.import.precollect` | Is it working? | Did the pre-collection pass find and create all required nodes? |
| `nodes.import.precollect` | Is it fast enough? | How long did the pre-collection scan + bulk create take? |
| `nodes.import.precollect` | Is it overloaded? | How many nodes are being created concurrently? |
| `nodes.import.precollect` | What failed? | Which node creation failed during pre-collection? |
| `nodes.import.translate` | Is it working? | Are path translations succeeding? |
| `nodes.import.translate` | What failed? | Which paths are unresolvable (no map match, no auto-swap)? |
| `nodes.import.translate` | Is it correct? | What proportion of paths matched by map vs auto-swap vs external? |
| `nodes.validate` | Is it working? | Did validation complete? |
| `nodes.validate` | Is it fast enough? | How long did the validation scan take? |
| `nodes.validate` | Is it correct? | How many unmapped/external/malformed paths were found? |

### Metrics

All metrics use the `Migration` meter (`WellKnownMeterNames.Migration`). New constants will be added to `WellKnownMetricNames`.

| Metric Name | Instrument | Unit | Operation | Decision |
|---|---|---|---|---|
| `migration.nodes.export.tree.count` | `Counter<long>` | `{node}` | `nodes.export.tree` | Is it working? / Is it correct? |
| `migration.nodes.export.tree.duration_ms` | `Histogram<double>` | `ms` | `nodes.export.tree` | Is it fast enough? |
| `migration.nodes.export.tree.errors` | `Counter<long>` | `{operation}` | `nodes.export.tree` | What failed? |
| `migration.nodes.export.discover.count` | `Counter<long>` | `{path}` | `nodes.export.discover` | Is it working? / Is it correct? |
| `migration.nodes.import.replicate.count` | `Counter<long>` | `{node}` | `nodes.import.replicate` | Is it working? |
| `migration.nodes.import.replicate.duration_ms` | `Histogram<double>` | `ms` | `nodes.import.replicate` | Is it fast enough? |
| `migration.nodes.import.replicate.errors` | `Counter<long>` | `{node}` | `nodes.import.replicate` | What failed? |
| `migration.nodes.import.replicate.skipped` | `Counter<long>` | `{node}` | `nodes.import.replicate` | Is it working? (resumed) |
| `migration.nodes.import.replicate.in_flight` | `UpDownCounter<long>` | `{node}` | `nodes.import.replicate` | Is it overloaded? |
| `migration.nodes.import.precollect.count` | `Counter<long>` | `{node}` | `nodes.import.precollect` | Is it working? |
| `migration.nodes.import.precollect.duration_ms` | `Histogram<double>` | `ms` | `nodes.import.precollect` | Is it fast enough? |
| `migration.nodes.import.precollect.errors` | `Counter<long>` | `{node}` | `nodes.import.precollect` | What failed? |
| `migration.nodes.import.precollect.in_flight` | `UpDownCounter<long>` | `{node}` | `nodes.import.precollect` | Is it overloaded? |
| `migration.nodes.import.translate.count` | `Counter<long>` | `{path}` | `nodes.import.translate` | Is it working? |
| `migration.nodes.import.translate.unresolvable` | `Counter<long>` | `{path}` | `nodes.import.translate` | What failed? |
| `migration.nodes.import.translate.map_hit` | `Counter<long>` | `{path}` | `nodes.import.translate` | Is it correct? |
| `migration.nodes.import.translate.autoswap_hit` | `Counter<long>` | `{path}` | `nodes.import.translate` | Is it correct? |
| `migration.nodes.import.translate.external` | `Counter<long>` | `{path}` | `nodes.import.translate` | Is it correct? |
| `migration.nodes.validate.duration_ms` | `Histogram<double>` | `ms` | `nodes.validate` | Is it fast enough? |
| `migration.nodes.validate.unmapped_paths` | `Counter<long>` | `{path}` | `nodes.validate` | Is it correct? |
| `migration.nodes.validate.external_paths` | `Counter<long>` | `{path}` | `nodes.validate` | Is it correct? |
| `migration.nodes.validate.malformed_targets` | `Counter<long>` | `{path}` | `nodes.validate` | Is it correct? |

### Traces

All spans use the `Migration` ActivitySource (`WellKnownActivitySourceNames.Migration`).

| Component | Span Name | Tags | Parent | Decision |
|---|---|---|---|---|
| `ClassificationTreeCapture` | `nodes.export.tree` | `job.id`, `operation=export`, `module=WorkItems` | `workitems.export` (existing) | Is it working? / Is it fast enough? |
| `ReferencedPathTracker` | `nodes.export.discover` | `job.id`, `operation=export`, `module=WorkItems` | `workitems.export` (existing) | Is it working? |
| `NodeEnsurer` | `nodes.import.replicate` | `job.id`, `operation=import`, `module=WorkItems`, `nodes.total` | `workitems.import` (existing) | Is it working? / Is it fast enough? |
| `NodeEnsurer` | `nodes.import.replicate.node` | `node.path`, `node.type` (area/iteration) | `nodes.import.replicate` | Where is it slow? / What failed? |
| `NodeEnsurer` | `nodes.import.precollect` | `job.id`, `operation=import`, `module=WorkItems` | `workitems.import` (existing) | Is it working? / Is it fast enough? |
| `NodeEnsurer` | `nodes.import.precollect.node` | `node.path`, `node.type` | `nodes.import.precollect` | Where is it slow? / What failed? |
| `AzureDevOpsNodeCreator` | `nodes.api.ensure` | `node.path`, `node.type`, `http.status_code` | `nodes.import.replicate.node` or `nodes.import.precollect.node` | Where is it slow? / What failed? |
| `AzureDevOpsNodeCreator` | `nodes.api.set_dates` | `node.path`, `http.status_code` | `nodes.import.replicate.node` | Where is it slow? |
| `AzureDevOpsClassificationTreeReader` | `nodes.api.enumerate` | `node.type`, `project.name` | `nodes.export.tree` | Where is it slow? |
| `NodeStructureValidator` | `nodes.validate` | `job.id`, `operation=validate`, `module=WorkItems` | Root | Is it fast enough? |

**Context propagation:** Automatic via `Activity` hierarchy. All NodeStructure spans are children of existing `workitems.export` or `workitems.import` root spans (established by `WorkItemExportOrchestrator` / `WorkItemImportOrchestrator`). Validation runs as a standalone root span.

### Logging

| Event | Level | Fields | Operation | Decision |
|---|---|---|---|---|
| Source tree capture started | `Information` | operationId, nodeType (area/iteration), project.name | `nodes.export.tree` | Is it working? |
| Source tree capture completed | `Information` | operationId, areaNodeCount, iterationNodeCount, durationMs | `nodes.export.tree` | Is it working? / Is it fast enough? |
| Source tree capture failed | `Error` | operationId, errorType, errorMessage, durationMs | `nodes.export.tree` | What failed? |
| New referenced path discovered | `Debug` | operationId, fieldName, pathValue (DataClassification.Customer) | `nodes.export.discover` | Is it correct? |
| Referenced paths summary | `Information` | operationId, distinctAreaPaths, distinctIterationPaths | `nodes.export.discover` | Is it correct? |
| Node replication started | `Information` | operationId, totalNodes, sourceFile | `nodes.import.replicate` | Is it working? |
| Node replication completed | `Information` | operationId, created, skipped, failed, durationMs | `nodes.import.replicate` | Is it working? / Is it fast enough? |
| Node replication failed | `Error` | operationId, errorType, errorMessage, durationMs | `nodes.import.replicate` | What failed? |
| Node created | `Debug` | operationId, node.path, node.type | `nodes.import.replicate` | Is it correct? |
| Node skipped (checkpoint) | `Debug` | operationId, node.path | `nodes.import.replicate` | Is it working? |
| Node creation failed | `Warning` | operationId, node.path, node.type, errorType, httpStatusCode | `nodes.import.replicate` | What failed? |
| Set iteration dates failed | `Warning` | operationId, node.path, errorType | `nodes.import.replicate` | What failed? |
| Pre-collection started | `Information` | operationId, source (referenced-paths.json / revision-scan) | `nodes.import.precollect` | Is it working? |
| Pre-collection completed | `Information` | operationId, distinctPaths, nodesCreated, durationMs | `nodes.import.precollect` | Is it working? / Is it fast enough? |
| Pre-collection node failed | `Warning` | operationId, node.path, node.type, errorType, httpStatusCode | `nodes.import.precollect` | What failed? |
| Path translated | `Trace` | operationId, fieldName, sourcePath (DataClassification.Customer), targetPath (DataClassification.Customer), matchType | `nodes.import.translate` | Is it correct? |
| Path unresolvable | `Warning` | operationId, fieldName, sourcePath (DataClassification.Customer), isExternal, revisionFolder | `nodes.import.translate` | What failed? |
| Revision skipped (unresolvable path) | `Warning` | operationId, revisionFolder, fieldName, sourcePath (DataClassification.Customer) | `nodes.import.translate` | What failed? |
| ADO API retry | `Warning` | operationId, node.path, attempt, maxAttempts, delayMs, httpStatusCode | `nodes.import.replicate` / `nodes.import.precollect` | Is it overloaded? |
| ADO API auth failure | `Error` | operationId, node.path, httpStatusCode | `nodes.import.replicate` / `nodes.import.precollect` | What failed? |
| Validation started | `Information` | operationId, source (referenced-paths.json / revision-scan) | `nodes.validate` | Is it working? |
| Validation completed | `Information` | operationId, unmappedCount, externalCount, malformedCount, durationMs | `nodes.validate` | Is it correct? |
| Validation: unmapped path | `Warning` | operationId, fieldName, pathValue (DataClassification.Customer), affectedRevisions | `nodes.validate` | Is it correct? |
| Tool disabled warning | `Warning` | operationId, sourceProject, targetProject | (startup) | Is it working? |

> Debug and Trace levels are disabled by default.

### Correlation

| Field | Source | Scope |
|---|---|---|
| `operationId` / `traceId` | `Activity.Current.TraceId` | All telemetry |
| `parentId` | `Activity.Current.ParentSpanId` | All child spans |
| `job.id` | Job context (`IJobContext`) | All telemetry within a job |
| `module` | `"WorkItems"` | All NodeStructure spans and metrics |
| `operation` | `"export"` / `"import"` / `"validate"` | All spans |
| `node.path` | Current node being processed | Node creation spans, logs, and error metrics (DataClassification.Customer) |
| `node.type` | `"area"` / `"iteration"` | Node creation spans and metrics |
| `wi.id` | Work item ID (integer, not customer data) | Translation logs within revision processing |

### Validation Queries

#### Failure Identification
```kql
// Which node creations failed and why?
customMetrics
| where name in ("migration.nodes.import.replicate.errors", "migration.nodes.import.precollect.errors")
| extend node_path = tostring(customDimensions["node.path"]),
         node_type = tostring(customDimensions["node.type"])
| summarize failures = sum(value) by name, node_path, node_type, bin(timestamp, 1m)
| order by failures desc
```

#### Latency Analysis
```kql
// P50/P95/P99 latency for node replication and pre-collection
customMetrics
| where name in ("migration.nodes.import.replicate.duration_ms", "migration.nodes.import.precollect.duration_ms")
| summarize p50 = percentile(value, 50), p95 = percentile(value, 95), p99 = percentile(value, 99)
    by name, bin(timestamp, 5m)
```

#### Load Observation
```kql
// In-flight node creation concurrency over time
customMetrics
| where name in ("migration.nodes.import.replicate.in_flight", "migration.nodes.import.precollect.in_flight")
| summarize max_inflight = max(value), avg_inflight = avg(value) by name, bin(timestamp, 1m)
```

#### End-to-End Trace
```kql
// Trace a single import job's node structure operations
dependencies
| where customDimensions["job.id"] == "<job-id>"
| where name startswith "nodes."
| project timestamp, name, duration, success,
         node_path = tostring(customDimensions["node.path"]),
         parent = tostring(customDimensions["parentId"])
| order by timestamp asc
```

#### Error Diagnosis
```kql
// Correlate node creation errors with logs and traces
traces
| where customDimensions["job.id"] == "<job-id>"
| where severityLevel >= 2 // Warning+
| where message has "node" or message has "Node"
| join kind=inner (
    dependencies
    | where name startswith "nodes."
    | where success == false
) on $left.operation_Id == $right.operation_Id
| project timestamp, message, name, duration,
         node_path = tostring(customDimensions["node.path"]),
         httpStatusCode = tostring(customDimensions["http.status_code"])
| order by timestamp asc
```

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST provide a `NodeStructure` tool type declarable under `MigrationPlatform.Tools` as a keyed entry (key = `"NodeStructure"`), following the same keyed-object pattern as `FieldTransform`, and loadable by extension tool references.

- **FR-002**: The tool MUST support an `AreaPathMappings` ordered list of `NodeMapping` entries (each with a `Match` regex pattern and a `Replacement` regex replacement string), applied to `System.AreaPath` field values at import time. The first matching rule wins. Matching uses `Regex.IsMatch` with `RegexOptions.IgnoreCase`. Replacement uses `Regex.Replace` with the same pattern, supporting capture group back-references (`$1`, `$2`, etc.).

- **FR-003**: The tool MUST support an `IterationPathMappings` ordered list of `NodeMapping` entries (each with `Match` and `Replacement`), applied to `System.IterationPath` field values at import time. Same regex semantics as FR-002.

- **FR-004**: Path matching MUST be case-insensitive (`RegexOptions.IgnoreCase`) and the input path MUST be trimmed of leading/trailing whitespace before matching. The path separator is the backslash character (`\`), consistent with the ADO REST API path format.

- **FR-004a** *(ReDoS protection)*: All user-supplied regex patterns in `AreaPathMappings` and `IterationPathMappings` MUST be compiled with `RegexOptions.NonBacktracking` (or a `MatchTimeout` of 1 second if `NonBacktracking` is unavailable on the target runtime). `ValidateAsync` (FR-021) MUST validate that each `Match` pattern is syntactically valid regex; invalid patterns MUST be reported as configuration errors.

- **FR-004b** *(NodeMapping model)*: Each mapping entry is a `NodeMapping` record with two properties: `Match` (string — .NET regex pattern) and `Replacement` (string — .NET regex replacement supporting `$1`, `$2`, etc.). The `AreaPathMappings` and `IterationPathMappings` configuration properties are `IReadOnlyList<NodeMapping>` (ordered; first match wins).

- **FR-005**: When no mapping rule matches a given path (i.e., no `Match` pattern in the mapping list produces a regex match) and the path begins with the source project name segment, the tool MUST automatically substitute the source project name prefix with the target project name (auto-swap). When no rule matches and the path does NOT begin with the source project name (unanchored path), the original value MUST be passed through unchanged. Language override (FR-012/FR-013) is applied before this check.

- **FR-006**: The tool MUST support an `AutoCreateNodes` boolean flag (default `false`). When `true`, any target area or iteration path that does not exist in the target project MUST be created via the ADO Classification Nodes API before the work item is written.

- **FR-007**: Node creation calls MUST be idempotent: the tool MUST check whether a node exists before creating it. If the target API returns a 409 (conflict), the node is treated as already present.

- **FR-008**: When creating a nested node path, the tool MUST create all missing ancestor nodes in order from root to leaf before creating the leaf.

- **FR-009**: The tool MUST support a `SkipOnUnresolvableArea` boolean flag (default `false`). When `true`, revisions whose area path cannot be resolved or created MUST be skipped (not written) and a warning progress event MUST be emitted.

- **FR-010**: The tool MUST support a `SkipOnUnresolvableIteration` boolean flag (default `false`). When `true`, revisions whose iteration path cannot be resolved or created MUST be skipped and a warning progress event MUST be emitted.

- **FR-011**: When a skip flag is `false` and a path cannot be resolved, the import MUST fail immediately with a descriptive error that includes: the field name (`System.AreaPath` or `System.IterationPath`), the unresolvable path value, and the revision folder path (which encodes the work item ID and revision index).

- **FR-012**: The tool MUST support an `AreaLanguageOverride` string. When set, the root segment of all source area path strings is normalised to the override value before any mapping lookup.

- **FR-013**: The tool MUST support an `IterationLanguageOverride` string. When set, the root segment of all source iteration path strings is normalised to the override value before any mapping lookup.

- **FR-014**: Language override normalisation MUST be applied before regex mapping rules are evaluated. The override applies to the root segment only; intermediate and leaf segment names are not modified.

- **FR-015**: The tool MUST support a `ReplicateSourceTree` boolean flag (default `false`). When `true` and the package contains `Nodes/source-tree.json` (written by the export phase — see FR-015a), the import phase MUST enumerate all nodes from that artifact and create any that are missing in the target, before processing any revision folder.

- **FR-015a** *(export-side)*: The export phase MUST always write a `Nodes/source-tree.json` artifact containing the full area and iteration node tree from the source project (regardless of `ReplicateSourceTree` setting — the flag controls import-side replication only). The artifact schema is:
  ```json
  {
    "areaNodes": ["ProjectName\\Area\\Child", ...],
    "iterationNodes": [
      { "path": "ProjectName\\Iteration\\Sprint 1", "startDate": "2024-01-15", "finishDate": "2024-01-28", "isBacklogIteration": false },
      { "path": "ProjectName\\Iteration\\Sprint 2", "startDate": null, "finishDate": null, "isBacklogIteration": false }
    ]
  }
  ```
  Area nodes are stored as plain path strings (no dates). Iteration nodes are stored as objects with `path`, `startDate` (ISO 8601 or null), `finishDate` (ISO 8601 or null), and `isBacklogIteration` (boolean). This artifact is read-only at import time; import MUST NOT call the live source API to retrieve nodes.

- **FR-015b** *(export-side — path discovery)*: During work item export, the export phase MUST maintain an in-memory set of all distinct `System.AreaPath` and `System.IterationPath` values encountered in exported `revision.json` files. Each time a previously-unseen path is discovered, the set is updated and written to `Nodes/referenced-paths.json` in the package. The artifact schema is:
  ```json
  {
    "areaPaths": ["ProjectName\\Team A", "ProjectName\\Team B"],
    "iterationPaths": ["ProjectName\\Sprint 1", "ProjectName\\Sprint 2"]
  }
  ```
  This artifact is consumed at import time by FR-024 to avoid a full revision scan.

- **FR-016**: `ReplicateSourceTree` node replication at import time MUST be processed one node at a time from the `Nodes/source-tree.json` artifact; the full node list MUST NOT be buffered in memory.

- **FR-016a**: The tool MUST maintain a persisted checkpoint (in `IStateStore`) recording which nodes have been confirmed present in the target during the current import run. On resume after interruption, nodes already recorded in this checkpoint MUST be skipped without re-checking the target API.

- **FR-017**: The tool MUST be injectable as `INodeStructureTool` through the platform's dependency injection container, enabling resolution by `WorkItemsModule` and `TeamsModule` extensions. The interface MUST be designed to accommodate both consumers.

- **FR-018**: The tool MUST be declared at the `MigrationPlatform.Tools` config root using the type name `"NodeStructure"` as the key, following the established keyed-object pattern used by `FieldTransform`. Extensions reference it by type name. The extension-level `overrides` pattern proposed in `analysis/proposed-features.md` (array-with-id + `ref`) is **deferred** — see discrepancies.md.

- **FR-019**: All path value logging MUST be scoped under `DataClassification.Customer` (path strings are customer data). Work item IDs embedded in revision folder paths are integer identifiers and are not customer data.

- **FR-020**: `System.AreaPath` and `System.IterationPath` values are written verbatim into `revision.json` at export time (no export-time path transformation). The export phase additionally captures the full source tree (FR-015a/FR-028) and discovered paths (FR-015b/FR-029) to `Nodes/` as package metadata. Path remapping is applied exclusively at import time.

- **FR-021**: `ValidateAsync` MUST scan all `revision.json` files in the package (or read from `Nodes/referenced-paths.json` if present), collect the union of all `System.AreaPath` and `System.IterationPath` values across all revisions, and report each distinct path that no mapping rule matches (after applying `Regex.IsMatch` + `Regex.Replace` with the replacement string), together with the count of revisions affected. `ValidateAsync` MUST also:
  - Validate that each `Match` pattern in `AreaPathMappings` / `IterationPathMappings` is syntactically valid regex (per FR-004a).
  - Flag any mapping rule whose `Replacement` value, after regex substitution on a matched path, produces a target path that is empty or contains characters illegal in ADO classification node names. The illegal character set is: `\`, `/`, `$`, `?`, `*`, `"`, `:`, `>`, `<`, `|`, `#`, `%`, `+`, and control characters (Unicode category Cc).
  - Flag external paths (FR-025) distinctly from unmapped paths.

- **FR-022**: Node creation API calls MUST be retried on transient failures (5xx, 408, 429) using exponential back-off. Authentication and authorisation failures (401, 403) MUST NOT be retried; they MUST surface immediately as a fatal import error with a message identifying the missing permission. Client errors indicating an invalid path (400) MUST also surface as fatal errors without retry.

- **FR-023**: The tool MUST support an `Enabled` boolean flag (default `true`). When `false`, all **import-side** tool behaviour (path mapping, node creation, pre-collection, replication) is bypassed. The `Enabled` flag has no effect on export-side behaviour: export artifacts (`Nodes/source-tree.json`, `Nodes/referenced-paths.json`) are always written unconditionally per FR-028/FR-029, because they serve as package metadata for downstream tooling. If the tool is `Enabled: false` and the source and target project names differ, the tool MUST log a warning indicating that path remapping is disabled and area/iteration paths may be incorrect in the target.

- **FR-024**: When `AutoCreateNodes: true`, before processing any revision folder the tool MUST collect the complete set of distinct `System.AreaPath` and `System.IterationPath` values. If `Nodes/referenced-paths.json` exists in the package (written by the export phase — FR-015b), the tool MUST read discovered paths from that artifact. If the artifact is absent (legacy package), the tool MUST fall back to scanning all revision folders via `IArtefactStore.EnumerateAsync()`. The tool then applies mapping/auto-swap to each path and creates any missing nodes in the target in bulk. This pre-collection ensures all required nodes are present before the first API write call.

- **FR-025**: When an external path (a path whose root segment does not match the source project name, after language override) is encountered at import time and the path does not exist in the target, the tool MUST emit a warning that explicitly identifies the path as external (not anchored in the source project name). Skip/fail behaviour applies per `SkipOnUnresolvableArea` / `SkipOnUnresolvableIteration`. ValidateAsync (FR-021) MUST also flag external paths distinctly from unmapped paths.

- **FR-026**: When creating iteration nodes (during `ReplicateSourceTree` bulk replication or `AutoCreateNodes` on-demand creation), the tool MUST set the node's `StartDate` and `FinishDate` from the values captured in `Nodes/source-tree.json` (for bulk replication) or from the source node metadata (not available for on-demand creation — dates are omitted). If the `SetIterationDates` API call fails, the failure MUST be logged as a warning (non-blocking); node creation is still considered successful.

- **FR-027**: Glob-based node filters (`Areas.Filters` / `Iterations.Filters` — as implemented in the predecessor `TfsNodeStructureTool`) are **explicitly deferred** to a later feature. In this version, all nodes referenced by work item revisions are processed. Operators who require partial-tree migration can achieve this via `AreaPathMappings`/`IterationPathMappings` regex rules that selectively remap desired subtrees.

- **FR-028** *(export-side — always-on)*: The source classification tree capture (`Nodes/source-tree.json`) MUST be written on every export regardless of any configuration flag. This ensures the package always contains the source tree for downstream tooling, documentation, and import-time `ReplicateSourceTree`. The `ReplicateSourceTree` flag controls only import-side behaviour.

- **FR-029** *(export-side — referenced paths)*: The export-time path discovery (`Nodes/referenced-paths.json`) MUST be written on every work item export. The in-memory path set MUST be loaded from the existing `Nodes/referenced-paths.json` on export resume (if present) to support incremental additions without duplication.

### Key Entities

- **NodeStructureTool configuration**: Declared under `MigrationPlatform.Tools.NodeStructure`. Carries `Enabled`, `AreaPathMappings` (`IReadOnlyList<NodeMapping>`), `IterationPathMappings` (`IReadOnlyList<NodeMapping>`), `AreaLanguageOverride`, `IterationLanguageOverride`, `AutoCreateNodes`, `SkipOnUnresolvableArea`, `SkipOnUnresolvableIteration`, `ReplicateSourceTree`. No `id` or `type` discriminator fields — the key `"NodeStructure"` is the type.

- **NodeMapping**: A record with two properties: `Match` (string — .NET regex pattern) and `Replacement` (string — .NET regex replacement string supporting `$1`, `$2`, etc.). Used in `AreaPathMappings` and `IterationPathMappings`. First matching rule wins.

- **Area Path**: The `System.AreaPath` field value in `revision.json`. Represents the organisational area classification of a work item revision. Format: `"ProjectName\\...\\NodeName"` (backslash-separated).

- **Iteration Path**: The `System.IterationPath` field value in `revision.json`. Represents the sprint/iteration context of a work item revision. Format: `"ProjectName\\...\\SprintName"` (backslash-separated).

- **Classification Node**: An ADO area or iteration node in the target project's classification tree. Created via the ADO Classification Nodes REST API. Identified by a full path string.

- **Classification Tree Snapshot** (`Nodes/source-tree.json`): A package artifact always written by the export phase. Area nodes are stored as plain path strings. Iteration nodes are stored as objects with `path`, `startDate`, `finishDate` (nullable ISO 8601), and `isBacklogIteration` (boolean). Read at import time by the tool to drive bulk node replication when `ReplicateSourceTree: true`, without accessing the live source.

- **Referenced Paths Artifact** (`Nodes/referenced-paths.json`): A package artifact written incrementally during work item export. Contains the distinct union of all `System.AreaPath` and `System.IterationPath` values found across all exported revisions. Read at import time to avoid a full revision scan when `AutoCreateNodes: true`.

- **Node Replication Progress**: A persisted record (in `IStateStore`) of which classification node paths have been confirmed present in the target during the current import run. Used to make `ReplicateSourceTree` resumable without redundant API calls.

- **Extension Tool Reference**: The `NodeStructure` tool is declared once under `MigrationPlatform.Tools` (keyed-object pattern) and consumed by any extension that depends on it. There is no per-extension tool reference array.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Work items whose `System.AreaPath` and `System.IterationPath` values match at least one `AreaPathMappings` or `IterationPathMappings` regex rule are placed in the correct target classification node (as produced by `Regex.Replace`) in 100% of matched cases.

- **SC-002**: When `AutoCreateNodes: true`, a migration with a completely empty target classification tree completes successfully without any manual target preparation, for any package where all paths are either mapped or pass-through.

- **SC-003**: When `ReplicateSourceTree: true`, the tool replicates all source classification nodes (read from `Nodes/source-tree.json` in the package) to the target without holding more than a single node record in memory at any point (verifiable by test instrumentation).

- **SC-004**: When `SkipOnUnresolvableArea: true` or `SkipOnUnresolvableIteration: true`, an import with at least one unresolvable path completes without aborting; every skipped revision appears in the progress event log with a warning.

- **SC-005**: Regex matching is case-insensitive (`RegexOptions.IgnoreCase`): a pattern `"Area\\\\Team A"` matches both `"Area\Team a"` and `"Area\Team A"`.

- **SC-006**: Running the same import twice against the same package and a partially-populated target produces the same final state (idempotency): no duplicate nodes are created, no revision is applied twice.

- **SC-007**: Operators can discover configuration gaps before running a full import: the `ValidateAsync` pass reports all distinct `System.AreaPath` and `System.IterationPath` values across all revisions in the package that no mapping rule matches, identifying the count of affected revisions for each unmatched path.

- **SC-008**: When an import is interrupted during node replication (`ReplicateSourceTree: true`) and resumed, the tool does not re-create nodes already confirmed in the node-creation checkpoint; the resumed run completes correctly.

- **SC-009**: After a work item export, the package contains `Nodes/source-tree.json` (full classification tree) and `Nodes/referenced-paths.json` (all distinct paths encountered in exported revisions), regardless of configuration flags.

- **SC-010**: When `Nodes/referenced-paths.json` exists in the package, the import-time pre-collection pass reads paths from this artifact instead of scanning all `revision.json` files, reducing import startup time proportionally.

---

## Assumptions

- **Path matching uses .NET regex** (`Regex.IsMatch` + `Regex.Replace`) with `RegexOptions.IgnoreCase` and `RegexOptions.NonBacktracking` (ReDoS protection). Each `AreaPathMappings`/`IterationPathMappings` entry is a `NodeMapping` with `Match` (regex pattern) and `Replacement` (regex replacement string supporting `$1`, `$2` capture group back-references). First matching rule wins. This matches the regex model of the predecessor `TfsNodeStructureTool` in `azure-devops-migration-tools`, with the addition of `NonBacktracking` for safety.
- **Auto-swap is the default pass-through behaviour** when no explicit mapping matches. If a path begins with the source project name it is remapped to start with the target project name. This matches the predecessor `TfsNodeStructureTool` behaviour. Paths not anchored in the source project pass through unchanged.
- **Glob-based node filters are deferred** (FR-027). The predecessor tool supported `Areas.Filters`/`Iterations.Filters` glob patterns; these are intentionally out of scope for this version.
- The `NodeStructureTool` operates exclusively at **import time** for path remapping. Export always writes the source classification tree to `Nodes/source-tree.json` and discovered paths to `Nodes/referenced-paths.json`; import reads from these package artifacts — never from the live source API (package-only import invariant).
- `AutoCreateNodes` defaults to `false`. Operators must explicitly opt in to target mutation. This avoids unexpected node creation in production targets for operators who add the tool without reading the documentation.
- When both `ReplicateSourceTree` and `AutoCreateNodes` are `true`, `ReplicateSourceTree` executes as a pre-import bulk step (from the package artifact), and `AutoCreateNodes` serves as a safety net for any path encountered during revision processing that was not captured in the bulk step.
- The tool is inert unless declared under `MigrationPlatform.Tools.NodeStructure`; if absent, extensions that depend on it skip path translation silently. Export-side artifacts (`Nodes/source-tree.json`, `Nodes/referenced-paths.json`) are still written when the Nodes module is active.
- Initial consumer is `WorkItemsModule` (`Revisions` extension). `TeamsModule` will use the same `INodeStructureTool` interface. The interface MUST be designed with both consumers' calling contracts in mind from the outset; the plan phase MUST document the anticipated TeamsModule contract before the interface is finalised.
- The ADO Classification Nodes API returns 404 when a node does not exist, 201 on successful creation, and 409 (treated as success) when the node already exists. Auth failures (401/403) are non-retryable fatal errors.
- Language override applies only to the root segment of the path. Intermediate and leaf segment names are not modified.
- The path separator is the backslash character (`\`), consistent with the ADO REST API path format.
- Iteration node start/finish dates and `isBacklogIteration` flag from the source are preserved in `Nodes/source-tree.json` and applied when creating nodes via `ReplicateSourceTree`. Dates are NOT available for on-demand `AutoCreateNodes` creation (the source is not queried at import time).
- Schema versioning and a config upgrader for the new `NodeStructure` tool type and the new `Nodes/` package artifacts are required per system-architecture guardrail rule 9, and will be addressed in the plan/implementation phases.
- Architecture sources: `analysis/proposed-features.md` M2 and T2 entries, cross-validated against `azure-devops-migration-tools` `TfsNodeStructureTool` implementation, and all guardrail and context files listed in the Architecture References section.
