# Implementation Plan: WorkItemsModule — NodeStructure Tool

**Branch**: `023-workitems-nodestructure-tool` | **Date**: 2026-04-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/023-workitems-nodestructure-tool/spec.md`

---

## Summary

The NodeStructure Tool adds area/iteration path mapping, node creation, and classification tree replication to the WorkItemsModule. The design splits the feature into two concerns: a **pure path-mapping tool** (`INodeStructureTool`) called per-revision during import, and an **infrastructure service** (`INodeCreator`) that manages target-side ADO API calls for node creation. Path mappings use an ordered list of regex `Match`/`Replacement` rules (first match wins, with `RegexOptions.NonBacktracking` for ReDoS protection), matching the model from the predecessor `TfsNodeStructureTool`. Export always captures the source classification tree (`Nodes/source-tree.json`) and discovered paths (`Nodes/referenced-paths.json`) as package metadata. Import uses these artifacts for bulk replication and pre-collection passes before streaming revision processing.

---

## Technical Context

**Language/Version**: C# 10+, .NET 10 (multi-targeting `net481;net10.0` for Abstractions only)
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, `System.Text.Json`, `System.Text.RegularExpressions` (regex mapping with `NonBacktracking`), ADO REST API (Classification Nodes)
**Storage**: `IArtefactStore` (package artifacts: `Nodes/source-tree.json`, `Nodes/referenced-paths.json`), `IStateStore` (checkpoint: `nodestructure-nodes-confirmed`)
**Testing**: Reqnroll.MSTest + Moq (`MockBehavior.Strict`)
**Target Platform**: .NET 10 (MigrationAgent, CLI); net481 (TFS exporter — no NodeStructure involvement)
**Project Type**: Library/module extension within existing modular monolith
**Performance Goals**: Path translation < 5µs per call (regex match + replace with pre-compiled `NonBacktracking` patterns); node creation bound by ADO API latency (~100ms per call)
**Constraints**: Streaming — no in-memory buffer of all revisions; bounded path set (typically 10s–100s of distinct paths)
**Scale/Scope**: Typical: 10–500 classification nodes, 10–100 distinct paths per project

---

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Confirmed ALL files in `/.agents/guardrails/`, `/.agents/context/`, and relevant `/docs/` files have been read.

- [x] **Package-First (I):** Export writes `Nodes/source-tree.json` and `Nodes/referenced-paths.json` via `IArtefactStore`. Import reads from these artifacts — never from the live source API. No direct source-to-target migration.
- [x] **Streaming (II):** `Nodes/source-tree.json` is streamed one node at a time during import replication (FR-016). Revision processing is standard streaming via `RevisionFolderProcessor`. Only the distinct path set (bounded, ~100s) is held in memory for pre-collection.
- [x] **WorkItems Layout (III):** No changes to `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` layout. New artifacts live under `Nodes/` (separate top-level folder).
- [x] **Checkpointing (IV):** Node replication checkpoint stored in `IStateStore` under key `nodestructure-nodes-confirmed`. Standard cursor-based checkpointing.
- [x] **Module Isolation (V):** All persistence through `IArtefactStore`/`IStateStore`. `INodeCreator` and `IClassificationTreeReader` are abstractions in `Abstractions.Agent`. No concrete store references in module code.
- [x] **Separation of Planes (VI):** No migration logic in CLI, TUI, or control plane. All NodeStructure logic executes within the MigrationAgent.
- [x] **Determinism (VII):** Same export input → same `source-tree.json` and `referenced-paths.json`. Config schema addition is additive (minor version bump, no upgrader needed). Package artifact schema is versioned in `manifest.json`.
- [x] **ATDD-First (VIII):** All 7 user stories have Given/When/Then acceptance scenarios (29 scenarios total). Each scenario will be implemented via the ATDD inner loop.
- [x] **SOLID & DI (IX):** All services via constructor injection. `NodeStructureOptions` is `sealed` with `init`-only properties and `SectionName` constant. Registration via `AddNodeStructureToolServices()` extension method. Interfaces in `Abstractions.Agent`.

---

## Project Structure

### Documentation (this feature)

```text
specs/023-workitems-nodestructure-tool/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── interfaces.md
├── discrepancies.md     # Architecture discrepancies log
└── tasks.md             # Phase 2 output (speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   └── Options/
│       ├── NodeStructureOptions.cs          # Configuration model
│       └── NodeMapping.cs                   # Regex Match/Replacement pair
│
├── DevOpsMigrationPlatform.Abstractions.Agent/
│   └── Tools/
│       ├── INodeStructureTool.cs            # Pure path-mapping interface
│       ├── INodeCreator.cs                  # Target node creation interface
│       ├── IClassificationTreeReader.cs     # Source tree enumeration interface
│       ├── INodeStructureValidator.cs       # Package validation interface
│       ├── PathTranslation.cs               # Translation result record
│       ├── ProjectMapping.cs                # Source/target project context
│       ├── ClassificationNodeType.cs        # Area/Iteration enum
│       ├── IterationNodeEntry.cs            # Node entry with dates
│       ├── ClassificationTreeSnapshot.cs    # source-tree.json DTO
│       ├── ReferencedPathsArtifact.cs       # referenced-paths.json DTO
│       ├── NodeStructureValidationReport.cs # Validation result
│       └── UnmappedPathFinding.cs           # Unmapped path detail
│
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   └── Tools/
│       └── NodeStructure/
│           ├── NodeStructureTool.cs                    # INodeStructureTool impl
│           ├── NodeStructureValidator.cs               # INodeStructureValidator impl
│           ├── NodeEnsurer.cs                          # Pre-import node creation orchestration
│           ├── ClassificationTreeCapture.cs            # Export-side tree capture
│           ├── ReferencedPathTracker.cs                # Export-side path discovery
│           ├── NodeReplicationProgress.cs              # Checkpoint record
│           ├── AzureDevOpsNodeCreator.cs               # INodeCreator impl (ADO REST)
│           ├── AzureDevOpsClassificationTreeReader.cs  # IClassificationTreeReader impl
│           └── NodeStructureToolServiceCollectionExtensions.cs  # DI registration
│
tests/
└── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
    └── Tools/
        └── NodeStructure/
            ├── NodeStructureToolTests.cs
            ├── NodeStructureValidatorTests.cs
            ├── NodeEnsurerTests.cs
            ├── ClassificationTreeCaptureTests.cs
            ├── ReferencedPathTrackerTests.cs
            └── Steps/                        # Reqnroll step definitions
                ├── NodeStructureSteps.cs
                └── NodeStructureContext.cs

features/
├── export/
│   └── workitems/
│       └── nodestructure/
│           ├── tree-capture.feature
│           └── path-discovery.feature
├── import/
│   └── workitems/
│       └── nodestructure/
│           ├── path-mapping.feature
│           ├── auto-create-nodes.feature
│           ├── skip-unresolvable.feature
│           ├── replicate-source-tree.feature
│           └── language-override.feature
└── platform/
    └── validation/
        └── nodestructure-validation.feature
```

**Structure Decision**: The NodeStructure tool follows the established FieldTransform tool pattern — abstractions in `Abstractions.Agent/Tools/`, implementations in `Infrastructure.Agent/Tools/NodeStructure/`, tests alongside in the agent test project. New `Nodes/` package folder is documented in `contracts/interfaces.md`.

---

## Complexity Tracking

> No constitution violations requiring justification.

| Decision | Rationale |
|----------|-----------|
| In-memory `HashSet<string>` for path pre-collection | Bounded by distinct path count (10s–100s), not revision count. Acceptable per streaming constraint. |
| In-memory `HashSet<string>` for confirmed nodes checkpoint | Same bounding — nodes per project, not revisions. |
| `ReferencedPathsArtifact` loaded fully at import time | The artifact contains distinct paths (bounded set), not revision data. Not a streaming violation. |

---

## Architecture Decisions

### AD-1: Split pure tool from I/O service

**Decision**: `INodeStructureTool` (pure path mapping, no I/O) + `INodeCreator` (target ADO API calls, stateful).

**Rationale**: Tools called inside `RevisionFolderProcessor` must be fast and deterministic. ADO API calls in the per-revision hot path would be a reliability disaster. The pre-collection pattern (FR-024) naturally separates path discovery from node creation.

**Alternatives rejected**: Single fat `INodeStructureTool` with embedded API calls — violates documented tool purity contract in `docs/modules.md`.

### AD-2: Export always captures source tree and discovered paths

**Decision**: `Nodes/source-tree.json` and `Nodes/referenced-paths.json` are always written on export, regardless of configuration flags.

**Rationale**: These are package metadata. Always having them enables import-time decisions without re-scanning, and provides an audit trail of source structure. The `ReplicateSourceTree` flag controls import-side behaviour only.

### AD-3: Pre-collection uses referenced-paths.json fast path

**Decision**: Import-time pre-collection reads `Nodes/referenced-paths.json` if present; falls back to scanning all revision folders if absent (legacy packages).

**Rationale**: Export-time path discovery (FR-015b/FR-029) makes import startup O(1) for path discovery instead of O(n) in revision count. Legacy packages without the artifact gracefully degrade to the scan path.

### AD-4: Node creation checkpoint in IStateStore

**Decision**: `NodeReplicationProgress` with a `HashSet<string>` of confirmed paths, persisted after each node creation.

**Rationale**: Simpler than cursor-based sequential checkpointing because node order in `source-tree.json` may not be lexicographic. A set-based approach handles any ordering and is still bounded by node count.

**Constitution note**: This uses `IStateStore` (compliant with checkpoint guardrail IV) but uses a set rather than a forward-only cursor. The set approach is justified because: (a) nodes are not ordered lexicographically in the artifact, (b) the set is bounded by node count (~hundreds, not millions), (c) the alternative (sorting nodes and using a cursor) would require in-memory sort which violates streaming guardrail II.

### AD-5: INodeStructureTool interface designed for dual consumers

**Decision**: `TranslatePath(fieldName, sourcePathValue, ProjectMapping)` operates on individual path values, not field dictionaries.

**Rationale**: Both `WorkItemsModule` (revision field values) and `TeamsModule` (team area/iteration path settings) work with individual path strings. A field-dictionary interface would couple the tool to revision structure.

### AD-6: Streaming deserialization for source-tree.json at import time

**Decision**: Use `System.Text.Json` streaming APIs (`Utf8JsonReader` or `DeserializeAsyncEnumerable`) to read `source-tree.json` one node at a time during bulk replication.

**Rationale**: While `source-tree.json` is typically small (hundreds of nodes), the streaming approach is consistent with the architecture's streaming principle and prevents edge cases with unusually large classification trees from causing memory issues.

### AD-7: Regex-based path mapping with ReDoS protection

**Decision**: `AreaPathMappings` and `IterationPathMappings` are ordered lists of `NodeMapping` records (each with `Match` regex pattern and `Replacement` regex replacement string). Matching uses `Regex.IsMatch` + `Regex.Replace` with `RegexOptions.IgnoreCase | RegexOptions.NonBacktracking`. First matching rule wins.

**Rationale**: The predecessor `TfsNodeStructureTool` in `azure-devops-migration-tools` uses regex mapping with capture groups (`$1`, `$2`), which operators rely on for complex path restructuring (e.g., consolidating multiple source subtrees into a single target hierarchy). Exact-match is impractical for real-world migrations. `NonBacktracking` prevents ReDoS from malicious or poorly-written patterns — the old tool had no such protection.

**Alternatives rejected**: Exact-match dictionaries — too inflexible for production use cases requiring pattern-based restructuring. Regex without `NonBacktracking` — leaves a DoS vulnerability.

---

## Constitution Check — Post-Design Re-evaluation

All constitution principles re-checked against the completed design:

- [x] **Package-First (I):** Verified — all export writes go through `IArtefactStore`, all import reads from package artifacts.
- [x] **Streaming (II):** Verified — `source-tree.json` streamed at import (AD-6). Only bounded sets (distinct paths, confirmed nodes) held in memory. No revision buffering.
- [x] **WorkItems Layout (III):** Verified — no changes to WorkItems folder structure.
- [x] **Checkpointing (IV):** Verified — `IStateStore` used for node replication progress (AD-4).
- [x] **Module Isolation (V):** Verified — all 4 interfaces (`INodeStructureTool`, `INodeCreator`, `IClassificationTreeReader`, `INodeStructureValidator`) defined in `Abstractions.Agent`. Implementations in `Infrastructure.Agent`.
- [x] **Separation of Planes (VI):** Verified — no migration logic in CLI/TUI/control plane.
- [x] **Determinism (VII):** Verified — additive schema change, artifact schemas documented in contracts.
- [x] **ATDD-First (VIII):** Verified — 29 acceptance scenarios across 7 user stories.
- [x] **SOLID & DI (IX):** Verified — constructor injection throughout, `IOptions<NodeStructureOptions>` for config, dedicated `AddNodeStructureToolServices()` extension.
- [x] **Engineering Practice (X):** Verified — OpenTelemetry metrics/traces/logging defined in spec Observability section, `DataClassification.Customer` scoping for path values, retry with exponential back-off (FR-022), ReDoS protection via `RegexOptions.NonBacktracking` (FR-004a).
