# Implementation Plan: WorkItemsModule — NodeStructure Tool

**Branch**: `023-workitems-nodestructure-tool` | **Date**: 2026-04-26 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/023-workitems-nodestructure-tool/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

The NodeStructure Tool provides area and iteration path remapping for cross-project Azure DevOps work item migration. The tool is split into two architectural concerns:

1. **Pure path mapping** (`INodeStructureTool`) — translates `System.AreaPath`/`System.IterationPath` values via explicit maps, auto project-name swap, and language override. Stateless, no I/O. Called per-revision during import.

2. **Node creation service** (`INodeCreator`) — ensures target classification nodes exist via the ADO REST API. Called during import pre-processing (bulk node creation before revision processing begins). Supports checkpointed resumability via `IStateStore`.

Additionally, the export phase always captures the full source classification tree to `Nodes/source-tree.json` via `IClassificationTreeReader`, and incrementally writes `Nodes/referenced-paths.json` with all distinct paths discovered during revision export.

## Technical Context

**Language/Version**: C# 10+, .NET 10  
**Primary Dependencies**: `IArtefactStore`, `IStateStore`, `IOptions<T>`, ADO REST API (Classification Nodes), OpenTelemetry, `System.Text.Json`  
**Storage**: Package filesystem via `IArtefactStore` (`Nodes/source-tree.json`, `Nodes/referenced-paths.json`), `IStateStore` for node-creation checkpoint  
**Testing**: Reqnroll.MSTest + Moq (`MockBehavior.Strict`)  
**Target Platform**: Migration Agent (worker service), CLI (via Aspire)  
**Project Type**: Library (tool + infrastructure services within existing solution)  
**Performance Goals**: Pre-collection pass must complete in time proportional to revision count; path translation is O(1) per call (dictionary lookup).  
**Constraints**: Node creation is bound by ADO API rate limits (200 req/s); pre-collection must not buffer all revision data (only distinct path strings).  
**Scale/Scope**: Typical projects have 10–500 distinct area/iteration paths; large projects may have 1000+. Revision counts may be in the millions.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Before completing this gate, confirm that ALL files in
> `/.agents/guardrails/`, ALL files in `/.agents/context/`, and relevant `/docs/` files
> have been read. Skipping either `.agents/` subdirectory is a constitution violation.

**All guardrail files read**: `system-architecture.md`, `workitems-rules.md`, `migration-rules.md`, `coding-standards.md`, `testing-standards.md`, `definition-of-done.md`, `module-template.md`, `aspire-integration.md`, `acceptance-test-format.md`, `atdd-workflow.md`.  
**Context files read**: `package-format.md`, `workitems-format.md`.  
**Docs read**: `modules.md` (Tool Resolution), `configuration.md` (Tools section).

- [x] **Package-First (I):** Export always writes `Nodes/source-tree.json` and `Nodes/referenced-paths.json` to package via `IArtefactStore`. Import reads from package only — never from the live source API. Path remapping operates on `revision.json` data read from the package. No direct source-to-target migration.
- [x] **Streaming (II):** Import processes one revision folder at a time. The pre-collection pass (FR-024) reads from `Nodes/referenced-paths.json` when available, otherwise streams revision folders via `EnumerateAsync` and collects only distinct path strings (bounded set, not full revision data). `ReplicateSourceTree` processes one node at a time from the artifact (FR-016).
- [x] **WorkItems Layout (III):** No changes to `WorkItems/` folder structure. `Nodes/` is a new top-level module folder in the package, not a revision folder modification.
- [x] **Checkpointing (IV):** Node-creation checkpoint stored in `IStateStore` (FR-016a). Work item import cursor under `.migration/Checkpoints/` is unchanged.
- [x] **Module Isolation (V):** All persistence through `IArtefactStore`/`IStateStore`. `INodeStructureTool` and `INodeCreator` interfaces defined in Abstractions. ADO API calls wrapped behind `INodeCreator` (guardrail rule 12).
- [x] **Separation of Planes (VI):** Tool logic lives in Infrastructure.Agent. No migration logic in CLI, TUI, or Control Plane. Export-side `IClassificationTreeReader` runs in Agent context only.
- [x] **Determinism (VII):** Path translation is deterministic (same input → same output). Config schema addition is non-breaking (no upgrader needed for existing configs). New `Nodes/` package artifacts require schema version entries in manifest.
- [x] **ATDD-First (VIII):** Spec has 7 user stories with 20+ Given/When/Then acceptance scenarios. Each will be implemented via ATDD inner loop.
- [x] **SOLID & DI (IX):** `NodeStructureOptions` is sealed, `init`-only, with `SectionName` constant. All services registered via `AddNodeStructureToolServices()`. Constructor injection only. Interfaces in Abstractions.Agent.

## Project Structure

### Documentation (this feature)

```text
specs/023-workitems-nodestructure-tool/
├── spec.md              # Feature specification
├── plan.md              # This file
├── discrepancies.md     # Architecture discrepancies
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/
│   └── interfaces.md    # Phase 1 output
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT created by /speckit.plan)
```

### Source Code (new and modified files)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   └── Options/
│       └── NodeStructureOptions.cs                    # NEW — sealed config model
│
├── DevOpsMigrationPlatform.Abstractions.Agent/
│   └── Tools/
│       ├── INodeStructureTool.cs                      # NEW — pure path-mapping interface
│       ├── INodeCreator.cs                            # NEW — target node creation interface
│       ├── IClassificationTreeReader.cs               # NEW — source node enumeration (export)
│       ├── INodeStructureValidator.cs                 # NEW — package validation interface
│       ├── PathTranslation.cs                         # NEW — translation result record
│       ├── ProjectMapping.cs                          # NEW — source/target project context
│       ├── ClassificationNodeType.cs                  # NEW — Area/Iteration enum
│       ├── IterationNodeEntry.cs                      # NEW — iteration node with dates + backlog flag
│       ├── ClassificationTreeSnapshot.cs              # NEW — source-tree.json artifact model
│       ├── ReferencedPathsArtifact.cs                 # NEW — referenced-paths.json artifact model
│       └── NodeStructureValidationReport.cs           # NEW — validation result records
│
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   └── Tools/
│       └── NodeStructure/
│           ├── NodeStructureTool.cs                   # NEW — INodeStructureTool impl
│           ├── NodeStructureValidator.cs              # NEW — INodeStructureValidator impl
│           ├── NodeStructureToolServiceCollectionExtensions.cs  # NEW — DI registration
│           ├── NodeReplicationProgress.cs             # NEW — checkpoint record
│           ├── ClassificationTreeCapture.cs           # NEW — export-side tree capture (always-on)
│           ├── ReferencedPathTracker.cs               # NEW — export-side path discovery
│           └── NodeEnsurer.cs                         # NEW — pre-collection + bulk create
│
│   └── Import/
│       ├── RevisionFolderProcessor.cs                 # MODIFIED — integrate INodeStructureTool
│       └── RevisionFolderProcessorFactory.cs          # MODIFIED — pass tool to processor
│
│   └── Modules/
│       └── WorkItemsModule.cs                         # MODIFIED — call pre-processor + exporter
│
│   └── AzureDevOps/
│       ├── AzureDevOpsNodeCreator.cs                  # NEW — INodeCreator impl
│       └── AzureDevOpsClassificationTreeReader.cs     # NEW — IClassificationTreeReader impl

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
│   └── Tools/
│       └── NodeStructure/
│           ├── NodeStructureToolTests.cs              # NEW — unit tests for path mapping
│           ├── NodeStructureValidatorTests.cs         # NEW — unit tests for validation
│           ├── NodeEnsurerTests.cs                    # NEW — unit tests for pre-processing
│           ├── ClassificationTreeCaptureTests.cs      # NEW — unit tests for export
│           ├── ReferencedPathTrackerTests.cs          # NEW — unit tests for path discovery
│           └── Steps/                                 # NEW — Reqnroll step definitions
│               ├── NodeStructureSteps.cs
│               └── NodeStructureContext.cs

features/
├── import/
│   └── work-items/
│       └── node-structure/
│           ├── path-mapping.feature                   # NEW — User Stories 1, 5
│           ├── create-missing-nodes.feature           # NEW — User Story 2
│           ├── skip-invalid-paths.feature             # NEW — User Story 3
│           ├── replicate-all-nodes.feature            # NEW — User Stories 4, 6
│           └── validation.feature                     # NEW — ValidateAsync scenarios
├── export/
│   └── work-items/
│       └── node-structure/
│           ├── export-classification-tree.feature     # NEW — source tree capture (always-on)
│           └── export-path-discovery.feature          # NEW — User Story 7 (referenced paths)
```

**Structure Decision**: Follows the established `IFieldTransformTool` pattern. All new interfaces in `Abstractions.Agent/Tools/`. Implementation in `Infrastructure.Agent/Tools/NodeStructure/`. ADO-specific implementations in `Infrastructure.Agent/AzureDevOps/`. Options in `Abstractions/Options/`.

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| `INodeCreator` performs I/O (not a pure tool) | Node creation requires ADO API calls; cannot be made pure | Split from `INodeStructureTool` to preserve tool purity contract. The service is infrastructure, not a "tool" per `docs/modules.md` |
| `NodeEnsurer` reads all revision.json for distinct paths (fallback only) | FR-024 requires pre-collection pass when `Nodes/referenced-paths.json` is absent (legacy packages) | Only collects distinct path strings (bounded set); does not buffer full revision data. Modern packages use the referenced-paths artifact. |

---

## Architecture Decisions

### AD-001: Split Tool vs Service

`INodeStructureTool` (pure path mapping) is separated from `INodeCreator` (target API I/O). This preserves the `docs/modules.md` contract that tools are "pure transformations or lookup services — they perform no I/O." The service is consumed at the orchestration layer (`WorkItemsModule.ImportAsync()` pre-processing), not inside the per-revision `RevisionFolderProcessor` hot path.

### AD-002: Pre-Collection Pass Design

FR-024's pre-collection pass first checks for `Nodes/referenced-paths.json` in the package (written during export — FR-015b). If present, the discovered paths are read directly from this artifact (fast path). If absent (legacy package), it falls back to streaming all revision folders via `IArtefactStore.EnumerateAsync()`, extracting `System.AreaPath` and `System.IterationPath` from each `revision.json`, applying `INodeStructureTool.TranslatePath()`, and collecting distinct translated paths into a `HashSet<string>`. This set is bounded by the number of distinct paths (typically 10–500), not by the number of revisions (potentially millions). This does NOT violate Constitution Principle II (streaming) because it does not load all revisions into memory — it reads each revision once and retains only the path string.

### AD-003: Factory Integration Pattern

`RevisionFolderProcessorFactory` must be extended to pass both `IFieldTransformTool?` and `INodeStructureTool?` to `RevisionFolderProcessor`. The factory receives these via constructor injection (optional parameters). This also fixes the pre-existing gap where `IFieldTransformTool` is not passed through the factory.

### AD-004: Export-Import Artifact Boundary

`Nodes/source-tree.json` is always written by `ClassificationTreeCapture` (export). `Nodes/referenced-paths.json` is written incrementally during work item revision export by `ReferencedPathTracker`. The import side reads from these package artifacts — MUST NOT call `IClassificationTreeReader` — enforcing the Source → Package → Target invariant.

### AD-005: TeamsModule Future Contract

`INodeStructureTool.TranslatePath(fieldName, sourcePathValue, context)` is designed to be field-agnostic. `TeamsModule` will call it with team-specific path fields (e.g., default team area path). The interface requires no changes for `TeamsModule` integration — the `fieldName` parameter is used for logging/diagnostics, not for dispatch logic.

### AD-006: Node Creation Order

When creating nested paths (e.g., `"Project\\Area\\SubArea"`), the implementation MUST create ancestors before descendants. `INodeCreator.EnsureExistsAsync()` is responsible for walking the path segments from root to leaf and creating each missing segment in order.
