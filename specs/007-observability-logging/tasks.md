# Tasks: Three-Channel Observability

**Input**: Design documents from `/specs/007-observability-logging/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/api-contracts.md

**Tests**: Unit test tasks are not included. Gherkin feature files (ATDD Phase 1 artifacts) are mandatory per user story. Unit/integration tests should be written during ATDD sessions.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Multi-project solution**: `src/<ProjectName>/` at repository root
- All interfaces and DTOs in `src/DevOpsMigrationPlatform.Abstractions/`
- Infrastructure implementations in `src/DevOpsMigrationPlatform.Infrastructure/`
- Control plane API in `src/DevOpsMigrationPlatform.ControlPlane/`
- Migration agent in `src/DevOpsMigrationPlatform.MigrationAgent/`
- CLI in `src/DevOpsMigrationPlatform.CLI.Migration/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Verify baseline and confirm no pre-existing build errors

- [x] T001 Verify solution builds clean with `dotnet clean && dotnet build --no-incremental` before starting any work - Status: complete

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core abstractions and storage extensions that ALL user stories depend on

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T002 Add `AppendAsync(string relativePath, string content, CancellationToken cancellationToken)` method to `IArtefactStore` interface in `src/DevOpsMigrationPlatform.Abstractions/Storage/IArtefactStore.cs` - Status: complete
- [x] T003 [P] Implement `AppendAsync` in `FileSystemArtefactStore` using `File.AppendAllTextAsync` with parent directory creation in `src/DevOpsMigrationPlatform.Infrastructure/Storage/FileSystemArtefactStore.cs` - Status: complete
- [x] T004 [P] Implement `AppendAsync` in `AzureBlobArtefactStore` using `AppendBlobClient.AppendBlockAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Storage/AzureBlobArtefactStore.cs` (create file if store does not yet exist; stub with `NotImplementedException` if Azure Blob infrastructure is not yet in place) - Status: complete/superseded; completed because superseded by specs/034-package-manager-adoption
  Evidence: AzureBlobArtefactStore implementation path no longer exists; runtime package logging routes through IPackageAccess.
- [x] T005 [P] Create `DiagnosticLogRecord` immutable record type with `Timestamp`, `Level`, `Category`, `Message`, `Exception?`, `TraceId?`, `SpanId?` in `src/DevOpsMigrationPlatform.Abstractions/Models/DiagnosticLogRecord.cs` - Status: complete
- [x] T006 [P] Create sealed `DiagnosticLogOptions` class with `SectionName`, `MinimumLevel` (default `"Warning"`), `ChannelCapacity` (1024), `FlushIntervalMs` (500), `FlushBatchSize` (50) in `src/DevOpsMigrationPlatform.Abstractions/Diagnostics/DiagnosticLogOptions.cs` - Status: complete/superseded; completed because superseded by spec.md FR-003 (default Information level)
  Evidence: DiagnosticLogOptions default is Information in src/DevOpsMigrationPlatform.Abstractions/Diagnostics/DiagnosticLogOptions.cs.

**Checkpoint**: Foundation ready — `IArtefactStore.AppendAsync` is available, `DiagnosticLogRecord` and `DiagnosticLogOptions` types exist. User story implementation can now begin.

---

## Phase 3: User Story 2 — Complete the PackageProgressSink (Priority: P1) 🎯 MVP

**Goal**: Persist `ProgressEvent` records to `Logs/progress.jsonl` in the package so the package is a complete audit trail.

**Independent Test**: Run an export to completion. Verify `Logs/progress.jsonl` exists in the package and contains one NDJSON line per `ProgressEvent` emitted during the export.

### Gherkin Feature File for User Story 2 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 2 acceptance scenarios and committed before any step definitions or production code are written.**

- [x] T007 [US2] Create `features/platform/observability/package-progress-sink.feature` — translate `spec.md` User Story 2 acceptance scenarios (FR-006, FR-007, FR-008) into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) - Status: complete

### Implementation for User Story 2

- [x] T008 [US2] Implement `PackageProgressSink` bounded-channel drain loop: `BoundedChannel<ProgressEvent>` (capacity 100, `DropOldest`), `BackgroundService` drain batching up to 50 records or 500ms, serialize each `ProgressEvent` as JSON + `\n`, call `IArtefactStore.AppendAsync("Logs/progress.jsonl", batch, ct)`, catch/count failures in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/PackageProgressSink.cs` - Status: complete/superseded; completed because superseded by specs/033-runtime-state-categories and specs/034-package-manager-adoption
  Evidence: PackageProgressSink writes run-scoped .migration/runs/<runId>/logs/progress.ndjson via IPackageAccess.AppendLogAsync.
- [x] T009 [US2] Register `PackageProgressSink` as `IProgressSink` in agent DI and as `IHostedService` for the background drain in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs` - Status: complete/superseded; completed because superseded by core wiring consolidation in CoreAgentServiceExtensions
  Evidence: DI registration is centralized in src/DevOpsMigrationPlatform.Infrastructure.Agent/CoreAgentServiceExtensions.cs.
- [ ] T010 [US2] Verify end-to-end: run an export, confirm `Logs/progress.jsonl` exists in package output with at least one NDJSON record per module stage transition - Status: incomplete
  Evidence: No fresh scenario evidence in this session proving end-to-end persisted progress log output.

**Checkpoint**: At this point, User Story 2 should be fully functional — every export produces `Logs/progress.jsonl` in the package.

---

## Phase 4: User Story 1 — Operator Diagnoses a Failed Migration (Priority: P1)

**Goal**: Persist `ILogger` diagnostic output to `Logs/agent.jsonl` in the package so operators can self-serve troubleshooting from the package alone.

**Independent Test**: Run an export against a project with a known inaccessible attachment. After the job fails, inspect `Logs/agent.jsonl` — it must contain structured Warning/Error records identifying the problem.

### Gherkin Feature File for User Story 1 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 1 acceptance scenarios and committed before any step definitions or production code are written.**

- [x] T011 [US1] Create `features/platform/observability/package-diagnostics-sink.feature` — translate `spec.md` User Story 1 acceptance scenarios (FR-001 through FR-005) into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) - Status: complete

### Implementation for User Story 1

- [x] T012 [US1] Implement `PackageLoggerProvider` (`ILoggerProvider`) and inner `PackageLogger` (`ILogger`) in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/PackageLoggerProvider.cs`: maps `LogLevel`, category, formatted message, exception, `Activity.Current` trace/span to `DiagnosticLogRecord`; writes to `BoundedChannel<DiagnosticLogRecord>` (capacity from `DiagnosticLogOptions.ChannelCapacity`, `DropOldest`); background drain flushes batches to `IArtefactStore.AppendAsync("Logs/agent.jsonl", ...)`; respects `DiagnosticLogOptions.MinimumLevel` - Status: complete/superseded; completed because superseded by specs/033-runtime-state-categories and specs/034-package-manager-adoption
  Evidence: PackageLoggerProvider persists run-scoped diagnostics.ndjson via IPackageAccess, not Logs/agent.jsonl via IArtefactStore directly.
- [x] T013 [US1] Create `DiagnosticsServiceExtensions` with `AddDiagnosticsServices(this IServiceCollection)` to register `DiagnosticLogOptions` from configuration, `PackageLoggerProvider`, and hosted drain service in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/DiagnosticsServiceExtensions.cs` - Status: complete
- [x] T014 [US1] Wire `PackageLoggerProvider` into agent by calling `AddDiagnosticsServices()` from `MigrationAgentServiceExtensions` and adding provider via `builder.Logging.AddProvider()` in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs` - Status: complete/superseded; completed because superseded by core wiring consolidation in CoreAgentServiceExtensions
  Evidence: Provider wiring is done through AddDiagnosticsServices in CoreAgentServiceExtensions.
- [ ] T015 [US1] Verify end-to-end: run an export, confirm `Logs/agent.jsonl` exists in package output with structured NDJSON records at Warning+ level - Status: incomplete
  Evidence: No fresh run evidence in this session confirming expected diagnostics file output for the current contract.

**Checkpoint**: At this point, User Stories 1 AND 2 should both work — every export produces both `Logs/progress.jsonl` and `Logs/agent.jsonl` in the package.

---

## Phase 5: User Story 6 — Operator Controls Log Level and Follow Mode on Export (Priority: P1)

**Goal**: The `export` command accepts `--level` to control diagnostic verbosity per job and `--follow` to stream diagnostics inline. Tiered log levels ensure the agent writes full detail to the package while the control plane filters at its own deployment level.

**Independent Test**: Run `devopsmigration export --config migration.json --level Debug --follow`. Verify the agent writes Debug+ records to `agent.jsonl`, diagnostics stream to the console, and the CLI exits on job completion.

### Gherkin Feature Files for User Story 6 (mandatory)

> **NOTE: These `.feature` files are the ATDD Phase 1 artifacts. They must be written from the `spec.md` User Story 6 acceptance scenarios and committed before any step definitions or production code are written.**

- [x] T016 [US6] Create `features/cli/export/export-follow-and-level.feature` — translate `spec.md` User Story 6 acceptance scenarios (FR-021 through FR-027) into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) - Status: complete
- [x] T017 [P] [US6] Create `features/platform/observability/tiered-log-levels.feature` — translate FR-028, FR-029, FR-030 (tiered log level architecture) into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) - Status: complete

### Implementation for User Story 6 — Agent Side

- [x] T018 [US6] Implement `ControlPlaneLoggerProvider` (`ILoggerProvider`) and inner `ControlPlaneLogger` in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/ControlPlaneLoggerProvider.cs`: same bounded-channel pattern as `PackageLoggerProvider`; drain loop POSTs batches to `POST /agents/lease/{leaseId}/diagnostics`; failures caught, counted, logged at Debug; respects `DiagnosticLogOptions.MinimumLevel` - Status: complete
- [x] T019 [US6] Register `ControlPlaneLoggerProvider` in `DiagnosticsServiceExtensions.AddDiagnosticsServices()` in `src/DevOpsMigrationPlatform.Infrastructure/Telemetry/DiagnosticsServiceExtensions.cs` - Status: complete
- [x] T020 [US6] Wire `ControlPlaneLoggerProvider` into agent via `builder.Logging.AddProvider()` in `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs` - Status: complete/superseded; completed because superseded by core wiring consolidation in CoreAgentServiceExtensions
  Evidence: ControlPlaneLoggerProvider is registered through shared core agent wiring.

### Implementation for User Story 6 — Control Plane Side

- [x] T021 [P] [US6] Create sealed `DiagnosticLogStoreOptions` with `SectionName`, `Capacity` (1000), `MinimumLevel` (`"Warning"`) in `src/DevOpsMigrationPlatform.ControlPlane/Services/DiagnosticLogStoreOptions.cs` - Status: complete/superseded; completed because superseded by implemented DiagnosticLogStoreOptions contract
  Evidence: Current options use section DiagnosticLog and default Information in src/DevOpsMigrationPlatform.ControlPlane/Jobs/DiagnosticLogStoreOptions.cs.
- [x] T022 [US6] Create `DiagnosticLogStore` mirroring `JobProgressStore` pattern: `ConcurrentDictionary<Guid, JobEntry>` with `ConcurrentQueue<DiagnosticLogRecord>` ring buffer (bounded by `DiagnosticLogStoreOptions.Capacity`), `List<ChannelWriter<DiagnosticLogRecord>>` SSE subscribers, `Add()` filters by CP deployment-level minimum, `GetSnapshot()` with level filter, `Subscribe()` returns `ChannelReader` in `src/DevOpsMigrationPlatform.ControlPlane/Services/DiagnosticLogStore.cs` - Status: complete
- [x] T023 [US6] Create `DiagnosticsController` with `POST /agents/lease/{leaseId}/diagnostics` (accept batch, validate lease, call `DiagnosticLogStore.Add()`), `GET /jobs/{jobId}/diagnostics` (snapshot with optional `?level=` filter), `GET /jobs/{jobId}/diagnostics?follow=true` (SSE stream with optional `?level=` filter) in `src/DevOpsMigrationPlatform.ControlPlane/Controllers/DiagnosticsController.cs` - Status: complete
- [x] T024 [US6] Register `DiagnosticLogStore` and `DiagnosticLogStoreOptions` in control plane DI in the appropriate `Program.cs` or service extensions file - Status: complete

### Implementation for User Story 6 — Job Definition

- [x] T025 [US6] Extend job definition/contract with optional `diagnostics.minimumLevel` field (additive, defaults to `"Warning"`) — update model in `src/DevOpsMigrationPlatform.Abstractions/` and ensure agent reads it to configure `DiagnosticLogOptions.MinimumLevel` - Status: complete/superseded; completed because superseded by implemented JobDiagnostics default Information
  Evidence: JobDiagnostics.MinimumLevel defaults to Information in src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobDiagnostics.cs.

### Implementation for User Story 6 — CLI Side

- [x] T026 [P] [US6] Add `Follow` (bool, default `false`) and `Level` (string, default `"Information"`, validated against `Trace|Debug|Information|Warning|Error|Critical`) properties to `ExportCommandSettings` in `src/DevOpsMigrationPlatform.CLI.Migration/Settings/ExportCommandSettings.cs` - Status: complete/superseded; completed because superseded by specs/028.1-task-bootstrap and specs/028.2-job-execution-by-task
  Evidence: QueueCommandSettings now owns --follow and --level; ExportCommandSettings file does not exist.
- [x] T027 [US6] Add `StreamDiagnosticsAsync(Guid jobId, LogLevel? level, CancellationToken ct)` returning `IAsyncEnumerable<DiagnosticLogRecord>` to `ControlPlaneClient` for SSE consumption in `src/DevOpsMigrationPlatform.CLI.Migration/JobRunners/ControlPlaneClient.cs` - Status: complete
- [x] T028 [US6] Update `MigrationExportCommand` to pass `--level` to job definition, implement `--follow` lifecycle: stream diagnostics SSE to console, print summary on job terminal state, detach on Ctrl+C with "Job continues. Use TUI to watch." message in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs` - Status: complete/superseded; completed because superseded by queue command model from specs/028.1-task-bootstrap and specs/028.2-job-execution-by-task
  Evidence: QueueCommand implements follow/level lifecycle; MigrationExportCommand path is obsolete.
- [x] T029 [US6] Implement standalone mode behaviour: when no `--url`, `--follow` is implicit, locally-started CP's `DiagnosticLogStoreOptions.MinimumLevel` set to operator's `--level` value in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs` - Status: complete/superseded; completed because superseded by queue command model from specs/028.1-task-bootstrap and specs/028.2-job-execution-by-task
  Evidence: Standalone implicit follow behavior is implemented in QueueCommand.
- [ ] T030 [US6] Verify end-to-end: run `export --level Debug --follow`, confirm Debug records in `agent.jsonl`, diagnostics stream to console, CLI exits on completion - Status: incomplete
  Evidence: No fresh end-to-end execution evidence captured in this reconciliation session for the asserted follow/debug behavior.

**Checkpoint**: At this point, User Stories 1, 2, AND 6 should all work — exports produce both log files, operators can control verbosity, and `--follow` streams diagnostics inline.

---

## Phase 6: User Story 4 — Rename Endpoints and CLI Commands (Priority: P2)

**Goal**: Eliminate naming confusion by renaming `/logs` → `/progress` for progress events and `manage logs` → `manage diagnostics` / `manage progress` for CLI commands.

**Independent Test**: After renaming, `GET /jobs/{id}/progress` returns `ProgressEvent` records. `manage progress` returns a snapshot. `manage diagnostics` downloads from the package. No endpoint or command uses the word "logs" for progress events.

### Gherkin Feature File for User Story 4 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 4 acceptance scenarios and committed before any step definitions or production code are written.**

- [x] T031 [US4] Create `features/platform/observability/endpoint-rename.feature` — translate `spec.md` User Story 4 acceptance scenarios (FR-013, FR-014, FR-015) into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) - Status: complete

### Implementation for User Story 4

- [x] T032 [US4] Rename `GET /jobs/{jobId}/logs` route to `GET /jobs/{jobId}/progress` (and `?follow=true` variant) in `ProgressController` in `src/DevOpsMigrationPlatform.ControlPlane/Controllers/` (find existing controller and update route attributes) - Status: complete
- [ ] T033 [P] [US4] Rename `ManageLogsCommand` to `ManageDiagnosticsCommand` and update to download diagnostic logs from completed job package (no `--follow`) in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/ManageDiagnosticsCommand.cs` (rename existing file) - Status: incomplete
  Evidence: ManageDiagnosticsCommand currently prints guidance text and does not download package diagnostics content.
- [x] T034 [P] [US4] Create `ManageDiagnosticsCommandSettings` with `--job` (required) and `--level` (optional, client-side filter) in `src/DevOpsMigrationPlatform.CLI.Migration/Settings/ManageDiagnosticsCommandSettings.cs` - Status: complete/superseded; completed because superseded by inline command settings pattern
  Evidence: ManageDiagnostics settings are implemented as nested Settings class in ManageDiagnosticsCommand.
- [x] T035 [P] [US4] Create `ManageProgressCommand` with snapshot-only behaviour (no `--follow`) calling `GET /jobs/{jobId}/progress` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/ManageProgressCommand.cs` - Status: complete
- [x] T036 [P] [US4] Create `ManageProgressCommandSettings` with `--job` (required) in `src/DevOpsMigrationPlatform.CLI.Migration/Settings/ManageProgressCommandSettings.cs` - Status: complete/superseded; completed because superseded by inline command settings pattern
  Evidence: ManageProgress settings are implemented as nested Settings class in ManageProgressCommand.
- [ ] T037 [US4] Update `Program.cs` command registration: replace `manage logs` with `manage diagnostics` and add `manage progress` in `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs` - Status: incomplete
  Evidence: Program.cs still registers deprecated manage logs command, so replacement is not complete.
- [x] T038 [US4] Update `ControlPlaneClient` to use `/progress` path instead of `/logs` for progress event methods in `src/DevOpsMigrationPlatform.CLI.Migration/JobRunners/ControlPlaneClient.cs` - Status: complete
- [x] T039 [P] [US4] Rename `ILogsClient` interface to `IProgressClient` (if it exists) in `src/DevOpsMigrationPlatform.CLI.Migration/JobRunners/` - Status: complete/superseded; completed because superseded by current ControlPlaneClient progress API surface
  Evidence: No ILogsClient interface remains; client methods use /progress and FollowLogsAsync semantics.

**Checkpoint**: At this point, all API endpoints and CLI commands use correct naming — progress for `ProgressEvent`, diagnostics for `DiagnosticLogRecord`.

---

## Phase 7: User Story 5 — Download Package Logs via Control Plane API (Priority: P2)

**Goal**: Operators without direct filesystem access can download `progress.jsonl` and `agent.jsonl` from the package via the control plane API.

**Independent Test**: Submit an export job. After completion, call `GET /jobs/{id}/logs/download?type=diagnostics` — receives `Logs/agent.jsonl` contents as `application/x-ndjson`. `manage diagnostics --job <id>` outputs the same.

### Gherkin Feature File for User Story 5 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 5 acceptance scenarios and committed before any step definitions or production code are written.**

- [x] T040 [US5] Create `features/platform/observability/log-download.feature` — translate `spec.md` User Story 5 acceptance scenarios (FR-016, FR-017) into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) - Status: complete

### Implementation for User Story 5

- [ ] T041 [US5] Create `LogDownloadController` with `GET /jobs/{jobId}/logs/download?type=progress|diagnostics` endpoint: resolve job's `packageUri`, read `Logs/progress.jsonl` or `Logs/agent.jsonl` via `IArtefactStore`, return with `Content-Type: application/x-ndjson` in `src/DevOpsMigrationPlatform.ControlPlane/Controllers/LogDownloadController.cs` - Status: incomplete
  Evidence: No LogDownloadController or /jobs/{jobId}/logs/download endpoint exists in current ControlPlane controllers.
- [ ] T042 [US5] Add `DownloadDiagnosticsAsync(Guid jobId)` and `DownloadProgressAsync(Guid jobId)` methods to `ControlPlaneClient` in `src/DevOpsMigrationPlatform.CLI.Migration/JobRunners/ControlPlaneClient.cs` - Status: incomplete
  Evidence: ControlPlaneClient has no DownloadDiagnosticsAsync/DownloadProgressAsync methods.
- [ ] T043 [US5] Implement download logic in `ManageDiagnosticsCommand`: call `DownloadDiagnosticsAsync`, parse NDJSON, filter by `--level` client-side, output to stdout in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/ManageDiagnosticsCommand.cs` - Status: incomplete
  Evidence: ManageDiagnosticsCommand does not parse/download NDJSON or apply level filtering to downloaded records.

**Checkpoint**: At this point, `manage diagnostics` and `manage progress` are fully functional — snapshot from ring buffer and download from package both work.

---

## Phase 8: User Story 3 — Operator Watches Live Diagnostics in the TUI (Priority: P2)

**Goal**: The TUI displays a diagnostics panel that streams log records alongside the existing metrics and progress panels.

**Independent Test**: Start an export with the TUI connected. Trigger a warning condition. The TUI diagnostics panel displays the warning within 5 seconds.

### Gherkin Feature File for User Story 3 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 3 acceptance scenarios and committed before any step definitions or production code are written.**

- [x] T044 [US3] Create `features/platform/observability/diagnostics-streaming.feature` — translate `spec.md` User Story 3 acceptance scenarios (FR-018, FR-019, FR-020) into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) - Status: complete

### Implementation for User Story 3

- [x] T045 [US3] Implement `DiagnosticsPanel` as a `Terminal.Gui` `View` subclass: subscribes to `GET /jobs/{jobId}/diagnostics?follow=true&level=Warning`, renders log records in a scrolling list with level-based coloring, supports level filter toggle (Warning ↔ Information) in `src/DevOpsMigrationPlatform.CLI.Migration/Views/DiagnosticsPanel.cs` - Status: complete/superseded; completed because superseded by TuiLogView diagnostics stream integration
  Evidence: Diagnostics streaming is integrated in TuiLogView via StreamDiagnosticsAsync; standalone DiagnosticsPanel is Spectre-based and not the active path.
- [x] T046 [US3] Integrate `DiagnosticsPanel` into existing TUI layout alongside metrics panel and progress table without replacing either in the TUI's main view composition - Status: complete/superseded; completed because superseded by TuiLogView diagnostics stream integration
  Evidence: TUI main feed uses TuiLogView logs mode to render diagnostics alongside progress/metrics.

**Checkpoint**: At this point, ALL user stories are implemented — full three-channel observability is operational.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Launch profiles, SystemTest coverage, build verification, scenario validation

- [x] T047 Add `manage diagnostics` debug profile (`manage diagnostics --job <test-job-id> --level Warning`) to `.vscode/launch.json` - Status: complete
- [x] T048 [P] Add `manage progress` debug profile (`manage progress --job <test-job-id>`) to `.vscode/launch.json` - Status: complete
- [x] T049 [P] Update existing `export` debug profile with `--follow` and `--level Warning` options in `.vscode/launch.json` - Status: complete
- [x] T050 [P] Create `[TestCategory("SystemTest")]` test for `export --follow --level` that runs an export with `--follow` and `--level Debug`, asserts diagnostic output streams to console, and verifies `Logs/agent.jsonl` contains Debug+ records in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/ExportCommandFollowTests.cs` - Status: complete/superseded; completed because superseded by queue command model from specs/028.1-task-bootstrap and specs/028.2-job-execution-by-task
  Evidence: ExportCommandFollowTests covers queue --follow/--level behavior instead of legacy export command.
- [ ] T051 [P] Create `[TestCategory("SystemTest")]` test for `manage diagnostics` that submits a job, then runs `manage diagnostics --job <id>`, and asserts NDJSON output is returned in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/ManageDiagnosticsCommandTests.cs` - Status: incomplete
  Evidence: ManageDiagnosticsCommandTests only assert generic output and do not verify NDJSON download behavior.
- [x] T052 [P] Create `[TestCategory("SystemTest")]` test for `manage progress` that submits a job, then runs `manage progress --job <id>`, and asserts ProgressEvent records are returned in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/ManageProgressCommandTests.cs` - Status: complete
- [x] T053 Run `dotnet clean && dotnet build --no-incremental` — MUST pass with zero errors - Status: complete
- [ ] T054 Run `dotnet test` — ALL tests MUST pass - Status: incomplete
  Evidence: dotnet test --no-build did not complete in this session (stalled and was stopped).
- [ ] T055 Run scenario config `scenarios/export-ado-workitems-single-project.json` via `.vscode/launch.json` debug profile and verify observable output (both `Logs/progress.jsonl` and `Logs/agent.jsonl` produced) - Status: incomplete
  Evidence: Referenced scenario path scenarios/export-ado-workitems-single-project.json no longer exists and no replacement run evidence was captured.

---

## Dependencies

### User Story Completion Order

```
Phase 2 (Foundational) ──→ Phase 3 (US2) ──→ Phase 4 (US1) ──→ Phase 5 (US6)
                                                                       │
                                                                       ├──→ Phase 6 (US4)
                                                                       │
                                                                       ├──→ Phase 7 (US5) ── depends on US4 for manage diagnostics
                                                                       │
                                                                       └──→ Phase 8 (US3) ── depends on US6 DiagnosticsController SSE
```

### Key Dependencies

| Task | Depends On | Reason |
|------|-----------|--------|
| T003, T004 | T002 | `AppendAsync` interface must exist before implementations |
| T008 | T002, T003 | `PackageProgressSink` calls `IArtefactStore.AppendAsync` |
| T012 | T005, T006 | `PackageLoggerProvider` uses `DiagnosticLogRecord` and `DiagnosticLogOptions` |
| T014 | T012, T013 | Agent wiring depends on provider and extension method existing |
| T018 | T012 | `ControlPlaneLoggerProvider` follows same pattern as `PackageLoggerProvider` |
| T022 | T021 | `DiagnosticLogStore` uses `DiagnosticLogStoreOptions` |
| T023 | T022 | `DiagnosticsController` depends on `DiagnosticLogStore` |
| T027 | T023 | `ControlPlaneClient.StreamDiagnosticsAsync` targets `DiagnosticsController` SSE |
| T028 | T026, T027 | Export command uses settings and client SSE method |
| T032 | — | Rename can start independently (controller already exists) |
| T037 | T034 | Program.cs registration depends on commands existing |
| T041 | T023 | `LogDownloadController` depends on `DiagnosticsController` pattern |
| T043 | T042, T033 | `ManageDiagnosticsCommand` download logic depends on client method and command existing |
| T045 | T023 | `DiagnosticsPanel` subscribes to `DiagnosticsController` SSE endpoint |

### Parallel Execution Opportunities

**Within Phase 2** (after T002):
- T003, T004, T005, T006 — all modify different files, no interdependencies

**Within Phase 5**:
- T016 + T017 — Gherkin feature files, independent
- T021 (CP options) can start parallel with T018 (agent provider) — different projects
- T026 (CLI settings) can start parallel with T018-T024 — different project

**Within Phase 6**:
- T033, T034, T035, T036 — all create new files in different directories
- T039 — independent rename

**Within Phase 9**:
- T047, T048, T049 — all modify the same file but independent sections

---

## Implementation Strategy

### MVP Scope (Phases 1–3: US2 only)
- **What ships**: `PackageProgressSink` completes the existing stub. Every export produces `Logs/progress.jsonl`.
- **Value**: Closes the gap in the documented package contract. Zero new abstractions beyond `AppendAsync`.

### First Increment (Phases 1–5: US2 + US1 + US6)
- **What ships**: Full P1 scope — both package log files, `export --follow`, `export --level`, tiered log levels, CP diagnostics infrastructure.
- **Value**: Operators can self-serve troubleshooting from the package and watch exports live.

### Full Feature (All Phases)
- **What ships**: Complete three-channel observability — package persistence, live TUI streaming, renamed APIs, download endpoints, full CLI surface.
- **Value**: Production-ready observability for all operator personas.

### Task Count Summary

| Phase | Story | Tasks | Parallel Opportunities |
|-------|-------|-------|----------------------|
| 1 — Setup | — | 1 | — |
| 2 — Foundational | — | 5 | 4 tasks parallelizable after T002 |
| 3 — US2 (P1) 🎯 | PackageProgressSink | 4 | — |
| 4 — US1 (P1) | Diagnostics Persistence | 5 | — |
| 5 — US6 (P1) | Export --follow/--level | 15 | 4 tasks parallelizable |
| 6 — US4 (P2) | Endpoint/CLI Rename | 9 | 5 tasks parallelizable |
| 7 — US5 (P2) | Package Log Download | 4 | — |
| 8 — US3 (P2) | TUI Diagnostics Panel | 3 | — |
| 9 — Polish | — | 9 | 6 tasks parallelizable |
| **Total** | | **55** | **19 parallelizable** |

