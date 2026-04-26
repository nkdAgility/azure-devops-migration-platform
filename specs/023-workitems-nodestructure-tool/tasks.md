# Tasks: WorkItemsModule — NodeStructure Tool

**Input**: Design documents from `/specs/023-workitems-nodestructure-tool/`
**Prerequisites**: plan.md (required), spec.md (required), research.md, data-model.md, contracts/interfaces.md, quickstart.md

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US7)
- Exact file paths included in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Domain types, interfaces, configuration model, and DI registration — the foundation all user stories depend on.

- [ ] T001 Create `ClassificationNodeType` enum in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ClassificationNodeType.cs`
- [ ] T002 [P] Create `ProjectMapping` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ProjectMapping.cs`
- [ ] T003 [P] Create `PathTranslation` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/PathTranslation.cs`
- [ ] T004 [P] Create `IterationNodeEntry` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IterationNodeEntry.cs`
- [ ] T005 [P] Create `ClassificationTreeSnapshot` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ClassificationTreeSnapshot.cs`
- [ ] T006 [P] Create `ReferencedPathsArtifact` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ReferencedPathsArtifact.cs`
- [ ] T007 [P] Create `UnmappedPathFinding` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/UnmappedPathFinding.cs`
- [ ] T008 [P] Create `NodeStructureValidationReport` record in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/NodeStructureValidationReport.cs`
- [ ] T009 Create `NodeStructureOptions` sealed options class in `src/DevOpsMigrationPlatform.Abstractions/Options/NodeStructureOptions.cs` — `SectionName = "MigrationPlatform:Tools:NodeStructure"`, init-only properties, data annotation validation. `AreaPathMappings` and `IterationPathMappings` are `IReadOnlyList<NodeMapping>` (ordered regex rules).
- [ ] T009a [P] Create `NodeMapping` sealed record in `src/DevOpsMigrationPlatform.Abstractions/Options/NodeMapping.cs` — `Match` (string, regex pattern) and `Replacement` (string, regex replacement). Init-only properties.
- [ ] T010 [P] Create `INodeStructureTool` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeStructureTool.cs`
- [ ] T011 [P] Create `INodeCreator` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeCreator.cs`
- [ ] T012 [P] Create `IClassificationTreeReader` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IClassificationTreeReader.cs`
- [ ] T013 [P] Create `INodeStructureValidator` interface in `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeStructureValidator.cs`
- [ ] T014 Create `NodeStructureToolServiceCollectionExtensions` with `AddNodeStructureToolServices()` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeStructureToolServiceCollectionExtensions.cs` — register options, `INodeStructureTool`, `INodeStructureValidator`; leave `INodeCreator` and `IClassificationTreeReader` for connector-specific DI
- [ ] T015 Create `NodeReplicationProgress` checkpoint record in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeReplicationProgress.cs`
- [ ] T016 Add `WellKnownMetricNames` constants for all `migration.nodes.*` metrics in `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMetricNames.cs`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core implementation classes that all user stories share. MUST complete before user story work begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T017 [US1] Create `features/import/workitems/nodestructure/path-mapping.feature` — translate spec.md User Story 1 acceptance scenarios (regex mapping with Match/Replacement, auto-swap, pass-through, tool disabled) into conformant Gherkin per `.agents/guardrails/acceptance-test-format.md`
- [ ] T018 Implement `NodeStructureTool` (`INodeStructureTool`) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeStructureTool.cs` — constructor takes `IOptions<NodeStructureOptions>`, pre-compiles all `NodeMapping` patterns with `RegexOptions.IgnoreCase | RegexOptions.NonBacktracking`. Implements `TranslatePath()` with language override → iterate mapping rules (`Regex.IsMatch` then `Regex.Replace`, first match wins) → auto-swap → pass-through logic, `IsEnabled` property. Pure, no I/O.
- [ ] T019 Create unit tests for `NodeStructureTool` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeStructure/NodeStructureToolTests.cs` — cover regex map hit (with capture groups `$1`/`$2`), auto-swap, external path pass-through, case-insensitive matching, whitespace trimming, language override normalisation, tool disabled state, `Enabled: false` with differing project names emits warning (FR-023)

**Checkpoint**: Foundation ready — `INodeStructureTool` core path translation working, all user story implementation can proceed.

---

## Phase 3: User Story 1 — Map Area and Iteration Paths Across Projects (Priority: P1) 🎯 MVP

**Goal**: Operators can declare ordered regex mapping rules (`Match`/`Replacement` pairs) for `System.AreaPath` and `System.IterationPath` that are applied at import time.

**Independent Test**: Configure a single source → target area path mapping, import work items, verify target work items carry the mapped path.

### Implementation for User Story 1

- [ ] T020 [US1] Integrate `INodeStructureTool` into `RevisionFolderProcessor` — call `TranslatePath()` for `System.AreaPath` and `System.IterationPath` fields during import, apply result to revision before target API write. Update `RevisionFolderProcessorFactory` to pass `INodeStructureTool`.
- [ ] T021 [US1] Add OpenTelemetry metrics recording in `NodeStructureTool` — `migration.nodes.import.translate.count`, `.map_hit`, `.autoswap_hit`, `.external`, `.unresolvable` counters using `MigrationMetrics` pattern
- [ ] T022 [US1] Add structured logging in `NodeStructureTool` — `Path translated` (Trace level), `Path unresolvable` (Warning level) with `DataClassification.Customer` scoping for path values

**Checkpoint**: User Story 1 fully functional — path mapping applied during import, observable via metrics and logs.

---

## Phase 4: User Story 7 — Export-Time Path Discovery (Priority: P1) 🎯 MVP

**Goal**: Export maintains a running set of all distinct area/iteration paths and writes `Nodes/referenced-paths.json` to the package.

**Independent Test**: Export work items with known paths, verify `Nodes/referenced-paths.json` contains exactly the distinct paths from all exported revisions.

### Gherkin Feature File

- [ ] T023 [US7] Create `features/export/workitems/nodestructure/path-discovery.feature` — translate spec.md User Story 7 acceptance scenarios (new path discovered, duplicate ignored, final artifact contents, resume with existing artifact) into conformant Gherkin

### Implementation for User Story 7

- [ ] T024 [US7] Implement `ReferencedPathTracker` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/ReferencedPathTracker.cs` — maintains in-memory `HashSet<string>` (case-insensitive) for area and iteration paths, loads existing `Nodes/referenced-paths.json` on init (for resume), writes `ReferencedPathsArtifact` to `IArtefactStore` on each new discovery
- [ ] T025 [US7] Integrate `ReferencedPathTracker` into `WorkItemExportOrchestrator` — after each `revision.json` is written, extract `System.AreaPath`/`System.IterationPath` and feed to tracker
- [ ] T026 [US7] Add OpenTelemetry span `nodes.export.discover` and metric `migration.nodes.export.discover.count` in `ReferencedPathTracker`
- [ ] T027 [US7] Create unit tests for `ReferencedPathTracker` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeStructure/ReferencedPathTrackerTests.cs` — cover new path, duplicate, resume from existing artifact, case-insensitive dedup

**Checkpoint**: Export produces `Nodes/referenced-paths.json` with all distinct paths. Package contains export-side path metadata.

---

## Phase 5: User Story 4a — Export Source Classification Tree (Priority: P1 — export-side prerequisite for US4)

**Goal**: Export always captures the full source area and iteration tree into `Nodes/source-tree.json`.

**Independent Test**: Run export, verify `Nodes/source-tree.json` contains all source area and iteration nodes with dates.

### Gherkin Feature File

- [ ] T028 [US4] Create `features/export/workitems/nodestructure/tree-capture.feature` — translate export-side acceptance scenarios (always written, area nodes as strings, iteration nodes with dates/backlog flag) into conformant Gherkin

### Implementation for User Story 4a (export side)

- [ ] T029 [P] [US4] Implement `AzureDevOpsClassificationTreeReader` (`IClassificationTreeReader`) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/AzureDevOpsClassificationTreeReader.cs` — enumerate area and iteration nodes from source ADO REST API via `IAsyncEnumerable`
- [ ] T030 [US4] Implement `ClassificationTreeCapture` in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/ClassificationTreeCapture.cs` — calls `IClassificationTreeReader`, writes `ClassificationTreeSnapshot` to `IArtefactStore` at `Nodes/source-tree.json`. Add span `nodes.export.tree` and metrics `.count`, `.duration_ms`, `.errors`
- [ ] T031 [US4] Integrate `ClassificationTreeCapture` into `WorkItemsModule.ExportAsync()` — call at the start of export before work item revision processing
- [ ] T032 [US4] Create unit tests for `ClassificationTreeCapture` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeStructure/ClassificationTreeCaptureTests.cs` — cover successful capture, API failure, empty tree

**Checkpoint**: Export produces both `Nodes/source-tree.json` (full tree) and `Nodes/referenced-paths.json` (discovered paths). All export-side artifacts complete.

---

## Phase 6: User Story 2 — Auto-Create Missing Nodes in the Target (Priority: P2)

**Goal**: When `AutoCreateNodes: true`, the tool automatically creates missing area/iteration nodes via the ADO API before work items are written.

**Independent Test**: Import against a target with no nodes (beyond root), with `AutoCreateNodes: true`, verify all required nodes are created.

### Gherkin Feature File

- [ ] T033 [US2] Create `features/import/workitems/nodestructure/auto-create-nodes.feature` — translate spec.md User Story 2 acceptance scenarios (missing node created, existing node idempotent, disabled flag, nested path ancestor-first) into conformant Gherkin

### Implementation for User Story 2

- [ ] T034 [US2] Implement `AzureDevOpsNodeCreator` (`INodeCreator`) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/AzureDevOpsNodeCreator.cs` — `NodeExistsAsync` (GET, 200/404), `EnsureExistsAsync` (check → POST, handle 409, ancestor-first for nested paths), `SetIterationDatesAsync` (PATCH). Exponential back-off retry on 5xx/408/429. Fatal on 401/403/400. Spans: `nodes.api.ensure`, `nodes.api.set_dates`
- [ ] T035 [US2] Implement `NodeEnsurer` (pre-collection phase) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeEnsurer.cs` — reads `Nodes/referenced-paths.json` (fast path) or falls back to scanning all revision folders via `IArtefactStore.EnumerateAsync()` (note: fallback reads and deserialises each `revision.json` — potentially expensive for large packages), applies `INodeStructureTool.TranslatePath()` to each, calls `INodeCreator.EnsureExistsAsync()` for each distinct translated path. Add span `nodes.import.precollect` and metrics `.count`, `.duration_ms`, `.errors`, `.in_flight`
- [ ] T036 [US2] Integrate `NodeEnsurer` pre-collection into `WorkItemsModule.ImportAsync()` — call after `ReplicateSourceTree` step (Phase 8) and before `RevisionFolderProcessor` loop, when `AutoCreateNodes: true`
- [ ] T037 [US2] Create unit tests for `NodeEnsurer` (pre-collection) in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeStructure/NodeEnsurerTests.cs` — cover fast path (referenced-paths.json), fallback scan, empty package, node already exists, API failure

**Checkpoint**: Auto-create nodes working. Import can succeed against an empty target classification tree.

---

## Phase 7: User Story 3 — Skip or Fail Gracefully on Unresolvable Paths (Priority: P3)

**Goal**: When a path cannot be resolved, the operator can configure skip (with warning) or fail behaviour.

**Independent Test**: Configure `SkipOnUnresolvableArea: true`, present an unresolvable revision, verify it is skipped with warning.

### Gherkin Feature File

- [ ] T038 [US3] Create `features/import/workitems/nodestructure/skip-unresolvable.feature` — translate spec.md User Story 3 acceptance scenarios (skip area, skip iteration, fail when both false) into conformant Gherkin

### Implementation for User Story 3

- [ ] T039 [US3] Add skip/fail logic in `RevisionFolderProcessor` integration point — when `PathTranslation.TargetPath` is null: check `SkipOnUnresolvableArea`/`SkipOnUnresolvableIteration`, emit warning progress event and skip revision, or fail with descriptive error (field name, path value, revision folder)
- [ ] T040 [US3] Add structured logging for skip/fail events — `Revision skipped (unresolvable path)` (Warning), import failure with descriptive error message (Error). External path warning (FR-025) — MUST identify the path as external (not anchored in source project) distinctly from generic unresolvable warnings. Add dedicated unit test cases verifying: (a) external path emits warning with "external" identification, (b) generic unresolvable path emits warning without "external" label.

**Checkpoint**: Graceful degradation on bad paths. Large migrations can succeed with a small percentage of bad paths skipped.

---

## Phase 8: User Story 4 — Replicate All Source Nodes to the Target (Priority: P4)

**Goal**: When `ReplicateSourceTree: true`, the import pre-populates the target classification tree from `Nodes/source-tree.json`.

**Independent Test**: Import with `ReplicateSourceTree: true` against an empty target, verify all source nodes appear before any work item is written.

### Gherkin Feature File

- [ ] T041 [US4] Create `features/import/workitems/nodestructure/replicate-source-tree.feature` — translate spec.md User Story 4 acceptance scenarios (all nodes replicated, flag disabled, resume after interruption) into conformant Gherkin

### Implementation for User Story 4 (import side)

- [ ] T042 [US4] Implement `NodeEnsurer` (replicate phase) in `NodeEnsurer.cs` — streaming read of `Nodes/source-tree.json` via `System.Text.Json` streaming APIs (one node at a time per AD-6), check `NodeReplicationProgress` checkpoint, call `INodeCreator.EnsureExistsAsync()` for each, persist checkpoint after each node. Add span `nodes.import.replicate` and per-node span `nodes.import.replicate.node`. Metrics: `.count`, `.duration_ms`, `.errors`, `.skipped`, `.in_flight`
- [ ] T043 [US4] Integrate `NodeEnsurer` replicate phase into `WorkItemsModule.ImportAsync()` — call before pre-collection phase (Phase 6) when `ReplicateSourceTree: true` and `Nodes/source-tree.json` exists. Log warning and skip if artifact absent.
- [ ] T044 [US4] Add resumability tests for `NodeEnsurer` replicate phase — simulate interruption at node 50 of 200, verify resume skips confirmed nodes and continues from 51

**Checkpoint**: Full tree replication working. Operators can replicate the complete source tree to the target before any work item processing.

---

## Phase 9: User Story 5 — Override Localised Node Names (Priority: P5)

**Goal**: Cross-locale migrations normalise the root segment of paths before mapping.

**Independent Test**: Configure `AreaLanguageOverride: "Area"`, present a path starting with `"Área"`, verify root segment is normalised.

### Gherkin Feature File

- [ ] T045 [US5] Create `features/import/workitems/nodestructure/language-override.feature` — translate spec.md User Story 5 acceptance scenarios (area override, iteration override) into conformant Gherkin

### Implementation for User Story 5

- [ ] T046 [US5] Verify language override logic in `NodeStructureTool.TranslatePath()` is complete — root segment normalisation was implemented in T018 as part of the core translation pipeline. Add specific unit tests for localised root segments (`"Área"` → `"Area"`, `"Iteración"` → `"Iteration"`).

**Checkpoint**: Cross-locale path normalisation working.

---

## Phase 10: User Story 6 — Preserve Iteration Node Start/Finish Dates (Priority: P4)

**Goal**: When replicating iteration nodes via `ReplicateSourceTree`, sprint dates are preserved.

**Independent Test**: Export iteration nodes with dates, import with `ReplicateSourceTree: true`, verify target nodes have correct dates.

### Gherkin Feature File

- [ ] T047 [US6] Create `features/import/workitems/nodestructure/iteration-dates.feature` — translate spec.md User Story 6 acceptance scenarios (dates set, area nodes no dates, null dates no error) into conformant Gherkin

### Implementation for User Story 6

- [ ] T048 [US6] Add date-setting logic to `NodeEnsurer` replicate phase — after creating each iteration node, call `INodeCreator.SetIterationDatesAsync()` with `startDate`/`finishDate` from `IterationNodeEntry`. Skip if both null. Log warning on failure (non-blocking per FR-026). Add span `nodes.api.set_dates`.
- [ ] T049 [US6] Add unit tests for iteration date setting — cover dates set, area node skipped, null dates no-op, API failure logged as warning

**Checkpoint**: Sprint dates preserved during tree replication.

---

## Phase 11: Validation (Cross-Cutting)

**Goal**: Pre-import validation scans the package for unmapped, external, and malformed paths.

### Gherkin Feature File

- [ ] T050 Create `features/platform/validation/nodestructure-validation.feature` — translate spec.md validation requirements (FR-021: unmapped paths, external paths, malformed targets, invalid regex patterns, revision count per path) into conformant Gherkin

### Implementation

- [ ] T051 Implement `NodeStructureValidator` (`INodeStructureValidator`) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeStructureValidator.cs` — reads `Nodes/referenced-paths.json` (fast path) or scans all revision folders, applies `INodeStructureTool.TranslatePath()` to each path, collects `UnmappedPathFinding` entries (with affected revision counts), flags external paths distinctly, validates `Match` patterns for syntactically valid regex (FR-004a), validates that `Replacement` values after substitution do not produce empty or ADO-illegal-character paths (`\`, `/`, `$`, `?`, `*`, `"`, `:`, `>`, `<`, `|`, `#`, `%`, `+`, control chars). Returns `NodeStructureValidationReport`. Add span `nodes.validate` and metrics `.duration_ms`, `.unmapped_paths`, `.external_paths`, `.malformed_targets`
- [ ] T052 Create unit tests for `NodeStructureValidator` in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeStructure/NodeStructureValidatorTests.cs` — cover all mapped (valid), unmapped paths, external paths, malformed target values (ADO illegal chars), invalid regex patterns, empty package, referenced-paths fast path vs revision scan fallback

**Checkpoint**: Validation provides operators with a complete picture of path coverage gaps before import.

---

## Phase 12: Documentation Sync (MANDATORY)

**Purpose**: Ensure all canonical docs reflect what was implemented. Blocking gate — spec is not complete without this.

- [ ] T053 Update `docs/configuration.md` — add `### NodeStructure Tool` subsection under `## Tools` with JSON schema, property table, and config example (resolves discrepancy #1 in `discrepancies.md`)
- [ ] T054 [P] Update `docs/modules.md` — add `NodeStructureTool` to Tool Resolution section; update `WorkItemsModule` responsibility row to note `Revisions` extension optionally consumes `INodeStructureTool` (resolves discrepancy #2)
- [ ] T055 [P] Update `.agents/context/package-format.md` — add `Nodes/` as a top-level package folder with `source-tree.json` and `referenced-paths.json` artifact descriptions (resolves discrepancy #3)
- [ ] T056 Mark all items in `specs/023-workitems-nodestructure-tool/discrepancies.md` as `Resolved` or `N/A`
- [ ] T057 Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [ ] T058 Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T059 Run `dotnet test` — ALL tests MUST pass
- [ ] T060 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output

---

## Dependencies & Execution Order

### Phase Dependencies

```
Phase 1 (Setup)           → no dependencies
Phase 2 (Foundational)    → depends on Phase 1
Phase 3 (US1 — P1)        → depends on Phase 2
Phase 4 (US7 — P1)        → depends on Phase 1 (types only), can parallel with Phase 3
Phase 5 (US4a export)     → depends on Phase 1 (types only), can parallel with Phase 3/4
Phase 6 (US2 — P2)        → depends on Phase 2 + Phase 4 (needs referenced-paths)
Phase 7 (US3 — P3)        → depends on Phase 3 (needs RevisionFolderProcessor integration)
Phase 8 (US4 import)      → depends on Phase 5 (export side) + Phase 6 (INodeCreator)
Phase 9 (US5 — P5)        → depends on Phase 2 (language override in core tool — verify only)
Phase 10 (US6 — P4)       → depends on Phase 8 (replicate phase in NodeEnsurer)
Phase 11 (Validation)     → depends on Phase 2 + Phase 4 (needs tool + referenced-paths)
Phase 12 (Doc Sync)       → depends on ALL prior phases
```

### User Story Dependencies

| Story | Phase | Can Start After | Independent Test? |
|---|---|---|---|
| US1 (Path Mapping) | 3 | Phase 2 | ✅ Yes — configure map, import, verify target paths |
| US7 (Path Discovery) | 4 | Phase 1 | ✅ Yes — export, verify referenced-paths.json |
| US4a (Tree Capture) | 5 | Phase 1 | ✅ Yes — export, verify source-tree.json |
| US2 (Auto-Create) | 6 | Phase 2 + 4 | ✅ Yes — import against empty target, verify nodes created |
| US3 (Skip/Fail) | 7 | Phase 3 | ✅ Yes — import with bad path, verify skip/fail |
| US4 (Replicate) | 8 | Phase 5 + 6 | ✅ Yes — import with ReplicateSourceTree, verify all nodes |
| US5 (Language) | 9 | Phase 2 | ✅ Yes — configure override, verify normalisation |
| US6 (Dates) | 10 | Phase 8 | ✅ Yes — replicate with dates, verify target dates |

### Parallel Opportunities

**Maximum parallelism after Phase 1:**
- Phase 3 (US1), Phase 4 (US7), and Phase 5 (US4a export) can all start simultaneously
- Phase 9 (US5) can start as soon as Phase 2 completes

**Sequential chains:**
- Phase 6 (US2) → Phase 8 (US4 import) → Phase 10 (US6)
- Phase 3 (US1) → Phase 7 (US3)

---

## Implementation Strategy

### MVP First (User Stories 1 + 7 only)

1. Complete Phase 1: Setup (all types and interfaces)
2. Complete Phase 2: Foundational (core `NodeStructureTool`)
3. Complete Phase 3: US1 — Path Mapping (import integration)
4. Complete Phase 4: US7 — Path Discovery (export integration)
5. **STOP and VALIDATE**: Export produces `referenced-paths.json`, import applies path mapping

### Incremental Delivery

6. Phase 5: US4a — Tree Capture (export `source-tree.json`)
7. Phase 6: US2 — Auto-Create Nodes (pre-collection + node creation)
8. Phase 7: US3 — Skip/Fail on unresolvable paths
9. Phase 8: US4 — Replicate Source Tree (bulk replication)
10. Phase 9: US5 — Language Override (verify + test)
11. Phase 10: US6 — Iteration Dates (date preservation)
12. Phase 11: Validation
13. Phase 12: Documentation Sync
