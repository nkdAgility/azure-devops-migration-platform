# Pending Actions

Derived from specs/002–010 task files, discrepancy reports, and cross-referenced against current source code.
Items are grouped by feature spec and categorised as **Code**, **Tests**, or **Docs/Verification**.

> **Legend**
> - 🔴 Not started — no relevant file or method exists
> - 🟡 Partial — scaffold/stub exists but is not functional
> - 🟢 Implemented — code exists; task checkbox not yet updated in tasks.md

---

## spec 004 — Fix CLI Architecture and Add Command Testing

### Tests

- 🔴 `T023` Integration test `MigrationPlatformHostTests.cs` — configuration binding validation (config values flow from `--config` through `IOptions<T>` to services).
- 🔴 `T024` Tests in existing command test files verifying config values reach target services via DI.
- 🔴 `T025` Tests for default config file resolution (`migration.json`) when `--config` is not specified.
- 🔴 `T029` Update any services that still access config files directly to receive config via DI (where applicable).
- 🔴 `T030` Config validation tests: malformed JSON and missing required sections produce clear error messages.
- 🔴 `T031` Feature file `features/cli/execute/host-builder-architecture.feature` — Gherkin for User Story 3 host builder architecture scenarios.
- 🔴 `T032` `ArchitectureTests.cs` — assert `Program.cs` line count < 50.
- 🔴 `T033` Unit tests: adding a new command does not require modifying `Program.cs` or host setup.
- 🔴 `T034` Integration tests: complete DI container service registration and resolution.

### Docs

- 🔴 `T038` XML doc-comments on `MigrationPlatformHost` and `CommandBase<T>` explaining host builder architecture pattern.
- 🔴 `T040` Update command help text for comprehensive information display.
- 🔴 `T041` Error message validation for all invalid command usage scenarios.

---

## spec 005 — System Inventory Tests

> **Status**: The `SystemTestConfiguration`, `SystemTestContext`, and `SystemTestBase` helpers exist. Three system test methods exist in `InventoryCommandTests.cs` for US1. US2 (CI) and US3 (documentation) are fully unimplemented.

### Tests

- 🔴 `T019` Feature file `features/cli/inventory/system-test-ci-execution.feature` — Gherkin for US2 CI execution scenarios.
- 🔴 `T020` GitHub Actions workflow `.github/workflows/system-tests.yml` for system test execution (separate from the existing `main.yml`; uses `AZDEVOPS_SYSTEM_TEST_ORG` and `AZDEVOPS_SYSTEM_TEST_PAT` secrets).
- 🔴 `T021` System test method `InventoryCommand_SystemTest_CIEnvironment_ExecutesSecurely`.
- 🔴 `T022` System test method `InventoryCommand_SystemTest_MissingSecrets_ContinuesPipeline`.
- 🔴 `T023` Credential security validation: no token values appear in test output or logs.
- 🔴 `T024` Test execution timeout and retry logic for network resilience in CI.
- 🔴 `T026` Conditional test execution logic (local vs CI environment).

### Docs

- 🔴 `T027` Feature file `features/platform/documentation/contributor-onboarding-system-tests.feature` — Gherkin for US3 contributor onboarding scenarios.
- 🔴 `T028` System test documentation section in `docs/contributors.md` (setup, secrets config, troubleshooting, cross-platform instructions).
- 🔴 `T035` Validation commands and test setup verification procedures in `docs/contributors.md`.
- 🔴 `T036` Validate system test execution time meets 30-second maximum requirement.

---

## spec 006 — Work Items Export (Azure DevOps via REST API)

> **Status**: `IWorkItemRevisionSource`, `AzureDevOpsWorkItemRevisionSource`, `IWorkItemRevisionSourceFactory`, `AzureDevOpsWorkItemRevisionSourceFactory`, and `WorkItemsModule` all exist and export is functional. Attachment download was implemented via `AzureDevOpsAttachmentBinarySource` (a different shape from what spec 006 defines). `WriteStreamAsync` on `IArtefactStore` does not exist (only `WriteBinaryAsync`). `IAzureDevOpsAttachmentDownloader` with streaming + SHA-256 + retry pipeline does not exist.

### Code

- 🔴 `T002` Add `Task WriteStreamAsync(string path, Stream content, CancellationToken cancellationToken)` to `IArtefactStore`. Implement in `FileSystemArtefactStore`. (Required by streaming attachment download contract.)
- 🔴 `T004` Add `[JsonIgnore] public string? DownloadUrl { get; init; }` to `AttachmentMetadata`.
- 🔴 `T005` Extend `AttachmentDownloadResult` with `Sha256` and `Size` properties; add `Succeeded(string sha256, long size)` factory overload.
- 🔴 `T006` Add `AttachmentsProcessed` and `AttachmentsFailed` to `ProgressEvent`.
- 🔴 `T007` Create `IAttachmentDownloader` interface in `Abstractions/Services/`.
- 🔴 `T008` Create `IWorkItemRevisionSourceFactory` — already exists but verify signature matches spec (takes `organisationUrl`, `project`, `pat`, `wiqlQuery`).
- 🔴 `T009` Rename local `IAttachmentDownloader` in `TfsAttachmentDownloader.cs` to `ITfsAttachmentDownloader` to avoid namespace collision with new Abstractions interface.
- 🔴 `T020` Marker interface `IAzureDevOpsAttachmentDownloader : IAttachmentDownloader` in `Infrastructure.AzureDevOps`.
- 🔴 `T021` `AzureDevOpsAttachmentDownloader` — streaming download via `IAzureDevOpsClientFactory`, SHA-256 in-flight via `CryptoStream`, stores via `IArtefactStore.WriteStreamAsync`.
- 🔴 `T022` Extend `WorkItemExportOrchestrator` — delta detection (skip re-downloading same URL on adjacent revisions), update attachment metadata (`Sha256`, `Size`, `RelativePath`), emit `AttachmentsProcessed`/`AttachmentsFailed` per `ProgressEvent`.
- 🔴 `T023` Register `AzureDevOpsAttachmentDownloader` + `AddResiliencePipeline("attachment-download", ...)` (8 retries, exponential back-off, transient 5xx/408/429) in `WorkItemExportServiceCollectionExtensions`.

### Tests

- 🔴 `T010` Extend `features/export/work-items/revisions/export-work-item-revisions.feature` with `@azure-devops-rest` tagged scenarios for US1 (canonical folder layout, `changedDate`-derived folder name, full `revision.json` field set).
- 🔴 `T016` Extend `WorkItemExportOrchestratorTests` for new constructor overload (with `IProgressSink`) and `ExportAsync(source, includeAttachments, ct)` overload.
- 🔴 `T017` Extend `ExportWorkItemRevisionsContext.cs` and `ExportWorkItemRevisionsSteps.cs` for `@azure-devops-rest` scenarios.
- 🔴 `T019` Extend `features/export/work-items/attachments/export-attachments.feature` with `@azure-devops-rest` scenarios (US2: binary storage, sha256/size metadata, delta, retry).
- 🔴 `T024` Extend `WorkItemExportOrchestratorTests` with `IAzureDevOpsAttachmentDownloader` mock tests (delta, failure, `includeAttachments=false`).
- 🔴 `T025` Extend `ExportWorkItemRevisionsContext.cs` and `ExportWorkItemRevisionsSteps.cs` for `@azure-devops-rest` attachment scenarios.
- 🔴 `T026` Verify `features/export/work-items/revisions/export-work-item-revisions.feature` covers US3 cursor/checkpoint scenarios (skip on `Completed`, re-process on `InProgress`, idempotent re-run).
- 🔴 `T027` Extend `WorkItemExportOrchestratorTests` — cursor written after attachments and `revision.json`; `InProgress` cursor triggers re-process; fully-`Completed` cursor triggers zero writes.
- 🔴 `T028` SC-004 count reconciliation assertion in `WorkItemExportOrchestratorTests`.
- 🔴 `T029` Extend feature file for US4 `ProgressEvent` emission per revision.
- 🔴 `T030` Extend `WorkItemExportOrchestratorTests` — `IProgressSink.Emit` called once per revision; counters increment correctly.
- 🔴 `T031` SC-003 schema validation check in `ExportWorkItemRevisionsContext.cs` — capture + deserialise every written `revision.json`, assert required fields present.

### Docs

- 🔴 `T033` Add `### WorkItemsModule — ADO Export` subsection to `docs/modules.md` describing `IWorkItemRevisionSourceFactory`, `AzureDevOpsWorkItemRevisionSource`, `WorkItemQueryWindowStrategy` reuse, `IAzureDevOpsAttachmentDownloader`, and O(N) call pattern.
- 🔴 `T034` Note in `docs/architecture.md` Migration Agent row: source connectors implement `IWorkItemRevisionSource`; `AzureDevOpsWorkItemRevisionSource` in `Infrastructure.AzureDevOps` is the first concrete implementation.
- 🔴 `T035` Add "Attachment Download Contract" section to `.agents/context/workitems-format.md` — streaming download (`WriteStreamAsync`), SHA-256 in-flight via `CryptoStream`, retry policy (8 retries, exponential back-off, transient 5xx/408/429), delta detection.

---

## spec 007 — Three-Channel Observability (Verification Only)

> **Status**: All code and documentation changes are implemented. The following tasks are end-to-end verification runs that have not been confirmed.

### Verification

- 🟡 `T010` Run an export and confirm `Logs/progress.jsonl` exists in the package output with at least one NDJSON record per module stage transition.
- 🟡 `T015` Run an export and confirm `Logs/agent.jsonl` exists in the package output with structured NDJSON records at `Warning+` level.
- 🟡 `T030` Run `export --level Debug --follow`, confirm Debug records appear in `agent.jsonl` and diagnostics stream to console; CLI exits on completion.
- 🟡 `T055` Run `scenarios/export-ado-workitems-single-project.json` via `.vscode/launch.json` debug profile; verify both `Logs/progress.jsonl` and `Logs/agent.jsonl` are produced.

---

## spec 008-simulated-data-source — Simulated Source and Target

> **Status**: Entire feature not started. No `Simulated` source or target implementation exists anywhere in `src/`.

### Code

- 🔴 `SimulatedWorkItemRevisionSource` — implements `IWorkItemRevisionSource`; generates deterministic work item revisions from `source.workItemCount` and `source.seed`; no external connections.
- 🔴 `SimulatedInventoryService` — implements `IInventoryService`; returns counts consistent with seed and `workItemCount`.
- 🔴 `SimulatedTargetService` — accepts any valid package as import target; writes no external state; validates round-trip counts.
- 🔴 `SimulatedSourceConfiguration` / `SimulatedTargetConfiguration` binding classes (source type `Simulated`, fields: `workItemCount`, `seed`, `includeAttachments`, `configHash`).
- 🔴 Registration and DI wiring for simulated source/target in `ServiceCollectionExtensions`.
- 🔴 `configHash` mismatch detection — reject resume when seed or `workItemCount` changes between runs.
- 🔴 Scenario config files: `scenarios/export-simulated.json` and `scenarios/migrate-simulated.json`.
- 🔴 `.vscode/launch.json` entries for simulated export and migrate profiles.

### Tests

- 🔴 Feature files for US1 (simulated inventory), US2 (simulated export), US3 (simulated end-to-end migrate), US4 (simulated system test).
- 🔴 `[TestCategory("SystemTest")]` test `SimulatedMigrationCommandTests` — runs `devopsmigration migrate` with 100 simulated work items; asserts package structure, cursor, and `Logs/progress.jsonl` without mocking platform internals.
- 🔴 Determinism test — same seed produces identical `discovery-summary.csv` counts across two runs.
- 🔴 `configHash` mismatch test — resume rejected when parameters change between runs.

---

## spec 008-tui-job-dashboard — TUI Job Dashboard

> **Status**: All view classes (`TuiJobListView`, `TuiMetricsView`, `TuiLogView`, `TuiMainView`), `TuiCommand`, `IControlPlaneClient`, and all five Gherkin feature files exist. All TUI unit test classes exist. `GET /jobs` endpoint and `GetAllJobsAsync` are implemented. `PrintJobSubmitted` is implemented and called from `MigrationExportCommand`. `docs/control-plane.md` includes `GET /jobs` and `GET /jobs/{jobId}/telemetry` rows. The following remain pending.

### Code

- 🟡 `T018` `MigrationImportCommand` — add `PrintJobSubmitted` call after `SubmitAsync`. *(Currently a stub that returns exit code 1. The call site should be added as the command is implemented.)*
- 🟡 `T019` `MigrationMigrateCommand` — same as T018.
- 🔴 `T044` Wire all five job state transitions through control plane controllers: `Queued` (on job submission), `Leased` (on agent pickup — already done), `Running` (on first progress push — already done), `Completed` (on job-ended signal — already done), `Failed` (on failure push — already done). Verify `Queued` is set in `JobsController.Submit`.

### Tests

- 🔴 `T043` `[TestCategory("SystemTest")]` test in `TuiSystemTests.cs` — run `devopsmigration export --config ...`; assert stdout contains `"Job ID  :"` and `"Control :"` lines, and `"Job ID  :"` appears before the first progress output line.

---

## spec 009 — Resumable Export and Import

> **Status**: Export-side resume (`--force-fresh` for export, `IStateStore.DeleteAsync`, `ICheckpointingService.DeleteCursorAsync`, `MigrationJobResume`, `PhaseTrackingService`, `JobPhaseRecord`) is fully implemented. `.vscode/launch.json` force-fresh profiles exist for export, import, and migrate. Import-side resume (`WorkItemImportOrchestrator`, `IWorkItemTargetService`, `AzureDevOpsWorkItemTargetService`, `WorkItemsModule.ImportAsync`) is entirely unimplemented.

### Code

- 🔴 `T013` `IWorkItemTargetService` interface in `Abstractions/Services/` — four methods: `CreateOrGetWorkItemAsync`, `ApplyFieldsAsync`, `ApplyLinksAsync`, `UploadAttachmentAsync`.
- 🔴 `T014` `AzureDevOpsWorkItemTargetService` stub — implements `IWorkItemTargetService`; all methods throw `NotImplementedException` (shape only; full ADO REST implementation is a follow-on task).
- 🔴 `T015` `WorkItemImportOrchestrator` — streaming import engine: reads revision folders from `IArtefactStore`, applies four-stage pipeline (CreatedOrUpdated → AppliedFields → AppliedLinks → UploadedAttachments), uses cursor from `ICheckpointingService` to resume from last completed stage, emits `ProgressEvent` per item.
- 🔴 `T017` Stage A (`CreatedOrUpdated`) in `WorkItemImportOrchestrator`.
- 🔴 `T018` Stage B (`AppliedFields`) in `WorkItemImportOrchestrator`.
- 🔴 `T019` Stage C (`AppliedLinks`) in `WorkItemImportOrchestrator`.
- 🔴 `T020` Stage D (`UploadedAttachments`) in `WorkItemImportOrchestrator`.
- 🔴 `T021` `AzureDevOpsWorkItemTargetService` Stage A — real ADO REST `POST /_apis/wit/workitems/{type}`.
- 🔴 `T022` `AzureDevOpsWorkItemTargetService` Stages B, C, D — real ADO REST `PATCH` (fields/links) and `POST` (attachment upload).
- 🔴 `T024` `WorkItemsModule.ImportAsync` — construct `WorkItemImportOrchestrator` from `ImportContext.ArtefactStore`, `ImportContext.StateStore`, injected `IWorkItemTargetService`; call `orchestrator.ImportAsync`.
- 🔴 `T025` Register `AzureDevOpsWorkItemTargetService` as `IWorkItemTargetService` in an `ImportServiceCollectionExtensions`.

### Tests

- 🔴 `T012` Gherkin scenarios in `features/import/work-items/revisions/import-work-item-revisions.feature` — US2 resume scenarios (resume from `AppliedFields` cursor, idempotent re-run, four-stage progression).
- 🔴 `T016` Unit tests for `WorkItemImportOrchestrator` in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Import/WorkItemImportOrchestratorTests.cs`.
- 🔴 `T023` Reqnroll step definitions `ImportWorkItemRevisionsContext.cs` and `ImportWorkItemRevisionsSteps.cs` for the import feature scenarios.
- 🔴 `T026` Feature file `features/cli/execute/resume-mode.feature` — US3 Both-mode resume scenarios.

### Docs

- 🔴 `T033` Rectify remaining documentation discrepancies logged in `specs/009-resumable-export-import/discrepancies.md`. Specifically: export cursor behaviour section in `.agents/context/checkpointing.md` (verify the export cursor subsection added in the previous docs-integration pass is present).

---

## Cross-Cutting: MigrationImportCommand and MigrationMigrateCommand

Both commands are currently stubs returning exit code 1 with a "not available in this release" message. They are hidden from the Preview release channel (`[HideFromChannel(ReleaseChannel.Preview)]`). Full implementation is blocked by the import orchestrator work listed in spec 009 above. The stubs are intentional and do not represent a defect.

---

## Summary Table

| Area | Not Started 🔴 | Partial 🟡 | Blocking? |
|------|---------------|-----------|-----------|
| spec 004 — CLI architecture tests | 9 | 0 | No |
| spec 005 — System inventory tests (US2 + US3) | 7 | 0 | No |
| spec 006 — ADO attachment streaming | 8 code + 12 tests + 3 docs | 0 | Yes — `IArtefactStore.WriteStreamAsync` is a prerequisite for spec 009 import |
| spec 007 — Observability verification runs | 0 | 4 | No |
| spec 008-simulated — Simulated source/target | entire feature | 0 | No |
| spec 008-tui — TUI polish | 1 code + 1 test | 2 code | No |
| spec 009 — Import orchestrator | 9 code + 4 tests + 1 doc | 0 | Yes — blocks full migrate |

**Highest priority unblocked work**: spec 006 `IArtefactStore.WriteStreamAsync` and the `AzureDevOpsAttachmentDownloader` chain, as they unblock the spec 009 import orchestrator which in turn unblocks `MigrationImportCommand` and `MigrationMigrateCommand`.
