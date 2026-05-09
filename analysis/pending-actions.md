# Pending Actions

## Maintenance Policy

This file is the canonical backlog of unimplemented spec tasks. It MUST be updated:
- **When a spec branch is merged**: remove all items that are now implemented by that spec.
- **When a new spec is created**: add a section for any tasks not yet started.
- **Automatically reviewed** by the end-session skill as part of the Phase 5 Documentation Sync gate.

A stale `pending-actions.md` is a guardrail violation (see `.agents/guardrails/test-first-workflow.md` Phase 5 and `.specify/memory/constitution.md` Spec-Completion Gate).

---

Derived from specs/002–010 task files, discrepancy reports, and cross-referenced against current source code.
Items are grouped by feature spec and categorised as **Code**, **Tests**, or **Docs/Verification**.

> **Legend**
> - 🔴 Not started — no relevant file or method exists
> - 🟡 Partial — scaffold/stub exists but is not functional
> - 🟢 Implemented — code exists; task checkbox not yet updated in tasks.md

---

## spec 030 review note (2026-05-04)

- Reviewed against `specs/030-module-analiser-refactor/tasks.md` during implementation continuation.
- No explicit spec-030 entries existed in this backlog; existing entries remain for earlier specs and are unchanged.

## spec 004 — Fix CLI Architecture and Add Command Testing

### Tests

- 🟢 `T023` `MigrationPlatformHostTests.cs` created — config binding, DI resolution, delegate invocation, ExtractConfigFileArg tests.
- 🟢 `T024` Covered by `CreateDefaultBuilder_InvokesConfigureServicesDelegate` and `CreateDefaultBuilder_ConfigureServicesDelegateReceivesConfiguration`.
- 🟢 `T025` Covered by `ExtractConfigFileArg_WhenNoConfig_DefaultsToMigrationJson`.
- ⬜ `T029` ~~Update any services that still access config files directly to receive config via DI.~~ **Superseded** — `MigrationPlatformHost.CreateDefaultBuilder` binds all configuration via `IOptions<T>` pattern. No services access config files directly.
- 🔴 `T030` Config validation tests: malformed JSON and missing required sections produce clear error messages.
- 🟢 `T031` Feature file `features/cli/execute/host-builder-architecture.feature` created.
- ⬜ `T032` ~~`ArchitectureTests.cs` — assert `Program.cs` line count < 50.~~ **N/A** — Guardrail SA-16 challenged.
- 🟢 `T033` Covered by `CreateDefaultBuilder_SupportsArbitraryServiceRegistration_WithoutHostChanges`.
- 🟢 `T034` Covered by `CreateDefaultBuilder_RegistersEnvironmentOptions` and `CreateDefaultBuilder_RegistersAnsiConsole`.

### Docs

- 🟢 `T038` XML doc-comments already present on `MigrationPlatformHost` and `CommandBase<T>`.
- 🔴 `T040` Update command help text for comprehensive information display.
- 🔴 `T041` Error message validation for all invalid command usage scenarios.

---

## spec 005 — System Inventory Tests

> **Status**: The `SystemTestConfiguration`, `SystemTestContext`, and `SystemTestBase` helpers exist. Three system test methods exist in `InventoryCommandTests.cs` for US1. Feature file `features/cli/inventory/system-test-ci-execution.feature` created (5 scenarios). T021-T023 CI security tests implemented.

### Tests

- 🟢 `T019` Feature file `features/cli/inventory/system-test-ci-execution.feature` created (5 scenarios covering CI credential security).
- 🔴 `T020` GitHub Actions workflow `.github/workflows/system-tests.yml` for system test execution (separate from the existing `main.yml`; uses `AZDEVOPS_SYSTEM_TEST_ORG` and `AZDEVOPS_SYSTEM_TEST_PAT` secrets).
- 🟢 `T021` Covered by `InventoryCommand_SystemTest_CIEnvironment_ExecutesSecurely` in `InventoryCommandTests.cs`.
- 🟢 `T022` Covered by `InventoryCommand_SystemTest_MissingSecrets_SkipsGracefully` in `InventoryCommandTests.cs`.
- 🟢 `T023` Covered by `InventoryCommand_SystemTest_CredentialSecurity_NoTokenInOutput` in `InventoryCommandTests.cs`.
- ⬜ `T024` ~~Test execution timeout and retry logic for network resilience in CI.~~ **Superseded** — `SystemTestBase.ExecuteSystemTestAsync` already implements 30-second timeout with `CancellationTokenSource` and proper failure messaging.
- ⬜ `T026` ~~Conditional test execution logic (local vs CI environment).~~ **Superseded** — Handled by `[TestCategory("SystemTest")]` filter (excluded from unit test runs) + `SystemTestConfiguration.IsAvailable()` guard that skips when env vars are absent.

### Docs

- 🔴 `T027` Feature file `features/platform/documentation/contributor-onboarding-system-tests.feature` — Gherkin for US3 contributor onboarding scenarios.
- 🔴 `T028` System test documentation section in `docs/contributor-guide.md` (setup, secrets config, troubleshooting, cross-platform instructions).
- 🔴 `T035` Validation commands and test setup verification procedures in `docs/contributor-guide.md`.
- 🔴 `T036` Validate system test execution time meets 30-second maximum requirement.

---

## spec 006 — Work Items Export (Azure DevOps via REST API)

> **Status**: ✅ **Fully implemented.** Attachment streaming with SHA-256 via CryptoStream, delta detection via `previousAttachmentUrls` HashSet, resilience pipeline (8 retries, exponential back-off), and progress counters (`AttachmentsProcessed`/`AttachmentsFailed`) all implemented. Feature files and Reqnroll step definitions exist. Only 3 minor test-gap items remain.

### Code

- 🟢 `T002` Implemented: `WriteStreamAsync` exists on `IArtefactStore` (line 102) and `FileSystemArtefactStore`.
- 🟢 `T004` Implemented: `public string? DownloadUrl { get; init; }` exists on `AttachmentMetadata`.
- ⬜ `T005` ~~Extend `AttachmentDownloadResult` with `Sha256` and `Size` properties.~~ **Reconciled** — `AttachmentMetadata` already has `Sha256` and `Size` properties. The `AttachmentDownloadResult` type was not needed; metadata is populated directly.
- 🟢 `T006` Implemented: `AttachmentsProcessed` and `AttachmentsFailed` exist on `ProgressEvent`.
- ⬜ `T007` ~~Create `IAttachmentDownloader` interface in `Abstractions/Services/`.~~ **Reconciled** — Shipped as `IAttachmentBinarySource` in `Abstractions/Services/`. Streaming upgrade will be applied to this existing interface.
- ⬜ `T008` ~~Create `IWorkItemRevisionSourceFactory`.~~ **Implemented** — Exists in `Abstractions/Services/IWorkItemRevisionSourceFactory.cs` with signature `CreateAsync(MigrationEndpointOptions, CancellationToken)`.
- 🟢 `T009` Implemented: Renamed to `ITfsAttachmentDownloader` in `TfsAttachmentDownloader.cs`.
- ⬜ `T020` ~~Marker interface `IAzureDevOpsAttachmentDownloader : IAttachmentDownloader`.~~ **Reconciled** — `AzureDevOpsAttachmentBinarySource` implements `IAttachmentBinarySource` directly. No marker interface needed.
- 🟢 `T021` Implemented: `AzureDevOpsAttachmentBinarySource.StreamToStoreAsync` uses `GetStreamAsync` → `CryptoStream` (SHA-256 in-flight) → `IArtefactStore.WriteStreamAsync`.
- 🟢 `T022` Implemented: `WorkItemExportOrchestrator` has delta detection via `previousAttachmentUrls` HashSet, updates `Sha256`/`Size`/`RelativePath` on metadata, emits `AttachmentsProcessed`/`AttachmentsFailed` per `ProgressEvent`.
- 🟢 `T023` Implemented: `ExportServiceCollectionExtensions` registers `AddHttpClient("AttachmentDownload").AddPolicyHandler(GetAttachmentRetryPolicy())` with 8 retries, exponential back-off, transient 5xx/408/429.

### Tests

- 🟢 `T010` Feature file `export-work-item-revisions.feature` already has `@azure-devops-rest` tagged scenarios for canonical folder layout, cursor/checkpoint, and resume.
- 🟢 `T016` `WorkItemExportOrchestratorTests` has 20 tests covering attachment download, delta detection, failure counting, progress emission, cursor ordering.
- 🟢 `T017` `ExportWorkItemRevisionsContext.cs` and `ExportWorkItemRevisionsSteps.cs` exist and cover `@azure-devops-rest` scenarios.
- 🟢 `T019` `export-attachments.feature` has 5 `@azure-devops-rest` scenarios (binary beside revision.json, no global root, multi-attachment, empty revision, path safety).
- 🟢 `T024` Covered by `WorkItemExportOrchestratorTests` (delta, failure counters tested via `IAttachmentBinarySource` mock).
- 🟢 `T025` `ExportAttachmentsContext.cs` and `ExportAttachmentsSteps.cs` exist and cover attachment scenarios.
- 🟢 `T026` `export-work-item-revisions.feature` covers cursor/checkpoint scenarios (skip on Completed, re-process on InProgress, idempotent re-run).
- 🟢 `T027` Covered: `ExportAsync_CursorWrittenAfterAttachments` test verifies cursor ordering; existing tests cover `InProgress`/`Completed` cursor logic.
- 🔴 `T028` SC-004 count reconciliation assertion in `WorkItemExportOrchestratorTests`.
- 🔴 `T029` Extend feature file for US4 `ProgressEvent` emission per revision.
- 🟢 `T030` Covered: `ExportAsync_EmitsProgressPerWorkItem` and `ExportAsync_AttachmentFailure_IncrementsFailedCounter` tests verify emission and counters.
- 🔴 `T031` SC-003 schema validation check in `ExportWorkItemRevisionsContext.cs` — capture + deserialise every written `revision.json`, assert required fields present.

### Docs

- 🟢 `T033` Added `### WorkItemsModule — ADO Export` subsection to `docs/module-development-guide.md`.
- 🟢 `T034` Updated `docs/architecture.md` `Infrastructure.AzureDevOps` row with source connector and streaming attachment binary source notes.
- 🟢 `T035` Added "Attachment Download Contract" section to `.agents/context/workitems-format-summary.md`.

---

## spec 007 — Three-Channel Observability (Verification Only)

> **Status**: All code and documentation changes are implemented. Three system tests for package log file production have been verified.

### Code

- 🟢 `PackageLoggerProvider` hosted-service registration — Fixed: refactored to extend `BackgroundService` and registered via `AddHostedService` in `DiagnosticsServiceExtensions.cs`. Agent host logging filter changed from blanket `SetMinimumLevel(Warning)` to category-specific filters so package/control-plane sinks receive `Information`+ logs.

### Verification

- 🟢 `T010` `QueueExportSimulated_ProducesProgressJsonl` — asserts `Logs/progress.jsonl` exists with ≥1 records. **Passes.**
- 🟢 `T015` `QueueExportSimulated_ProducesAgentJsonl` — asserts `Logs/agent.jsonl` exists with ≥1 records. **Passes.**
- 🟡 `T030` Run `export --level Debug --follow`, confirm Debug records appear in `agent.jsonl` and diagnostics stream to console; CLI exits on completion.
- 🟢 `T055` `QueueExportSimulated_ProducesBothLogFiles` — asserts both log files exist. **Passes.**

---

## spec 008-simulated-data-source — Simulated Source and Target

> **Status**: ✅ **Fully implemented.** `SimulatedWorkItemRevisionSource`, `SimulatedWorkItemRevisionSourceFactory`, `SimulatedWorkItemImportTarget`, `SimulatedWorkItemImportTargetFactory`, `SimulatedProjectDiscoveryService`, `SimulatedWorkItemDiscoveryService`, `SimulatedEndpointOptions`, `SimulatedGeneratorConfig`, and `SimulatedServiceCollectionExtensions` all exist in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/`. Scenario configs (`roundtrip-simulated.json`, `queue-export-workitems-simulated-source.json`, `queue-import-workitems-simulated-target.json`, `queue-import-workitems-simulated-fixture.json`) and `.vscode/launch.json` entries (9 simulated profiles) all exist. `SimulatedMigrationCommandTests.cs` exists with `[TestCategory("SystemTest_Simulated")]` tests.

### Code

- 🟢 `SimulatedWorkItemRevisionSource` — Implemented in `Infrastructure.Simulated/Export/`.
- 🟢 `SimulatedInventoryService` — Implemented as `SimulatedWorkItemDiscoveryService` + `SimulatedProjectDiscoveryService` in `Infrastructure.Simulated/Services/`.
- 🟢 `SimulatedTargetService` — Implemented as `SimulatedWorkItemImportTarget` in `Infrastructure.Simulated/Import/`.
- 🟢 `SimulatedSourceConfiguration` / `SimulatedTargetConfiguration` — Implemented as `SimulatedEndpointOptions` + `SimulatedGeneratorConfig` in `Infrastructure.Simulated/Options/`.
- 🟢 Registration and DI wiring — `SimulatedServiceCollectionExtensions`.
- 🟢 Scenario config files — Multiple simulated scenarios in `scenarios/`.
- 🟢 `.vscode/launch.json` entries — 9 simulated profiles exist.

### Tests

- 🟢 `SimulatedMigrationCommandTests` — System tests exist with `[TestCategory("SystemTest_Simulated")]`.
- 🟢 Determinism and roundtrip tests — Covered by `simulated-export.feature` and `simulated-roundtrip.feature`.

---

## spec 008-tui-job-dashboard — TUI Job Dashboard

> **Status**: ✅ **Complete.** All view classes (`TuiJobListView`, `TuiMetricsView`, `TuiLogView`, `TuiMainView`), `TuiCommand`, `IControlPlaneClient`, and all five Gherkin feature files exist. All TUI unit test classes exist. `GET /jobs` endpoint and `GetAllJobsAsync` are implemented. `PrintJobSubmitted` is implemented and called from `QueueCommand` for all modes. `docs/control-plane.md` includes `GET /jobs` and `GET /jobs/{jobId}/telemetry` rows. T043 (PrintJobSubmitted unit tests) and T044 (Queued state on submission) both verified.

### Code

- ⬜ `T018` ~~`MigrationImportCommand` — add `PrintJobSubmitted` call.~~ **N/A** — `QueueCommand` handles all four modes (Export/Prepare/Import/Migrate) and calls `PrintJobSubmitted` for all. Separate command classes are unnecessary.
- ⬜ `T019` ~~`MigrationMigrateCommand` — same as T018.~~ **N/A** — Same rationale as T018.
- 🟢 `T044` Verified: `JobStore.Enqueue` sets `_states[jobId] = "Queued"` immediately on submission. All five transitions implemented.

### Tests

- 🟢 `T043` Covered by `PrintJobSubmittedTests` (Unit) and `TuiSystemTests` — `PrintJobSubmitted_OutputContainsJobIdLine_SC004`, `PrintJobSubmitted_OutputContainsControlLine_SC004`, `PrintJobSubmitted_JobIdLineBeforeControlLine_SC004`.

---

## spec 009 — Resumable Export and Import

> **Status**: ✅ **Import-side resume fully implemented by spec 013 (ADO Work Items Import)**. Export-side resume, `--force-fresh`, `IStateStore.DeleteAsync`, `ICheckpointingService.DeleteCursorAsync`, `MigrationJobResume`, `PhaseTrackingService`, `JobPhaseRecord` are all implemented. `WorkItemImportOrchestrator`, `IWorkItemImportTarget`, `AzureDevOpsWorkItemImportTarget`, `RevisionFolderProcessor`, `SqliteIdMapStore`, and `WorkItemsModule.ImportAsync` are now fully implemented (see spec 013). The original spec 009 stubs (`IWorkItemTargetService`, `AzureDevOpsWorkItemTargetService`) were superseded by the richer `IWorkItemImportTarget` abstraction introduced in spec 013.

### Code (resolved by spec 013)

- 🟢 `T013` — Superseded by `IWorkItemImportTarget` in `Abstractions/Services/IWorkItemImportTarget.cs`.
- 🟢 `T014` — Superseded by `AzureDevOpsWorkItemImportTarget` in `Infrastructure.AzureDevOps/Import/`.
- 🟢 `T015` — Implemented: `WorkItemImportOrchestrator` in `Infrastructure/Import/`.
- 🟢 `T017–T020` — Implemented: Stages A–D in `RevisionFolderProcessor`.
- 🟢 `T021–T022` — Implemented: `AzureDevOpsWorkItemImportTarget` (Create/Update/Links/Attachments).
- 🟢 `T024` — Implemented: `WorkItemsModule.ImportAsync` fully wired.
- 🟢 `T025` — Implemented: `ImportServiceCollectionExtensions.AddAzureDevOpsWorkItemImport()`.

### Tests (resolved by spec 013)

- 🟢 `T012` — Feature file `features/import/work-items/revisions/import-work-item-revisions.feature` exists with resume scenarios.
- 🟢 `T016` Covered by `StreamingImportReplaySteps.cs`, `ImportCursorResumeSteps.cs`, `ImportCommentsSteps.cs`, `ImportEmbeddedImagesSteps.cs`, and `WorkItemResolutionStrategiesSteps.cs` — all exercising `WorkItemImportOrchestrator`.
- 🟢 `T023` — Covered by existing Reqnroll contexts: `StreamingImportReplayContext.cs`, `ImportCursorResumeContext.cs`, `ImportCommentsContext.cs`, `WorkItemResolutionStrategiesContext.cs`.
- 🟢 `T026` — Feature file `features/cli/execute/resume-mode.feature` created (5 scenarios: export resume, import resume, force-fresh, completed cursor, InProgress cursor).

### Docs

- 🟢 `T033` — Import orchestrator and `IWorkItemImportTarget` fully documented in `docs/work-item-iteration-guide.md` section 6 (added by spec 013).

---

## Cross-Cutting: MigrationImportCommand and MigrationMigrateCommand

`QueueCommand` handles all four modes (Export, Prepare, Import, Migrate) directly. `ExecuteMigrateAsync` implements the phase-ordering logic (export → prepare → import sequentially). Separate `MigrationImportCommand` and `MigrationMigrateCommand` classes are unnecessary — the unified `QueueCommand` pattern is the canonical approach.

---

## spec 015 — Work Item Scoped Fetch Service

> **Status**: ✅ **Complete.** All 31 tasks (T001–T031) are implemented and verified. `IWorkItemFetchService`, `AzureDevOpsWorkItemFetchService`, `TfsWorkItemFetchService`, `WorkItemFieldFilterEvaluator`, `FetchedWorkItem`, `WorkItemFetchScope`, and `WorkItemFieldFilterOptions` are all implemented. Inventory and dependency analysis callers refactored to use `IWorkItemFetchService`. All discrepancies (D-001 through D-004) resolved. Additionally, `IWorkItemQueryWindowStrategy` and `IWorkItemDiscoveryService` were aligned to accept `OrganisationEndpoint` directly, and adapter classes were eliminated.

### Additional Changes (beyond original spec scope)

- `MigrationEndpointOptions` gained an abstract `ToOrganisationEndpoint()` method — all concrete types (`AzureDevOpsEndpointOptions`, `TeamFoundationServerEndpointOptions`, `SimulatedEndpointOptions`) implement it.
- `IWorkItemQueryWindowStrategy.EnumerateWindowsAsync` changed from `MigrationEndpointOptions` to `OrganisationEndpoint`.
- `IWorkItemDiscoveryService.DiscoverWorkItemsAsync` and `CountWorkItemsAsync` changed from `MigrationEndpointOptions` to `OrganisationEndpoint`.
- Deleted: `AzureDevOpsEndpointOptionsAdapter`, `TfsMigrationEndpointOptionsAdapter`, `MigrationEndpointExtensions`.

---

## Summary Table

| Area | Not Started 🔴 | Partial 🟡 | Reconciled/N/A ⬜ | Blocking? |
|------|---------------|-----------|-------------------|-----------|
| spec 004 — CLI architecture tests | 3 (T030, T040, T041) | 0 | 2 (T029 superseded, T032 N/A), 5 implemented | No |
| spec 005 — System inventory tests (US2 + US3) | 5 (T020, T027, T028, T035, T036) | 0 | 2 superseded (T024, T026), 3 implemented | No |
| spec 006 — ADO attachment streaming | 3 tests (T028, T029, T031) | 0 | 4 reconciled, all code/docs done | No |
| spec 007 — Observability verification runs | 0 | 1 (T030 manual) | 1 code fix done, 3 tests pass | No |
| spec 008-simulated — Simulated source/target | ✅ Complete | — | — | — |
| spec 008-tui — TUI polish | ✅ Complete | — | 2 (T018/T019 N/A) | — |
| spec 009 — Import orchestrator | ✅ Complete | — | — | — |
| spec 013 — ADO Work Items Import | ✅ Complete (T001–T051) | — | — | — |
| spec 015 — Work Item Scoped Fetch | ✅ Complete (T001–T031) | — | — | — |

**Remaining real work**: 12 items total (3 in spec 004, 5 in spec 005, 3 in spec 006, 1 in spec 007). None are blocking. Specs 006 (code/docs), 007 (code fix applied), 008-simulated, 008-tui, 009, 013, and 015 are fully complete.


---

## Architecture Review — TUI Extraction (MM-M1)

Identified by architecture review (architecture-review skill, 2025):

- 🔴 **Extract TUI views into a dedicated DevOpsMigrationPlatform.TUI project** (architecture review finding MM-M1).  
  Currently, TUI views (Spectre.Console Renderables, ANSI sink, live display logic) reside in  
  DevOpsMigrationPlatform.CLI.Migration, violating the Modular Monolith boundary.  
  Resolution: create a separate project that the CLI references rather than coupling TUI code directly  
  to the CLI entry-point assembly.  
  **Priority**: Medium. No functional impact; purely a structural concern.

---

## spec 025 — Config Travels in Package (Agent Config Package)

### Code

- 🟢 T001-T020 Core implementation complete (PackageConfigStore, CLI WriteAsync, Agent ReadAsync, ModulePipelineWorkerBase, TfsJobAgentWorker, ActiveJobConfigState).
- 🟢 T026 CLI error surface for duplicate WriteAsync.
- 🟢 T027 TfsJobAgentWorker reads config from PackageConfig.
- 🟢 T035 Explicit PackageConfigNotFoundException catch in ModulePipelineWorkerBase.
- 🟢 T047 Retry with back-off (3 retries) in PackageConfigStore.ReadAsync.
- 🔴 T038/T039 EF Core upgrader for v1→v2 MigrationJob schema (complex; deferred). Tracked in tasks.md. Not blocking.
- 🔴 T048 Singleton audit for IModule classes using IOptions. Low risk; deferred.

### Tests

- 🟢 T022-T024, T033-T034, T037 PackageConfigStoreTests (11 tests passing).
- 🟢 T049 QueueCommand atomicity test (WriteAsync throws → RunAsync never called).
- 🔴 T029/T029b System test: migration-config.json exists after simulated export (SystemTest category; excluded from non-system runs).
- 🔴 T030/T031 TFS net481 path observability verification (manual verification only).

### Docs/Features

- 🟢 T010/T025/T032 Gherkin: config-applied-on-export.feature, config-audit-trail.feature, legacy-package-fail-fast.feature.
- 🟢 T040 docs/agent-hosting.md execution flow updated.
- 🟢 T042 discrepancies.md — 4 of 5 resolved; #5 (configVersion upgrader) marked partial pending T038/T039.
