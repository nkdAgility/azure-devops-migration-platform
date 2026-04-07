# Tasks: Work Items Export — Azure DevOps via REST API

**Input**: Design documents from `/specs/006-ado-workitems-export/`
**Prerequisites**: plan.md ✅ spec.md ✅ research.md ✅ data-model.md ✅ contracts/ ✅ quickstart.md ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing.

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (different files, no incomplete-task dependencies)
- **[Story]**: Owning user story (US1–US4) per `spec.md`
- All paths are repository-root-relative

---

## Phase 1: Setup

**Purpose**: Add the single new NuGet dependency before any user story work begins.

- [ ] T001 Add `Microsoft.Extensions.Resilience` package (latest stable) to `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.csproj` under `<ItemGroup Condition="'$(TargetFramework)' != 'net481'">`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Interface and model changes in `Abstractions` that every user story depends on. No user story work can begin until this phase is complete.

> **⚠️ IMPORTANT** — `AttachmentDownloadResult` is used by `TfsAttachmentDownloader` (net481). The redesign MUST be **additive** — keep `FilePath` as a nullable property, add `Sha256` and `Size`. Add a new `Succeeded(string sha256, long size)` factory overload; the existing `Succeeded(string filePath)` factory MUST remain. The TFS file will then be updated (T008) to avoid a namespace collision.

- [ ] T002 Add `Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)` to `src/DevOpsMigrationPlatform.Abstractions/Storage/IArtefactStore.cs`
- [ ] T003 [P] Implement `WriteStreamAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Storage/FileSystemArtefactStore.cs`: create parent directory then `await content.CopyToAsync` (net10) / `content.CopyTo` (net481, no-await)
- [ ] T004 [P] Add `[System.Text.Json.Serialization.JsonIgnore] public string? DownloadUrl { get; init; }` to `src/DevOpsMigrationPlatform.Abstractions/Models/AttachmentMetadata.cs`
- [ ] T005 [P] Extend `src/DevOpsMigrationPlatform.Abstractions/Models/AttachmentDownloadResult.cs`: add `public string? Sha256 { get; }` and `public long Size { get; }` fields; add `Succeeded(string sha256, long size)` factory; keep existing `Succeeded(string filePath)` factory unchanged
- [ ] T006 [P] Add `public int AttachmentsProcessed { get; init; }` and `public int AttachmentsFailed { get; init; }` to `src/DevOpsMigrationPlatform.Abstractions/Models/ProgressEvent.cs`
- [ ] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Services/IAttachmentDownloader.cs` with `Task<AttachmentDownloadResult> DownloadAsync(string downloadUrl, string destinationPath, IArtefactStore store, CancellationToken cancellationToken)`
- [ ] T008 Rename the local `IAttachmentDownloader` interface in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Services/TfsAttachmentDownloader.cs` to `ITfsAttachmentDownloader` (avoids namespace collision with the new Abstractions interface added in T007)

**Checkpoint**: Run `dotnet clean && dotnet build --no-incremental`. All projects MUST compile before proceeding to user story phases.

---

## Phase 3: User Story 1 — Export All Work Item Revisions (Priority: P1) 🎯 MVP

**Goal**: A complete end-to-end export of all work item revisions from an ADO project to the canonical `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/revision.json` layout, without attachments (attachments are added in US2).

**Independent Test**: Point the exporter at a known ADO project (or a mock source), run export, verify `PackageRoot/WorkItems/` contains the correct revision folders and `revision.json` files in canonical layout with correct JSON payload.

### Gherkin Scenarios for User Story 1

> **ATDD Phase 1 artifact**: existing `export-work-item-revisions.feature` already has TFS scenarios. Add `@azure-devops-rest` tagged Gherkin scenarios covering the US1 acceptance scenarios from `spec.md` §User Story 1. These must be written and reviewed before any new step definitions.

- [ ] T009 [US1] Extend `features/export/work-items/revisions/export-work-item-revisions.feature`: add `@azure-devops-rest` tagged scenarios for all four US1 acceptance scenarios from `spec.md` — canonical folder layout per ADO revision, changedDate-derived folder name, complete `revision.json` fields, and default WIQL scope

### Implementation for User Story 1

- [ ] T010 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemRevisionSource.cs`: implement `IWorkItemRevisionSource`; constructor receives `IWorkItemQueryWindowStrategy`, `IAzureDevOpsClientFactory`, `string organisationUrl`, `string project`, `string pat`, `string wiqlQuery`; `GetRevisionsAsync` calls `_windowStrategy.EnumerateWindowsAsync(...)` then per-work-item `witClient.GetRevisionsAsync(id, expand: WorkItemExpand.All)`, maps each SDK revision to `WorkItemRevision` (fields as `WorkItemField` by reference name, links by relation type, `AttachmentMetadata` including `DownloadUrl` from relation attributes), yields lazily; propagates `CancellationToken`
- [ ] T011 [P] [US1] Extend `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`: add overloaded constructor accepting `IProgressSink progressSink` and `IAttachmentDownloader? attachmentDownloader = null`; add `ExportAsync(IWorkItemRevisionSource source, bool includeAttachments, CancellationToken cancellationToken)` overload (existing signature delegates to it with `includeAttachments: false`); track `revisionsProcessed` and `workItemId` counters; emit `ProgressEvent` after each revision via `_progressSink`
- [ ] T012 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs`: implement `IDataTypeModule`; `Name = "WorkItems"`, `DependsOn = []`; `ExportAsync` reads `query` and `includeAttachments` from `context.Job.Modules` scope parameters, constructs `AzureDevOpsWorkItemRevisionSource` with credentials from `context.Job.Source`, and calls `WorkItemExportOrchestrator.ExportAsync(source, includeAttachments, ct)`; `ImportAsync` and `ValidateAsync` throw `NotImplementedException`
- [ ] T013 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/WorkItemExportServiceCollectionExtensions.cs`: `AddAzureDevOpsWorkItemExportServices` registers `IAzureDevOpsClientFactory` (singleton, idempotent), `IWorkItemQueryWindowStrategy` (singleton, idempotent), `WorkItemExportOrchestrator` (transient), `WorkItemsModule` (transient)
- [ ] T014 [P] [US1] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: add tests for the new overloaded constructor and `ExportAsync(source, includeAttachments: false, ct)` path — verify `IProgressSink.Emit` called once per revision with correct `WorkItemId` and `RevisionsProcessed` counters; verify `AttachmentsProcessed = 0` when `includeAttachments = false`
- [ ] T015 [US1] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/ExportWorkItemRevisionsContext.cs` and `ExportWorkItemRevisionsSteps.cs`: wire `Mock<IProgressSink>` into context; add step bindings for the new `@azure-devops-rest` Gherkin scenarios (T009) using the mock `IWorkItemRevisionSource` to stand in for the ADO source

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass. User Story 1 is independently testable at this point.

---

## Phase 4: User Story 2 — Attachments Downloaded Alongside Revision (Priority: P2)

**Goal**: Attachment binaries streamed beside `revision.json`; SHA-256 verified; delta-detected (only new attachments per revision downloaded); failed attachments skipped with error log and counter.

**Independent Test**: Run export against a project (or mock) where at least one revision has a new attachment. Confirm the binary exists at `WorkItems/yyyy-MM-dd/<ticks>-<id>-<rev>/<guid>-<filename>`, `revision.json` has correct `relativePath`, `sha256`, and `size`, and duplicate attachments across revisions are not re-downloaded.

### Gherkin Scenarios for User Story 2

- [ ] T016 [P] [US2] Extend `features/export/work-items/attachments/export-attachments.feature`: add `@azure-devops-rest` tagged scenarios for the four US2 acceptance scenarios — attachment stored in revision folder, `revision.json` attachment metadata shape, no binary written when no attachments, transient retry success

### Implementation for User Story 2

- [ ] T017 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/IAzureDevOpsAttachmentDownloader.cs`: marker interface `public interface IAzureDevOpsAttachmentDownloader : IAttachmentDownloader { }`
- [ ] T018 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsAttachmentDownloader.cs`: implement `IAzureDevOpsAttachmentDownloader`; constructor receives `IAzureDevOpsClientFactory` and `ResiliencePipeline` (resolved by name `"attachment-download"` via `IResiliencePipelineProvider<string>`); `DownloadAsync` parses attachment GUID from URL, executes within the resilience pipeline, calls `witClient.GetAttachmentContentAsync(guid, ct)`, wraps the response stream in a `CryptoStream` (SHA256, read-through), calls `store.WriteStreamAsync(destinationPath, cryptoStream, ct)`, flushes and reads final hash bytes, returns `AttachmentDownloadResult.Succeeded(sha256Hex, fileSize)`; on permanent 4xx (not 408/429) does not retry and returns `Failed(exception)`
- [ ] T019 [US2] Extend `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`: implement attachment delta detection in the export loop — track `previousRevisionAttachmentUrls` (`HashSet<string>`) per work item, reset on each new `workItemId`; for each revision compute new attachments (url not in previous set); download each via `_attachmentDownloader.DownloadAsync(url, folderPath + fileName, artefactStore, ct)`; on `AttachmentDownloadResult.Succeeded` update `AttachmentMetadata` with `Sha256`, `Size`, `RelativePath`; on failure log error and increment `attachmentsFailed` counter; emit updated `ProgressEvent` with `AttachmentsProcessed` and `AttachmentsFailed`
- [ ] T020 [US2] Update `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/WorkItemExportServiceCollectionExtensions.cs`: register `IAzureDevOpsAttachmentDownloader` as `AzureDevOpsAttachmentDownloader` (singleton); add `AddResiliencePipeline("attachment-download", ...)` with 8 retries, exponential back-off (2 s base + jitter), retry on transient HTTP errors (5xx, 408, 429)
- [ ] T021 [P] [US2] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: add unit tests — attachment downloaded on first revision; delta: carry-forward attachment NOT re-downloaded on second revision; failed download increments `AttachmentsFailed` and export continues; `includeAttachments = false` skips download path entirely
- [ ] T022 [US2] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/ExportWorkItemRevisionsContext.cs` and `ExportWorkItemRevisionsSteps.cs`: add `Mock<IAttachmentDownloader>` to context; add step bindings for the `@azure-devops-rest` attachment scenarios (T016)

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass. User Stories 1 AND 2 independently testable.

---

## Phase 5: User Story 3 — Resume After Interruption (Priority: P2)

**Goal**: An interrupted export resumes cleanly from the cursor without re-processing completed revisions or corrupting partial state. The cursor is advanced only after all stages (attachment writes + revision.json write) complete for a revision.

**Independent Test**: Mock a source of N revisions; run the orchestrator, assert cursor written after every revision; simulate resume by pre-loading a mid-run cursor; verify only post-cursor revisions are processed; verify re-run on a fully-completed cursor writes zero new files.

> **Implementation note**: The resume logic is already implemented in `WorkItemExportOrchestrator`. US3 tasks are verification and hardening of that logic under the extended orchestrator behavior from US1/US2.

### Gherkin Scenarios for User Story 3

- [ ] T023 [US3] Verify all three US3 acceptance scenarios from `spec.md` are covered by existing Gherkin in `features/export/work-items/revisions/export-work-item-revisions.feature`; add any missing scenarios (e.g. "cursor points to AppliedFields stage — replay from next incomplete stage" and "fully-completed export produces zero new files on re-run")

### Implementation for User Story 3

- [ ] T024 [US3] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: add tests confirming cursor is written AFTER both attachment writes and revision.json write (not between); confirm a revision with partial attachment download (first download succeeds, second fails) still writes the cursor as Completed (skip-and-continue semantics); confirm a fully-completed cursor causes zero writes

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass.

---

## Phase 6: User Story 4 — Progress Reported Through IProgressSink (Priority: P3)

**Goal**: Every completed revision emits a `ProgressEvent` with correct cumulative counters: `WorkItemId`, `RevisionsProcessed`, `TotalWorkItems`, `AttachmentsProcessed`, `AttachmentsFailed`, `Timestamp`.

**Independent Test**: Run the orchestrator with a mock `IProgressSink`; verify `Emit` is called once per revision; verify counter values are cumulative and correct; verify the final event `RevisionsProcessed` equals source revision count.

### Gherkin Scenarios for User Story 4

- [ ] T025 [P] [US4] Extend `features/export/work-items/revisions/export-work-item-revisions.feature`: add `@azure-devops-rest` scenarios for US4 — progress event emitted per revision with correct fields; final completion event with correct counters; attachment failure increments `attachmentsFailed` in event

### Implementation for User Story 4

- [ ] T026 [US4] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: add tests for progress event content — verify `IProgressSink.Emit` called with `WorkItemId`, `RevisionsProcessed`, and `AttachmentsProcessed` matching expected values after a sequence of revisions; verify `AttachmentsFailed` incremented when a download fails

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass. All four user stories independently testable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation updates per `discrepancies.md`, and final solution-wide verification gate.

- [ ] T027 [P] Update `docs/modules.md`: add `### WorkItemsModule — ADO Export` subsection under the Module Responsibilities section describing `AzureDevOpsWorkItemRevisionSource`, reuse of `WorkItemQueryWindowStrategy`, `IAzureDevOpsAttachmentDownloader`, and O(N) call pattern (resolves discrepancy 1)
- [ ] T028 [P] Update `docs/architecture.md`: add a note in the Migration Agent component row that "source connectors implement `IWorkItemRevisionSource`; `AzureDevOpsWorkItemRevisionSource` in `Infrastructure.AzureDevOps` is the first concrete implementation" (resolves discrepancy 2)
- [ ] T029 [P] Update `.agents/context/workitems-format.md`: add an "Attachment Download Contract" section specifying streaming download (`WriteStreamAsync`, no `MemoryStream` buffering), SHA-256 verification, and retry policy (resolves discrepancy 3)
- [ ] T030 Run `dotnet clean && dotnet build --no-incremental` — full solution MUST build with zero errors and zero warnings
- [ ] T031 Run `dotnet test` — ALL tests MUST pass; zero failures, zero skipped (except those explicitly marked with a known skip reason)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user story phases
  - Within Phase 2: T002 must complete before T003; T007 must complete before T008; T003–T007 can be parallel with each other
- **Phase 3 (US1)**: Depends on Phase 2 — T010 and T011 can be parallel; T012 depends on T010+T011; T013 depends on T012
- **Phase 4 (US2)**: Depends on Phase 3 — T017 and T018 can be parallel; T019 depends on T017+T018; T020 depends on T019
- **Phase 5 (US3)**: Depends on Phase 4 — verification phase only
- **Phase 6 (US4)**: Depends on Phase 3 (progress sink wired in US1 orchestrator extension)
- **Phase 7 (Polish)**: Depends on Phases 5+6 — T027–T029 can be parallel; T030 depends on all prior; T031 depends on T030

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no dependencies on US2/US3/US4
- **US2 (P2)**: Can start after Phase 3 (US1) — requires extended orchestrator
- **US3 (P2)**: Can start after Phase 4 (US2) — verifies behavior added in US1+US2
- **US4 (P3)**: Can start after Phase 3 (US1) — `IProgressSink` wired in US1; counters added in US2 needed for full coverage

---

## Parallel Opportunities

### Phase 2 (after T002 completes)

```
Parallel batch A — all independent file changes:
  T003  FileSystemArtefactStore.WriteStreamAsync
  T004  AttachmentMetadata.DownloadUrl
  T005  AttachmentDownloadResult.Sha256+Size overload
  T006  ProgressEvent.AttachmentsProcessed+AttachmentsFailed
  T007  IAttachmentDownloader interface

Sequential follow-up:
  T008  TfsAttachmentDownloader rename (depends on T007)
```

### Phase 3 (US1)

```
Parallel batch B:
  T010  AzureDevOpsWorkItemRevisionSource (new file)
  T011  WorkItemExportOrchestrator extend (different file)
  T014  WorkItemExportOrchestratorTests extend (different file)

Sequential follow-up:
  T012  WorkItemsModule (depends on T010+T011)
  T013  WorkItemExportServiceCollectionExtensions (depends on T012)
  T015  ExportWorkItemRevisionsContext/Steps extend (depends on T010)
```

### Phase 4 (US2)

```
Parallel batch C:
  T017  IAzureDevOpsAttachmentDownloader (new file)
  T018  AzureDevOpsAttachmentDownloader (new file)
  T021  OrchestratorTests extend (different file)

Sequential follow-up:
  T019  Orchestrator attach loop (depends on T017+T018)
  T020  DI registration update (depends on T019)
  T022  Context/Steps extend (depends on T017+T018)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (T001)
2. Phase 2: Foundational (T002–T008)
3. Phase 3: User Story 1 (T009–T015)
4. **STOP AND VALIDATE** — run export against a real ADO test project, inspect `WorkItems/` layout
5. Ship / demo US1 independently

### Incremental Delivery

- US1 → working revision export (no attachments) → validate against ADO test project
- US2 → add attachment streaming → validate attachment binaries + sha256
- US3 → verify resume → kill process mid-run, restart, confirm zero duplicates
- US4 → verify progress events → confirm TUI shows live counters

### Parallel Team Strategy

With two developers after Phase 2 is done:

- Developer A: US1 (T009–T015) → then US3 (T023–T024)
- Developer B: Gherkin updates (T016, T025) in parallel → then US2 (T017–T022) → then US4 (T025–T026)
- Both verify Polish phase (T027–T031) together

---

## Summary

| Phase | Tasks | Count | Notes |
|---|---|---|---|
| Phase 1: Setup | T001 | 1 | NuGet dependency |
| Phase 2: Foundational | T002–T008 | 7 | Abstractions interface/model changes |
| Phase 3: US1 (P1) | T009–T015 | 7 | Revision source + module + orchestrator |
| Phase 4: US2 (P2) | T016–T022 | 7 | Attachment downloader + delta detection |
| Phase 5: US3 (P2) | T023–T024 | 2 | Resume verification + hardening |
| Phase 6: US4 (P3) | T025–T026 | 2 | Progress event validation |
| Phase 7: Polish | T027–T031 | 5 | Docs + build/test gate |
| **Total** | **T001–T031** | **31** | |

**MVP scope**: Phases 1–3 (T001–T015) — 15 tasks for US1 end-to-end.  
**Parallel opportunities**: 3 parallel batches identified across Phases 2–4.
