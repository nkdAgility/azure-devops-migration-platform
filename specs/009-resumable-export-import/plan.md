# Implementation Plan: Resumable Export and Import

**Branch**: `009-resumable-export-import` | **Date**: 2026-04-10 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/009-resumable-export-import/spec.md`

## Summary

Historical implementation plan for resumable export/import.  
Current implementation has delivered core behavior with architectural evolution (`MigrationJob` → `Job`, command-specific CLI → queue-centric CLI, direct infra pathing → package-manager boundaries).

### Reconciliation Snapshot (2026-05-17)

- **Current status**: Most planned capabilities are implemented; this plan now serves as historical design intent with reconciled status tracked in `tasks.md`.
- **Remaining incomplete task IDs**: `T005`, `T015`, `T026`, `T034`.
- **Completed because superseded (IDs)**: `T002`, `T006`, `T007`, `T008`, `T009`, `T011`, `T012`, `T013`, `T014`, `T016`, `T017`, `T018`, `T019`, `T020`, `T021`, `T022`, `T023`, `T025`, `T027`, `T031`, `T032`.
- **Contradictions and reconciliation**:
  - References to `MigrationAgentWorker`, `Migration*Command*`, `MigrationJob*` are superseded by `JobAgentWorker`, `QueueCommand*`, and `Job*`.
  - References to direct concrete storage/paths are superseded by `IPackageAccess` and `.migration` state routing.
- **Verification evidence**:
  - Build pass: `dotnet build DevOpsMigrationPlatform.slnx --nologo` (2026-05-17).
  - Implemented services: `CheckpointingService`, `PhaseTrackingService`, `WorkItemImportOrchestrator`.
  - Implemented CLI: `QueueCommandSettings --force-fresh`, `QueueCommand`.
  - Known verification gap: full-suite `dotnet test` and launch-profile scenario evidence remain open (`T034`).
  - Known contradiction: `quickstart.md` force-fresh overwrite wording conflicts with FR-012 non-overwrite requirement.

## Technical Context

**Language/Version**: C# 12 / .NET 10 (Abstractions multi-targeted `net481;net10.0`)  
**Primary Dependencies**: `DevOpsMigrationPlatform.Abstractions` (ICheckpointingService, IArtefactStore, IStateStore, CursorEntry, CursorStage), `DevOpsMigrationPlatform.Infrastructure` (CheckpointingService, WorkItemExportOrchestrator), `DevOpsMigrationPlatform.Infrastructure.AzureDevOps`, Reqnroll + MSTest  
**Storage**: Package filesystem via `IArtefactStore` / `IStateStore` — `Checkpoints/*.cursor.json`, `Checkpoints/job.phase.json`  
**Testing**: Reqnroll `.feature` files + `[Binding]` step definitions under `tests/DevOpsMigrationPlatform.Infrastructure.Tests/`; MSTest unit tests for orchestrators  
**Target Platform**: net10.0 Migration Agent; net481 TFS subprocess (Abstractions only)  
**Project Type**: Service library module within the Migration Agent job engine  
**Performance Goals**: Resume evaluation is O(1) — a single cursor read and string compare; no scan of the full package  
**Constraints**: Streaming only — no revision list buffered in memory; no in-memory sort of `EnumerateAsync`; all persistence through `IArtefactStore` / `IStateStore`; no concrete store references in module code  
**Scale/Scope**: Packages up to 200,000 revision folders; both `file:///` and Azure Blob Storage (`https://*.blob.core.windows.net/...`) stores

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

**Mandatory context loaded**: `.agents/20-guardrails/core/architecture-boundaries.md`, `.agents/20-guardrails/core/coding-standards.md`, `.agents/20-guardrails/workflow/testing-rules.md`, `.agents/20-guardrails/domains/module-rules.md`, `.agents/30-context/domains/checkpointing-summary.md`, `.agents/30-context/domains/import-streaming.md`, `.agents/30-context/domains/job-lifecycle.md`, `.agents/30-context/domains/package-manager.md`, `docs/architecture.md`, `docs/module-development-guide.md` — all read in current session.

- [x] **Package-First (I):** `WorkItemImportOrchestrator` reads only from `IArtefactStore`; no source API calls during import. Export writes to `IArtefactStore` only. No direct source→target path exists or is introduced.
- [x] **Streaming (II):** `WorkItemImportOrchestrator` uses `IArtefactStore.EnumerateAsync` lazily with `await foreach` — one folder at a time. No `ToList()` or array materialisation of revision folders.
- [x] **WorkItems Layout (III):** Import reads the existing `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` layout without renaming, reordering, or flattening.
- [x] **Checkpointing (IV):** Import cursor written via `ICheckpointingService` to `Checkpoints/workitems.cursor.json`. Both-mode phase written to `Checkpoints/job.phase.json` via `IStateStore`. No watermark tables or in-memory progress counters.
- [x] **Module Isolation (V):** `WorkItemImportOrchestrator` injected with `IArtefactStore`, `ICheckpointingService`, `IWorkItemTargetService`. No `FileSystemArtefactStore` or `AzureBlobArtefactStore` reference in module or orchestrator code.
- [x] **Separation of Planes (VI):** Phase tracking lives in the Job Engine / MigrationAgentWorker layer. Control plane forwards `MigrationJob.Resume` unchanged — never inspects or acts on it. CLI encodes `--force-fresh` into `MigrationJob.Resume.Mode` before submission. This works identically across standalone, self-hosted, and cloud topologies because the control plane is topology-transparent.
- [x] **Determinism (VII):** Adding `MigrationJobResume` to `MigrationJob` is additive (new optional field, default null → treated as `Auto`). No breaking schema change; no upgrader needed.
- [x] **ATDD-First (VIII):** All four user stories have Given/When/Then scenarios in spec.md. Each scenario maps to one ATDD task and will be implemented via Specification → Test Gen → Implementation → Review.
- [x] **SOLID & DI (IX):** `WorkItemImportOrchestrator` receives all dependencies via constructor. `MigrationJobResume` is a sealed record with `init`-only properties. No raw `IConfiguration` reads. New service registrations in dedicated `Add*Services` extension. `IWorkItemTargetService` defined in Abstractions.

**Gate: PASS** — no violations. Proceed to Phase 0.

## Project Structure

### Documentation (this feature)

```
specs/009-resumable-export-import/
├── plan.md              ← this file
├── research.md          ← Phase 0 output ✓
├── data-model.md        ← Phase 1 output ✓
├── quickstart.md        ← Phase 1 output ✓
├── contracts/
│   └── cli-contracts.md ← Phase 1 output ✓
└── tasks.md             ← Phase 2 output (created; reconciled to repository truth on 2026-05-16)
```

### Source Code Layout (affected paths)

```
src/DevOpsMigrationPlatform.Abstractions/
├── Models/
│   ├── MigrationJob.cs                        ← add Resume property
│   └── MigrationJobResume.cs                  ← NEW: sealed record { ResumeMode Mode }
├── Checkpointing/
│   ├── CursorEntry.cs                         ← unchanged
│   ├── CursorStage.cs                         ← unchanged
│   └── JobPhaseRecord.cs                      ← NEW: { bool ExportCompleted, bool ImportCompleted }
└── Services/
    ├── ICheckpointingService.cs               ← add DeleteCursorAsync
    ├── IStateStore.cs                         ← add DeleteAsync
    └── IWorkItemTargetService.cs              ← NEW: target-side write operations

src/DevOpsMigrationPlatform.Infrastructure/
├── Checkpointing/
│   └── CheckpointingService.cs               ← implement DeleteCursorAsync
├── Import/                                   ← NEW folder
│   └── WorkItemImportOrchestrator.cs         ← NEW: staged import + cursor resume
├── Modules/
│   └── WorkItemsModule.cs                    ← implement ImportAsync using orchestrator
└── JobEngine/
    └── PhaseTrackingService.cs               ← NEW: read/write Checkpoints/job.phase.json

src/DevOpsMigrationPlatform.MigrationAgent/
└── MigrationAgentWorker.cs                   ← honour Resume.Mode; skip completed phases

src/DevOpsMigrationPlatform.CLI.Migration/
├── Settings/
│   ├── MigrationExportCommandSettings.cs     ← add --force-fresh flag
│   └── MigrationImportCommandSettings.cs     ← add --force-fresh flag
└── Commands/
    ├── MigrationExportCommand.cs             ← set job.Resume from settings
    └── MigrationImportCommand.cs             ← set job.Resume from settings

tests/DevOpsMigrationPlatform.Infrastructure.Tests/
├── Import/                                   ← NEW folder
│   ├── ImportWorkItemRevisionsContext.cs
│   ├── ImportWorkItemRevisionsSteps.cs
│   └── WorkItemImportOrchestratorTests.cs
└── JobEngine/
    ├── PhaseTrackingContext.cs
    └── PhaseTrackingSteps.cs

features/import/work-items/revisions/
└── import-work-item-revisions.feature        ← extend with resume scenarios

features/platform/checkpointing/
└── cursor-resume.feature                     ← extend with forced-fresh-start scenario

features/cli/execute/
└── resume-mode.feature                       ← NEW: --force-fresh observable output system test

.vscode/launch.json                           ← add --force-fresh profiles
```

## Complexity Tracking

No constitution violations requiring justification. All changes are additive or implement existing `NotImplementedException` placeholders. No new NuGet packages are needed.

---

## Phase 0: Research

*All unknowns resolved from codebase analysis — see [research.md](research.md) for full findings.*

### Decision Summary

| Decision | Rationale |
|---|---|
| Reuse `ICheckpointingService` + `CursorEntry` for import cursor | Already used by export; schema matches `.agents/30-context/domains/checkpointing-summary.md` exactly |
| `WorkItemExportOrchestrator` unchanged | Full cursor skip/write logic already implemented; only gap is `DeleteCursorAsync` |
| Per-stage cursor in import | Matches `CursorStage` enum; enables fine-grained resume without reprocessing completed stages |
| `Checkpoints/job.phase.json` for Both-mode | Fits `IStateStore` key pattern; agent reads before deciding which phases to run |
| `MigrationJobResume.Mode: Auto \| ForceFresh` on `MigrationJob` | Travels CLI → ControlPlane → Agent via the normal job contract; topology-transparent |
| `DeleteCursorAsync` on `ICheckpointingService` | Symmetric with Read/Write; clean abstraction for forced fresh-start |
| `IWorkItemTargetService` in Abstractions | Constitution IX — interfaces in Abstractions; ADO implementation in Infrastructure.AzureDevOps |
| `IStateStore.DeleteAsync` | Needed by `CheckpointingService.DeleteCursorAsync` to remove cursor files |
| `idmap.json` for attachment idempotency at Stage D | ADO REST does not expose SHA256 in attachment lists; local record is the only reliable check |

---

## Phase 1: Design

See [data-model.md](data-model.md) for full type definitions and state transitions.  
See [contracts/cli-contracts.md](contracts/cli-contracts.md) for CLI flags, wire format, and `launch.json` requirements.  
See [quickstart.md](quickstart.md) for operator usage guide.

### New Types

| Type | Assembly | Purpose |
|---|---|---|
| `MigrationJobResume` | Abstractions/Models | Carries `ResumeMode` for the job; null on `MigrationJob` = `Auto` |
| `ResumeMode` | Abstractions/Models | Enum: `Auto` = use cursor; `ForceFresh` = delete cursors and restart |
| `JobPhaseRecord` | Abstractions/Checkpointing | Serialised `Checkpoints/job.phase.json`; `ExportCompleted`, `ImportCompleted` |
| `IWorkItemTargetService` | Abstractions/Services | Target-side create/update operations for import stages |
| `WorkItemImportOrchestrator` | Infrastructure/Import | Staged import engine with cursor resume at stage level |
| `PhaseTrackingService` | Infrastructure/JobEngine | Reads/writes `Checkpoints/job.phase.json` via `IStateStore` |

### Modified Types

| Type | Change | Breaking? |
|---|---|---|
| `MigrationJob` | Add `Resume` property (`MigrationJobResume?`) | No — additive; null = `Auto` |
| `ICheckpointingService` | Add `DeleteCursorAsync` | No — interface extension |
| `IStateStore` | Add `DeleteAsync` | No — interface extension |
| `CheckpointingService` | Implement `DeleteCursorAsync` | N/A |
| `WorkItemsModule` | Implement `ImportAsync` | N/A — was `NotImplementedException` |
| `MigrationExportCommandSettings` | Add `--force-fresh` flag | No — new optional flag |
| `MigrationImportCommandSettings` | Add `--force-fresh` flag | No — new optional flag |

### Topology Confirmation

`--force-fresh` works identically in all three topologies:

| Topology | Control Plane location | How Resume travels |
|---|---|---|
| Standalone | In-process via Aspire (`http://localhost:5100`) | CLI → HTTP → in-process ControlPlaneHost → MigrationAgent |
| Self-hosted | Dedicated server or remote host | CLI → HTTP → ControlPlaneHost → MigrationAgent |
| Cloud | Azure Container Apps | CLI → HTTPS → ControlPlaneHost → MigrationAgent |

The control plane stores and forwards `MigrationJob.Resume` unchanged in all cases. The agent is the only component that acts on it.

### Feature Scenarios → ATDD Task Map

| Task | Scenario | Key New Code |
|---|---|---|
| T1 | `DeleteCursorAsync` service + unit test | `ICheckpointingService.DeleteCursorAsync`, `IStateStore.DeleteAsync`, `CheckpointingService` impl |
| T2 | Export forced fresh-start feature scenario + steps | `cursor-resume.feature` new scenario; `MigrationAgentWorker` forced fresh |
| T3 | `IWorkItemTargetService` interface + stub | Abstractions/Services; `AzureDevOpsWorkItemTargetService` stub in Infra.AzureDevOps |
| T4 | `WorkItemImportOrchestrator` — no cursor, imports all | Import folder, orchestrator class, stage A–D loop |
| T5 | Import cursor written after each folder completes | Orchestrator cursor write; feature scenario S2-E |
| T6 | Import skips folders ≤ cursor on resume | Orchestrator resume skip; feature scenario S2-A |
| T7 | Import resumes at stage level within partially processed folder | Orchestrator stage resume; feature scenario S2-B |
| T8 | Import Stage A idempotency via idmap | `idmap.json` read on Stage A; feature scenario S2-C |
| T9 | Import Stage D attachment idempotency via idmap | `idmap.json` read on Stage D; feature scenario S2-D |
| T10 | `WorkItemsModule.ImportAsync` wires orchestrator | Module implements `ImportAsync` |
| T11 | `MigrationJobResume` model + `MigrationJob.Resume` | New record; property on job |
| T12 | `PhaseTrackingService` + `JobPhaseRecord` | Service reads/writes `job.phase.json` |
| T13 | `MigrationAgentWorker` Both-mode phase skip | Worker reads phase record; skips completed phases; feature scenario S3-A |
| T14 | Both-mode forced fresh-start deletes `job.phase.json` | Worker ForceFresh path; feature scenario S3-B |
| T15 | `--force-fresh` CLI flag + `launch.json` entries | `MigrationExportCommandSettings`, `MigrationImportCommandSettings`, `MigrationMigrateCommandSettings`, command wiring |
| T16 | Doc discrepancy rectification | Update `.agents/30-context/domains/checkpointing-summary.md`, `.agents/30-context/domains/job-lifecycle.md` |

### Architecture Alignment Post-Design

All design decisions confirmed consistent with the constitution and guardrails:

- Import orchestrator lazy-streams `EnumerateAsync` ✓ (Principle II)
- No revision list materialised ✓ (Principle II)
- Cursor via `ICheckpointingService` ✓ (Principle IV)
- `IWorkItemTargetService` in Abstractions; impl in `Infrastructure.AzureDevOps` ✓ (Principles V, IX)
- `MigrationJob.Resume` additive — no upgrader needed ✓ (Principle VII)
- `PhaseTrackingService` writes `IStateStore` only ✓ (Principle V)
- Phase tracking in agent worker — not in control plane ✓ (Principle VI)
- `--force-fresh` encoded in `MigrationJob` before submission — topology-transparent ✓ (Principle VI)

Discrepancies from [discrepancies.md](discrepancies.md) are addressed in task T17.

