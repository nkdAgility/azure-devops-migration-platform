# Feature Specification: OpenTelemetry Observability — CLI DI and Phase 2 Live Progress Streaming

**Feature Branch**: `002-otel-observability-phase2`  
**Created**: 2026-04-03  
**Status**: Draft  

## Architecture References

This spec is grounded in and extends the following canonical documents. Where this spec adds new detail or behaviour, those docs have been updated to match.

| Document | Sections relevant to this feature |
|---|---|
| [docs/tui.md](../../docs/tui.md) | IProgressSink implementations; Status Display (SSE vs polling); ProgressEvent schema; TUI Disconnection |
| [docs/control-plane.md](../../docs/control-plane.md) | API surface — `GET /jobs/{jobId}/logs`, `GET /jobs/{jobId}/logs?follow=true`, `POST /agents/lease/{leaseId}/progress`; Progress Reporting; ring buffer |
| [docs/migration-agent.md](../../docs/migration-agent.md) | Responsibilities — Report progress; Execution Flow — IProgressSink composite registration |
| [docs/cli.md](../../docs/cli.md) | `migrate logs` command (`--follow`, NDJSON); CLI Observability section |
| [docs/architecture.md](../../docs/architecture.md) | Progress is Event-Driven; Phase 2 implementation list |
| [docs/aspire-integration.md](../../docs/aspire-integration.md) | ServiceDefaults — OTel configuration shared by Control Plane and Migration Agent |

---

## Clarifications

### Session 2026-04-03

- Q: Should `GET /jobs/{jobId}/logs` and `GET /jobs/{jobId}/logs?follow=true` require authentication? → A: Same auth model as `GET /jobs/{jobId}` — caller must have job visibility (owner or admin).
- Q: What format should `migrate logs` use when printing events to stdout? → A: NDJSON — one compact JSON `ProgressEvent` object per line, consistent with the existing NDJSON protocol used throughout the system.
- Q: When the ring buffer is full and a new event arrives, should the newest event be dropped (write fails) or the oldest event be evicted? → A: Oldest event is evicted (`BoundedChannelFullMode.DropOldest`) so the live stream always reflects the most recent activity.
- Q: Should `ControlPlaneProgressSink` batch multiple `ProgressEvent` records into a single POST or send each event individually? → A: Individual events, fire-and-forget via bounded background `Channel`; no batching in v1. High-volume jobs rely on the `DropOldest` ring buffer to stay bounded.
- Q: Is there a limit on the number of concurrent SSE subscribers per job? → A: No hard limit in v1. The Control Plane broadcasts from a shared per-job `Channel` reader list. Subscriber count is a known operational constraint to document, not enforce.

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — CLI Command Sends Telemetry to Azure Monitor (Priority: P1)

An operator runs `migrate export --config migration.json`. During and after execution, the CLI process emits traces and metrics (command duration, errors, job submission outcome) to Azure Monitor. The operator can inspect these in Application Insights without needing to connect to the Control Plane.

**Why this priority**: This covers the "external Azure Monitoring of the running local version" requirement. It closes the observability gap for the CLI process itself, which currently has no OTel DI setup. It mirrors the pattern in the reference implementation (`MigrationToolHost.UseOpenTelemeter`).

**Independent Test**: Can be tested in isolation by running any CLI command with a valid Azure Monitor connection string configured and verifying a trace/metric appears in Application Insights — no Control Plane or Agent required.

**Acceptance Scenarios**:

1. **Given** a connection string is configured for Azure Monitor, **When** any CLI command completes execution, **Then** a trace span for that command appears in Application Insights within 60 seconds.
2. **Given** a CLI command fails with a non-zero exit code, **When** the process exits, **Then** the trace span is marked as failed with the error details.
3. **Given** no Azure Monitor connection string is configured, **When** any CLI command runs, **Then** the command runs normally without error and no telemetry is emitted externally.
4. **Given** the CLI process starts, **When** `Program.cs` initialises, **Then** a root activity source is created for the CLI process so any command span is a child of the process span.

---

### User Story 2 — Agent Streams ProgressEvents to Control Plane in Real Time (Priority: P1)

A Migration Agent is executing a work item export job. As it processes each revision, it emits `ProgressEvent` records via `ControlPlaneProgressSink`, which POSTs them to the Control Plane. The Control Plane stores these in a per-job ring buffer. Any consumer can read the latest state or subscribe to the live stream.

**Why this priority**: This is the foundational Phase 2 piece. Without it, the TUI live view and `migrate logs --follow` have no data source. Everything else in Phase 2 depends on this.

**Independent Test**: Can be tested by running a short export job, then calling `GET /jobs/{jobId}/logs` immediately after — the ring buffer should contain at least one `ProgressEvent` from the job.

**Acceptance Scenarios**:

1. **Given** a Migration Agent is executing a job, **When** it emits a `ProgressEvent` via `IProgressSink`, **Then** `ControlPlaneProgressSink` POSTs the event to `POST /agents/lease/{leaseId}/progress` within 1 second.
2. **Given** the Control Plane receives a progress event, **When** stored in the ring buffer, **Then** it is retrievable via `GET /jobs/{jobId}/logs` immediately.
3. **Given** the per-job ring buffer is full (capacity reached), **When** a new event arrives, **Then** the oldest event is evicted (`DropOldest` semantics) and the new event is stored — the live stream always reflects the most recent activity.
4. **Given** the Control Plane process restarts, **When** an agent resumes posting events, **Then** a fresh ring buffer is created for the job without error.
5. **Given** `ControlPlaneProgressSink` encounters a transient network failure, **When** the POST fails, **Then** the event is dropped (best-effort), the job continues unaffected, and the failure is logged at debug level.

---

### User Story 3 — `migrate logs --follow` Tails Live Events in the Terminal (Priority: P2)

An operator submits a job and wants a live log stream in their terminal without opening the TUI. They run `migrate logs --job <jobId> --follow`. The CLI connects to the Control Plane SSE stream and prints structured event lines to stdout as they arrive. Ctrl+C disconnects without stopping the job.

**Why this priority**: Provides an immediate, low-overhead way to see job progress from a terminal. Satisfies the "CLI shows the executing run as a live stream to the CLI" requirement — described as an acceptable format for local single-run scenarios.

**Independent Test**: Run `migrate logs --job <jobId> --follow` against a running job. Events should appear in the terminal. Ctrl+C should disconnect the CLI without affecting the running job.

**Acceptance Scenarios**:

1. **Given** a job is running and the SSE endpoint is available, **When** `migrate logs --job <id> --follow` is run, **Then** `ProgressEvent` lines appear in the terminal as NDJSON (one compact JSON object per line) as they are emitted.
2. **Given** the SSE stream is followed, **When** the job completes, **Then** the CLI detects the stream end and exits with code 0.
3. **Given** the operator presses Ctrl+C during `--follow`, **When** the CLI process exits, **Then** the running job on the Control Plane is unaffected.
4. **Given** `migrate logs --job <id>` is run without `--follow`, **When** executed, **Then** the most recent buffered events are printed as NDJSON and the CLI exits immediately.
5. **Given** the job ID does not exist, **When** `migrate logs --job <unknown-id>` is run, **Then** the CLI prints a clear error and exits with a non-zero code.
6. **Given** the caller does not have visibility of the job, **When** `migrate logs --job <id>` is called, **Then** the Control Plane returns 403 and the CLI prints a permission error.

---

### User Story 4 — TUI Displays Live Progress Stream (Priority: P3)

An operator opens the TUI with `migrate tui --job <jobId>`. The TUI progress panel shows live `ProgressEvent` updates as the job executes, consuming the SSE endpoint. The metrics panel is refreshed from `GET /jobs/{jobId}/telemetry`. Both views update independently.

**Why this priority**: The TUI is the richest observability surface, but it depends on Stories 2 and 3 infrastructure being in place. Lower priority because `migrate logs --follow` already covers the "see what is happening" use case for local runs.

**Independent Test**: Open the TUI for a running job. The progress table should update live. The TUI can be closed and reopened without affecting the job.

**Acceptance Scenarios**:

1. **Given** the TUI is open for a running job, **When** the agent emits a `ProgressEvent`, **Then** the progress table updates within 2 seconds.
2. **Given** the TUI loses its SSE connection temporarily, **When** the connection is restored, **Then** the TUI reconnects and resumes receiving events without operator intervention.
3. **Given** the TUI is open, **When** the job completes, **Then** the TUI transitions the job state to `Completed` and stops fetching new events.
4. **Given** the TUI is closed by the operator, **When** the process exits, **Then** the job continues running on the Control Plane without interruption.

---

### Edge Cases

- What happens when the Control Plane is unreachable when `ControlPlaneProgressSink` tries to POST? Event is dropped, job continues; failure is logged at debug level — no retry.
- What happens if a ring buffer for a job already exists when a new POST arrives? The existing buffer is reused; each POST appends to it.
- What happens if a ring buffer POST targets an unknown lease ID? 404 is returned; the agent logs at debug level and continues.
- What happens when the CLI command exits before all pending telemetry is flushed? OTel SDK `ForceFlush` is called on disposal of the `TracerProvider` / `MeterProvider` before process exit.
- What happens when the SSE stream receives no events for a long time? A heartbeat comment line is sent every 15 seconds to keep the connection alive through proxies and load balancers.

---

## Requirements *(mandatory)*

### Functional Requirements

**CLI Observability**

- **FR-001**: The CLI `Program.cs` MUST create a named `ActivitySource` for the process and register it with the OTel SDK so command spans are children of the process span.
- **FR-002**: OTel SDK (traces, metrics, logs) MUST be registered in the CLI `ServiceCollection` in `Program.cs` alongside existing service registrations.
- **FR-003**: The Azure Monitor exporter MUST be registered when a connection string is available (from `appsettings.json`, environment variable, or a hardcoded product telemetry key) and silently omitted when not present.
- **FR-004**: Each CLI command that executes a job-level operation MUST start a child `Activity` span so per-command duration and outcome are traceable.
- **FR-005**: The OTel `TracerProvider` and `MeterProvider` MUST be flushed and disposed before the CLI process exits to ensure all pending telemetry is delivered.

**ControlPlaneProgressSink**

- **FR-006**: A `ControlPlaneProgressSink` class MUST implement `IProgressSink` and POST each `ProgressEvent` to `POST /agents/lease/{leaseId}/progress` on the Control Plane.
- **FR-007**: `ControlPlaneProgressSink` MUST be best-effort: transient failures must not throw or propagate to the Job Engine.
- **FR-008**: `ControlPlaneProgressSink` MUST be registered alongside `ConsoleProgressSink` and `PackageProgressSink` in the Migration Agent DI container so all three receive every event.

**Control Plane Ring Buffer and Streaming**

- **FR-009**: The Control Plane MUST maintain a bounded per-job snapshot buffer (`ConcurrentQueue<ProgressEvent>`) capped at a configurable capacity (default: 1000 events), evicting the oldest event on overflow. For SSE fan-out, each active subscriber receives its own bounded `Channel<ProgressEvent>` with `BoundedChannelFullMode.DropOldest` at the same capacity. This dual-structure design ensures a non-destructive snapshot alongside live streaming to multiple concurrent consumers.
- **FR-010**: The Control Plane MUST expose `GET /jobs/{jobId}/logs` — authenticated (same visibility rules as `GET /jobs/{jobId}`) — returning the current ring buffer contents as a JSON array (non-streaming snapshot).
- **FR-011**: The Control Plane MUST expose `GET /jobs/{jobId}/logs?follow=true` — authenticated (same visibility rules as `GET /jobs/{jobId}`) — as a Server-Sent Events (SSE) stream delivering `ProgressEvent` records serialised as compact JSON in the SSE `data:` field, with a heartbeat comment (`:
`) every 15 seconds to maintain idle connections.
- **FR-012**: `POST /agents/lease/{leaseId}/progress` MUST store the received `ProgressEvent` in the job's ring buffer and broadcast it to all active SSE subscribers for that job. Individual events are POSTed one at a time; no batching is required in v1.
- **FR-013**: When a job transitions to a terminal state (`Completed`, `Failed`, `Cancelled`), the SSE stream MUST send a final `event: job-ended\ndata: {}\n\n` message and close the response.
- **FR-014a**: `GET /jobs/{jobId}/logs` and `GET /jobs/{jobId}/logs?follow=true` MUST return 403 when the caller lacks visibility of the job, consistent with all other job-scoped endpoints.

**`migrate logs` Command**

- **FR-015**: The `migrate logs --job <jobId>` command MUST call `GET /jobs/{jobId}/logs` and print the buffered events to stdout as NDJSON (one compact JSON `ProgressEvent` per line), then exit.
- **FR-016**: The `migrate logs --job <jobId> --follow` command MUST open the SSE stream and print each arriving event to stdout as NDJSON until the stream closes or the operator presses Ctrl+C.
- **FR-017**: `migrate logs --follow` MUST exit with code 0 when the stream ends normally (`job-ended` event received) and with a non-zero code on unrecoverable network error.

**TUI Live View**

- **FR-018**: The TUI progress panel MUST subscribe to `GET /jobs/{jobId}/logs?follow=true` and render incoming `ProgressEvent` records in the progress table.
- **FR-019**: The TUI MUST reconnect the SSE stream automatically with exponential back-off (max 30 s interval) on connection loss.
- **FR-020**: The TUI metrics panel MUST continue polling `GET /jobs/{jobId}/telemetry` independently of the SSE subscription at its existing interval.

### Key Entities

- **`ControlPlaneProgressSink`**: `IProgressSink` implementation in `DevOpsMigrationPlatform.Infrastructure`. POSTs `ProgressEvent` to the Control Plane via `HttpClient`. Registered alongside the other sinks in the Migration Agent.
- **`JobProgressStore`**: New Control Plane service. Holds a `ConcurrentDictionary<Guid, JobProgressEntry>` — one entry per active job, each containing a `ConcurrentQueue<ProgressEvent>` snapshot buffer and a `List<ChannelWriter<ProgressEvent>>` for SSE fan-out. Analogous to the existing `JobTelemetryStore`.
- **`ProgressController`**: New or updated Control Plane controller exposing `POST /agents/lease/{leaseId}/progress`, `GET /jobs/{jobId}/logs`, and `GET /jobs/{jobId}/logs?follow=true`.
- **CLI `ActivitySource`**: Named `ActivitySource` registered in `Program.cs`. Pattern mirrors `ActivitySourceProvider` in the reference implementation.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Azure Monitor receives at least one trace span per CLI command execution when a connection string is configured.
- **SC-002**: `GET /jobs/{jobId}/logs` returns at least one `ProgressEvent` within 5 seconds of a job starting on the Agent.
- **SC-003**: `migrate logs --job <id> --follow` displays incoming events with less than 2 seconds of end-to-end latency from Agent emission to terminal display.
- **SC-004**: The per-job ring buffer never exceeds its configured capacity regardless of job size or duration.
- **SC-005**: Closing the TUI or terminating `migrate logs --follow` has zero impact on a running job — the job continues to completion.
- **SC-006**: The OTel CLI pipeline adds no observable delay to command startup beyond SDK initialisation (target: under 50 ms added to cold start on a developer machine).

---

## Assumptions

- The CLI uses Spectre.Console.Cli with a `ServiceCollection`-backed `TypeRegistrar` (already in place). OTel services are added to this same `ServiceCollection` in `Program.cs`.
- The Azure Monitor connection string for product telemetry (NKD Agility subscription) can be stored in `appsettings.json` or hardcoded as a constant — both patterns are acceptable and mirror the reference implementation.
- `ControlPlaneProgressSink` fires-and-forgets each POST via a background `Channel` to keep `IProgressSink.Emit` synchronous and non-blocking on the Job Engine hot path.
- The TUI SSE consumer uses `HttpClient` with `HttpCompletionOption.ResponseHeadersRead` and a `StreamReader`. WebSocket and SignalR are out of scope — SSE is sufficient for unidirectional server-to-client flow.
- The ring buffer default capacity of 1000 events covers typical job progress granularity without memory pressure. Very large jobs will cycle out the oldest events via `DropOldest` semantics so the live stream always reflects recent activity.
- `ControlPlaneProgressSink` is only registered when the Migration Agent has a valid Control Plane base URL. It is not registered in standalone test harness scenarios.
- The `.NET 4.8` subprocess (`CLI.TfsMigration`) is out of scope for direct OTel instrumentation. Its telemetry continues to flow via the existing `MetricSnapshot`-in-NDJSON bridge.
- The number of concurrent SSE subscribers per job is not limited in v1. This is a known operational constraint — high subscriber counts on a single job will increase memory and CPU on the Control Plane host.
- `ControlPlaneProgressSink` POSTs events individually (no batching). For very high-frequency export jobs, the bounded background `Channel` in the sink provides natural flow control; excess events are dropped before reaching the network.
