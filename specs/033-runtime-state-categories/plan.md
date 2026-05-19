# Implementation Plan: Runtime State Categories and Resume Semantics Alignment

**Branch**: `033-runtime-state-categories` | **Date**: 2026-05-07 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/033-runtime-state-categories/spec.md`

## Summary

Align runtime behavior with the documented scoped package model and the clarified save/progress cadence model. The implementation introduces explicit category boundaries for (1) package-wide orchestration state, (2) scoped module resume state (project, organisation, migration), (3) fine-grained processing save/progress state (including work-item-batch saves and work-item-level progress), and (4) run-scoped audit state. The plan updates path contracts, checkpoint semantics, and observability/progress behavior so resume correctness, operator visibility, and deterministic replay remain consistent across Inventory/Export/Import.

## Reconciliation Status (2026-05-17)

- **Overall**: Partially implemented; task file reconciled to mixed complete/incomplete/superseded state.
- **Incomplete implementation gaps**:
  - Planned O-1 span names `state.paths.resolve`, `state.workitems.batch.save`, and `state.progress.emit` are not present in current source.
  - Commit-discipline tasks T076-T078 are not evidenced by actual commits.
- **Superseded references**:
  - Multiple planned file locations were superseded by later architecture moves from specs/034-package-manager-adoption and specs/035-workitem-import-support.
- **Verification evidence**:
  - `/speckit.analyze` result: stale task paths + contradiction findings.
  - `/speckit.checklist` result: PASS/FAIL matrix with key FR-003 and observability gaps.
  - Targeted runtime-state test execution recorded in checklist output.

## Technical Context

**Language/Version**: C# 12 / .NET 10 (plus net481 support where already present in shared abstractions)  
**Primary Dependencies**: `Microsoft.Extensions.*` DI/options/logging, existing migration abstractions (`IArtefactStore`, `IStateStore`, `ICheckpointingService`), OpenTelemetry (`ActivitySource`, metrics, structured logging)  
**Storage**: Filesystem/Blob package via `IArtefactStore`; state via `IStateStore` under scoped metadata paths (`/{org}/{project}/.migration/`, `/{org}/.migration/`, and `/.migration/`)  
**Testing**: MSTest + Reqnroll + Moq; full solution build/test gates (`dotnet build`, `dotnet test`)  
**Target Platform**: Windows/Linux .NET 10 agent runtime; TFS connector support remains in net481 agent boundary  
**Project Type**: Modular monolith backend + CLI/TUI orchestration tooling  
**Performance Goals**: Resume from latest durable checkpoint with minimal replay; progress visibility updates at fine-grained cadence without unbounded buffering  
**Constraints**: Preserve streaming guarantees, lexicographic traversal, and package determinism; no run-folder participation in authoritative resume/phase gating; no connector coverage regression  
**Scale/Scope**: Cross-cutting state-contract update touching path helpers, checkpointing semantics, work-item export/import progress cadence, inventory alignment, tests, and associated docs/contracts

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading confirmed:** all required guardrails and context files were read this session before planning.

- [x] **Package-First (I):** Design keeps Source → Package → Target only; state authority remains package-resident via `IArtefactStore`/`IStateStore`.  
- [x] **Streaming (II):** Design preserves one-folder-at-a-time processing and forbids global in-memory sort/materialization.  
- [x] **WorkItems Layout (III):** WorkItems folder contract remains unchanged; only save/progress cadence and cursor identity semantics change.  
- [x] **Checkpointing (IV):** Design keeps authoritative resume semantics on scoped action-qualified cursors (`/{org}/{project}/.migration/`, `/{org}/.migration/`, and `/.migration/`) with read precedence project → org → package.  
- [x] **Module Isolation (V):** No direct filesystem bypass; state and artefacts remain behind abstractions and identity service conventions.  
- [x] **Separation of Planes (VI):** No migration logic moved into control plane/CLI/TUI; runtime execution remains in agent path.  
- [x] **Determinism (VII):** Resume and phase-gate decisions become more deterministic by removing run-scope authority and action-namespace collisions.  
- [x] **ATDD-First (VIII):** Spec contains prioritized user stories and Given/When/Then acceptance scenarios for state authority, action identity, and fine-grained cadence behavior.  
- [x] **SOLID & DI (IX):** Planned changes stay within existing DI and abstraction seams (`PackagePaths`, checkpointing services, orchestrators).  
- [x] **Full Connector Coverage (XI):** State semantics and progress/save cadence changes are connector-agnostic and must be validated for Simulated, AzureDevOpsServices, and TFS-capable paths.

## Observability Contract

*GATE: Must be completed before task generation. Every operation below must be represented in `tasks.md`.*

### Operations Table

| Operation | Class / Method | Span Name (O-1) | Metrics Instruments (O-2) | Log Events (O-3) | ProgressEvent Stage (O-4) |
|---|---|---|---|---|---|
| Resolve authoritative state paths | `PackagePaths` + checkpoint consumers | `state.paths.resolve` | state path resolution attempts/errors/duration | Info on resolved authoritative scope; warning/error on invalid scope usage | `StatePathResolved`, `StatePathRejected` |
| Read/write project action cursor | checkpointing service + module orchestrators | `state.cursor.update` | cursor writes, cursor reads, cursor write latency, cursor conflicts | Info on cursor advance; warning when stale/non-authoritative path encountered | `CheckpointAdvanced` |
| Persist work-item batch checkpoint | work-item export/import orchestrators | `state.workitems.batch.save` | batches completed, batch save latency, batch save errors | Info on completed batch persisted; warning on replay window | `BatchCompleted` |
| Emit fine-grained processing progress | module orchestrators across long-running processing | `state.progress.emit` | progress events emitted, progress lag, progress errors | Debug/Info for per-item or per-batch progress cadence | `ProcessingProgress` |
| Enforce run-scope audit-only behavior | plan/phase gate evaluators | `state.runscope.guard` | run-scope guard checks, violations blocked | Warning/Error when run-scoped files are attempted as authoritative input | `RunScopeIgnored` |

### Wiring Checklist

- [x] **O-1 ActivitySource:** New/updated operations include explicit spans in the migration activity source space.
- [x] **O-2 Metric instruments:** Progress/save/path guard operations emit attempt/completion/error/duration and in-flight where applicable.
- [x] **O-2 Meter registration:** Existing meters reused unless a new explicit state meter is justified; if new, register in both relevant hosts.
- [x] **O-3 Log structured params:** All logs use structured fields and classification-safe payloads.
- [x] **O-4 IProgressSink wiring:** Progress cadence is explicit and optional-sink-safe.
- [x] **O-4 ModuleCounters property:** Counters used by CLI/TUI telemetry snapshots remain consistent with `JobMetrics` extraction.
- [x] **O-4 CLI row:** Existing mode-driven views continue to consume telemetry snapshots for aggregate counters.
- [x] **DI wiring verified:** Any added services/strategies are registered via dedicated extensions.

### Tests Required for Observability

- [x] Unit tests for state path authority selection and run-scope rejection behavior.
- [x] Unit tests for action-qualified cursor identity (inventory/export/import isolation).
- [x] Unit tests for batch-save cadence and replay minimization semantics.
- [x] Unit tests for progress emission cadence (work-item-level and non-work-item reasonable granularity).
- [x] Simulated system tests validating end-to-end resume + progress visibility behavior.

## Project Structure

### Documentation (this feature)

```text
specs/033-runtime-state-categories/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── runtime-state-contract.md
└── tasks.md (generated later by /speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Infrastructure.Agent/
│   ├── Context/
│   │   ├── PackagePaths.cs
│   │   └── CheckpointingService.cs
│   ├── Discovery/
│   │   └── InventoryOrchestrator.cs
│   └── WorkItems/
│       ├── WorkItemExportOrchestrator.cs
│       ├── WorkItemImportOrchestrator.cs
│       └── RevisionFolderProcessor.cs
├── DevOpsMigrationPlatform.MigrationAgent/
│   └── JobAgentWorker.cs
└── DevOpsMigrationPlatform.Abstractions*/

tests/
├── DevOpsMigrationPlatform.Infrastructure.Agent.Tests/
└── DevOpsMigrationPlatform.CLI.Migration.Tests/

docs/
├── package-format-reference.md
├── package-guide.md
└── migration-process-guide.md
```

**Structure Decision**: Targeted cross-module runtime/state contract updates in existing projects; no new project introduced.

## Phase 0 — Research Outcomes

Research outcomes are captured in [research.md](research.md). No unresolved `NEEDS CLARIFICATION` entries remain.

## Phase 1 — Design Outputs

- Data model: [data-model.md](data-model.md)
- Contract: [contracts/runtime-state-contract.md](contracts/runtime-state-contract.md)
- Validation quickstart: [quickstart.md](quickstart.md)
- Agent context update: `.github/copilot-instructions.md` plan pointer updated to this plan

## Post-Design Constitution Re-Check

All constitutional gates remain **PASS** after Phase 1 artifacts. No violations or exceptions introduced.

## Complexity Tracking

No constitution violations requiring justification.
