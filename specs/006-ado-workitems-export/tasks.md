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

> **⚠️ IMPORTANT — Additive-only constraint**: `AttachmentDownloadResult` is used by `TfsAttachmentDownloader` (net481). The change MUST be **additive** — keep the existing `FilePath` nullable property and the existing `Succeeded(string filePath)` factory unchanged. Add new `Sha256`, `Size` fields and a new `Succeeded(string sha256, long size)` overload alongside the existing member. Do NOT remove or rename anything.

> **⚠️ IMPORTANT — No circular dependency**: `WorkItemsModule` lives in `Infrastructure`. `Infrastructure` references only `Abstractions` — it does NOT reference `Infrastructure.AzureDevOps`. To avoid a build-breaking circular dependency, `WorkItemsModule` MUST NOT construct `AzureDevOpsWorkItemRevisionSource` directly. Instead it injects `IWorkItemRevisionSourceFactory` (T008), whose implementation lives in `Infrastructure.AzureDevOps` (T012).

- [ ] T002 Add `Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)` to `src/DevOpsMigrationPlatform.Abstractions/Storage/IArtefactStore.cs`
- [ ] T003 [P] Implement `WriteStreamAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Storage/FileSystemArtefactStore.cs`: create parent directory then `await content.CopyToAsync(fileStream, ct)` (net10) / synchronous `content.CopyTo(fileStream)` wrapped in `Task.CompletedTask` (net481); propagate `CancellationToken`
- [ ] T004 [P] Add `[System.Text.Json.Serialization.JsonIgnore] public string? DownloadUrl { get; init; }` to `src/DevOpsMigrationPlatform.Abstractions/Models/AttachmentMetadata.cs`
- [ ] T005 [P] Extend `src/DevOpsMigrationPlatform.Abstractions/Models/AttachmentDownloadResult.cs` additively: add `public string? Sha256 { get; init; }` and `public long Size { get; init; }`; add factory overload `public static AttachmentDownloadResult Succeeded(string sha256, long size) => new(...)`; keep existing `Succeeded(string filePath)` factory and `FilePath` property unchanged
- [ ] T006 [P] Add `public int AttachmentsProcessed { get; init; }` and `public int AttachmentsFailed { get; init; }` to `src/DevOpsMigrationPlatform.Abstractions/Models/ProgressEvent.cs`
- [ ] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Services/IAttachmentDownloader.cs` with method `Task<AttachmentDownloadResult> DownloadAsync(string downloadUrl, string destinationPath, IArtefactStore store, CancellationToken cancellationToken)`
- [ ] T008 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemRevisionSourceFactory.cs` with method `IWorkItemRevisionSource Create(string organisationUrl, string project, string pat, string wiqlQuery)` — this factory lets `WorkItemsModule` (in `Infrastructure`) obtain an ADO revision source without a direct project reference to `Infrastructure.AzureDevOps`
- [ ] T009 Rename the local `IAttachmentDownloader` interface (if present) in `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Services/TfsAttachmentDownloader.cs` to `ITfsAttachmentDownloader` to avoid a namespace collision with the new Abstractions interface added in T007; update all usages within that file

**Checkpoint**: Run `dotnet clean && dotnet build --no-incremental`. All projects MUST compile before proceeding to user story phases.

---

## Phase 3: User Story 1 — Export All Work Item Revisions (Priority: P1) 🎯 MVP

**Goal**: A complete end-to-end export of all work item revisions from an ADO project to the canonical `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/revision.json` layout, without attachments (attachments are added in US2).

**Independent Test**: Point the exporter at a known ADO project (or a mock source), run export, verify `PackageRoot/WorkItems/` contains the correct revision folders and `revision.json` files in canonical layout with correct JSON payload.

### Gherkin Scenarios for User Story 1

> **ATDD Phase 1 artifact**: `export-work-item-revisions.feature` may already contain TFS scenarios. Add `@azure-devops-rest` tagged Gherkin scenarios covering the US1 acceptance scenarios from `spec.md` §User Story 1 before writing any new step definitions.

- [ ] T010 [US1] Extend `features/export/work-items/revisions/export-work-item-revisions.feature`: add `@azure-devops-rest` tagged scenarios for all four US1 acceptance scenarios — canonical folder layout per ADO revision, `changedDate`-derived folder name, complete `revision.json` field set (`workItemId`, `revisionIndex`, `changedDate`, `fields`, `relatedLinks`, `externalLinks`, `hyperlinks`, `attachments`), and default WIQL scope behavior

### Implementation for User Story 1

- [ ] T011 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemRevisionSource.cs`: implement `IWorkItemRevisionSource`; constructor receives `IWorkItemQueryWindowStrategy`, `IAzureDevOpsClientFactory`, `string organisationUrl`, `string project`, `string pat`, `string wiqlQuery`; `GetRevisionsAsync` calls `_windowStrategy.EnumerateWindowsAsync(organisationUrl, project, pat, ...)` then per-work-item `witClient.GetRevisionsAsync(id, expand: WorkItemExpand.All)`, maps each SDK revision to `WorkItemRevision` (fields by reference name key, links by relation type, `AttachmentMetadata` with `DownloadUrl` from `rel="AttachedFile"` relation `url` attribute), yields lazily; propagates `CancellationToken`; reads `context.Job.Source.Url` (not `orgOrCollection`) for the organisation URL
- [ ] T012 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemRevisionSourceFactory.cs`: implement `IWorkItemRevisionSourceFactory`; constructor receives `IWorkItemQueryWindowStrategy` and `IAzureDevOpsClientFactory`; `Create(organisationUrl, project, pat, wiqlQuery)` returns `new AzureDevOpsWorkItemRevisionSource(...)` — this is the sole construction point for `AzureDevOpsWorkItemRevisionSource`
- [ ] T013 [P] [US1] Extend `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`: add overloaded constructor accepting `IProgressSink progressSink` and `IAttachmentDownloader? attachmentDownloader = null`; add `ExportAsync(IWorkItemRevisionSource source, bool includeAttachments, CancellationToken cancellationToken)` overload (existing overload delegates with `includeAttachments: false`); track `revisionsProcessed` counter; after each revision call `_progressSink.Emit(new ProgressEvent { WorkItemId = ..., RevisionsProcessed = ..., AttachmentsProcessed = 0 })` — note: `Emit` is `void`, not async
- [ ] T014 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs`: implement `IDataTypeModule`; `Name = "WorkItems"`, `DependsOn = []`; constructor receives `IWorkItemRevisionSourceFactory`, `WorkItemExportOrchestrator`, `ILogger<WorkItemsModule>`; `ExportAsync` reads `query` (default: `SELECT [System.Id] FROM WorkItems WHERE [System.TeamProject] = @project ORDER BY [System.Id]`) and `includeAttachments` (default: `true`) from `context.Job.Modules["WorkItems"].Scopes[0].Parameters`, calls `_sourceFactory.Create(context.Job.Source.Url, context.Job.Source.Project, context.Job.Source.Authentication.ResolvedAccessToken, query)`, then `_orchestrator.ExportAsync(source, includeAttachments, ct)`; `ImportAsync` and `ValidateAsync` throw `NotImplementedException("Deferred")`
- [ ] T015 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/WorkItemExportServiceCollectionExtensions.cs`: `AddAzureDevOpsWorkItemExportServices` registers `IAzureDevOpsClientFactory` (singleton, idempotent), `IWorkItemQueryWindowStrategy` (singleton, idempotent), `IWorkItemRevisionSourceFactory` as `AzureDevOpsWorkItemRevisionSourceFactory` (singleton), `WorkItemExportOrchestrator` (transient), `WorkItemsModule` (transient)
- [ ] T016 [P] [US1] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: add tests for the new overloaded constructor and `ExportAsync(source, includeAttachments: false, ct)` — verify `IProgressSink.Emit` called once per revision with correct `WorkItemId` and cumulative `RevisionsProcessed`; verify `AttachmentsProcessed = 0` when `includeAttachments = false`
- [ ] T017 [US1] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/ExportWorkItemRevisionsContext.cs` and `ExportWorkItemRevisionsSteps.cs`: wire `Mock<IProgressSink>` into context; add step bindings for the `@azure-devops-rest` scenarios added in T010 using `Mock<IWorkItemRevisionSource>` to stand in for the ADO source
- [ ] T018 [US1] Verify that the Reqnroll `.feature` files (`export-work-item-revisions.feature`, `export-attachments.feature`) are included in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/DevOpsMigrationPlatform.Infrastructure.Tests.csproj` test discovery; add explicit `<None Include="..." CopyToOutputDirectory="Always" />` entries if not automatically picked up

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass. User Story 1 is independently testable at this point.

---

## Phase 4: User Story 2 — Attachments Downloaded Alongside Revision (Priority: P2)

**Goal**: Attachment binaries streamed beside `revision.json`; SHA-256 verified; delta-detected (only new attachments per revision downloaded); failed attachments skipped with error log and counter.

**Independent Test**: Run export against a project (or mock) where at least one revision has a new attachment. Confirm the binary exists at `WorkItems/yyyy-MM-dd/<ticks>-<id>-<rev>/<guid>-<filename>`, `revision.json` carries correct `relativePath`, `sha256`, and `size`, and duplicate attachments across revisions are NOT re-downloaded.

### Gherkin Scenarios for User Story 2

- [ ] T019 [P] [US2] Extend `features/export/work-items/attachments/export-attachments.feature`: add `@azure-devops-rest` tagged scenarios for the four US2 acceptance scenarios — attachment binary stored in revision folder, `revision.json` attachment metadata shape (`originalName`, `relativePath`, `sha256`, `size`), no binary written when revision has no attachments, transient download failure triggers retry and eventually succeeds

### Implementation for User Story 2

- [ ] T020 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/IAzureDevOpsAttachmentDownloader.cs`: `public interface IAzureDevOpsAttachmentDownloader : IAttachmentDownloader { }` — marker interface enabling ADO-specific DI registration and typed mocking
- [ ] T021 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsAttachmentDownloader.cs`: implement `IAzureDevOpsAttachmentDownloader`; constructor receives `IAzureDevOpsClientFactory` and `IResiliencePipelineProvider<string>`; `DownloadAsync` parses attachment GUID from URL, wraps execution in pipeline `"attachment-download"`, calls `witClient.GetAttachmentContentAsync(guid, ct)`, wraps response stream in `new CryptoStream(responseStream, SHA256.Create(), CryptoStreamMode.Read)`, calls `store.WriteStreamAsync(destinationPath, cryptoStream, ct)`, flushes `CryptoStream`, reads hash bytes → lowercase hex string, returns `AttachmentDownloadResult.Succeeded(sha256Hex, streamLength)`; on permanent 4xx (not 408 or 429) returns `AttachmentDownloadResult.Failed(exception)` without entering the retry pipeline
- [ ] T022 [US2] Extend `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs`: add `_previousRevisionAttachmentUrls` (`HashSet<string>`) field, reset on each new `workItemId`; for each revision where `includeAttachments = true`, compute new attachments (URLs not in previous set); for each new attachment call `_attachmentDownloader!.DownloadAsync(url, Path.Combine(folderPath, $"{attachmentId}-{originalName}"), artefactStore, ct)`; on success update `AttachmentMetadata` (`Sha256`, `Size`, `RelativePath`); on failure log at `Error` level and increment `attachmentsFailed`; update `_previousRevisionAttachmentUrls`; include `AttachmentsProcessed` and `AttachmentsFailed` in each `_progressSink.Emit(...)` call
- [ ] T023 [US2] Update `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/WorkItemExportServiceCollectionExtensions.cs`: register `IAzureDevOpsAttachmentDownloader` as `AzureDevOpsAttachmentDownloader` (singleton); add `services.AddResiliencePipeline("attachment-download", builder => builder.AddRetry(new RetryStrategyOptions { MaxRetryAttempts = 8, BackoffType = DelayBackoffType.Exponential, Delay = TimeSpan.FromSeconds(2), UseJitter = true, ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>(e => IsTransientStatus(e.StatusCode)) }))`
- [ ] T024 [P] [US2] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: using `Mock<IAzureDevOpsAttachmentDownloader>` (NOT `Mock<IAttachmentDownloader>`) — test: attachment downloaded on first revision; delta: same-URL attachment on second revision NOT re-downloaded (zero additional `DownloadAsync` calls); failed download increments `AttachmentsFailed` and export continues; `includeAttachments = false` path calls no `DownloadAsync` at all
- [ ] T025 [US2] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/ExportWorkItemRevisionsContext.cs` and `ExportWorkItemRevisionsSteps.cs`: add `Mock<IAzureDevOpsAttachmentDownloader>` to context; wire it into the orchestrator constructor; add step bindings for the `@azure-devops-rest` attachment scenarios added in T019

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass. User Stories 1 AND 2 independently testable.

---

## Phase 5: User Story 3 — Resume After Interruption (Priority: P2)

**Goal**: An interrupted export resumes cleanly from the cursor without re-processing completed revisions. The cursor is written only after all stages for a revision (attachment writes + `revision.json` write) complete. A revision whose cursor is not yet `Completed` is replayed in full on resume.

**Independent Test**: Mock a source of N revisions; run the orchestrator; verify cursor written after every revision; pre-load a mid-run cursor with `stage = "InProgress"` (not `"Completed"`); verify that partial revision is replayed fully (re-downloads attachments and re-writes `revision.json`); verify a fully-`Completed` cursor causes zero new writes.

> **Implementation note**: Core resume logic exists in `WorkItemExportOrchestrator`. US3 tasks harden and verify that behavior under the extended orchestrator from US1/US2.

### Gherkin Scenarios for User Story 3

- [ ] T026 [US3] Verify `features/export/work-items/revisions/export-work-item-revisions.feature` covers all three US3 acceptance scenarios; the three required scenarios are: (1) export reads `Checkpoints/workitems.cursor.json` on start and skips all revision folders where `stage = "Completed"` at or before the cursor position; (2) a revision folder whose cursor shows `stage` is NOT `"Completed"` causes the orchestrator to fully re-process that revision (re-download attachments, re-write `revision.json`) before advancing — do NOT reference import-phase labels (`AppliedFields`, `AppliedLinks`) in these scenarios; export uses only `stage = "Completed"` or `stage = "InProgress"`; (3) a fully-completed run re-executed exits with zero new files written

### Implementation for User Story 3

- [ ] T027 [US3] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: add tests: (a) cursor is written AFTER both attachment write(s) and `revision.json` write complete; (b) a pre-loaded `stage = "InProgress"` cursor for a revision causes that revision to be fully re-processed; (c) a pre-loaded fully-`Completed` cursor causes zero `IArtefactStore` write calls
- [ ] T028 [P] [US3] Add SC-004 count reconciliation assertion in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: after a complete mock export run, assert that the total `revision.json` write count equals the total revision count yielded by the mock `IWorkItemRevisionSource`

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass.

---

## Phase 6: User Story 4 — Progress Reported Through IProgressSink (Priority: P3)

**Goal**: Every completed revision emits a `ProgressEvent` via `IProgressSink.Emit(...)` with correct cumulative counters: `WorkItemId`, `RevisionsProcessed`, `TotalWorkItems`, `AttachmentsProcessed`, `AttachmentsFailed`, and `Timestamp`.

**Independent Test**: Run the orchestrator with a mock `IProgressSink`; capture all `Emit` calls; verify one call per revision; verify counter values are cumulative; verify the final `RevisionsProcessed` equals the total revision count from the mock source.

### Gherkin Scenarios for User Story 4

- [ ] T029 [P] [US4] Extend `features/export/work-items/revisions/export-work-item-revisions.feature`: add `@azure-devops-rest` tagged scenarios for US4 — (1) a `ProgressEvent` is emitted after each revision folder completes with correct `workItemId`, `revisionsProcessed`, `totalWorkItems`, and `attachmentsProcessed`; (2) an attachment download failure increments `attachmentsFailed` in the emitted event; (3) the final event after export completes carries correct total counters

### Implementation for User Story 4

- [ ] T030 [US4] Extend `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemExportOrchestratorTests.cs`: verify `IProgressSink.Emit` called exactly once per completed revision; verify `RevisionsProcessed` increments by 1 per call; verify `AttachmentsProcessed` matches downloaded attachment count; verify `AttachmentsFailed` increments when a download returns `Failed`; verify `WorkItemId` matches the revision's work item ID in every event
- [ ] T031 [P] [US4] Add SC-003 schema validation check in the acceptance test context (`ExportWorkItemRevisionsContext.cs`): after each `revision.json` is written via the mock `IArtefactStore`, capture the written JSON, deserialise it into `WorkItemRevision`, and assert all required fields are present and `Attachments` is always a non-null array

**Checkpoint**: `dotnet clean && dotnet build --no-incremental` and `dotnet test` MUST both pass. All four user stories independently testable.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: SC-001 design documentation, architecture docs, and the mandatory solution-wide build/test gate.

- [ ] T032 [P] Add XML doc-comment to `WorkItemsModule.ExportAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` documenting the SC-001 design guarantee: processes one `WorkItemRevision` at a time via `IAsyncEnumerable`; streams attachment binaries directly to `IArtefactStore.WriteStreamAsync`; no revision list or attachment byte array is accumulated in memory; a 10,000 work-item project stays within ≤512 MB working-set by design
- [ ] T033 [P] Update `docs/modules.md`: add `### WorkItemsModule — ADO Export` subsection describing `IWorkItemRevisionSourceFactory`, `AzureDevOpsWorkItemRevisionSource`, reuse of `WorkItemQueryWindowStrategy`, `IAzureDevOpsAttachmentDownloader`, and the O(N) per-work-item call pattern
- [ ] T034 [P] Update `docs/architecture.md`: note in the Migration Agent component row that source connectors implement `IWorkItemRevisionSource`; `AzureDevOpsWorkItemRevisionSource` (constructed via `IWorkItemRevisionSourceFactory`) in `Infrastructure.AzureDevOps` is the first concrete implementation
- [ ] T035 [P] Update `.agents/context/workitems-format.md`: add "Attachment Download Contract" section specifying streaming download (`WriteStreamAsync`, no `MemoryStream`), SHA-256 in-flight computation via `CryptoStream`, retry policy (8 retries, exponential back-off, transient 5xx/408/429), delta detection (adjacent-revision URL comparison)
- [ ] T036 Run `dotnet clean && dotnet build --no-incremental` — full solution MUST build with zero errors and zero warnings
- [ ] T037 Run `dotnet test` — ALL tests MUST pass; zero failures, zero skipped (except those explicitly marked with a documented skip reason)

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Depends on Phase 1 — BLOCKS all user story phases
  - Within Phase 2: T002 must complete before T003; T007 must complete before T009; T003–T008 can run in parallel
- **Phase 3 (US1)**: Depends on Phase 2 — T011, T012, T013, T016 can be parallel; T014 depends on T011+T012+T013; T015 depends on T014; T017 depends on T011+T016; T018 depends on T010
- **Phase 4 (US2)**: Depends on Phase 3 — T020, T021, T024 can be parallel; T022 depends on T020+T021; T023 depends on T022; T025 depends on T020+T021
- **Phase 5 (US3)**: Depends on Phase 4 — verification and hardening only
- **Phase 6 (US4)**: Depends on Phase 3 — `IProgressSink.Emit` wired in US1; US2 counters needed for full coverage
- **Phase 7 (Polish)**: Depends on Phases 5+6 — T032–T035 can be parallel; T036 depends on all prior; T037 depends on T036

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no dependencies on US2/US3/US4
- **US2 (P2)**: Can start after Phase 3 (US1) — requires extended orchestrator
- **US3 (P2)**: Can start after Phase 4 (US2) — verifies behavior added in US1+US2
- **US4 (P3)**: Can start after Phase 3 (US1) — `Emit` wired in US1; US2 counters needed for attachment failure test

---

## Parallel Opportunities

### Phase 2 (after T002 completes)

```
Parallel batch A — all independent file changes:
  T003  FileSystemArtefactStore.WriteStreamAsync
  T004  AttachmentMetadata.DownloadUrl
  T005  AttachmentDownloadResult additive extend
  T006  ProgressEvent new fields
  T007  IAttachmentDownloader interface
  T008  IWorkItemRevisionSourceFactory interface  ← fixes circular dependency

Sequential follow-up:
  T009  TfsAttachmentDownloader rename (depends on T007)
```

### Phase 3 (US1)

```
Parallel batch B:
  T011  AzureDevOpsWorkItemRevisionSource (new file)
  T012  AzureDevOpsWorkItemRevisionSourceFactory (new file)
  T013  WorkItemExportOrchestrator extend (different file)
  T016  WorkItemExportOrchestratorTests extend (different file)

Sequential follow-up:
  T014  WorkItemsModule (depends on T011+T012+T013)
  T015  WorkItemExportServiceCollectionExtensions (depends on T014)
  T017  ExportWorkItemRevisionsContext/Steps extend (depends on T011+T016)
  T018  Infrastructure.Tests.csproj feature file links (depends on T010)
```

### Phase 4 (US2)

```
Parallel batch C:
  T020  IAzureDevOpsAttachmentDownloader (new file)
  T021  AzureDevOpsAttachmentDownloader (new file)
  T024  OrchestratorTests extend — attachment tests (different file)

Sequential follow-up:
  T022  Orchestrator attachment delta loop (depends on T020+T021)
  T023  DI registration update (depends on T022)
  T025  Context/Steps extend (depends on T020+T021)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1: Setup (T001)
2. Phase 2: Foundational (T002–T009)
3. Phase 3: User Story 1 (T010–T018)
4. **STOP AND VALIDATE** — run export against a real ADO test project; inspect `WorkItems/` layout
5. Ship / demo US1 independently

### Incremental Delivery

- US1 → working revision export (no attachments) → validate against ADO test project
- US2 → add attachment streaming → validate attachment binaries + sha256
- US3 → harden resume → kill process mid-run, restart, confirm zero duplicates
- US4 → verify progress events → confirm TUI shows live counters

### Parallel Team Strategy

With two developers after Phase 2 is done:

- Developer A: US1 (T010–T018) → then US3 (T026–T028)
- Developer B: Gherkin updates (T019, T029) in parallel → then US2 (T020–T025) → then US4 (T030–T031)
- Both verify Polish phase (T032–T037) together

---

## Summary

| Phase | Tasks | Count | Notes |
|---|---|---|---|
| Phase 1: Setup | T001 | 1 | NuGet dependency |
| Phase 2: Foundational | T002–T009 | 8 | Abstractions changes + `IWorkItemRevisionSourceFactory` (circular dep fix) |
| Phase 3: US1 (P1) | T010–T018 | 9 | Revision source + factory + module + orchestrator |
| Phase 4: US2 (P2) | T019–T025 | 7 | Attachment downloader + delta detection |
| Phase 5: US3 (P2) | T026–T028 | 3 | Resume hardening + SC-004 reconciliation |
| Phase 6: US4 (P3) | T029–T031 | 3 | Progress events + SC-003 schema validation |
| Phase 7: Polish | T032–T037 | 6 | SC-001 doc + arch docs + build/test gate |
| **Total** | **T001–T037** | **37** | |

**MVP scope**: Phases 1–3 (T001–T018) — 18 tasks for US1 end-to-end.
**Parallel opportunities**: 3 parallel batches across Phases 2–4.
**Key changes vs prior version**: added `IWorkItemRevisionSourceFactory` (T008/T012) to prevent circular dependency; corrected `Emit` method (not `ReportAsync`); corrected mock type to `IAzureDevOpsAttachmentDownloader` in T024/T025; added SC-001 doc task (T032); added SC-003 schema task (T031); added SC-004 count task (T028); sharpened US3 scenario description (T026) — removed import-phase stage labels; added `.csproj` feature file task (T018); uses `Source.Url` throughout (not stale `orgOrCollection`).
