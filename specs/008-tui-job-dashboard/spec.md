# Feature Specification: TUI Job Dashboard

**Feature Branch**: `008-tui-job-dashboard`  
**Created**: 2026-04-09  
**Status**: Reconciled (partially implemented; partially superseded)  
**Input**: User description: "Id like to build out the TUI. It should show a list of all the jobs. On clicking a job it should show both the metrics and the logs stream for that job in separate windows. The cli should show both the job ID and the url of the ControlPlane when it queues the job. Even in standalone mode I can connect the TUI to the ControlPlane"

## Architecture References

Documents read during spec creation:

| Document | Status |
|----------|--------|
| `docs/tui-guide.md` | Confirmed accurate — describes full desired TUI behaviour. Spec implements what is documented. |
| `docs/cli-guide.md` | Confirmed accurate — job submission output pattern described. |
| `docs/control-plane.md` | Confirmed accurate — includes `/jobs`, `/jobs/{jobId}/telemetry`, `/progress`, and `/diagnostics` surface entries used by this spec. |
| `.agents/20-guardrails/core/architecture-boundaries.md` | Guardrails 15, 16, 17, 18 apply directly to this feature. |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Job List View in the TUI (Priority: P1)

As an operator I want to open the TUI and see a live-refreshing list of all my migration jobs (state, name, submitted time) so that I can monitor everything at a glance without using CLI commands.

**Why this priority**: The job list is the entry screen of the TUI. Nothing else is reachable without it. It is the first deliverable that makes the TUI usable.

**Independent Test**: Can be tested by running `devopsmigration tui` (with or without `--url`) and verifying that a Terminal.Gui window appears with a scrollable table of jobs — even before click-through or detail views are implemented.

**Acceptance Scenarios**:

1. **Given** a control plane is reachable and the operator runs `devopsmigration tui`, **When** the TUI launches, **Then** a Terminal.Gui window displays a table of jobs visible to the authenticated operator, including job ID (short form), job state, and submission timestamp.
2. **Given** the TUI is open on the job list, **When** jobs are added or their state changes on the control plane, **Then** the job list refreshes and shows the updated states without the operator restarting the TUI.
3. **Given** no control plane is reachable, **When** the operator runs `devopsmigration tui`, **Then** the TUI exits with a clear, actionable error message identifying which URL was attempted.
4. **Given** the operator runs `devopsmigration tui` with no `--url` flag and no `MIGRATION_API_URL` set, **When** the TUI launches, **Then** the TUI attempts to connect to the default local control plane URL (`http://localhost:5100`) — it does NOT start any services. If nothing is listening there, it exits with an error advising the operator to run a migration command first or pass `--url`.

---

### User Story 2 — Job Detail View: Metrics Panel and Live Log Stream (Priority: P1)

As an operator watching a running job I want to see real-time progress metrics and a live log stream in panels beside the job list so that I can monitor job health on a single screen without navigating away.

**Why this priority**: This is the core value of the TUI — live observability of a running job on the same screen as the job list.

**Independent Test**: Can be tested by selecting a running job from the job list and verifying that the Metrics Panel (center) populates with telemetry counts and the Log Panel (right) streams live events — all within the same single-screen view, no navigation required.

**Acceptance Scenarios**:

1. **Given** the operator selects a job from the job list, **When** the row is selected, **Then** the **Metrics Panel** (center) updates to show per-module work item counts, revision counts, and throughput rates for that job; and the **Log Panel** (right) begins streaming `ProgressEvent` messages in real time — all within the same single-screen view.
2. **Given** the job is running, **When** the Migration Agent pushes new `ProgressEvent` records via the lease protocol, **Then** the Log Panel updates in real time without operator interaction (within the SSE push latency).
3. **Given** the job is running, **When** the Metrics Panel polling interval elapses (default 5 s), **Then** the metric values in the Metrics Panel refresh with the latest counts from the control plane.
4. **Given** the TUI loses its SSE connection to the log stream, **When** connectivity is restored, **Then** the TUI reconnects automatically with exponential back-off (max 30 s) and resumes the live stream from the ring buffer without operator intervention.
5. **Given** the operator deselects a job (presses Escape in the Job List panel) while the Metrics and Log panels are populated, **When** the deselection occurs, **Then** all active SSE subscriptions for the previously-selected job are cancelled immediately and no background threads remain subscribed.
6. **Given** a job in a terminal state (Completed, Failed, Cancelled), **When** the operator selects it from the job list or when the job reaches a terminal state while selected, **Then** the Log Panel appends a final status entry (e.g. `── Job Completed ──`) as the last log line, the status bar updates to show the terminal state, and no reconnection attempts are made.

---

### User Story 3 — Diagnostics Log Panel (Priority: P2)

As an operator I want to toggle the Log Panel between the progress event stream and the structured diagnostic log stream so that I can switch between operational progress and internal agent diagnostics without leaving the TUI.

**Why this priority**: High operational value; builds directly on the Log Panel SSE infrastructure from US2 (P1). Independently testable once the Log Panel exists.

**Independent Test**: Can be tested by selecting a running job, pressing Tab within the Log Panel, and verifying the panel switches to displaying `DiagnosticLogRecord` entries with level-based visual formatting from `GET /jobs/{jobId}/diagnostics?follow=true`.

**Acceptance Scenarios**:

1. **Given** a job is selected and the Log Panel is in Progress mode, **When** the operator presses Tab within the Log Panel, **Then** the panel switches to Diagnostics mode: diagnostic log records stream in real time from `GET /jobs/{jobId}/diagnostics?follow=true` with visual level indicators (Information = white, Warning = yellow, Error = red, Critical = red bold). The panel header shows `Log [Diagnostics]`; pressing Tab again returns to `Log [Progress]`.
2. **Given** the Log Panel is in Diagnostics mode, **When** the operator applies a level filter (e.g., Warning and above), **Then** only records at or above the selected level are displayed.
3. **Given** the control plane's `Diagnostics:MinimumLevel` is set to `Information`, **When** the agent emits a `Debug` record, **Then** that record does not appear in the Log Panel's Diagnostics mode (filtered at control plane floor, not TUI).

---

### User Story 4 — CLI Displays Job ID and Control Plane URL on Submit (Priority: P1)

As an operator who has just submitted a job I want to see the assigned Job ID and the control plane URL printed to the terminal so that I can copy the job ID for use with other commands (e.g., `tui --job`, `manage status --job`) and understand which control plane is managing the job.

**Why this priority**: This is a small but high-value CLI output change. It unblocks every other TUI workflow — the operator needs the job ID to jump directly to a job detail view.

**Independent Test**: Can be tested by running any migration command (`export`, `import`, `migrate`, `prepare`) and verifying that the console output includes the job ID and the resolved control plane URL after successful job submission.

**Acceptance Scenarios**:

1. **Given** the operator runs a migration command that submits a job, **When** the job is accepted by the control plane, **Then** the terminal displays a line containing the Job ID (full UUID) and the control plane URL (e.g., `http://localhost:5100`) in a format suitable for copying.
2. **Given** the operator is in standalone mode (no `--url` and no `MIGRATION_API_URL`), **When** the job is accepted, **Then** the output shows the local control plane URL (`http://localhost:5100`) alongside the job ID — confirming which endpoint received the job.
3. **Given** the operator runs with `--url https://my-control-plane.example.com`, **When** the job is accepted, **Then** the output shows that remote URL alongside the job ID.
4. **Given** submission fails (network error, validation rejection), **When** the error is reported, **Then** the control plane URL attempted is still shown so the operator knows where the request was directed.

---

### User Story 5 — Jump Directly to a Job via `tui --job` (Priority: P2)

As an operator who already knows their job ID I want to run `devopsmigration tui --job <jobId>` to open the TUI directly on that job's detail view, bypassing the job list, so that I can quickly rejoin monitoring a known job.

**Why this priority**: Documented in `docs/tui-guide.md` and directly enabled by the job ID output in User Story 4. Independently testable once the detail view exists.

**Independent Test**: Can be tested by running `devopsmigration tui --job <known-jobId>` and verifying the TUI opens directly on the detail view for that job without showing the job list first.

**Acceptance Scenarios**:

1. **Given** the operator runs `devopsmigration tui --job <jobId>`, **When** the TUI launches, **Then** the job list row for that job is pre-selected and the Metrics Panel and Log Panel are immediately populated — all on the single-screen view.
2. **Given** the operator runs `devopsmigration tui --job <jobId>` and the job does not exist or is not visible to the operator, **When** lookup fails, **Then** the TUI exits with a clear error identifying the unknown or inaccessible job ID.
3. **Given** the operator presses Escape or Q from the `--job` detail view, **When** navigation is triggered, **Then** the TUI falls back to the job list view rather than exiting.

---

### Edge Cases

- What happens when the job list is empty (no jobs visible to the caller)? The TUI shows an empty state message rather than a blank table.
- What happens when the ring buffer is full and old events are evicted? The log stream shows only buffered events; a watermark message indicates that earlier events were evicted and can be retrieved from the package log files.
- What if the operator navigates away from a detail view while SSE reconnection back-off is in progress? The back-off is cancelled immediately; no orphaned reconnection tasks remain.
- What if a job transitions to a terminal state while the detail view is open? The SSE stream receives an `event: job-ended` signal; the TUI shows a final status overlay and stops reconnection attempts.
- What if the operator runs `devopsmigration tui` in standalone mode when the local stack is already running (started by a previous CLI command)? The TUI connects to the existing control plane at `http://localhost:5100`.
- What if the operator runs `devopsmigration tui` before running any migration command? The control plane is not running; the TUI exits with an error advising the operator to run a migration command first or supply `--url`.

---

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The TUI MUST render all views using **Terminal.Gui** exclusively. `System.Console`, ANSI escape sequences, and Spectre.Console widgets MUST NOT appear in TUI view classes.
- **FR-002**: On launch without `--job`, the TUI MUST display a scrollable job list table showing visible jobs, each with job ID (short/UUID), state, mode, and submission timestamp.
- **FR-003**: The job list MUST auto-refresh at a configurable interval (default 10 s) to reflect state changes on the control plane.
- **FR-004**: Selecting a job row in the Job List MUST update the **Metrics Panel** (center) and **Log Panel** (right) in place on the same single-screen view — no navigation or new Window is pushed. Deselecting (Escape) MUST cancel all active SSE subscriptions and clear both panels.
- **FR-005**: The **Log Panel** MUST default to Progress mode, subscribing to `GET /jobs/{jobId}/progress?follow=true` (SSE) and displaying incoming `ProgressEvent` records. A Tab keypress within the panel MUST toggle to Diagnostics mode, subscribing to `GET /jobs/{jobId}/diagnostics?follow=true` instead. A mode indicator (`[Progress]` / `[Diagnostics]`) MUST be visible in the panel header. Toggling cancels the current stream and starts the new one.
- **FR-006**: The **Metrics Panel** MUST poll `GET /jobs/{jobId}/telemetry` at a configurable interval (default 5 s) and display the returned `MetricSnapshot` fields: per-module counts (work items, revisions, links, attachments), throughput rates, and revision duration means.
- **FR-007**: The **Log Panel**'s Diagnostics mode MUST display `DiagnosticLogRecord` entries with visual level indicators (Information = white, Warning = yellow, Error/Critical = red). A client-side `MinLevel` filter (default `Information`) MUST suppress records below the threshold before rendering.
- **FR-008**: The TUI MUST reconnect SSE streams automatically using exponential back-off (initial 1 s, doubling, max 30 s) on connection loss.
- **FR-009**: When the TUI process exits or navigates away from a detail view, ALL active SSE subscriptions for that view MUST be cancelled immediately via `CancellationToken` propagation.
- **FR-010**: When `--job <jobId>` is provided, the TUI MUST bypass the job list and open the detail view directly for that job.
- **FR-011**: The TUI MUST NOT start any hosted services (control plane, agents, databases). It is a pure viewer. When no `--url` is provided and `MIGRATION_API_URL` is unset, the TUI MUST attempt to connect to `http://localhost:5100`. If the connection fails, it MUST exit with an actionable error advising the operator to run a migration command first (which starts the local stack) or supply `--url`.
- **FR-012**: Every migration command that submits a job (`export`, `import`, `migrate`) MUST print the **Job ID** (full UUID) and the **resolved control plane URL** to the terminal immediately after successful submission. `prepare` validates configuration only and does NOT submit a job; it is excluded.
- **FR-013**: The CLI output for job submission MUST present the Job ID and control plane URL as clearly labelled fields, each on its own line.
- **FR-014**: The TUI MUST forward the same authentication credential as all other CLI commands. It MUST NOT prompt for credentials.
- **FR-015**: When a job reaches a terminal state while the detail view is open, the TUI MUST display a final status marker and cease all reconnection attempts for that job's SSE streams.
- **FR-016**: The `TuiCommand` settings MUST include a `--job` option (job ID string, optional) in addition to the inherited `--url` option.
- **FR-017**: The control plane MUST track job state through the full agent lifecycle: `Queued` (on submit), `Leased` (on agent pickup via `GET /agents/lease`), `Running` (on first `ProgressEvent` push), `Completed` (on `POST /agents/lease/{id}/complete`), `Failed` (on `POST /agents/lease/{id}/fail`). The TUI Job List MUST reflect the current state for each visible job.

### Key Entities

- **JobSummary**: Represents a row in the job list — job ID, state, mode, submission timestamp, submitter UPN.
- **JobSelection**: Transient state owned by `TuiMainView` — the currently-selected `JobSummary` from the Job List; drives which job's data populates the Metrics and Log panels.
- **ProgressEvent**: Existing record from `DevOpsMigrationPlatform.Abstractions` — module, stage, lastProcessed, counts, timestamp.
- **DiagnosticRecord**: Structured log record — level, timestamp, message, optional source module; streamed from `GET /jobs/{jobId}/diagnostics?follow=true`.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: An operator can launch the TUI and view the job list within 3 seconds of the control plane becoming reachable. *(Manual verification — not automated.)*
- **SC-002**: A running job's log stream updates in the TUI within 2 seconds of the Migration Agent emitting a `ProgressEvent` (end-to-end: agent → control plane SSE → TUI render). *(Manual verification — not automated.)*
- **SC-003**: The TUI reconnects automatically after an SSE drop within 30 seconds, without operator intervention.
- **SC-004**: Every job submission command outputs the Job ID and control plane URL in a single terminal write, before any progress output begins.
- **SC-005**: Navigating between the job list and job detail views (and back) 10 times produces no increase in background thread count — verified by observing no thread-pool growth. *(Manual verification — not automated.)*
- **SC-006**: The TUI exits cleanly (exit code 0) when the operator presses Q or Ctrl+Q from the job list, with no unhandled exceptions.
- **SC-007**: The Log Panel's Diagnostics mode level filter correctly excludes records below the selected threshold — verified across 100 mixed-level records.

---

## Assumptions

- The operator's terminal supports at least 80×24 characters; the layout degrades gracefully on smaller terminals.
- The **Metrics Panel** uses `GET /jobs/{jobId}/telemetry` as its data source, returning a `MetricSnapshot` with numeric counters and throughput rates. This endpoint is already implemented in `TelemetryController.cs`. It is absent only from the `docs/control-plane.md` API table, which will be updated as part of this feature (T038/T039). See `discrepancies.md`.
- The TUI always requires a reachable control plane — there is no offline mode for the TUI (guardrail #20).
- Authentication is handled upstream (`devopsmigration login` or Windows Integrated Auth); the TUI never prompts for credentials.
- "Standalone mode" refers to the local Aspire stack topology — the CLI starts the full control plane stack; the TUI only connects to it. The TUI never starts hosted services. In standalone mode, the operator starts the stack by running a migration command (e.g., `devopsmigration export`) first; the TUI then connects to `http://localhost:5100`.
- The TUI uses a **single-screen three-panel layout**: Job List (left ~30%), Metrics (center ~35%), Log (right ~35%), all always visible in one `Window`. Selecting a job updates the Metrics and Log panels in place; there is no separate detail screen. The Log Panel toggles between Progress and Diagnostics streams via Tab.
- Mobile, web-based, and multi-tenant admin views are out of scope for this feature.

## Clarifications

### Session 2026-04-09

- Q: Does the TUI start the local control plane stack when no `--url` is provided? → A: No. The TUI is a pure viewer — it never starts any hosted services. Only CLI migration commands (export, import, migrate, prepare) start the local stack via `LocalStackHost`. The TUI connects to an already-running control plane or exits with an error.

---

## Current status

Reconciled to repository truth on 2026-05-17. This spec is **partially implemented and partially superseded** by later specs and architectural changes. Core TUI wiring, control-plane job listing, telemetry endpoint docs, and CLI submission output are implemented; several test and behavior tasks remain incomplete.

## Remaining incomplete work (IDs)

`T025`, `T027`, `T030`, `T031`, `T032`, `T033`, `T034`, `T035`, `T037`, `T042`, `T043`.

## Completed because superseded (IDs + source)

- `T003`, `T009` → superseded by `specs/021.2-separation-of-concerns/tasks.md` (abstraction and path moves into `Abstractions/ControlPlaneApi` and `ControlPlane/Jobs`).
- `T017`, `T018`, `T019`, `T020` → superseded by `specs/025.1-fold-to-job/tasks.md` (`queue`/`prepare` submission contract).
- `T028` → superseded by `specs/028.1-task-bootstrap/tasks.md` and `specs/028.2-job-execution-by-task/tasks.md` (task-bootstrap/task-centric TUI behavior).

## Contradictions and reconciliation

- Original FR-012 text excludes prepare submission output, but current implementation submits prepare jobs and prints job submission output; tasks were reconciled as superseded by current command contract.
- Original TUI panel semantics (Progress/Diagnostics pair) diverged to a broader feed model (`Trace/Logs/Metrics-Feed`); reconciled by marking corresponding task work incomplete/superseded where appropriate.
- Original path assumptions (`Abstractions/Models`, `ControlPlane/Services`) diverged after boundary refactor; reconciled by mapping to current paths instead of forcing legacy structure.

## Verification evidence

- Implementation evidence: `src/DevOpsMigrationPlatform.CLI.Migration/Commands/TuiCommand.cs`, `Views/TuiMainView.cs`, `Views/TuiJobListView.cs`, `Views/TuiMetricsView.cs`, `Views/TuiLogView.cs`.
- Control plane evidence: `src/DevOpsMigrationPlatform.ControlPlane/Controllers/JobsController.cs`, `Controllers/AgentLeaseController.cs`, `Controllers/ProgressController.cs`, `Jobs/JobStore.cs`, `Jobs/IJobStore.cs`.
- CLI submission output evidence: `src/DevOpsMigrationPlatform.CLI.Migration/Commands/ControlPlaneCommandBase.cs`, `Commands/QueueCommand.cs`, `Commands/PrepareCommand.cs`, `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/PrintJobSubmittedTests.cs`.
- Docs/launch evidence: `docs/control-plane.md`, `.vscode/launch.json`.
- Validation commands run in this reconciliation: `dotnet clean && dotnet build --no-incremental` (completed), `dotnet test --no-build` (failed after clean because test assemblies were missing; marked incomplete evidence for T042).

