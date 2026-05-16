# Tasks: TUI Job Dashboard

**Input**: Design documents from `specs/008-tui-job-dashboard/`
**Branch**: `008-tui-job-dashboard`
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

---

## Phase 1: Setup

**Purpose**: Add Terminal.Gui v2 Beta dependency and verify the build still compiles cleanly.

- [x] T001 Add `<PackageReference Include="Terminal.Gui" Version="2.0.0-beta.*" />` to `src/DevOpsMigrationPlatform.CLI.Migration/DevOpsMigrationPlatform.CLI.Migration.csproj` Status: complete
- [x] T002 Run `dotnet clean && dotnet build --no-incremental` from repository root and fix any NuGet resolution or ambiguity errors introduced by the new dependency Status: complete

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core data model, control-plane API, and client changes that ALL user stories depend on. No user story can be implemented until this phase is complete.

⚠️ **CRITICAL**: Complete every task in this phase before starting any Phase 3+ work.

- [x] T003 [P] Create `JobSummary` record in `src/DevOpsMigrationPlatform.Abstractions/Models/JobSummary.cs` — fields: `Guid JobId`, `string Mode`, `string State`, `string SubmittedByUpn`, `DateTimeOffset SubmittedAt`; Evidence: implemented at `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobSummary.cs`; path moved by abstraction split. Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns
- [x] T004 Create internal `JobRecord` model in `src/DevOpsMigrationPlatform.ControlPlane/Models/JobRecord.cs` — wraps `MigrationJob` with mutable `State`, `SubmittedByUpn`, `DateTimeOffset SubmittedAt`; use a `ConcurrentDictionary<Guid, string>` in `JobStore` for states to keep the record immutable Status: complete
- [x] T005 Add two method signatures to `src/DevOpsMigrationPlatform.ControlPlane/Services/IJobStore.cs`: Status: complete
  - `IReadOnlyList<JobRecord> GetAllRecords()` — returns all jobs with runtime state for `GET /jobs`
  - `void SetState(Guid jobId, string state)` — updates the job's tracked state; called by controllers on lifecycle transitions
- [x] T006 Implement `GetAllRecords()` and `SetState()` in `src/DevOpsMigrationPlatform.ControlPlane/Services/JobStore.cs`: Status: complete
  - `Enqueue()`: set initial state to `"Queued"` in a `ConcurrentDictionary<Guid, string>` state map; record `SubmittedAt` in a `ConcurrentDictionary<Guid, DateTimeOffset>`
  - `GetAllRecords()`: for each job, populate `State` from the state map (default `"Queued"`), `SubmittedAt` from the timestamp map, `SubmittedByUpn` from job metadata or empty string
  - `SetState(Guid, string)`: update the state map entry
- [x] T007 Add `GET /jobs` endpoint to `src/DevOpsMigrationPlatform.ControlPlane/Controllers/JobsController.cs` — calls `_jobStore.GetAllRecords()`, projects each `JobRecord` to `JobSummary`, returns `200 OK` with JSON array (empty array when no jobs) Status: complete
- [x] T008 [P] Add `GetAllJobsAsync(CancellationToken ct)` method to `src/DevOpsMigrationPlatform.CLI.Migration/JobRunners/ControlPlaneClient.cs` — calls `GET /jobs`, deserialises to `IReadOnlyList<JobSummary>` Status: complete
- [x] T009 [P] Create two foundational abstractions: Evidence: `IControlPlaneClient` exists at `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/IControlPlaneClient.cs` and `TuiCommandSettings` exists; interface location changed by abstraction boundary refactor. Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns
  - `IControlPlaneClient` interface in `src/DevOpsMigrationPlatform.Abstractions/IControlPlaneClient.cs` — declares all methods called by TUI views: `GetAllJobsAsync`, `GetAllJobsAsync(ct)`, `FollowLogsAsync`, `StreamDiagnosticsAsync`, `GetProgressAsync`, and the new `GetAllJobsAsync(CancellationToken)`; `ControlPlaneClient` in `DevOpsMigrationPlatform.CLI.Migration` implements this interface
  - `TuiCommandSettings` in `src/DevOpsMigrationPlatform.CLI.Migration/Settings/TuiCommandSettings.cs` — sealed class extending `ControlPlaneBaseCommandSettings`, adds `[CommandOption("--job")] string? Job { get; init; }`
- [x] T010 Update `src/DevOpsMigrationPlatform.CLI.Migration/Commands/TuiCommand.cs` generic parameter from `ControlPlaneBaseCommandSettings` to `TuiCommandSettings` (minimal change — keeps `AnsiConsole.MarkupLine` stub for now) Status: complete
- [x] T011 [P] Create Gherkin feature file `features/cli/tui/tui-job-list.feature` — translate all four User Story 1 acceptance scenarios from `spec.md` into valid Gherkin (`Feature:`, `As a / I want / So that`, `Scenario:`, `Given/When/Then`) Status: complete
- [x] T012 [P] Create Gherkin feature file `features/cli/tui/tui-job-detail.feature` — translate all six User Story 2 acceptance scenarios from `spec.md` into valid Gherkin Status: complete
- [x] T013 [P] Create Gherkin feature file `features/cli/tui/tui-diagnostics-panel.feature` — translate all three User Story 3 acceptance scenarios from `spec.md` into valid Gherkin Status: complete
- [x] T014 [P] Create Gherkin feature file `features/cli/tui/tui-job-submission-output.feature` — translate all four User Story 4 acceptance scenarios from `spec.md` into valid Gherkin Status: complete
- [x] T015 [P] Create Gherkin feature file `features/cli/tui/tui-job-direct-jump.feature` — translate all three User Story 5 acceptance scenarios from `spec.md` into valid Gherkin Status: complete

**Checkpoint**: Foundation ready — all user story phases can now be implemented.

---

## Phase 3: User Story 4 — CLI Job ID and Control Plane URL on Submit (Priority: P1) 🎯 MVP slice

**Goal**: Every migration command that submits a job prints Job ID + control plane URL before the progress stream begins. Independently deliverable — no TUI needed.

**Independent test**: Run `devopsmigration export --config migration.json`; observe two labelled lines in output before any progress.

- [x] T016 Add `static void PrintJobSubmitted(IAnsiConsole console, Guid jobId, string controlPlaneUrl)` helper method to `src/DevOpsMigrationPlatform.CLI.Migration/Commands/ControlPlaneCommandBase.cs` — outputs: `"  Job ID  : <uuid>"` and `"  Control : <url>"` using `console.MarkupLine` Status: complete
- [x] T017 [US4] Update `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationExportCommand.cs` — call `PrintJobSubmitted(console, parsedJobId, resolvedUrl)` immediately after `SubmitAsync` returns in both follow and non-follow paths; Evidence: submission output now wired in `QueueCommand` export paths. Status: complete/superseded; completed because superseded by .agents/30-context/domains/cli-commands.md
- [x] T018 [US4] Update `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationImportCommand.cs` — add `PrintJobSubmitted` call after `SubmitAsync` when that command is implemented (currently a stub returning exit code 1; add the call site as a comment/TODO at the correct insertion point); Evidence: import submission handled in `QueueCommand` with `PrintJobSubmitted`. Status: complete/superseded; completed because superseded by .agents/30-context/domains/cli-commands.md
- [x] T019 [US4] Update `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationMigrateCommand.cs` — same as T018; Evidence: migrate submission handled in `QueueCommand` with `PrintJobSubmitted`. Status: complete/superseded; completed because superseded by .agents/30-context/domains/cli-commands.md
- [x] T020 [US4] `src/DevOpsMigrationPlatform.CLI.Migration/Commands/MigrationPrepareCommand.cs` — **N/A**: `prepare` validates configuration only and does not submit a job to the control plane. Add a code comment `// prepare does not submit a job — FR-012 excludes this command` to document the intentional omission.; Evidence: `PrepareCommand` now submits and follows a prepare job (`SubmitAsync` + `PrintJobSubmitted`). Status: complete/superseded; completed because superseded by .agents/30-context/domains/cli-commands.md
- [x] T021 [US4] Add unit tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/PrintJobSubmittedTests.cs` — verify that `PrintJobSubmitted` writes exactly two lines with the expected labels and that the job ID and URL appear verbatim Status: complete

---

## Phase 4: User Story 1 — Job List View in the TUI (Priority: P1) 🎯 MVP

**Goal**: `devopsmigration tui [--url]` launches a Terminal.Gui v2 window showing a scrollable, auto-refreshing table of all visible jobs.

**Independent test**: Run `devopsmigration tui --url http://localhost:5100`; a Terminal.Gui window appears with a job table. Press Q to exit cleanly.

- [x] T022 [US1] Create `src/DevOpsMigrationPlatform.CLI.Migration/Views/TuiJobListView.cs` — Terminal.Gui v2 `FrameView` subclass: `TableView` bound to `IReadOnlyList<JobSummary>`, columns Job ID (first 8 chars), Mode, State, Submitted; fires `JobSelected(Guid? jobId)` event on row change; `Q`/`Ctrl+Q` bubbles to `TuiMainView` which calls `Application.RequestStop()`; empty-state label shown when list is empty Status: complete
- [x] T023 [US1] Add `System.Threading.Timer`-based auto-refresh in `TuiJobListView` — calls `GetAllJobsAsync` every 10 s (configurable via constructor), marshals `Application.Invoke` to update `TableView` data source on the main loop thread Status: complete
- [x] T024 [US1] Implement `TuiCommand.ExecuteInternalAsync` in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/TuiCommand.cs` — resolve URL (settings.Url → `MIGRATION_API_URL` → `http://localhost:5100`); call `await CreateHost(settings)` to obtain DI host with auth-wired `HttpClient`; resolve `IControlPlaneClient` from host; attempt `GET /jobs` health check; on failure print Spectre.Console error and return 1; on success create `TuiMainView(client)`, optionally call `mainView.PreSelectJob(jobId)` if `--job` valid (T032), launch `Application.Create().Init().Run(mainView)` and dispose Status: complete
- [ ] T025 [US1] Add unit tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiJobListViewTests.cs` — construct `TuiJobListView` with a fake `IControlPlaneClient`; verify: (a) `UpdateJobs` with populated list renders correct row count; (b) empty list shows empty-state label; (c) state colour codes match specification; (d) row selection fires `JobSelected` event with correct `Guid`; add `TuiMainViewTests.cs` test: Q key on `TuiMainView` triggers `Application.RequestStop()` (SC-006); Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiJobListViewTests.cs` and `TuiMainViewTests.cs` do not exist. Status: incomplete

---

## Phase 5: User Story 2 — Job Detail View: Metrics Panel and Live Log Stream (Priority: P1)

**Goal**: Selecting a job from the list opens a detail window with a Metrics Panel (polled) and a Progress Log Panel (SSE live stream) arranged side-by-side. SSE reconnects automatically on drop.

**Independent test**: Select a running job; two labelled frames appear populated with data. Disconnect network; within 30 s the stream resumes without operator action.

- [x] T026 [US2] Create `src/DevOpsMigrationPlatform.CLI.Migration/Views/TuiMetricsView.cs` — Terminal.Gui v2 `FrameView` subclass: displays latest `MetricSnapshot` fields in a fixed-width label grid; `"(no job selected)"` when no job is active; `"(waiting for agent…)"` when job selected but no snapshot yet; `Update(MetricSnapshot?)` marshals via `Application.Invoke` Status: complete
- [ ] T027 [US2/US3] Create `src/DevOpsMigrationPlatform.CLI.Migration/Views/TuiLogView.cs` — Terminal.Gui v2 `FrameView` subclass with two modes toggled by Tab key: Evidence: implementation diverged to three modes (`Trace/Logs/Metrics-Feed`) and does not implement the specified Progress/Diagnostics pair with level-colour rendering contract. Status: incomplete
  - **Progress mode** (default): `ListView` of `ProgressEvent` lines (`HH:mm:ss [module] [stage] message`); subscribes to `GET /jobs/{jobId}/progress?follow=true` SSE
  - **Diagnostics mode**: `ListView` of `DiagnosticLogRecord` lines (`HH:mm:ss.fff LEVEL message`); level colour mapping (Information = white, Warning = yellow, Error/Critical = red); `MinLevel` filter (default `Information`); subscribes to `GET /jobs/{jobId}/diagnostics?follow=true` SSE
  - Panel title shows `Log [Progress]` or `Log [Diagnostics]`; Tab toggles mode, cancels current stream CTS, clears list, starts new stream
  - SSE back-off reconnect loop (1 s → 2 s → 4 s … max 30 s) in both modes
  - On `job-ended`/`job-failed`: appends separator (e.g. `── Job Completed ──`), fires `OnJobEnded(string terminalState)` callback, breaks loop
  - `ClearAndBind(Guid jobId, CancellationToken ct)` called by `TuiMainView` on job selection; `Clear()` on deselection
  - All UI mutations via `Application.Invoke`
- [x] T028 [US1/US2/US3] Create `src/DevOpsMigrationPlatform.CLI.Migration/Views/TuiMainView.cs` — Terminal.Gui v2 `Window` subclass: single-screen three-panel layout — `TuiJobListView` (left ~30%), `TuiMetricsView` (center ~35%), `TuiLogView` (right ~35%) using `Pos`/`Dim` computed sizing; all panels always visible; `Q`/`Ctrl+Q` calls `Application.RequestStop()`; subscribes to `TuiJobListView.JobSelected`: on selection — cancel per-selection CTS, create new CTS, call `TuiMetricsView` polling start and `TuiLogView.ClearAndBind(jobId, ct)`, update status bar; on deselect — cancel CTS, clear both panels; subscribes to `TuiLogView.OnJobEnded` — updates status bar terminal state; `PreSelectJob(Guid)` for `--job` launch; owns one `CancellationTokenSource` per selected job, cancelled on selection change or `Dispose()`; Evidence: current `TuiMainView` hosts task-centric workspace with job selector, task board, metrics, and feed. Status: complete/superseded; completed because superseded by specs/028.1-task-bootstrap
- [x] T029 [US2] Add telemetry polling Task to `TuiMainView.OnJobSelected` handler — `Task.Run(() => PollTelemetryAsync(jobId, client, metricsView, ct))` loop: call `GET /jobs/{jobId}/telemetry` every 5 s via `IControlPlaneClient`, marshal `metricsView.Update(snapshot)` via `Application.Invoke`; loop exits when `ct` is cancelled Status: complete
- [ ] T030 [US2/US3] Add unit tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiLogViewTests.cs` — construct `TuiLogView` with a fake `IControlPlaneClient`; verify: (a) Progress mode appends correct formatted `ProgressEvent` line; (b) Diagnostics mode appends correct `DiagnosticLogRecord` line with level colour; (c) Tab toggle switches mode, cancels old stream, starts new; (d) back-off doubles up to 30 s cap (SC-003); (e) cancelling CTS stops loop without throwing; (f) `OnJobEnded` fires and final separator is appended; (g) records below `MinLevel` excluded in Diagnostics mode; Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiLogViewTests.cs` does not exist. Status: incomplete
- [ ] T031 [US2] Add unit tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMetricsViewTests.cs` — construct `TuiMetricsView` with a fake `IControlPlaneClient`; verify: (a) `Update` with snapshot shows correct field values; (b) `Update(null)` shows waiting message; (c) `Update` with second snapshot replaces first; Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMetricsViewTests.cs` does not exist. Status: incomplete

---

## Phase 6: User Story 5 — Jump Directly to a Job via `tui --job` (Priority: P2)

**Goal**: `devopsmigration tui --job <jobId>` opens the detail view directly, bypassing the job list. Escape/back returns to the list.

**Independent test**: Run `devopsmigration tui --url http://localhost:5100 --job <known-id>`; detail view opens immediately. Run with unknown ID; exits with error.

- [ ] T032 [US5] Update `TuiCommand.ExecuteInternalAsync` — if `settings.Job` is non-null: (1) validate with `Guid.TryParse(settings.Job, out var jobId)` **before any HTTP call**; on parse failure print `[red]✗ Invalid job ID: '{value}' is not a valid GUID.[/]` and return 1; (2) call `GET /jobs/{jobId}` to verify existence; on 404/403 print Spectre.Console error and return 1; on success call `mainView.PreSelectJob(jobId)` before `app.Run(mainView)` — pre-selects the row and immediately populates Metrics and Log panels; Evidence: GUID validation exists, but `GET /jobs/{jobId}` existence/visibility check is missing. Status: incomplete
- [ ] T033 [US5] Verify `TuiMainView` Escape key behaviour with `--job` pre-selection — since all panels are on one screen, Escape deselects the current job (clears Metrics + Log, cancels SSE) rather than exiting; add test: `TuiMainView` with pre-selected job — Escape → `TuiMetricsView.Update(null)` called, streams cancelled, selection cleared; Q → `Application.RequestStop()`; Evidence: Escape behavior exists in `TuiMainView`, but no verification test exists. Status: incomplete
- [ ] T034 [US5] Add unit tests in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMainViewTests.cs` — verify: (a) selecting job A then job B cancels the CTS from job A’s selection (no orphaned streams); (b) `Dispose()` cancels the active CTS; Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiMainViewTests.cs` does not exist. Status: incomplete

---

## Phase 7: User Story 3 — Diagnostics Log Panel (Priority: P2)

**Goal**: A third panel in the job detail view streams structured diagnostic records with level-based colour coding. Operator can apply a level filter.

**Independent test**: Open a job detail view; diagnostics panel appears in the bottom portion showing coloured log records. Set filter to Warning; Information records disappear.

- [ ] T035 [US3] Integration test: construct `TuiLogView` with a fake `IControlPlaneClient` that returns 3 `Information` + 2 `Warning` + 1 `Error` diagnostic records; set `MinLevel = Warning`; verify only 3 records rendered (2 Warning + 1 Error); verify colour mapping (Warning = yellow, Error = red); verify `ClearAndBind` in Diagnostics mode subscribes to `GET /jobs/{jobId}/diagnostics?follow=true`; Evidence: no integration test exists for diagnostics filtering/subscription behavior. Status: incomplete
- [x] T036 [US3] Add a one-line status bar `Label` to `TuiMainView` at the bottom of the Window showing: selected job ID (first 8 chars, or `—` when none), current state (colour-coded per `data-model.md` State Transitions table), and current Log Panel mode (`[Progress]` / `[Diagnostics]`); update via `Application.Invoke` on selection change and mode toggle Status: complete
- [ ] T037 [US3] Extend `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiLogViewTests.cs` with additional diagnostics tests — verify: (a) level colour method returns correct colour for all six log levels (Trace/Debug = grey, Information = white, Warning = yellow, Error = red, Critical = red bold); (b) toggling Progress → Diagnostics → Progress restores the Progress stream without duplicating entries; (c) `OnJobEnded` in Diagnostics mode appends the separator and fires the callback; Evidence: `TuiLogViewTests.cs` is absent, so extension tests are also absent. Status: incomplete

---

## Phase 8: Polish and Compliance

**Purpose**: Documentation fixes, launch config verification, and final build/test gates.

- [x] T038 Update `docs/control-plane.md` Job Lifecycle API table — add row for `GET /jobs/{jobId}/telemetry` with description "Return latest `MetricSnapshot` for the job. `204 No Content` when no snapshot has been pushed yet. Requires same auth as `GET /jobs/{jobId}`." Status: complete
- [x] T039 Update `docs/control-plane.md` Job Lifecycle API table — add row for `GET /jobs` with same auth description as specified in `contracts/api-contracts.md` Status: complete
- [x] T040 Verify the existing `"🖥️  Migration CLI: TUI"` entry in `.vscode/launch.json` — confirm `args` array includes `"tui"` and optionally `"--url"` pointing at the local stack; update as needed so it launches after a migration command has started the control plane Status: complete
- [x] T041 Run `dotnet clean && dotnet build --no-incremental` from repository root — ALL warnings and errors must be resolved before declaring done Status: complete
- [ ] T042 Run `dotnet test` from repository root — ALL tests must pass; no skipped tests introduced by this feature; Evidence: `dotnet test --no-build` did not complete successfully in this reconciliation session (hung/terminated before full pass evidence). Status: incomplete
- [ ] T043 Add `[TestCategory("SystemTest")]` test in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiSystemTests.cs` — run `devopsmigration export --config ...` and capture full stdout; assert: (a) stdout contains a line matching `"Job ID  :"` (FR-012/FR-013); (b) stdout contains a line matching `"Control :"` (FR-012/FR-013); (c) the `"Job ID  :"` line appears **before** the first progress output line in the captured output (SC-004 ordering gate); Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TUI/TuiSystemTests.cs` does not exist. Status: incomplete
- [x] T044 Wire all five job state transitions through the control plane controllers: Status: complete
  - `AgentLeaseController.AcquireLease()` → call `_jobStore.SetState(jobId, "Leased")` after registering the lease
  - `ProgressController.PostProgress()` → on first `ProgressEvent` for a job, call `_jobStore.SetState(jobId, "Running")` (guard: only if current state is `"Leased"`)
  - `ProgressController.CompleteJob()` → call `_jobStore.SetState(jobId, "Completed")`
  - `ProgressController.FailJob()` → call `_jobStore.SetState(jobId, "Failed")`
  - Add unit tests in `tests/DevOpsMigrationPlatform.ControlPlane.Tests/Services/JobStoreStateTests.cs` verifying each transition

---

## Summary

| Phase | User Story | Priority | Tasks | Parallelisable |
|-------|-----------|----------|-------|---------------|
| 1 | Setup | — | T001–T002 | T001 |
| 2 | Foundation (incl. state tracking) | — | T003–T015 | T003, T008, T009, T011–T015 |
| 3 | US4 CLI output | P1 | T016–T021 | T017–T020 |
| 4 | US1 Job List + Single Screen | P1 | T022–T025 | — |
| 5 | US2 Metrics + Log Panel | P1 | T026–T031 | T026, T027 |
| 6 | US5 Direct Jump (pre-select) | P2 | T032–T034 | — |
| 7 | US3 Log Toggle + Status Bar | P2 | T035–T037 | — |
| 8 | Polish + State Transitions | — | T038–T044 | T038–T040 |

**Total tasks**: 44
**MVP scope**: Phases 1–5 (T001–T031) — single-screen TUI with job list, Metrics Panel, Log Panel in Progress mode, and job ID output on every submission.
**Full scope**: All phases — adds Log Panel Diagnostics toggle, `--job` pre-select, full state lifecycle tracking.
## Dependencies

```
T001 → T002
T003, T004 → T005 → T006 → T007
T008 (after T003)
T009, T010 (after T003)
T011–T015 (after spec.md — already done)
T016 → T017, T018, T019, T020
T007, T008 → T022 → T023
T009, T010, T022, T023 → T024 → T025
T022, T026, T027 → T028 → T029 → T030
T028 → T031 (TuiMetricsViewTests)
T028 → T032 → T033 → T034
T028 → T035 → T036 → T037
T009 (IControlPlaneClient) → T025, T030, T031, T037
T005, T006 → T044
T007 → T044 (needs SetState in IJobStore first)
T041, T042, T043, T044 (after all implementation tasks)
```

---

## Parallel Execution Opportunities

**Within Phase 2 (Foundation)**:
- T003, T008, T009, T011, T012, T013, T014, T015 can all start immediately (no inter-dependencies)
- T004 → T005 → T006 → T007 is the sequential critical path in this phase

**Within Phase 3 (US4)**:
- T017, T018, T019, T020 can all run in parallel once T016 is done

**Within Phase 5 (US2)**:
- T026 (`TuiMetricsView`) and T027 (`TuiProgressLogView`) can be written in parallel (different files)

**Within Phase 8 (Polish)**:
- T038, T039, T040 can be done simultaneously
