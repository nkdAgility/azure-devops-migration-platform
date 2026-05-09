# Feature Specification: Three-Channel Observability

**Feature Branch**: `007-observability-logging`  
**Created**: 2026-04-09  
**Status**: Draft  
**Input**: User description: "Three-channel observability: OTel signals (traces, metrics, logs), Event Progress (IProgressSink), and ILogger diagnostics persisted to package and streamed to Control Plane/TUI"

## Architecture References

| Document | Status |
|---|---|
| `docs/architecture.md` | Discrepancy — conflates progress events with "logs" in naming and endpoints |
| `docs/tui-guide.md` | Discrepancy — references `GET /jobs/{jobId}/logs` for progress events; no diagnostics panel documented |
| `docs/control-plane.md` | Discrepancy — progress endpoint named `/logs`; no diagnostics endpoint; no download endpoint |
| `docs/agent-hosting.md` | Confirmed accurate — already documents three sinks and `Logs/` package writing |
| `docs/cli-guide.md` | Discrepancy — `manage logs` command conflates progress events with diagnostic logs |
| `.agents/guardrails/architecture-boundaries.md` | Confirmed accurate — guardrail #12 (agents stateless, durable state in package), #13 (IArtefactStore only), #18 (no UI coupling) all apply |
| `.agents/context/package-manager.md` | Confirmed accurate — package persistence still routes through `IArtefactStore` |
| `.agents/context/migration-package-concept.md` | Discrepancy — `Logs/` folder listed but contents not specified |

---

## User Scenarios & Testing *(mandatory)*

### User Story 1 — Operator Diagnoses a Failed Migration (Priority: P1)

An operator runs an export and it fails partway through. They need to understand what went wrong and what to do about it. Today, if they didn't capture terminal output, the diagnostic information is lost. The operator needs persistent, level-filtered diagnostic logs that travel with the migration package.

**Why this priority**: Without diagnostics, operators cannot self-serve troubleshooting. Every failure becomes a support ticket. This is the core "why" of the feature.

**Independent Test**: Run an export against a project with a known inaccessible attachment. After the job fails, inspect `Logs/agent.jsonl` in the package. The file must contain structured log records at Warning and Error level that identify the attachment, the HTTP status code, and a human-readable remediation hint.

**Acceptance Scenarios**:

1. **Given** a Migration Agent executing an export job, **When** an `ILogger.LogWarning` or `ILogger.LogError` call is made anywhere in the agent process, **Then** a structured NDJSON log record is appended to `Logs/agent.jsonl` in the package via `IArtefactStore`.
2. **Given** a completed or failed job with `Logs/agent.jsonl` in the package, **When** an operator opens the file, **Then** each line is a valid JSON object containing at minimum: `timestamp`, `level`, `category`, `message`, and (when present) `exception`.
3. **Given** a Migration Agent executing a job, **When** `ILogger` calls are made at Trace or Debug level, **Then** those records are written to `Logs/agent.jsonl` only if the configured minimum log level includes them (default minimum: Information).

---

### User Story 2 — Complete the PackageProgressSink (Priority: P1)

The `PackageProgressSink` is currently a TODO stub. Progress events are not persisted to the package. An operator who receives a package from another environment has no record of what happened during the export.

**Why this priority**: The package is the source of truth. Without `Logs/progress.jsonl`, the package is incomplete. This is a gap in the existing documented contract.

**Independent Test**: Run an export to completion. Verify `Logs/progress.jsonl` exists in the package and contains one NDJSON line per `ProgressEvent` emitted during the export.

**Acceptance Scenarios**:

1. **Given** a Migration Agent executing a job, **When** `IProgressSink.Emit` is called, **Then** a JSON-serialised `ProgressEvent` line is appended to `Logs/progress.jsonl` via `IArtefactStore`.
2. **Given** an export that completes successfully, **When** an operator inspects the package, **Then** `Logs/progress.jsonl` contains at least one record per module stage transition.
3. **Given** a `PackageProgressSink` writing to `IArtefactStore`, **When** the `Emit` call is made, **Then** the write is non-blocking (internally buffered) and does not slow down the export pipeline.

---

### User Story 3 — Operator Watches Live Diagnostics in the TUI (Priority: P2)

An operator is watching a long-running migration in the TUI. They see the progress table advancing but want to understand warnings as they happen — not after the job ends. The TUI is the primary interface for full live observation of running jobs — live job lists, events/metrics per job, and live diagnostic log streaming. The CLI also provides a live diagnostic stream via `export --follow` (implicit in standalone mode), but the TUI offers the complete dashboard experience. CLI `manage` commands are snapshot/download helpers only.

**Why this priority**: Real-time visibility reduces the time to detect and react to problems. Without it, operators wait for failure before investigating. Depends on the persistence layer from P1.

**Independent Test**: Start an export with the TUI connected. Trigger a warning condition (e.g., inaccessible attachment). The TUI diagnostics panel displays the warning within 5 seconds of it occurring on the agent.

**Acceptance Scenarios**:

1. **Given** a running job and a TUI connected to the control plane, **When** the Migration Agent emits an `ILogger` record at or above the control plane's configured minimum level, **Then** the TUI diagnostics panel displays the record within the configured polling/streaming interval.
2. **Given** a TUI diagnostics panel, **When** the operator changes the level filter (e.g., from Warning to Information), **Then** subsequent records at the new level appear in the panel (subject to the control plane's own minimum level — records below the CP floor are not available).
3. **Given** a TUI that disconnects and reconnects, **When** the reconnection occurs, **Then** the diagnostics panel replays recent records from the control plane ring buffer.

---

### User Story 4 — Rename Endpoints and CLI Commands to Eliminate Naming Confusion (Priority: P2)

The current API uses `/logs` for progress events and the CLI command is `manage logs`. This conflates two fundamentally different data channels. The naming must clearly distinguish progress events from diagnostic logs.

**Why this priority**: Correct naming is a prerequisite for adding the diagnostics channel without further confusion. Parallel with P2 because the TUI work depends on clear endpoint separation.

**Independent Test**: After renaming, `GET /jobs/{id}/progress` returns `ProgressEvent` records. `GET /jobs/{id}/diagnostics` returns `DiagnosticLogRecord` entries. The CLI `manage progress` returns a snapshot of progress events. The CLI `manage diagnostics` downloads diagnostic logs from a completed job's package.

**Naming mapping**:

| Old | New | Purpose |
|---|---|---|
| `GET /jobs/{jobId}/logs` | `GET /jobs/{jobId}/progress` | Progress event snapshot |
| `GET /jobs/{jobId}/logs?follow=true` | `GET /jobs/{jobId}/progress?follow=true` | Progress SSE (TUI only) |
| `manage logs` | `manage diagnostics` | Download diagnostic logs from completed job package |
| *(new)* | `manage progress` | Snapshot of progress events (no `--follow`) |

**Acceptance Scenarios**:

1. **Given** the control plane API, **When** a client calls `GET /jobs/{jobId}/progress`, **Then** it receives the same `ProgressEvent` data previously served by `GET /jobs/{jobId}/logs`.
2. **Given** the control plane API, **When** a client calls `GET /jobs/{jobId}/progress?follow=true`, **Then** it receives the same SSE stream previously served by `GET /jobs/{jobId}/logs?follow=true`.
3. **Given** the CLI, **When** an operator runs `manage progress --job <id>`, **Then** it displays a snapshot of `ProgressEvent` records. No `--follow` option is available — live streaming is TUI-only.
4. **Given** a completed job, **When** an operator runs `manage diagnostics --job <id> [--level Warning]`, **Then** it downloads `Logs/agent.jsonl` from the package via the control plane and outputs records filtered by level.

---

### User Story 5 — Download Package Logs via Control Plane API (Priority: P2)

An operator running in cloud or self-hosted mode does not have direct filesystem access to the package. They need to download the persisted log files (`progress.jsonl` and `agent.jsonl`) from the package via the control plane API. This is the backend for `manage diagnostics`.

**Why this priority**: Elevated from P3 to P2 because `manage diagnostics` is the primary post-mortem tool and depends on this endpoint. Operators who ran a Debug-level job on a remote control plane need the full package logs — the CP ring buffer only has Warning+ records.

**Independent Test**: Submit an export job in a test environment. After completion, call `GET /jobs/{id}/logs/download?type=progress` and `GET /jobs/{id}/logs/download?type=diagnostics`. Both return the respective NDJSON files from the package.

**Acceptance Scenarios**:

1. **Given** a completed job with `Logs/progress.jsonl` in the package, **When** a client calls `GET /jobs/{jobId}/logs/download?type=progress`, **Then** the response body is the contents of `Logs/progress.jsonl` with content type `application/x-ndjson`.
2. **Given** a completed job with `Logs/agent.jsonl` in the package, **When** a client calls `GET /jobs/{jobId}/logs/download?type=diagnostics`, **Then** the response body is the contents of `Logs/agent.jsonl` with content type `application/x-ndjson`.
3. **Given** a job where the package URI is `file:///`, **When** the download endpoint is called, **Then** the control plane reads from `FileSystemArtefactStore` and returns the file.
4. **Given** a job where the package URI is an Azure Blob Storage URL (`https://*.blob.core.windows.net/...`), **When** the download endpoint is called, **Then** the control plane reads from `AzureBlobArtefactStore` and returns the file.

---

### User Story 6 — Operator Controls Log Level and Follow Mode on Export (Priority: P1)

An operator running `devopsmigration export` needs to control diagnostic verbosity per job and optionally watch diagnostic output inline without switching to the TUI.

**Why this priority**: This is core operator workflow. Every export command needs a clear, predictable lifecycle — submit and exit, or submit and follow. The log level controls what gets persisted to the package and what flows through the system.

**Independent Test**: Run `devopsmigration export --config migration.json --level Debug --follow`. Verify that the agent writes Debug-level records to `agent.jsonl`, that diagnostic output streams to the console, and that on job completion the CLI exits.

**Acceptance Scenarios**:

1. **Given** an operator runs `export --level Debug`, **When** the job is submitted, **Then** the agent's diagnostic log level is set to Debug and `Logs/agent.jsonl` contains Debug+ records.
2. **Given** an operator runs `export` without `--follow` and a remote control plane is configured (`--url`), **When** the job is submitted, **Then** the CLI prints the `jobId` and exits immediately. The job continues running server-side.
3. **Given** an operator runs `export --follow` with a remote control plane, **When** the job is running, **Then** the CLI streams diagnostic log records to the console. On job completion, the CLI prints a summary and exits.
4. **Given** an operator runs `export --follow` and presses Ctrl+C, **When** the CLI receives the interrupt, **Then** the CLI detaches from the log stream and exits. The job continues running on the server. The operator is informed to use the TUI to resume watching.
5. **Given** an operator runs `export` in standalone mode (no `--url`), **When** the Aspire-managed control plane and agent start locally, **Then** `--follow` is implicit — diagnostic logs always stream to the console. The locally-started control plane uses the operator's `--level` setting.
6. **Given** `export --level Information` (default), **When** the agent writes to the package, **Then** `Logs/agent.jsonl` contains only Information+ records. Debug and Trace records are not persisted.

---

### Edge Cases

- What happens when `IArtefactStore.WriteAsync` fails during log writing? The export must not be blocked or crash. Log sink failures are best-effort — caught, counted as a dropped record, and the export continues.
- What happens when `Logs/agent.jsonl` grows very large (e.g., verbose logging on a 100k work item export)? The package sink must flush periodically and not accumulate unbounded memory. A bounded channel buffer with drop-oldest semantics prevents memory pressure.
- What happens when the TUI connects to a job that has already completed? The diagnostics ring buffer on the control plane has the most recent N records. For full history, the operator uses `manage diagnostics --job <id>` to download from the package.
- What happens when the control plane restarts during a running job? The in-memory ring buffers are lost. The package files (`progress.jsonl`, `agent.jsonl`) are the durable fallback. The agent continues writing to the package regardless.
- What happens when the agent is set to Debug but the control plane is at Warning? The agent writes Debug+ records to the package and pushes them to the control plane. The control plane drops records below its own configured minimum (Warning) before buffering or streaming to the TUI. The package retains the full Debug detail for post-mortem download via `manage diagnostics`.
- What happens with the TFS export subprocess? The .NET 4.8 `TfsExportAgent` uses `StdoutProgressSink` for progress events (NDJSON on stdout). Diagnostic logs from the TFS subprocess are written to stderr and captured by `TfsExporterProcessAdapter` in the CLI. The package log files are written by the TFS subprocess via its own `IArtefactStore` instance.

---

## Requirements *(mandatory)*

### Functional Requirements

**ILogger Diagnostics Persistence (P1)**

- **FR-001**: The Migration Agent MUST write `ILogger` output to `Logs/agent.jsonl` in the package via `IArtefactStore` using NDJSON format (one JSON object per line).
- **FR-002**: Each log record MUST contain: `timestamp` (ISO 8601), `level` (string name), `category` (logger category name), `message` (formatted message), and conditionally `exception` (full exception string) and `traceId`/`spanId` (when an `Activity` is active).
- **FR-003**: The minimum log level for the package sink MUST be configurable (default: Information). Levels below the configured threshold MUST be discarded.
- **FR-004**: The log sink MUST be non-blocking. Log records MUST be buffered internally (bounded channel, drop-oldest on overflow) and flushed to `IArtefactStore` asynchronously.
- **FR-005**: Failures writing to `IArtefactStore` MUST NOT halt or slow the migration. Failures MUST be counted and logged at Debug level to the console sink.

**PackageProgressSink Completion (P1)**

- **FR-006**: `PackageProgressSink` MUST append each `ProgressEvent` as a JSON line to `Logs/progress.jsonl` via `IArtefactStore`.
- **FR-007**: The sink MUST use the same bounded-channel non-blocking pattern as the diagnostics sink.
- **FR-008**: `PackageProgressSink` MUST receive `IArtefactStore` via dependency injection, not construct it internally.

**Diagnostics Streaming to Control Plane (P2)**

- **FR-009**: The Migration Agent MUST push `ILogger` records at the agent's configured minimum level to the control plane via `POST /agents/lease/{leaseId}/diagnostics`, separate from the progress event endpoint.
- **FR-010**: The control plane MUST maintain a bounded in-memory ring buffer for diagnostic log records per job, independent of the progress event ring buffer. The control plane MUST discard incoming records below its own deployment-level minimum log level (default: Warning) before buffering.
- **FR-011**: The control plane MUST expose `GET /jobs/{jobId}/diagnostics` (snapshot) and `GET /jobs/{jobId}/diagnostics?follow=true` (SSE) for real-time diagnostic streaming. These endpoints are consumed by the TUI and `export --follow` — not by CLI `manage` commands.
- **FR-012**: The diagnostics SSE stream MUST support a `level` query parameter to filter by minimum log level (e.g., `?follow=true&level=Warning`). The effective floor is the higher of the requested level and the control plane's deployment-level minimum.

**API and CLI Rename (P2)**

- **FR-013**: The progress event endpoint MUST be renamed from `GET /jobs/{jobId}/logs` to `GET /jobs/{jobId}/progress`. The SSE variant follows the same path with `?follow=true`.
- **FR-014**: The agent push endpoint MUST remain at `POST /agents/lease/{leaseId}/progress` (already correctly named).
- **FR-015**: The CLI command `manage logs` MUST be replaced by two commands: `manage progress` (snapshot of `ProgressEvent` records, no `--follow`) and `manage diagnostics` (download diagnostic logs from a completed job's package, no `--follow`). Neither command supports live streaming — all live observation is TUI-only.

**Package Log Download (P2)**

- **FR-016**: The control plane MUST expose `GET /jobs/{jobId}/logs/download` with a `type` query parameter (`progress` or `diagnostics`) that reads the corresponding file from the package via `IArtefactStore` and returns it. This is the backend for `manage diagnostics`.
- **FR-017**: The download endpoint MUST work with both `file:///` and Azure Blob Storage URL (`https://*.blob.core.windows.net/...`) package URIs transparently.

**TUI Diagnostics Panel (P2)**

- **FR-018**: The TUI MUST display a diagnostics panel that streams log records from `GET /jobs/{jobId}/diagnostics?follow=true`.
- **FR-019**: The diagnostics panel MUST support level filtering, defaulting to Warning and above. The effective floor is the control plane's deployment-level minimum.
- **FR-020**: The diagnostics panel MUST render alongside the existing metrics panel and progress table without replacing either.

**Export Command Log Level and Follow Mode (P1)**

- **FR-021**: The `export` command MUST accept a `--level` option (default: `Information`) that sets the agent's diagnostic log minimum level for the job. Valid values: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`.
- **FR-022**: The `--level` value MUST be passed to the agent via the job definition. The agent configures its `PackageLoggerProvider` and `ControlPlaneLoggerProvider` to emit at this level.
- **FR-023**: The `export` command MUST accept a `--follow` option. When present, the CLI streams diagnostic log records from `GET /jobs/{jobId}/diagnostics?follow=true` to the console after job submission.
- **FR-024**: In standalone mode (no `--url` configured), `--follow` MUST be implicit. The locally-started control plane MUST use the operator's `--level` value as its deployment-level minimum, ensuring full fidelity between what the agent writes and what the console displays.
- **FR-025**: In non-standalone mode without `--follow`, the CLI MUST print the `jobId` and exit immediately after successful job submission. The job continues running server-side.
- **FR-026**: When `--follow` is active and the operator presses Ctrl+C, the CLI MUST detach from the log stream and exit without cancelling the job. The CLI MUST print a message indicating the job continues and suggest using the TUI.
- **FR-027**: When `--follow` is active and the job reaches a terminal state (Completed, Failed, Cancelled), the CLI MUST print a summary and exit.

**Tiered Log Level Architecture (P1)**

- **FR-028**: The agent's diagnostic log level MUST be independent of the control plane's deployment-level log filter. The agent writes to the package at its own level; the control plane filters incoming records at its own level.
- **FR-029**: The control plane's deployment-level minimum log level (default: Warning) MUST be configurable via standard `Logging` configuration. This level gates what is buffered in the ring buffer, streamed via SSE, and exported to Application Insights / OTel.
- **FR-030**: In standalone mode, the control plane's deployment-level minimum MUST be set to the operator's `--level` value from the `export` command. In non-standalone mode, the control plane uses its own deployment configuration.

### Key Entities

- **ProgressEvent**: Existing structured domain event. Fixed schema. No levels. Drives progress table, cursor/resume, package audit trail. Persisted to `Logs/progress.jsonl`.
- **DiagnosticLogRecord**: Structured log record derived from `ILogger` output via OpenTelemetry. Has levels, categories, exceptions, trace correlation. Persisted to `Logs/agent.jsonl`.
- **MetricSnapshot**: Existing telemetry counters (work items/revisions exported, error counts, durations). Polled from agent. Not persisted to package.

---

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: After any export or import job, the package contains `Logs/progress.jsonl` with one or more NDJSON records corresponding to every module stage transition.
- **SC-002**: After any export or import job, the package contains `Logs/agent.jsonl` with structured diagnostic records at the configured minimum level.
- **SC-003**: An operator can identify the root cause of a failed migration by reading `Logs/agent.jsonl` without access to the original terminal output.
- **SC-004**: The TUI displays agent warnings and errors within 5 seconds of them being emitted.
- **SC-005**: Writing to the package log sinks adds less than 5% overhead to total job duration compared to running without them.
- **SC-006**: The API naming is consistent — progress endpoints serve `ProgressEvent` data, diagnostics endpoints serve `LogRecord` data, and no endpoint conflates the two.

---

## Assumptions

- A custom `ILoggerProvider` (not an OTel `BaseExporter<LogRecord>`) is the chosen mechanism for diagnostic log capture (see research R-002). This avoids modifying the shared OTel pipeline in ServiceDefaults, avoids OTel batch-processor latency, and follows the same downstream-registration pattern as `SnapshotMetricExporter`. The OTel logging bridge in ServiceDefaults continues to export to OTLP/Azure Monitor independently.
- `IArtefactStore` supports append-like semantics via repeated writes. The sinks will batch lines and write periodically (e.g., every 500ms or every 50 records, whichever comes first) rather than writing one line at a time.
- The agent's diagnostic log level is per-job, set via `--level` on the `export` (or `import`/`migrate`) command and passed to the agent through the job definition. Default: Information.
- The control plane has a deployment-level minimum log level (default: Warning) that is independent of any individual job's agent level. The CP drops incoming diagnostics records below its floor before buffering, streaming, or exporting to Application Insights.
- In standalone mode, the locally-started control plane adopts the operator's `--level` value, ensuring full fidelity. In non-standalone mode, the CP uses its own deployment config.
- The TFS export subprocess (.NET 4.8) will write its own `Logs/agent.jsonl` via its `IArtefactStore` instance using a .NET 4.8-compatible logging sink (not OTel, since the TFS subprocess does not run the OTel SDK).
- The rename from `/logs` to `/progress` is a breaking API change to the control plane. Since the platform is pre-release, this is acceptable without a versioned upgrader.
- The control plane diagnostics ring buffer uses the same bounded capacity as the progress ring buffer (default: 1000 records), configurable independently.
- All live observation of running jobs (job list, events, metrics, diagnostic log stream) is primarily through the TUI. The CLI also provides live diagnostic streaming via `export --follow` (implicit in standalone mode). CLI `manage` commands provide snapshot/download access only — no `--follow` on any `manage` subcommand.
- Architecture docs read: `docs/architecture.md`, `docs/tui-guide.md`, `docs/control-plane.md`, `docs/agent-hosting.md`, `docs/cli-guide.md`, `docs/development-setup.md`, `.agents/guardrails/architecture-boundaries.md`, `.agents/context/package-manager.md`, `.agents/context/migration-package-concept.md`.
