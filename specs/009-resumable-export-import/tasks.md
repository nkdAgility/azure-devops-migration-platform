# Tasks: Resumable Export and Import

**Feature**: `009-resumable-export-import`  
**Branch**: `009-resumable-export-import`  
**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/cli-contracts.md](contracts/cli-contracts.md)

---

## Summary

| Metric | Value |
|---|---|
| Total tasks | 34 |
| Phase 1 — Setup / Foundational | 4 |
| Phase 2 — US1: Resume interrupted export | 7 |
| Phase 3 — US2: Resume interrupted import | 14 |
| Phase 4 — US3: Both-mode phase resume | 7 |
| Phase 5 — Polish & cross-cutting | 2 |
| Parallel opportunities | T003, T004, T007, T009, T010, T014, T020, T021, T026, T027 |
| MVP scope | Phase 1 + Phase 2 (US1 export resume is already partially implemented — adds forced fresh-start) |

---

## Phase 1: Setup / Foundational

**Purpose**: Interface and model extensions that every user story depends on. Must be complete before any story phase begins.

- [X] T001 Add `DeleteAsync(string key, CancellationToken)` to `IStateStore` in `src/DevOpsMigrationPlatform.Abstractions/Storage/IStateStore.cs` — Status: complete
  - Evidence: `src/DevOpsMigrationPlatform.Abstractions.Storage/IStateStore.cs` defines `DeleteAsync`.
- [X] T002 Implement `DeleteAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Checkpointing/FileSystemStateStore.cs` and `src/DevOpsMigrationPlatform.Infrastructure/Storage/AzureBlobArtefactStore.cs` (or equivalent `IStateStore` implementations) — Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption/tasks.md package-boundary adoption
  - Supersession source+reason: package operations moved behind `IPackageAccess`; only `FileSystemStateStore` remains as direct `IStateStore` implementation.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem/FileSystemStateStore.cs` implements `DeleteAsync`; `src/DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem/ActivePackageAccess.cs` calls state-store delete through package boundary.
- [X] T003 [P] Add `DeleteCursorAsync(string moduleName, CancellationToken)` to `ICheckpointingService` in `src/DevOpsMigrationPlatform.Abstractions/Services/ICheckpointingService.cs` — Status: complete
  - Evidence: `src/DevOpsMigrationPlatform.Abstractions.Agent/Checkpointing/ICheckpointingService.cs` includes `DeleteCursorAsync`.
- [X] T004 [P] Implement `DeleteCursorAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Checkpointing/CheckpointingService.cs` — deletes `Checkpoints/<moduleName-lowercase>.cursor.json` via `IStateStore.DeleteAsync` — Status: complete
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Checkpointing/CheckpointingService.cs` implements `DeleteCursorAsync` and reset-meta routing.

**Checkpoint**: `IStateStore.DeleteAsync` and `ICheckpointingService.DeleteCursorAsync` exist and compile. All existing tests pass.

---

## Phase 2: User Story 1 — Resume an Interrupted Export (Priority: P1) 🎯 MVP

**Goal**: An operator who re-runs an interrupted export skips already-exported items and completes from the cursor position. A forced fresh-start deletes the cursor and re-runs all items.

**Independent Test**: Run export against 50 work items. Interrupt after ~25. Re-run with same config — verify only the remaining items are written. Then run with `--force-fresh` — verify all 50 are re-exported.

> Note: The core export resume (`WorkItemExportOrchestrator` cursor skip/write) is already implemented. This phase adds the forced fresh-start path and the `--force-fresh` CLI flag for export.

### Gherkin Feature File (US1)

- [ ] T005 [US1] Extend `features/platform/checkpointing/cursor-resume.feature` with a new scenario: "Forced fresh-start deletes the export cursor and re-processes all items from the beginning" — translating spec.md US1 Scenario 4 into conformant Gherkin — Status: incomplete
  - Evidence: `features/platform/checkpointing/cursor-resume.feature` has no force-fresh export scenario.

### Implementation (US1)

- [X] T006 [US1] Add `--force-fresh` flag to `src/DevOpsMigrationPlatform.CLI.Migration/Settings/MigrationExportCommandSettings.cs` — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md queue command model
  - Supersession source+reason: command surface is queue-centric; force-fresh is implemented once on queue settings.
  - Evidence: `src/DevOpsMigrationPlatform.CLI.Migration/Settings/QueueCommandSettings.cs` defines `--force-fresh`.
- [X] T007 [P] [US1] Add `MigrationJobResume` sealed record and `ResumeMode` enum to `src/DevOpsMigrationPlatform.Abstractions/Models/MigrationJobResume.cs` — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md job contract fold-in
  - Supersession source+reason: `MigrationJob*` contract was folded into `Job`; resume model exists as `JobResume`.
  - Evidence: `src/DevOpsMigrationPlatform.Abstractions/Jobs/JobResume.cs`.
- [X] T008 [US1] Add `Resume` property (`MigrationJobResume?`) to `MigrationJob` in `src/DevOpsMigrationPlatform.Abstractions/Models/MigrationJob.cs` — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md job contract fold-in
  - Supersession source+reason: property now lives on `Job` not `MigrationJob`.
  - Evidence: `src/DevOpsMigrationPlatform.Abstractions/Jobs/Job.cs` includes `JobResume? Resume`.
- [X] T009 [US1] Wire `--force-fresh` into `MigrationExportCommand` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs` — set `job.Resume = new MigrationJobResume { Mode = ResumeMode.ForceFresh }` when flag is present — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md queue command model
  - Supersession source+reason: queue command now submits all job kinds and carries force-fresh.
  - Evidence: `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs` sets `Resume = new JobResume { Mode = ResumeMode.ForceFresh }`.
- [X] T010 [US1] Handle `ResumeMode.ForceFresh` in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentWorker.cs` — call `ICheckpointingService.DeleteCursorAsync` for each registered module before running; do **not** delete `Checkpoints/idmap.json` (identity map is preserved so already-created target items are not duplicated) — Status: complete
  - Evidence: `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` force-fresh block deletes module cursors and phase/markers; `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs` logs cursor reset while preserving idmap.
- [X] T011 [US1] Add `.vscode/launch.json` entry: `"export: force-fresh (export-ado-workitems-single-project)"` — command `devopsmigration export --config scenarios/export-ado-workitems-single-project.json --force-fresh` — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md queue launch profile names
  - Supersession source+reason: equivalent profile exists with queue syntax.
  - Evidence: `.vscode/launch.json` contains `"📤 Migration CLI: Queue Export ADO WorkItems (Force Fresh)"`.

**Checkpoint**: Export force-fresh behavior is implemented in queue/job flow. `dotnet build` passes; targeted resume/checkpoint tests pass; full-suite + launch-profile verification remains open under T034.

---

## Phase 3: User Story 2 — Resume an Interrupted Import (Priority: P1)

**Goal**: An operator who re-runs an interrupted import skips fully-processed revision folders, resumes a partially-processed folder at the correct stage, and never duplicates work items or attachments in the target.

**Independent Test**: Run import from a 50-revision-folder package. Interrupt mid-way (simulate by stopping after Stage B on folder 25). Re-run — verify folders 1–24 skipped, folder 25 resumes at Stage C, folders 26–50 run normally with no duplicates.

### Gherkin Feature Files (US2)

- [X] T012 [US2] Extend `features/import/work-items/revisions/import-work-item-revisions.feature` with resume scenarios translating spec.md US2 Scenarios 1–4 into conformant Gherkin: — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T025
  - Scenario: Import resumes from cursor — skips folders at or before cursor position
  - Scenario: Import resumes at stage level — skips completed stages within a partially processed folder
  - Scenario: Stage A idempotency via idmap — skips work item creation if target ID already recorded
  - Scenario: Stage D idempotency via idmap — skips attachment upload if already recorded
  - Supersession source+reason: resume scenarios were split into dedicated feature files instead of overloading streaming replay feature.
  - Evidence: `features/platform/checkpointing/import-cursor-resume.feature`.

### Foundational: Target Service Interface

- [X] T013 [US2] Create `IWorkItemTargetService` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemTargetService.cs` with four methods: `CreateOrGetWorkItemAsync`, `ApplyFieldsAsync`, `ApplyLinksAsync`, `UploadAttachmentAsync` (see data-model.md for signatures) — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T011
  - Supersession source+reason: canonical import target seam is `IWorkItemImportTarget`.
  - Evidence: `src/DevOpsMigrationPlatform.Abstractions.Agent/Import/IWorkItemImportTarget.cs`.
- [X] T014 [P] [US2] Create `AzureDevOpsWorkItemTargetService` stub in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemTargetService.cs` — implement `IWorkItemTargetService`; all methods throw `NotImplementedException` (shape only; full ADO REST implementation is a follow-on task within this phase) — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T019
  - Supersession source+reason: stub approach replaced by concrete `AzureDevOpsWorkItemImportTarget`.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemImportTarget.cs`.

### Foundational: Import Orchestrator

- [ ] T015 [US2] Create `src/DevOpsMigrationPlatform.Infrastructure/Import/WorkItemImportOrchestrator.cs` — streaming import engine: — Status: incomplete
  - Constructor: `IArtefactStore`, `ICheckpointingService`, `IWorkItemTargetService`, `IProgressSink?`
  - `ImportAsync`: enumerate `WorkItems/` via `IArtefactStore.EnumerateAsync` lazily (`await foreach`)
  - For each revision folder: read cursor on first call; skip folders lexicographically ≤ `cursor.LastProcessed`
  - For the resume folder (path == `cursor.LastProcessed`): start from next stage after `cursor.Stage`
  - Execute stages A→B→C→D in order; write cursor after each stage completes
  - Write cursor with `stage: "Completed"` after Stage D
  - Do not buffer revision folders in memory; no in-memory sort
  - **Emit a structured log/`IProgressSink` event when a non-null cursor is detected at startup**: include module name, `cursor.LastProcessed` path, and estimated skip count (FR-011 / SC-006); this event fires on both Auto resume and ForceFresh (where cursor was just cleared)
  - **Start an OpenTelemetry `Activity` span** for the entire `ImportAsync` call (activity name: `"WorkItemImportOrchestrator.ImportAsync"`); record `exception.type` and `exception.message` as span attributes on failure; set span status to `Error` on throw (coding standards: each module execution MUST create an activity span)
  - **Error handling**: on any stage exception, let it propagate uncaught — do NOT swallow; the cursor will remain at the last successfully written stage so the run is safely resumable; the agent worker is responsible for catching module-level exceptions and recording the failure
  - **`ConfigureAwait(false)`** MUST be applied to every `await` expression inside this class (library/module code requirement per coding standards)
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs` exists, but no estimated skipped-count resume event is emitted.
- [X] T016 [US2] Unit tests for `WorkItemImportOrchestrator` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/WorkItemImportOrchestratorTests.cs` — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T036-T041
  - Supersession source+reason: tests were split by concern (cursor, filters, replay, memory safety).
  - Evidence: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/*` includes `ImportCursorResumeSteps.cs`, `WorkItemImportOrchestratorFilterTests.cs`, `StreamingMemorySafetySteps.cs`.

### Stage Implementations

- [X] T017 [US2] Implement Stage A (`CreatedOrUpdated`) in `WorkItemImportOrchestrator`: — Status: complete/superseded; completed because superseded by specs/019-workitem-idmap-sync/tasks.md id-map stage-a sync
  - Read `Checkpoints/idmap.json` via `IStateStore` at orchestrator startup (once per run)
  - If `idmap.workItems[sourceId]` exists → use existing target ID; skip creation
  - Else → call `IWorkItemTargetService.CreateOrGetWorkItemAsync`; write new mapping to idmap; persist idmap via `IStateStore`
  - Supersession source+reason: Stage A mapping uses SQLite-backed `IIdMapStore` and resolution strategy.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs` plus `specs/019-workitem-idmap-sync/spec.md`.
- [X] T018 [US2] Implement Stage B (`AppliedFields`) in `WorkItemImportOrchestrator`: — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md T008
  - Deserialise `revision.json` from `IArtefactStore.ReadAsync`
  - Call `IWorkItemTargetService.ApplyFieldsAsync` with field dictionary
  - Supersession source+reason: stage execution moved into `RevisionFolderProcessor`.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs`.
- [X] T019 [US2] Implement Stage C (`AppliedLinks`) in `WorkItemImportOrchestrator`: — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md T008
  - Read link list from `revision.json`
  - Call `IWorkItemTargetService.ApplyLinksAsync`; service is responsible for skipping existing links
  - Supersession source+reason: link application handled by revision processor + import target seam.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/RevisionFolderProcessor.cs`.
- [X] T020 [P] [US2] Implement Stage D (`UploadedAttachments`) in `WorkItemImportOrchestrator`: — Status: complete/superseded; completed because superseded by specs/019-workitem-idmap-sync/tasks.md attachment id-map sync
  - For each attachment in `revision.json`: check `idmap.attachments["workItemId:revisionIndex:relativePath"]`
  - If present → skip upload; reuse recorded target attachment ID
  - Else → read binary from `IArtefactStore`; call `IWorkItemTargetService.UploadAttachmentAsync`; write entry to idmap; persist idmap
  - Supersession source+reason: attachment idempotency uses `idmap.db` attachment mappings.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs` watermark/idmap calls.
- [X] T021 [P] [US2] Implement `AzureDevOpsWorkItemTargetService` Stage A in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemTargetService.cs` — real ADO REST `POST /_apis/wit/workitems/{type}` call — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T019
  - Supersession source+reason: target-side implementation delivered as `AzureDevOpsWorkItemImportTarget`.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemImportTarget.cs`.
- [X] T022 [US2] Implement `AzureDevOpsWorkItemTargetService` Stages B, C, D — real ADO REST `PATCH` (fields/links) and `POST` (attachment upload) calls — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T019
  - Supersession source+reason: stage operations implemented behind `IWorkItemImportTarget`.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemImportTarget.cs`.

### Step Definitions (US2)

- [X] T023 [US2] Create Reqnroll step definitions in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/ImportWorkItemRevisionsContext.cs` and `ImportWorkItemRevisionsSteps.cs` for the feature scenarios added in T012 — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T025
  - Supersession source+reason: resume steps live in dedicated `ImportCursorResume*` files.
  - Evidence: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Import/ImportCursorResumeContext.cs` and `ImportCursorResumeSteps.cs`.

### Wire-up

- [X] T024 [US2] Implement `WorkItemsModule.ImportAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` — construct `WorkItemImportOrchestrator` from `ImportContext.ArtefactStore`, `ImportContext.StateStore`, and injected `IWorkItemTargetService`; call `orchestrator.ImportAsync` — Status: complete
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs` implements `ImportAsync` and calls orchestrator.
- [X] T025 [US2] Register `AzureDevOpsWorkItemTargetService` as `IWorkItemTargetService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ExportServiceCollectionExtensions.cs` (or a new `ImportServiceCollectionExtensions.cs`) — Status: complete/superseded; completed because superseded by specs/013-ado-workitems-import/tasks.md T021
  - Supersession source+reason: registration moved to `ImportServiceCollectionExtensions` with keyed import target factory.
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ImportServiceCollectionExtensions.cs`.

**Checkpoint**: Import resume orchestration is implemented and covered by targeted tests. `dotnet build` passes; full-suite + scenario execution evidence remains open under T034.

---

## Phase 4: User Story 3 — Resume a Both-Mode Job (Priority: P2)

**Goal**: When a Both-mode job is re-run after the export phase completed, the export phase is skipped and the import phase resumes from its cursor. Forced fresh-start resets both phases.

**Independent Test**: Run `migrate` on a 20-item project. Interrupt during import. Re-run — verify export phase is skipped (no new revision folders written) and import resumes. Then run with `--force-fresh` — verify both phases re-run.

### Gherkin Feature Files (US3)

- [ ] T026 [P] [US3] Create `features/cli/execute/resume-mode.feature` with scenarios translating spec.md US3 Scenarios 1–3 into conformant Gherkin: — Status: incomplete
  - Scenario: Both-mode re-run skips completed export phase
  - Scenario: Both-mode forced fresh-start re-runs both phases
  - Scenario: Both-mode re-run where export was incomplete resumes export first
  - Evidence: `features/cli/execute/resume-mode.feature` does not contain both-mode-specific scenarios.

### Implementation (US3)

- [X] T027 [P] [US3] Create `JobPhaseRecord` sealed record in `src/DevOpsMigrationPlatform.Abstractions/Checkpointing/JobPhaseRecord.cs` — fields: `ExportCompleted`, `ImportCompleted`, `UpdatedAt` — Status: complete/superseded; completed because superseded by specs/033-runtime-state-categories/tasks.md phase-state expansion
  - Supersession source+reason: model delivered with additional `PrepareCompleted` phase flag.
  - Evidence: `src/DevOpsMigrationPlatform.Abstractions.Agent/Checkpointing/JobPhaseRecord.cs`.
- [X] T028 [US3] Create `PhaseTrackingService` in `src/DevOpsMigrationPlatform.Infrastructure/JobEngine/PhaseTrackingService.cs`: — Status: complete
  - `ReadPhaseRecordAsync`: read `Checkpoints/job.phase.json` via `IStateStore`; return default `JobPhaseRecord` if absent
  - `WritePhaseRecordAsync`: serialise `JobPhaseRecord` and write via `IStateStore`
  - `DeletePhaseRecordAsync`: call `IStateStore.DeleteAsync("Checkpoints/job.phase.json")`
  - **`ConfigureAwait(false)`** MUST be applied to every `await` expression inside this class (library/module code requirement per coding standards)
  - Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Checkpointing/PhaseTrackingService.cs`.
- [X] T029 [US3] Update `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentWorker.cs` to use `PhaseTrackingService`: — Status: complete
  - For `Mode == Both`: read phase record before running any module
  - If `ExportCompleted == true` → skip all modules tagged as Export phase
  - After export modules complete → write `ExportCompleted = true`
  - After import modules complete → write `ImportCompleted = true`
  - Evidence: `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` reads/writes phase record and computes runExport/runImport.
- [X] T030 [US3] Extend `MigrationAgentWorker` `ForceFresh` path: call `PhaseTrackingService.DeletePhaseRecordAsync` before resetting module cursors; do **not** delete `Checkpoints/idmap.json` (identity map preserved so already-created target items are not duplicated) — Status: complete
  - Evidence: `src/DevOpsMigrationPlatform.MigrationAgent/JobAgentWorker.cs` force-fresh branch deletes phase record and cursors; no idmap deletion path.
- [X] T031 [US3] Add `--force-fresh` to `src/DevOpsMigrationPlatform.CLI.Migration/Settings/MigrationImportCommandSettings.cs` and wire into `MigrationImportCommand` (mirrors T006/T009 for import); also add `--force-fresh` to `MigrationMigrateCommandSettings.cs` and wire into `MigrationMigrateCommand` — `migrate` is used as a sync command where re-running continues from where export and import left off, so `--force-fresh` resets both cursors — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md queue command model
  - Supersession source+reason: queue settings/command now own force-fresh for all modes.
  - Evidence: `src/DevOpsMigrationPlatform.CLI.Migration/Settings/QueueCommandSettings.cs` and `Commands/QueueCommand.cs`.
- [X] T032 [US3] Add `.vscode/launch.json` entries for `migrate: force-fresh` and `import: force-fresh` (see contracts/cli-contracts.md for exact profile names and commands) — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md queue-based launch profiles
  - Supersession source+reason: launch profiles use `queue` configs for Import/Migrate with force-fresh.
  - Evidence: `.vscode/launch.json` includes `"Queue Import (Force Fresh)"` and `"Queue Migrate (Force Fresh)"`.

**Checkpoint**: Both-mode phase tracking and force-fresh are implemented in current queue/job architecture. `dotnet build` passes; full-suite + launch-profile verification remains open under T034.

---

## Phase 5: Polish & Cross-Cutting

- [X] T033 Rectify documentation discrepancies logged in [discrepancies.md](discrepancies.md) and update canonical docs for new flags: — Status: complete
  - Add "Export Cursor Behaviour" subsection to `.agents/30-context/domains/checkpointing-summary.md`
  - Add `resume` block to the MigrationJob schema in `.agents/30-context/domains/job-lifecycle.md`
  - Add "Both-Mode Phase Tracking" section to `.agents/30-context/domains/checkpointing-summary.md`
  - Add `--force-fresh` to the `export`, `import`, and `migrate` command descriptions in `.agents/30-context/domains/cli-commands.md` (both the command table and a new "Resume Options" sub-section listing the flag, its default, and its semantics)
  - Add `--force-fresh` to the `export`, `import`, and `migrate` command descriptions in `docs/cli-guide.md` and add `--force-fresh` example invocations to the Examples section
  - Evidence: `.agents/30-context/domains/checkpointing-summary.md`, `.agents/30-context/domains/job-lifecycle.md`, `.agents/30-context/domains/cli-commands.md`, `docs/cli-guide.md`.
- [ ] T034 Run `dotnet clean && dotnet build --no-incremental` and `dotnet test` — confirm all pass; run `scenarios/export-ado-workitems-single-project.json` via `launch.json` profile and verify observable output shows resume behaviour — Status: incomplete
  - Evidence: this reconciliation run has successful `dotnet build` and targeted test coverage (`DevOpsMigrationPlatform.Infrastructure.Agent.Tests` filtered for `Checkpointing|Cursor|Resume`), but no completed full-suite `dotnet test` and no fresh launch-profile scenario evidence.

---

## Dependencies

```
T001 → T002 → T004
T001 → T003 → T004

T004 → T010 (ForceFresh path needs DeleteCursorAsync)
T007 → T008 → T009 → T010
T009 → T011

T013 → T014 → T015 → T016
T015 → T017 → T018 → T019 → T020
T015 → T023
T017 → T021 → T022
T020 → T022
T022 → T024 → T025

T027 → T028 → T029 → T030
T029 → T031 → T032
T008 → T029 (MigrationJob.Resume needed by worker)

T005, T006, T007, T009, T011 → Phase 2 complete
T012, T013..T025 → Phase 3 complete
T026..T032 → Phase 4 complete
T033 → Done
T034 → Incomplete
```

## Parallel Execution Opportunities

| Group | Tasks | Condition |
|---|---|---|
| Foundational interfaces | T003, T007 | Both modify different files; no shared dependency |
| Target service stub + model | T014, T027 | Different files; both unblock later tasks |
| Stage D + ADO Stage A | T020, T021 | Different files within same orchestrator phase |
| Feature files | T026, T005 | Different feature files; no shared dependency |

## Implementation Strategy

**MVP** = Phase 1 + Phase 2 (export forced fresh-start + `--force-fresh` CLI flag). This is immediately demonstrable: an interrupted export can be force-reset and re-run cleanly.

**Next increment** = Phase 3 (import resume). Core pain point for data integrity on large imports.

**Then** = Phase 4 (Both-mode) + Phase 5 (operator visibility). Lower risk, high operator confidence value.

**Finally** = Phase 5 (docs + final build/test gate).

## Incomplete Evidence Summary (Reconciled 2026-05-17)

- **T005**: `features/platform/checkpointing/cursor-resume.feature` has no explicit force-fresh export scenario.
- **T015**: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs` logs resume cursor but does not emit an estimated skipped-count resume event.
- **T026**: `features/cli/execute/resume-mode.feature` covers export/import resume but does not include Both-mode-specific scenarios from US3.
- **T034**: Full-suite `dotnet test` and launch-profile scenario proof were not completed during this reconciliation run (build passed; test command hangs and was stopped).

## Superseded Evidence Summary (Reconciled 2026-05-17)

- **Queue command migration**: T006/T009/T011/T031/T032 superseded by queue-centric job submission (`specs/025.1-fold-to-job/tasks.md`).
- **Import target seam + tests**: T012/T013/T014/T016/T021/T022/T023/T025 superseded by `specs/013-ado-workitems-import/tasks.md`.
- **Id-map and stage evolution**: T017/T020 superseded by `specs/019-workitem-idmap-sync/tasks.md`; T018/T019 by `specs/029-import-workitems-attachments-nodes/tasks.md`.
- **Phase state model evolution**: T027 superseded by `specs/033-runtime-state-categories/tasks.md`.
- **Package boundary adoption**: T002 superseded by `specs/034-package-manager-adoption/tasks.md`.


