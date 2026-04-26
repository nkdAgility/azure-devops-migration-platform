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
| `docs/modules.md` | Confirmed accurate — Tool Resolution section referenced; `NodeStructureTool` not yet described → **discrepancy logged** |
| `docs/configuration.md` | Confirmed accurate — `Tools` section documents `FieldTransform`; `NodeStructure` tool not yet documented → **discrepancy logged** |
| `.agents/guardrails/system-architecture.md` | Confirmed accurate |
| `.agents/guardrails/workitems-rules.md` | Confirmed accurate |
| `.agents/guardrails/migration-rules.md` | Confirmed accurate |
| `.agents/context/workitems-format.md` | Confirmed accurate |
| `azure-devops-migration-tools` — `TfsNodeStructureTool.cs` | Cross-referenced for behavioural completeness — iteration dates, auto project-name swap, unanchored paths, bulk pre-collection |

Discrepancies logged in: `specs/023-workitems-nodestructure-tool/discrepancies.md`

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Map Area and Iteration Paths Across Projects (Priority: P1)

A migration operator is moving work items from one Azure DevOps organisation (or project) to another where the area and iteration path structure has been renamed or restructured. Without path remapping, the import stage fails or silently places every work item at the root node, losing organisational structure.

The operator declares a mapping table of source paths → target paths so that `System.AreaPath` and `System.IterationPath` values in `revision.json` are translated to the correct target path before the API call is made.

**Why this priority**: Path resolution is a prerequisite for a successful cross-project import. Without it, nearly every cross-project or cross-organisation migration will produce incorrect data. All other tool features build on this core capability.

**Independent Test**: Configure a single source → target area path mapping, run an import of work items whose `revision.json` contains the source path, and verify that the created work items carry the mapped target path value.

**Acceptance Scenarios**:

1. **Given** a `revision.json` contains `System.AreaPath: "OldOrg\\OldProject\\Team A"` and the tool has `areaMap: { "OldOrg\\OldProject\\Team A": "NewOrg\\NewProject\\Team A - Migrated" }`, **When** the import stage applies the tool, **Then** the work item is written to the target with `System.AreaPath: "NewOrg\\NewProject\\Team A - Migrated"`.

2. **Given** a `revision.json` contains `System.IterationPath: "OldOrg\\OldProject\\Sprint 1"` and the tool has `iterationMap: { "OldOrg\\OldProject\\Sprint 1": "NewOrg\\NewProject\\Sprint 1" }`, **When** the import stage applies the tool, **Then** the work item is written with `System.IterationPath: "NewOrg\\NewProject\\Sprint 1"`.

3. **Given** a `revision.json` contains a path that has no entry in `areaMap` or `iterationMap`, and the path begins with the source project name, **When** the import stage applies the tool, **Then** the source project name prefix is automatically substituted with the target project name (auto-swap). If the path does not begin with the source project name it is passed through unchanged.

4. **Given** a `revision.json` contains a path that has no entry in `areaMap` or `iterationMap`, and the path does not begin with the source project name, **When** the import stage applies the tool, **Then** the original path value is preserved without modification (pass-through — the path is not anchored in the source project).

5. **Given** the `NodeStructure` tool is not declared under `MigrationPlatform.Tools`, **When** a module import runs, **Then** no path translation is applied and behaviour is identical to the pre-tool state.

---

### User Story 2 — Auto-Create Missing Nodes in the Target (Priority: P2)

An operator has declared correct path mappings but the target project does not yet contain all required area or iteration nodes. The import currently fails with an ADO API error when a work item is patched with a path that does not exist in the target classification tree.

The operator wants the tool to automatically call the ADO Classification Nodes API to create any missing node before it is referenced, removing the need to manually pre-configure the target tree.

**Why this priority**: Auto-create removes the most common manual remediation step before import. Without it, operators must pre-configure every target node — a fragile, error-prone step on large projects.

**Independent Test**: Run an import against a target project with no area or iteration nodes (beyond the root), with `createMissingNodes: true`, and confirm that all required nodes are created and work items are placed under them.

**Acceptance Scenarios**:

1. **Given** `createMissingNodes: true` and the target does not have the node `"NewProject\\Team A"`, **When** the import encounters a revision whose translated area path is `"NewProject\\Team A"`, **Then** the node is created via the ADO Classification Nodes API before the work item patch is attempted.

2. **Given** `createMissingNodes: true` and the node already exists, **When** the import encounters a revision referencing that node, **Then** no duplicate creation is attempted (the creation call is idempotent — check before POST).

3. **Given** `createMissingNodes: false` and a translated path does not exist in the target, **When** the import encounters that revision, **Then** the tool does not attempt creation, and the import proceeds according to the `skipRevisionWithInvalidAreaPath` / `skipRevisionWithInvalidIterationPath` settings.

4. **Given** `createMissingNodes: true` and a nested path `"Project\\Area\\Sub-Area"` is required, **When** the tool resolves the path, **Then** all intermediate nodes are created in order (parent before child).

---

### User Story 3 — Skip or Fail Gracefully on Unresolvable Paths (Priority: P3)

When a path cannot be resolved (no mapping entry, node creation disabled or failed, and node does not exist in target), the import currently fails hard. Operators running large migrations need a way to configure whether the tool skips the affected revision or fails the import.

**Why this priority**: Graceful degradation on bad paths is important for large migrations where a small percentage of items may have unusual paths. It allows the bulk of the migration to succeed while flagging items for manual review.

**Independent Test**: Configure `skipRevisionWithInvalidAreaPath: true`, present a revision whose area path cannot be resolved, and verify the revision is skipped with a warning emitted while the import continues.

**Acceptance Scenarios**:

1. **Given** `skipRevisionWithInvalidAreaPath: true` and a revision whose translated area path cannot be resolved or created, **When** the import applies the tool, **Then** the revision is skipped (not written), a warning progress event is emitted, and import continues to the next revision.

2. **Given** `skipRevisionWithInvalidIterationPath: true` and a revision whose translated iteration path cannot be resolved, **When** the import applies the tool, **Then** the revision is skipped, a warning is emitted, and import continues.

3. **Given** both skip flags are `false` and a path cannot be resolved, **When** the import applies the tool, **Then** the import emits a descriptive error identifying the unresolvable path and affected revision, and fails.

---

### User Story 4 — Replicate All Source Nodes to the Target (Priority: P4)

Operators performing full project migrations want the option to pre-populate the target classification tree with all nodes from the source project, regardless of whether any work item currently references them. This enables a like-for-like node tree migration before or alongside work item migration.

**Why this priority**: Useful for complete project structure migration but not required for incremental or revision-only scenarios. The `createMissingNodes` flag already handles the common on-demand case.

> **Architecture note**: To comply with the Source → Package → Target model (import must read only from the package, not from the live source), this feature requires the **export** phase to capture the full source classification tree into a package artifact (`WorkItems/classification-nodes.json`). The import phase reads from this artifact — never from the live source. `replicateAllExistingNodes` is therefore a two-phase capability: export captures the tree; import replicates it.

**Independent Test**: Configure a scenario with `replicateAllExistingNodes: true`, run a full export (which captures the classification tree artifact), then run import against a target with no pre-existing nodes, and verify all source nodes appear in the target before any work item is written.

**Acceptance Scenarios**:

1. **Given** `replicateAllExistingNodes: true` and an exported package containing `WorkItems/classification-nodes.json`, **When** the import phase initialises before processing any revision folder, **Then** all area and iteration nodes recorded in `classification-nodes.json` are created (or confirmed present) in the target project, processed one node at a time.

2. **Given** `replicateAllExistingNodes: false`, **When** the module imports work items, **Then** `classification-nodes.json` (if present) is ignored, and only nodes referenced by work item revisions are created on-demand via `createMissingNodes`.

3. **Given** `replicateAllExistingNodes: true` and the export was run with the same flag, **When** the import is interrupted after replicating 50 of 200 nodes and then resumed, **Then** the tool skips nodes already recorded as confirmed-present in the node creation checkpoint, and continues from node 51.

---

### User Story 5 — Override Localised Node Names (Priority: P5)

Azure DevOps installations in non-English locales name the root classification nodes differently (e.g. `"Área"` in Spanish, `"Bereich"` in German). Path matching fails because the source path begins with a localised root name that does not match the target.

The operator declares a language override so that localised root node names in both source and target paths are normalised before any mapping is applied.

**Why this priority**: Affects cross-locale migrations only — a minority scenario, but a hard blocker when encountered.

**Independent Test**: Configure `areaLanguageOverride: "Area"`, present a source revision with `System.AreaPath: "Área\\ProjectX\\Team A"`, and verify the tool normalises the root segment before path lookup.

**Acceptance Scenarios**:

1. **Given** `areaLanguageOverride: "Area"` and a revision with `System.AreaPath: "Área\\ProjectX\\Team A"`, **When** the tool processes the path, **Then** the root segment is normalised to `"Area"` before the mapping lookup proceeds.

2. **Given** `iterationLanguageOverride: "Iteration"` and a revision with `System.IterationPath: "Iteración\\ProjectX\\Sprint 1"`, **When** the tool processes the path, **Then** the root segment is normalised to `"Iteration"` before regex mapping is applied.

---

### User Story 6 — Preserve Iteration Node Start/Finish Dates (Priority: P4)

When an operator replicates the full iteration tree from source to target (`replicateAllExistingNodes: true`), the sprint schedule dates (start and finish) defined on each iteration node in the source project must be preserved in the target. Without this, the target project has the correct iteration hierarchy but loses all sprint scheduling information.

**Why this priority**: Sprint date loss is silent data loss. It does not block migration but degrades the usefulness of the target for planning. Rated P4 (same as `replicateAllExistingNodes`) because it only applies to that feature.

**Independent Test**: Export a source project whose iteration tree contains nodes with `StartDate` and `FinishDate` set. Import with `replicateAllExistingNodes: true`. Verify that each iteration node in the target has the correct start and finish dates.

**Acceptance Scenarios**:

1. **Given** `replicateAllExistingNodes: true` and `classification-nodes.json` contains an iteration node with `startDate: "2024-01-15"` and `finishDate: "2024-01-28"`, **When** the import creates that iteration node in the target, **Then** the node's start and finish dates are set to the exported values.

2. **Given** `replicateAllExistingNodes: true` and a source area node (not an iteration), **When** the import creates that area node, **Then** no date-setting API call is made (area nodes have no dates).

3. **Given** `replicateAllExistingNodes: true` and an iteration node in `classification-nodes.json` has no `startDate` or `finishDate` (they were null on the source), **When** the import creates that node, **Then** the node is created without dates and no error is emitted.

---



- What happens when a source path is an empty string or null? → Treated as unresolvable; skip/fail behaviour applies according to configuration.
- What happens when a path does not begin with the source project name (unanchored path — e.g. a cross-project link whose area/iteration belongs to a third project)? → The auto-swap does not apply. The path is passed through unchanged. If the pass-through value does not exist in the target, skip/fail behaviour applies. The warning MUST identify the path as unanchored.
- What happens when the ADO Classification Nodes API returns a transient error (5xx, 408, 429) during node creation? → Retried with exponential back-off per FR-022. Persistent transient failures surface as import errors.
- What happens when the ADO Classification Nodes API returns 401 or 403? → Non-retryable; the import fails immediately with a message identifying the missing permission. The operator must fix service account permissions before retrying.
- What happens when an `areaMap` or `iterationMap` target value is empty or contains characters illegal in ADO node names? → ValidateAsync (FR-021) flags this as a configuration error. At import time, the revision is treated as having an unresolvable path and skip/fail behaviour applies.
- What happens when a source path's translated target path exceeds the maximum classification node depth supported by the target ADO instance? → The node creation attempt fails; the revision is treated as having an unresolvable path and skip/fail behaviour applies. ValidateAsync should flag paths whose nesting depth exceeds the known ADO limit.
- What happens when two revisions in different work items reference the same previously-unresolved path concurrently (future parallel worker scenario)? → The node-creation checkpoint (FR-016a) is the source of truth; idempotent check-before-POST (FR-007) prevents duplicate creation.
- What happens when the tool is declared but not referenced by any extension? → The declaration is inert; no mapping, no node creation, no export artifact is produced.
- What happens when `replicateAllExistingNodes: true` is configured but `classification-nodes.json` is absent from the package (e.g., a package exported with the flag disabled)? → The import logs a warning and skips the bulk replication step; `createMissingNodes` (if enabled) handles on-demand creation during revision processing.
- What happens when `ValidateAsync` encounters a revision where `System.AreaPath` is absent (field was unchanged in that revision)? → ValidateAsync MUST collect AreaPath/IterationPath values across ALL revisions of ALL work items, not only the first or last revision. A path not present in the map is flagged regardless of which revision introduced it.
- What happens when `SetIterationDates` fails for a created node? → The failure is logged as a warning (non-blocking); the node is still present and the import continues. Date loss is surfaced in the progress log for operator review.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The platform MUST provide a `NodeStructure` tool type declarable under `MigrationPlatform.Tools` as a keyed entry (key = `"NodeStructure"`), following the same keyed-object pattern as `FieldTransform`, and loadable by extension tool references.

- **FR-002**: The tool MUST support an `areaMap` dictionary mapping source area path strings (exact full-path strings) to target area path strings, applied to `System.AreaPath` field values at import time. Matching is exact (not regex) and case-insensitive.

- **FR-003**: The tool MUST support an `iterationMap` dictionary mapping source iteration path strings (exact full-path strings) to target iteration path strings, applied to `System.IterationPath` field values at import time. Matching is exact and case-insensitive.

- **FR-004**: Path matching MUST be case-insensitive and trimmed of leading/trailing whitespace. The path separator is the backslash character (`\`), consistent with the ADO REST API path format.

- **FR-005**: When no mapping entry matches a given path and the path begins with the source project name segment, the tool MUST automatically substitute the source project name prefix with the target project name (auto-swap). When no mapping entry matches and the path does NOT begin with the source project name (unanchored path), the original value MUST be passed through unchanged. Language override (FR-012/FR-013) is applied before this check.

- **FR-006**: The tool MUST support a `createMissingNodes` boolean flag (default `false`). When `true`, any target area or iteration path that does not exist in the target project MUST be created via the ADO Classification Nodes API before the work item is written.

- **FR-007**: Node creation calls MUST be idempotent: the tool MUST check whether a node exists before creating it. If the target API returns a 409 (conflict), the node is treated as already present.

- **FR-008**: When creating a nested node path, the tool MUST create all missing ancestor nodes in order from root to leaf before creating the leaf.

- **FR-009**: The tool MUST support a `skipRevisionWithInvalidAreaPath` boolean flag (default `false`). When `true`, revisions whose area path cannot be resolved or created MUST be skipped (not written) and a warning progress event MUST be emitted.

- **FR-010**: The tool MUST support a `skipRevisionWithInvalidIterationPath` boolean flag (default `false`). When `true`, revisions whose iteration path cannot be resolved or created MUST be skipped and a warning progress event MUST be emitted.

- **FR-011**: When a skip flag is `false` and a path cannot be resolved, the import MUST fail immediately with a descriptive error that includes: the field name (`System.AreaPath` or `System.IterationPath`), the unresolvable path value, and the revision folder path (which encodes the work item ID and revision index).

- **FR-012**: The tool MUST support an `areaLanguageOverride` string. When set, the root segment of all source area path strings is normalised to the override value before any mapping lookup.

- **FR-013**: The tool MUST support an `iterationLanguageOverride` string. When set, the root segment of all source iteration path strings is normalised to the override value before any mapping lookup.

- **FR-014**: Language override normalisation MUST be applied before exact-match mapping. The override applies to the root segment only; intermediate and leaf segment names are not modified.

- **FR-015**: The tool MUST support a `replicateAllExistingNodes` boolean flag (default `false`). When `true` and the package contains a `WorkItems/classification-nodes.json` export artifact (written by the export phase — see FR-015a), the import phase MUST enumerate all nodes from that artifact and create any that are missing in the target, before processing any revision folder.

- **FR-015a** *(export-side)*: When `replicateAllExistingNodes: true` is configured, the export phase MUST write a `WorkItems/classification-nodes.json` artifact containing the full area and iteration node tree from the source project. The artifact schema is:
  ```json
  {
    "areaNodes": ["ProjectName\\Area\\Child", ...],
    "iterationNodes": [
      { "path": "ProjectName\\Iteration\\Sprint 1", "startDate": "2024-01-15", "finishDate": "2024-01-28" },
      { "path": "ProjectName\\Iteration\\Sprint 2", "startDate": null, "finishDate": null }
    ]
  }
  ```
  Area nodes are stored as plain path strings (no dates). Iteration nodes are stored as objects with `path`, `startDate` (ISO 8601 or null), and `finishDate` (ISO 8601 or null). This artifact is read-only at import time; import MUST NOT call the live source API to retrieve nodes.

- **FR-016**: `replicateAllExistingNodes` node replication at import time MUST be processed one node at a time from the `classification-nodes.json` artifact; the full node list MUST NOT be buffered in memory.

- **FR-016a**: The tool MUST maintain a persisted checkpoint (in `IStateStore`) recording which nodes have been confirmed present in the target during the current import run. On resume after interruption, nodes already recorded in this checkpoint MUST be skipped without re-checking the target API.

- **FR-017**: The tool MUST be injectable as `INodeStructureTool` through the platform's dependency injection container, enabling resolution by `WorkItemsModule` and `TeamsModule` extensions. The interface MUST be designed to accommodate both consumers.

- **FR-018**: The tool MUST be declared at the `MigrationPlatform.Tools` config root using the type name `"NodeStructure"` as the key, following the established keyed-object pattern used by `FieldTransform`. Extensions reference it by type name. The extension-level `overrides` pattern proposed in `analysis/proposed-features.md` (array-with-id + `ref`) is **deferred** — see discrepancies.md.

- **FR-019**: All path value logging MUST be scoped under `DataClassification.Customer` (path strings are customer data). Work item IDs embedded in revision folder paths are integer identifiers and are not customer data.

- **FR-020**: `System.AreaPath` and `System.IterationPath` values are written verbatim into `revision.json` at export time (no export-time path transformation beyond FR-015a node tree capture). Path remapping is applied exclusively at import time.

- **FR-021**: `ValidateAsync` MUST scan all `revision.json` files in the package, collect the union of all `System.AreaPath` and `System.IterationPath` values across all revisions, and report each distinct path that has no matching entry in `areaMap` / `iterationMap` respectively, together with the count of revisions affected. ValidateAsync MUST also flag any `areaMap` or `iterationMap` target path value that is empty or contains characters illegal in ADO node names.

- **FR-022**: Node creation API calls MUST be retried on transient failures (5xx, 408, 429) using exponential back-off. Authentication and authorisation failures (401, 403) MUST NOT be retried; they MUST surface immediately as a fatal import error with a message identifying the missing permission. Client errors indicating an invalid path (400) MUST also surface as fatal errors without retry.

- **FR-023**: The tool MUST support an `Enabled` boolean flag (default `true`). When `false`, all tool behaviour (path mapping, node creation, export artifact writing) is bypassed. If the tool is `Enabled: false` and the source and target project names differ, the tool MUST log a warning indicating that path remapping is disabled and area/iteration paths may be incorrect in the target.

- **FR-024**: When `createMissingNodes: true`, before processing any revision folder the tool MUST collect the complete set of distinct `System.AreaPath` and `System.IterationPath` values across ALL revision folders in the package, apply mapping/auto-swap to each, and create any missing nodes in the target in bulk. This pre-collection pass prevents mid-stream node creation during revision write and ensures all required nodes are present before the first API write call.

- **FR-025**: When an unanchored path (a path whose root segment does not match the source project name, after language override) is encountered at import time and the path does not exist in the target, the tool MUST emit a warning that explicitly identifies the path as unanchored (not anchored in the source project name). Skip/fail behaviour applies per `skipRevisionWithInvalidAreaPath` / `skipRevisionWithInvalidIterationPath`. ValidateAsync (FR-021) MUST also flag unanchored paths distinctly from unmapped paths.

- **FR-026**: When creating iteration nodes (during `replicateAllExistingNodes` bulk replication or `createMissingNodes` on-demand creation), the tool MUST set the node's `StartDate` and `FinishDate` from the values captured in `classification-nodes.json` (for bulk replication) or from the source node metadata (not available for on-demand creation — dates are omitted). If the `SetIterationDates` API call fails, the failure MUST be logged as a warning (non-blocking); node creation is still considered successful.

- **FR-027**: Glob-based node filters (`Areas.Filters` / `Iterations.Filters` — as implemented in the predecessor `TfsNodeStructureTool`) are **explicitly deferred** to a later feature. In this version, all nodes referenced by work item revisions are processed. Operators who require partial-tree migration must achieve this via explicit `areaMap`/`iterationMap` entries.

### Key Entities

- **NodeStructureTool configuration**: Declared under `MigrationPlatform.Tools.NodeStructure`. Carries `Enabled`, `areaMap`, `iterationMap`, `areaLanguageOverride`, `iterationLanguageOverride`, `createMissingNodes`, `skipRevisionWithInvalidAreaPath`, `skipRevisionWithInvalidIterationPath`, `replicateAllExistingNodes`. No `id` or `type` discriminator fields — the key `"NodeStructure"` is the type.

- **Area Path**: The `System.AreaPath` field value in `revision.json`. Represents the organisational area classification of a work item revision. Format: `"ProjectName\\...\\NodeName"` (backslash-separated).

- **Iteration Path**: The `System.IterationPath` field value in `revision.json`. Represents the sprint/iteration context of a work item revision. Format: `"ProjectName\\...\\SprintName"` (backslash-separated).

- **Classification Node**: An ADO area or iteration node in the target project's classification tree. Created via the ADO Classification Nodes REST API. Identified by a full path string.

- **Classification Tree Export Artifact** (`WorkItems/classification-nodes.json`): A package artifact written by the export phase when `replicateAllExistingNodes: true`. Area nodes are stored as plain path strings. Iteration nodes are stored as objects with `path`, `startDate`, and `finishDate` (nullable ISO 8601). Read at import time by the tool to drive bulk node replication without accessing the live source.

- **Node Creation Checkpoint**: A persisted record (in `IStateStore`) of which classification node paths have been confirmed present in the target during the current import run. Used to make `replicateAllExistingNodes` resumable without redundant API calls.

- **Extension Tool Reference**: The `NodeStructure` tool is declared once under `MigrationPlatform.Tools` (keyed-object pattern) and consumed by any extension that depends on it. There is no per-extension tool reference array.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Work items whose `System.AreaPath` and `System.IterationPath` values are covered by `areaMap` and `iterationMap` entries are placed in the correct target classification node in 100% of mapped cases.

- **SC-002**: When `createMissingNodes: true`, a migration with a completely empty target classification tree completes successfully without any manual target preparation, for any package where all paths are either mapped or pass-through.

- **SC-003**: When `replicateAllExistingNodes: true`, the tool replicates all source classification nodes (read from `WorkItems/classification-nodes.json` in the package) to the target without holding more than a single node record in memory at any point (verifiable by test instrumentation).

- **SC-004**: When `skipRevisionWithInvalidAreaPath: true` or `skipRevisionWithInvalidIterationPath: true`, an import with at least one unresolvable path completes without aborting; every skipped revision appears in the progress event log with a warning.

- **SC-005**: Path lookups are case-insensitive: `"Area\\Team a"` and `"Area\\Team A"` match the same map entry.

- **SC-006**: Running the same import twice against the same package and a partially-populated target produces the same final state (idempotency): no duplicate nodes are created, no revision is applied twice.

- **SC-007**: Operators can discover configuration gaps before running a full import: the `ValidateAsync` pass reports all distinct `System.AreaPath` and `System.IterationPath` values across all revisions in the package that have no matching map entry, identifying the count of affected revisions for each unmapped path.

- **SC-008**: When an import is interrupted during node replication (`replicateAllExistingNodes: true`) and resumed, the tool does not re-create nodes already confirmed in the node-creation checkpoint; the resumed run completes correctly.

---

## Assumptions

- **Path matching is exact full-path string** (case-insensitive). Regex pattern matching is not supported in `areaMap`/`iterationMap` in this version. If regex support is needed, it will be introduced as a separate configuration key in a later feature, with defined escaping rules and DoS protections.
- **Auto-swap is the default pass-through behaviour** when no explicit mapping matches. If a path begins with the source project name it is remapped to start with the target project name. This matches the predecessor `TfsNodeStructureTool` behaviour. Paths not anchored in the source project pass through unchanged.
- **Glob-based node filters are deferred** (FR-027). The predecessor tool supported `Areas.Filters`/`Iterations.Filters` glob patterns; these are intentionally out of scope for this version.
- The `NodeStructureTool` operates exclusively at **import time** for path remapping. For `replicateAllExistingNodes`, export writes the classification tree to `WorkItems/classification-nodes.json`; import reads from this package artifact — never from the live source API (package-only import invariant).
- `createMissingNodes` defaults to `false`. Operators must explicitly opt in to target mutation. This avoids unexpected node creation in production targets for operators who add the tool without reading the documentation.
- When both `replicateAllExistingNodes` and `createMissingNodes` are `true`, `replicateAllExistingNodes` executes as a pre-import bulk step (from the package artifact), and `createMissingNodes` serves as a safety net for any path encountered during revision processing that was not captured in the bulk step.
- The tool is inert unless declared under `MigrationPlatform.Tools.NodeStructure`; if absent, extensions that depend on it skip path translation silently.
- Initial consumer is `WorkItemsModule` (`Revisions` extension). `TeamsModule` will use the same `INodeStructureTool` interface. The interface MUST be designed with both consumers' calling contracts in mind from the outset; the plan phase MUST document the anticipated TeamsModule contract before the interface is finalised.
- The ADO Classification Nodes API returns 404 when a node does not exist, 201 on successful creation, and 409 (treated as success) when the node already exists. Auth failures (401/403) are non-retryable fatal errors.
- Language override applies only to the root segment of the path. Intermediate and leaf segment names are not modified.
- The path separator is the backslash character (`\`), consistent with the ADO REST API path format.
- Iteration node start/finish dates from the source are preserved in `classification-nodes.json` and applied when creating nodes via `replicateAllExistingNodes`. Dates are NOT available for on-demand `createMissingNodes` creation (the source is not queried at import time).
- Schema versioning and a config upgrader for the new `NodeStructure` tool type and the new `classification-nodes.json` package artifact are required per system-architecture guardrail rule 9, and will be addressed in the plan/implementation phases.
- Architecture sources: `analysis/proposed-features.md` M2 and T2 entries, cross-validated against `azure-devops-migration-tools` `TfsNodeStructureTool` implementation, and all guardrail and context files listed in the Architecture References section.
