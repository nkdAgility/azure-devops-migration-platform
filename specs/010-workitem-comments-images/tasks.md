# Tasks: Work Item Comments and Embedded Images Export

**Feature**: `010-workitem-comments-images`  
**Input**: Design documents from `specs/010-workitem-comments-images/`  
**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/ ✅ | quickstart.md ✅

## Format: `[ID] [P?] [Story?] Description`

- **[P]**: Can run in parallel (no file-level conflict, no incomplete dependency)
- **[US1]**: User Story 1 — Export Work Item Comments (P1)
- **[US2]**: User Story 2 — Download Embedded Images from HTML Fields (P2)
- **[US3]**: User Story 3 — Download Embedded Images from Markdown Fields and Comments (P3)

---

## Phase 1: Setup — NuGet Dependencies

**Purpose**: Add required third-party packages before any new code is written. These block all subsequent implementation.

- [X] T001 Add `HtmlAgilityPack` NuGet package to `src/DevOpsMigrationPlatform.Infrastructure/DevOpsMigrationPlatform.Infrastructure.csproj` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: moved to Agent-layer project layout; `HtmlAgilityPack` is present in `src/DevOpsMigrationPlatform.Infrastructure.Agent/DevOpsMigrationPlatform.Infrastructure.Agent.csproj`.
- [X] T002 [P] Add `Polly` and `Microsoft.Extensions.Http.Polly` NuGet packages to `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.csproj` — Status: complete

**Checkpoint**: Run `dotnet build` — project must compile before proceeding.

---

## Phase 2: Foundational — Abstractions and Shared Config

**Purpose**: Define all interfaces and records that every user story depends on. No US can begin until this phase is complete.

**⚠️ CRITICAL**: Implement these in order T003→T009; T003 must exist before T004 references it at compile time.

- [X] T003 Create `WorkItemIdentityRef` record in `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemIdentityRef.cs` — fields: `DisplayName`, `UniqueName`, `Descriptor` (all `string`, `init`-only) — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: type exists under Agent abstractions at `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/WorkItemIdentityRef.cs`.
- [X] T004 [P] Create `WorkItemComment` record in `src/DevOpsMigrationPlatform.Abstractions/Models/WorkItemComment.cs` — fields per `contracts/WorkItemComment.cs`: `CommentId`, `Version`, `Text`, `RenderedText?`, `Format`, `IsDeleted`, `CreatedBy` (WorkItemIdentityRef), `CreatedDate`, `ModifiedBy`, `ModifiedDate` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: implemented at `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/WorkItemComment.cs` with evolved schema.
- [X] T005 [P] Create `IWorkItemCommentSource` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemCommentSource.cs` — single method: `IAsyncEnumerable<WorkItemComment> GetCommentsAsync(int workItemId, bool includeDeleted, CancellationToken ct)` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: interface exists at `src/DevOpsMigrationPlatform.Abstractions.Agent/Export/IWorkItemCommentSource.cs`.
- [X] T006 [P] Create `IWorkItemCommentExportService` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemCommentExportService.cs` — single method: `Task ExportAsync(int workItemId, CancellationToken ct)` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: later design uses inline orchestrator handling instead of a separate export-service seam (`WorkItemExportOrchestrator.cs`).
- [X] T007 [P] Create `EmbeddedImageDownloadResult` record and `IEmbeddedImageDownloader` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IEmbeddedImageDownloader.cs` — `TryDownloadAsync(string imageUrl, CancellationToken ct) → Task<EmbeddedImageDownloadResult?>`; result has `byte[] Bytes` and `string Extension` — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md
  - Superseded evidence: contract moved to `Abstractions.Agent/Attachments`.
- [X] T008 [P] Create `IEmbeddedImageExportService` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IEmbeddedImageExportService.cs` — two methods: `Task<string> ProcessHtmlAsync(string html, string folderPath, CancellationToken ct)` and `Task<string> ProcessMarkdownAsync(string markdown, string folderPath, CancellationToken ct)` — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md
  - Superseded evidence: interface exists at `src/DevOpsMigrationPlatform.Abstractions.Agent/Attachments/IEmbeddedImageExportService.cs`.
- [X] T009 Create `CommentsScope` sealed options class in `src/DevOpsMigrationPlatform.Infrastructure/Modules/CommentsScope.cs` — properties: `Enabled` (bool, default `true`), `IncludeDeleted` (bool, default `false`), `SectionName = "Comments"` — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md
  - Superseded evidence: options now use extension config types (`CommentsExtensionOptionsConfig`) and parsed extensions in `WorkItemsModuleExtensions.cs`.
- [X] T009b [P] Create `EmbeddedImagesScope` sealed options class in `src/DevOpsMigrationPlatform.Infrastructure/Modules/EmbeddedImagesScope.cs` — properties: `Enabled` (bool, default `true`), `DownloadTimeoutSeconds` (int, default `30`), `SectionName = "EmbeddedImages"` — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md
  - Superseded evidence: replaced by `EmbeddedImagesExtensionOptionsConfig` under extension-based options parsing.
- [X] T009c [P] Add `CommentsScope Comments { get; init; }` and `EmbeddedImagesScope EmbeddedImages { get; init; }` nested properties to `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsScopeParameters.cs`; wire options binding via `services.Configure<WorkItemsScopeParameters>(...).Configure<CommentsScope>(...).Configure<EmbeddedImagesScope>(...)` — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md
  - Superseded evidence: extension model now parsed via `WorkItemsModuleExtensions.ParseCommentsExtension/ParseEmbeddedImagesExtension`.

**Checkpoint**: Run `dotnet build` — all 4 interfaces and 2 records and 2 scope classes must compile clean before Phase 3 begins.

---

## Phase 3: User Story 1 — Export Work Item Comments (P1) 🎯 MVP

**Goal**: Every comment on every exported work item is written as `comment.json` in a `<ticks>-<workItemId>-c<commentId>/` sub-folder inside the date folder matching the comment's `createdDate`. Each comment edit is a separate folder. Export is resumable via a cursor.

**Independent Test**: Run export on a project containing work item #12345 with 3 comments. Verify 3 sub-folders named `*-12345-c<N>/` exist inside `WorkItems/yyyy-MM-dd/` and each contains a valid `comment.json`.

### Feature File (ATDD Phase 1 — write before any step definitions or implementation)

- [X] T010 [US1] Create `features/export/work-items/comments/export-comments.feature` — translate all 4 P1 acceptance scenarios from `spec.md` User Story 1 into Reqnroll Gherkin per `.agents/20-guardrails/workflow/acceptance-test-format.md`; scenarios: (1) 3 comments → 3 folders, (2) 0 comments → 0 folders, (3) >1 page → all folders, (4) resume cursor skips completed work items — Status: complete

### Implementation for User Story 1

- [X] T011 [US1] Implement `AzureDevOpsWorkItemCommentSource` (single-page Comments API call only) in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemCommentSource.cs` — calls `WorkItemTrackingHttpClient.GetCommentsAsync` with v7.1-preview.4 API version; maps `WorkItemComment2` → `WorkItemComment` record; constructor-injected `WorkItemTrackingHttpClient` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: implemented in moved path `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Export/AzureDevOpsWorkItemCommentSource.cs`.
- [X] T012 [US1] Implement `WorkItemCommentExportService.ExportAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemCommentExportService.cs` — builds folder path `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-c<commentId>/` using `ModifiedDate` ticks, serialises `WorkItemComment` to `comment.json` via `IArtefactStore.WriteAsync`; calls `IWorkItemCommentSource.GetCommentsAsync(workItemId, WorkItemsScopeParameters.Comments.IncludeDeleted, ct)`; constructor-injected `IWorkItemCommentSource`, `IArtefactStore`, `IOptions<WorkItemsScopeParameters>` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: later design removed this service and performs inline comment export in `WorkItemExportOrchestrator`.
- [X] T013 [US1] Extend `AzureDevOpsWorkItemCommentSource` with pagination continuation-token loop in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemCommentSource.cs` — loop until `continuationToken` is null; yield each comment record — Status: complete
- [ ] T014 [US1] Fetch each comment's version history in `AzureDevOpsWorkItemCommentSource` — call `GetCommentVersionsAsync(workItemId, commentId)` and yield one `WorkItemComment` per version entry (not just the latest); set `Version`, `ModifiedDate`, `Text` from version payload — Status: incomplete
  - Incomplete evidence: `AzureDevOpsWorkItemCommentSource.cs` does not call `GetCommentVersionsAsync`; it only maps paged `response.Comments`.
- [X] T015 [US1] Add `workitems-comments.cursor.json` read/write logic to `WorkItemCommentExportService` in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemCommentExportService.cs` — read cursor at start; skip `workItemId <= lastProcessedWorkItemId`; write cursor `{lastProcessedWorkItemId, stage: "Completed"}` after each work item completes — Status: complete/superseded; completed because superseded by specs/020-resumable-batching-cursor/tasks.md
  - Superseded evidence: checkpointing moved to orchestrator-level export progress (`export.workitems.cursor` / `export_progress.db`) and no separate comment export service exists.
- [X] T016 [US1] Call `IWorkItemCommentExportService.ExportAsync(workItemId, ct)` from `WorkItemExportOrchestrator` after all revisions for a work item are written in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` — guard on `WorkItemsScopeParameters.Comments.Enabled` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: `WorkItemExportOrchestrator` directly uses `IWorkItemCommentSourceFactory` and inline matching logic.
- [X] T017 [US1] Wire `IWorkItemCommentExportService` into `WorkItemsModule` constructor and pass to `WorkItemExportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: module wiring now injects `IWorkItemCommentSourceFactory` instead (`WorkItemsModule.cs`).
- [X] T018 [US1] Register `AzureDevOpsWorkItemCommentSource` (as `IWorkItemCommentSource`) and `WorkItemCommentExportService` (as `IWorkItemCommentExportService`) in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ExportServiceCollectionExtensions.cs` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: DI registers `IWorkItemCommentSourceFactory` for connector-specific creation.
- [X] T019 [P] [US1] Write unit tests for `WorkItemCommentExportService` covering: (a) 3 comments → 3 `IArtefactStore.WriteAsync` calls, (b) 0 comments → 0 calls, (c) cursor skip logic in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/WorkItemCommentExportServiceTests.cs` — Status: complete/superseded; completed because superseded by specs/011-inline-comment-fetching/tasks.md
  - Superseded evidence: behavior is now covered by BDD/system tests (`ExportCommentsSteps`, CLI export tests) after service removal.

**Checkpoint**: `dotnet test` — US1 unit tests pass. Manual: run `scenarios/export-ado-workitems-single-project.json` and confirm comment folders appear for a work item known to have comments.

---

## Phase 4: User Story 2 — Download Embedded Images from HTML Fields (P2)

**Goal**: Every ADO-hosted image embedded in an HTML field of any exported revision is downloaded, stored beside `revision.json` with a SHA-256-derived filename, and the field value rewritten to the local filename. External URLs are preserved with a warning. Inaccessible URLs do not abort export.

**Independent Test**: Export a work item whose `System.Description` contains an embedded ADO image. Confirm the image file (`<sha256>.<ext>`) appears beside `revision.json` and the `img src` in `revision.json` points to that filename.

### Feature File Additions

- [X] T020 [US2] Add P2 acceptance scenarios to `features/export/work-items/embedded-images/export-embedded-images.feature` — create the file if it does not exist; translate all 4 P2 spec scenarios: (1) image downloaded + URL rewritten, (2) same URL → one copy, (3) external URL preserved + warning, (4) 404 URL preserved + warning + export continues — Status: complete

### Implementation for User Story 2

- [X] T021 [US2] Implement `AzureDevOpsEmbeddedImageDownloader.TryDownloadAsync` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsEmbeddedImageDownloader.cs` — returns `null` for non-ADO URLs (log WARN); returns `null` for 4xx/5xx (log WARN); otherwise returns `EmbeddedImageDownloadResult{Bytes, Extension}` where extension is inferred from `Content-Type` header using the mapping table in `research.md` — Status: complete
- [ ] T022 [US2] Add Polly `ResiliencePipeline` (3× retry, exponential back-off, `Retry-After` respect, 30s `HttpClient` timeout) to `AzureDevOpsEmbeddedImageDownloader` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsEmbeddedImageDownloader.cs` — pipeline created via `ResiliencePipelineBuilder`; injected `HttpClient` registered with `IHttpClientFactory` — Status: incomplete
  - Incomplete evidence: retries are registered, but explicit `Retry-After` handling and explicit 30s timeout are not evidenced in downloader implementation.
- [X] T023 [US2] Implement `EmbeddedImageExportService.ProcessHtmlAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Export/EmbeddedImageExportService.cs` — use `HtmlAgilityPack` to parse `img[src]` nodes; call `IEmbeddedImageDownloader.TryDownloadAsync`; compute `SHA256.HashData(bytes)` hex filename; call `IArtefactStore.WriteAsync(folderPath + filename, bytes)` only if not already written (per-call dedup dictionary); rewrite `src` attribute to local filename; return mutated HTML; constructor-injected `IEmbeddedImageDownloader`, `IArtefactStore`, `ILogger` — Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption/tasks.md
  - Superseded evidence: implemented in Agent layer with `IPackageAccess` boundary (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/EmbeddedImageExportService.cs`).
- [ ] T024 [US2] Call `IEmbeddedImageExportService.ProcessHtmlAsync` for each HTML-format field value inside `WorkItemExportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` — determine field format from `multilineFieldsFormat` dictionary in the work item response; guard on `WorkItemsScopeParameters.EmbeddedImages.Enabled`; overwrite `revision.json` with rewritten field values via `IArtefactStore` — Status: incomplete
  - Incomplete evidence: no invocation of `ProcessHtmlAsync` in `WorkItemExportOrchestrator.cs`.
- [ ] T025 [P] [US2] Register `AzureDevOpsEmbeddedImageDownloader` (as `IEmbeddedImageDownloader`) and `EmbeddedImageExportService` (as `IEmbeddedImageExportService`) in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ExportServiceCollectionExtensions.cs`; register named `HttpClient` for image downloader — Status: incomplete
  - Incomplete evidence: downloader registration exists, but file notes `IEmbeddedImageExportService` is not registered.
- [ ] T026 [P] [US2] Wire `IEmbeddedImageExportService` into `WorkItemsModule` constructor and pass to `WorkItemExportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` — Status: incomplete
  - Incomplete evidence: `WorkItemsModule` constructor wiring uses comment source factory; no embedded image export service dependency is passed to orchestrator.
- [X] T027 [P] [US2] Write unit tests for `EmbeddedImageExportService.ProcessHtmlAsync` covering: (a) single image downloaded + URL rewritten, (b) same URL twice → one `IArtefactStore.WriteAsync` call, (c) external URL → null return → preserved, (d) null return (404) → preserved in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/EmbeddedImageExportServiceTests.cs` — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md
  - Superseded evidence: coverage delivered through current Agent BDD test assets (`EmbeddedImagesSteps`) instead of the original test file path.

**Checkpoint**: `dotnet test` — US2 unit tests pass. Manual: export a work item with embedded images and confirm local image files are written and `revision.json` URLs are rewritten.

---

## Phase 5: User Story 3 — Download Embedded Images from Markdown Fields and Comments (P3)

**Goal**: The same image download and rewrite behaviour from US2 is extended to Markdown-format fields and Markdown/HTML comment text. After this phase, all ADO-hosted images in the package are stored locally regardless of field format or document type.

**Independent Test**: Export a work item with a Markdown-format comment containing `![](https://dev.azure.com/...)`. Confirm the image file appears inside the comment sub-folder beside `comment.json` and the Markdown reference is rewritten.

### Feature File Additions

- [ ] T028 [US3] Add P3 acceptance scenarios (P3-1, P3-2) to `features/export/work-items/embedded-images/export-embedded-images.feature`: (1) Markdown comment image → downloaded beside comment.json + URL rewritten, (2) Markdown revision field image → downloaded beside revision.json + URL rewritten — Status: incomplete
  - Incomplete evidence: file includes markdown field scenario, but does not include markdown-comment scenario.

### Implementation for User Story 3

- [X] T029 [US3] Implement `EmbeddedImageExportService.ProcessMarkdownAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Export/EmbeddedImageExportService.cs` — use `Regex` pattern `!\[.*?\]\((https://[^)]+)\)` to extract image URLs; same download/SHA-256/write/rewrite pipeline as `ProcessHtmlAsync`; return mutated Markdown string — Status: complete
- [ ] T030 [US3] Call `IEmbeddedImageExportService.ProcessMarkdownAsync` for Markdown-format fields inside `WorkItemExportOrchestrator` in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemExportOrchestrator.cs` — extend the existing field-format routing added in T024 to branch on `markdown` format — Status: incomplete
  - Incomplete evidence: no orchestrator call to `ProcessMarkdownAsync` found in `WorkItemExportOrchestrator.cs`.
- [ ] T031 [US3] Call `IEmbeddedImageExportService.ProcessHtmlAsync` or `ProcessMarkdownAsync` (based on `comment.Format`) for each comment's `Text` field inside `WorkItemCommentExportService` in `src/DevOpsMigrationPlatform.Infrastructure/Export/WorkItemCommentExportService.cs` — guard on `WorkItemsScopeParameters.EmbeddedImages.Enabled`; overwrite `comment.json` with rewritten text field — Status: incomplete
  - Incomplete evidence: `WorkItemCommentExportService` does not exist; current inline comment path writes raw comment payload near revisions.
- [ ] T032 [P] [US3] Extend unit tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Export/EmbeddedImageExportServiceTests.cs` — add tests for `ProcessMarkdownAsync`: (a) single Markdown image downloaded + reference rewritten, (b) same URL twice → one download — Status: incomplete
  - Incomplete evidence: the specified unit test file is absent; current markdown coverage is not in the requested test artifact.

**Checkpoint**: `dotnet test` — US3 unit tests pass. Manual: export a work item with Markdown comments containing images and confirm images appear inside comment sub-folders.

---

## Phase 6: Polish and Cross-Cutting Concerns

**Purpose**: OTel instrumentation, documentation updates, and final verification gate.

- [ ] T033 [P] Add OTel activity sources `DevOpsMigrationPlatform.WorkItems.Comments` and `DevOpsMigrationPlatform.WorkItems.EmbeddedImages` in `WorkItemCommentExportService` and `EmbeddedImageExportService` — emit spans per work item and per image; register in service collection per existing OTel patterns in `ExportServiceCollectionExtensions` — Status: incomplete
  - Incomplete evidence: these activity source names are not present in current source files.
- [ ] T034 [P] Add OTel counters to `WorkItemCommentExportService` and `EmbeddedImageExportService`: `workitems.comments.fetched`, `workitems.comments.folders_written`, `workitems.comments.pages_fetched`, `workitems.comments.skipped_tfs_not_supported`, `workitems.images.downloaded`, `workitems.images.skipped_external`, `workitems.images.failed`, `workitems.images.bytes_downloaded` — Status: incomplete
  - Incomplete evidence: these explicit counter names are not implemented in the current export path.
- [X] T035 [P] Write unit tests for `CommentsScope` and `EmbeddedImagesScope` properties in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Modules/CommentsScopeTests.cs` and `EmbeddedImagesScopeTests.cs` — verify defaults: `Comments.Enabled=true`, `Comments.IncludeDeleted=false`, `EmbeddedImages.Enabled=true`, `EmbeddedImages.DownloadTimeoutSeconds=30`; verify JSON binding maps correctly from scenario config — Status: complete/superseded; completed because superseded by specs/029-import-workitems-attachments-nodes/tasks.md
  - Superseded evidence: option validation is now covered via extension/options deserialization tests rather than scope-class unit tests.
- [X] T036 Update `.agents/30-context/domains/workitems-format-summary.md` to document `<ticks>-<workItemId>-c<commentId>/comment.json` sub-folder, comment version folder layout, and embedded image files beside `revision.json` — Status: complete
- [X] T037 [P] Update `.agents/30-context/domains/migration-package-concept.md` to document embedded-image files beside documents and `Checkpoints/workitems-comments.cursor.json` cursor — Status: complete
- [ ] T038 Run `dotnet clean && dotnet build --no-incremental` from repo root — MUST produce zero errors and zero warnings before task is declared complete — Status: incomplete
  - Incomplete evidence: current build succeeds but produces warnings (including CS0162, NU1702, MSB3277) in baseline run output.
- [ ] T039 Run `dotnet test` from repo root — ALL tests MUST pass before task is declared complete — Status: incomplete
  - Incomplete evidence: reconciliation session only ran targeted tests; no fresh full-repository `dotnet test` evidence captured for this reconciliation.
- [ ] T040 Run export via `.vscode/launch.json` debug profile using `scenarios/export-ado-workitems-single-project.json` — verify: comment folders appear, embedded images downloaded, `revision.json` URLs rewritten, no errors in console output — Status: incomplete
  - Incomplete evidence: no reproducible run artifact from this session verifies the specific debug-profile execution and rewrite outcomes.
- [X] T041 Create `[TestCategory("SystemTest")]` test `MigrationExportCommand_SystemTest_WorkItemComments_ExitsZero_AndWritesCommentFolders` in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/MigrationExportCommandTests.cs` — runs `devopsmigration export --config scenarios/export-ado-workitems-single-project.json --force-fresh` as subprocess; asserts: exit code 0, success message printed, `WorkItems/yyyy-MM-dd/` folders contain at least one `*-c<commentId>/comment.json` file (proving comments were exported); parses and logs counts of comment folders found — Status: complete

---

## Dependencies

```
Phase 1 (T001–T002)
    ↓
Phase 2 (T003–T009c)  ← All foundational types, interfaces, and scope configs
    ↓
Phase 3 (T010–T019)  ← US1 Comments (P1) — BLOCKS US3 (T031 needs WorkItemCommentExportService)
    ↓
Phase 4 (T020–T027)  ← US2 HTML Images (P2) — BLOCKS US3 (T029-T031 extend these services)
    ↓
Phase 5 (T028–T032)  ← US3 Markdown Images (P3)
    ↓
Phase 6 (T033–T041)  ← Polish, OTel, docs, and SystemTest
```

Within Phase 2, sequential ordering: T003, then T004 ‖ T005 ‖ T006 ‖ T007 ‖ T008, then T009 → T009b → T009c  
Within Phase 3, sequential ordering: T010 → T011 → T012 → T013 → T014 → T015 → T016 → T017 → T018 → T019  
Within Phase 4, sequential ordering: T020 → T021 → T022 → T023 → T024 → T025 → T026 → T027  
Within Phase 5: T028 → T029 → T030 → T031 → T032  

**Parallel opportunities per phase (within-story):**

| Phase | Parallel group |
|---|---|
| P1 (Setup) | T001 ‖ T002 |
| P2 (Foundational) | T003, then T004 ‖ T005 ‖ T006 ‖ T007 ‖ T008, then T009 ‖ T009b, then T009c |
| P3 (US1) | T011 ‖ T019 (after others complete) |
| P4 (US2) | T021 ‖ T022 after T020; T025 ‖ T026 ‖ T027 after T024 |
| P6 (Polish) | T033 ‖ T034 ‖ T035 ‖ T036 ‖ T037; T041 (parallel if test env configured separately) |

---

## Implementation Strategy

**MVP scope**: Complete Phase 1 + Phase 2 + Phase 3 (US1 only) to deliver comment export in a shippable state.

**Incremental delivery**:
1. **MVP**: T001–T019 → comments exported, no image download
2. **+HTML images**: T020–T027 → HTML revision images downloaded, commens images still raw URLs
3. **+Markdown images**: T028–T032 → all images downloaded from all content types
4. **+Polish**: T033–T040 → OTel metrics, docs complete, final gates

---

## Summary

| Phase | Tasks | User Story | Key Deliverable |
|---|---|---|---|
| 1 – Setup | T001–T002 | — | NuGet deps available |
| 2 – Foundational | T003–T009c | — | All interfaces + records + scope configs |
| 3 – US1 | T010–T019 | US1 (P1) | Comment folders written, cursor works |
| 4 – US2 | T020–T027 | US2 (P2) | HTML images downloaded + URLs rewritten |
| 5 – US3 | T028–T032 | US3 (P3) | Markdown images downloaded + URLs rewritten |
| 6 – Polish | T033–T041 | — | OTel, docs, final gates, SystemTest |
| **Total** | **43 tasks** | **3 stories** | |

