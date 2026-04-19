# Research: Three-Channel Observability

## R-001: IArtefactStore Append Semantics

**Decision**: Add `AppendAsync(string path, string content, CancellationToken ct)` to `IArtefactStore`.

**Rationale**: `WriteAsync` uses `File.WriteAllTextAsync()` — it overwrites the entire file on every call. For log files that accumulate thousands of lines during a job, a read-modify-write pattern is unacceptable (memory-unsafe for large logs, race-prone under concurrent writes). An explicit `AppendAsync` method is the correct solution.

**Alternatives considered**:
- *Read-modify-write in the sink*: Rejected — violates memory safety for large log files (FR-004), and two concurrent flushes could corrupt the file.
- *Numbered chunk files (e.g., `agent-001.jsonl`, `agent-002.jsonl`)*: Rejected — complicates the download endpoint and consumer tooling. A single NDJSON file per log type is simpler and sufficient.
- *Write to a temp file, then rename*: Rejected — still requires accumulating the full file content.

**Implementation notes**:
- `FileSystemArtefactStore`: `File.AppendAllTextAsync(fullPath, content, Encoding.UTF8, ct)`
- `AzureBlobArtefactStore`: Use `AppendBlobClient` with `AppendBlockAsync`.
- The interface addition is additive (non-breaking) and the method is only called by infrastructure sinks, not by modules.

---

## R-002: OTel Log Exporter vs ILoggerProvider

**Decision**: Use a custom `ILoggerProvider` rather than a custom OTel `BaseExporter<LogRecord>`.

**Rationale**: The OTel SDK's `AddOpenTelemetry()` logging bridge in ServiceDefaults works by registering an `OpenTelemetryLoggerProvider`. Plugging in a custom `BaseExporter<LogRecord>` requires modifying the OTel pipeline registration, which is shared across all services (Aspire dashboard, Azure Monitor, OTLP). A separate `ILoggerProvider` is cleaner — it registers alongside the OTel provider without interfering, and has direct access to the formatted message, log level, category, exception, and scopes.

**Alternatives considered**:
- *Custom `BaseExporter<LogRecord>`*: Rejected — requires modifying `ConfigureOpenTelemetry()` in ServiceDefaults, which affects all services. The exporter API also batches records, adding latency to the streaming path.
- *A second `OpenTelemetryLoggerProvider` instance*: Rejected — the OTel SDK doesn't support multiple instances cleanly.

**Implementation notes**:
- `PackageLoggerProvider` and `ControlPlaneLoggerProvider` implement `ILoggerProvider`.
- Both follow the same buffered channel pattern as `ControlPlaneProgressSink` (bounded channel, drop-oldest, background drain).
- Registered via `builder.Logging.AddProvider(new PackageLoggerProvider(...))` in the agent's DI setup.

---

## R-003: Buffered Write Strategy for Package Sinks

**Decision**: Both `PackageProgressSink` and `PackageLoggerProvider` use a bounded `Channel<T>` with a background drain loop that flushes batches to `IArtefactStore.AppendAsync`.

**Rationale**: `IProgressSink.Emit` is synchronous. `ILogger.Log` is synchronous. `IArtefactStore.AppendAsync` is async. A bounded channel bridges the sync-to-async gap without blocking the caller, exactly as `ControlPlaneProgressSink` already does for HTTP POST.

**Flush strategy**: 
- Maximum batch size: 50 records
- Maximum flush interval: 500ms  
- Whichever comes first triggers a flush
- On shutdown, drain remaining records before disposing

**Channel settings**:
- Capacity: 1024 records
- `BoundedChannelFullMode.DropOldest` — diagnostic records are best-effort; losing oldest records under extreme pressure is acceptable

---

## R-004: Diagnostics Ring Buffer on Control Plane

**Decision**: Create a `DiagnosticsStore` parallel to the existing `JobProgressStore`, with the same bounded ring buffer and channel-based subscriber pattern.

**Rationale**: The existing `JobProgressStore` is purpose-built for `ProgressEvent` records. Diagnostic log records have a different schema (level, category, exception). Sharing one store would require a union type and complicate both the serialisation and the SSE streaming. Two separate stores with the same implementation pattern is simpler.

**Alternatives considered**:
- *Single polymorphic store*: Rejected — adds schema complexity and makes level-based filtering harder.
- *Reuse `JobProgressStore` with a wrapper type*: Rejected — consumers (TUI, CLI) need different filtering and rendering logic for each stream.

**Implementation notes**:
- `DiagnosticLogStore` mirrors `JobProgressStore` exactly: `ConcurrentDictionary<Guid, Entry>` with `ConcurrentQueue<DiagnosticLogRecord>` and `List<ChannelWriter<DiagnosticLogRecord>>`.
- Default capacity: 1000 records (configurable via `DiagnosticLogOptions.Capacity`).
- `DiagnosticsController` mirrors `ProgressController` with the addition of a `level` query parameter for filtering.

---

## R-005: DiagnosticLogRecord Schema

**Decision**: Define a `DiagnosticLogRecord` record in `DevOpsMigrationPlatform.Abstractions` with the following fields:

```
Timestamp     : DateTimeOffset
Level         : string (e.g., "Warning", "Error", "Information")
Category      : string (logger category, e.g., "AzureDevOpsAttachmentBinarySource")
Message       : string (formatted message)
Exception     : string? (full exception ToString() when present)
TraceId       : string? (from Activity.Current when present)
SpanId        : string? (from Activity.Current when present)
```

**Rationale**: This is the minimal schema that gives an operator enough to diagnose a failure. It maps 1:1 to the fields available from `ILogger` calls. The `TraceId`/`SpanId` allow correlation with OTel distributed traces when available.

**Alternatives considered**:
- *Full OTel `LogRecord` schema*: Rejected — too complex for package serialisation and human readability. The OTel pipeline already exports the full schema to OTLP/Azure Monitor.
- *Just message + level*: Rejected — missing category and exception makes diagnosis incomplete.

---

## R-006: Endpoint Naming Convention

**Decision**: 

| Path | Verb | Purpose |
|---|---|---|
| `/jobs/{jobId}/progress` | GET | ProgressEvent snapshot or SSE stream (`?follow=true`) |
| `/jobs/{jobId}/diagnostics` | GET | DiagnosticLogRecord snapshot or SSE stream (`?follow=true&level=Warning`) |
| `/jobs/{jobId}/telemetry` | GET | MetricSnapshot poll (unchanged) |
| `/jobs/{jobId}/logs/download` | GET | Download package log files (`?type=progress` or `?type=diagnostics`) |
| `/agents/lease/{leaseId}/progress` | POST | Agent pushes ProgressEvent (unchanged) |
| `/agents/lease/{leaseId}/diagnostics` | POST | Agent pushes DiagnosticLogRecord batch |
| `/agents/lease/{leaseId}/complete` | POST | Agent signals job done (unchanged) |
| `/agents/lease/{leaseId}/fail` | POST | Agent signals job failed (unchanged) |

**Rationale**: Clear separation — each channel has its own noun. `/logs/download` uses `/logs/` as a container path for both file types since both live in the package's `Logs/` folder.

---

## R-007: TFS Subprocess Diagnostics

**Decision**: The .NET 4.8 TFS subprocess writes to `Logs/agent.jsonl` via its own `FileSystemArtefactStore` instance using a simple synchronous file append. It does not use OTel or `ILoggerProvider`.

**Rationale**: The TFS subprocess runs .NET 4.8 which doesn't support the OTel SDK. It already has `IArtefactStore` via the multi-targeted `Abstractions` project. A simple `StreamWriter` append inside a `TfsPackageLogSink` (implementing a minimal interface) is sufficient.

**Implementation notes**:
- The TFS subprocess writes diagnostic lines to the same `Logs/agent.jsonl` path.
- The CLI's `TfsExporterProcessAdapter` also captures stderr for diagnostic context.
- Since the TFS subprocess runs before the .NET 10 agent (separate operation), there's no concurrent write contention.

---

## R-008: Tiered Log Level Architecture

**Decision**: The agent's diagnostic log level is per-job and independent of the control plane's deployment-level minimum. The agent writes full detail to the package; the control plane filters incoming records at its own floor before buffering, streaming, or exporting.

**Rationale**: Operators need the ability to run a Debug-level export for troubleshooting without changing the control plane's deployment configuration. The package is the post-mortem record and should capture the full requested detail. The control plane is a shared resource — its buffer and App Insights export should remain at the deployment-configured level (default: Warning) to control cost and noise.

**Data flow**:
- Agent receives `--level` from the job definition → configures `PackageLoggerProvider` and `ControlPlaneLoggerProvider` at this level
- Both providers push all records at the agent's level to their respective destinations
- The package stores everything the agent writes (full fidelity)
- The control plane drops incoming records below its own deployment-level minimum before buffering
- SSE streams and App Insights / OTel export operate at the control plane's level
- In standalone mode, the locally-started CP adopts the operator's `--level`, so fidelity is fully aligned

**Alternatives considered**:
- *Single global log level*: Rejected — forces a tradeoff between package completeness and CP noise. Operators would have to choose between detailed post-mortem and clean live views.
- *Agent-only filtering (no CP filter)*: Rejected — a Debug-level agent would flood the CP ring buffer and App Insights with noise, increasing cost and reducing signal-to-noise for live monitoring.

---

## R-009: CLI Export --follow Lifecycle

**Decision**: `export --follow` streams diagnostic logs inline from the control plane SSE endpoint. In standalone mode, `--follow` is implicit. On Ctrl+C, the CLI detaches without cancelling the job.

**Rationale**: The operator's mental model for `export` is "submit and watch." In standalone mode (single-machine, Aspire-managed), there's no reason to submit-and-exit — the operator expects to see output. In remote mode, the default is fire-and-forget (print jobId, exit) with `--follow` as the opt-in for watching.

**Ctrl+C behaviour**: The job continues running because the agent holds its own lease independently. Cancelling the job requires `manage cancel`. This matches TUI behaviour (closing TUI doesn't cancel jobs).

**Alternatives considered**:
- *Ctrl+C cancels the job*: Rejected — destructive default. Operators who accidentally close a terminal would lose a long-running export.
- *Always follow in remote mode*: Rejected — operators submitting batch jobs don't want to babysit each one.

---

## R-010: manage logs → manage diagnostics (Option C Rename)

**Decision**: `manage logs` is renamed to `manage diagnostics`. A new `manage progress` command is added. Neither supports `--follow`.

**Rationale**: The old `manage logs` was ambiguous — it served progress events but was named "logs." Option C maps each command to its data channel: `manage diagnostics` downloads diagnostic logs from the package, `manage progress` provides a progress event snapshot. The `--follow` option is removed from all `manage` subcommands because live streaming is provided by the TUI and `export --follow`.

**Alternatives considered**:
- *Option A (rename to manage progress, add manage diagnostics with --follow)*: Rejected — duplicates TUI streaming functionality in the CLI manage commands.
- *Option B (keep manage logs, add manage diagnostics)*: Rejected — retains the ambiguous "logs" name.
