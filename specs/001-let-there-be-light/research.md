# Research: Telemetry Pipeline — Cloud Export + TUI Live Feed

**Feature**: Observability pipeline that routes OTel metrics/traces to a cloud provider (OTLP/Azure Monitor) and feeds live metrics to the TUI via the Control Plane.

---

## 1. OTLP Exporter in .NET 4.8 (TFS subprocess)

**Decision**: Do NOT add OTLP export to the TFS subprocess (`.Infrastructure.TfsObjectModel`, .NET 4.8).

**Rationale**:
- The TFS subprocess communicates with the agent exclusively via stdin/stdout NDJSON. It has no direct network path to the control plane or any OTLP endpoint.
- Adding an OTLP exporter would violate the subprocess isolation contract (Principle VI) by requiring network access and external secrets.
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` targets `netstandard2.0`+ and would compile, but the subprocess runs in environments where the OTLP endpoint may not be reachable (e.g. air-gapped TFS source networks).
- The .NET 4.8 subprocess already has `WorkItemExportMetrics` and `AttachmentDownloadMetrics` implementations. These values are read by the parent process.

**Alternative considered**: Add OTLP export directly to subprocess — rejected (network isolation violation).

**Solution**: Extend `ProgressEvent` with an optional `MetricSnapshot` payload. The subprocess populates it periodically (every N events or on interval). `TfsExporterProcessAdapter` in the .NET 10 Migration Agent reads these snapshots off stdout and forwards them via the `ControlPlaneTelemetrySink`. All OTel export happens only in the parent .NET 10 process.

---

## 2. Reading Metric Values from the OTel Pipeline In-Process

**Decision**: Use a custom `BaseExporter<Metric>` registered in the existing `MeterProvider` alongside OTLP and Azure Monitor exporters.

**Rationale**:
- `BaseExporter<Metric>` is the native OTel SDK extension point for consuming metric values — it is the correct, idiomatic way to intercept metrics in an OTel pipeline.
- The `PeriodicExportingMetricReader` already drives collection at a configurable interval; wiring through it means the snapshot collection uses the same aggregation engine (histograms, delta/cumulative counters) as OTLP export — no parallel infrastructure.
- A `BaseExporter<Metric>` receives `Metric` objects with fully aggregated `MetricPoint` data (sum, count, histogram buckets) — far richer than what `MeterListener` exposes.
- Fan-out to OTLP, Azure Monitor, and the TUI snapshot store is handled by the single `MeterProvider` with no additional plumbing.
- **`MeterListener` (BCL) is explicitly rejected**: it is a lower-level API below the OTel SDK, bypasses OTel aggregation, and would create a parallel metric-collection infrastructure alongside the existing `MeterProvider` pipeline.

**Implementation**:
```
MeterProvider
  └─ PeriodicExportingMetricReader (SnapshotIntervalSeconds)
       ├─ SnapshotMetricExporter : BaseExporter<Metric>   ← writes to IMetricSnapshotStore
       ├─ OtlpMetricExporter                              ← if OtlpEndpoint configured
       └─ AzureMonitorMetricExporter                       ← if AzureMonitorCS configured
```

`IMetricSnapshotStore` is a thread-safe singleton that holds the latest `MetricSnapshot`. It is injected into the `ControlPlaneTelemetryTimer` service, which reads it and pushes to the Control Plane on the same interval.

**Snapshot interval**: `SnapshotIntervalSeconds` (default 5 s) maps directly to the `PeriodicExportingMetricReader` period — one config value drives both OTel collection and TUI push cadence.

---

## 3. How the Agent Pushes Telemetry to the Control Plane

**Decision**: Add a new endpoint `POST /agents/lease/{leaseId}/telemetry` to the Control Plane. The Migration Agent's `ControlPlaneTelemetrySink` (background timer, not a `IProgressSink` impl) pushes a `TelemetrySnapshot` payload on this interval.

**Rationale**:
- Decouples telemetry from cursor progress. `POST /agents/lease/{leaseId}/progress` carries cursor state; telemetry is metric values that change at a different cadence.
- Separate endpoint means the control plane can store and serve them independently.
- The agent can push telemetry at 5-second intervals while progress is pushed after each cursor write. They should not share a transport to avoid blocking.

**Alternative considered**: Embedding metric values in the progress POST body — rejected because it entangles cursor semantics with metric aggregation cadence.

---

## 4. Cloud Provider Export (OTLP + Azure Monitor)

**Decision**: Support two exporter targets via configuration:
1. **OTLP** (`OpenTelemetry.Exporter.OpenTelemetryProtocol`) — compatible with Jaeger, Tempo, Prometheus, New Relic, Honeycomb, etc.
2. **Azure Monitor** (`Azure.Monitor.OpenTelemetry.AspNetCore`) — direct to Application Insights.

Both are configured via `TelemetryOptions` in `appsettings.json`. Zero or one or both may be active. If neither is configured, telemetry is collected and forwarded to the TUI pipeline only.

**Rationale**:
- OTLP is the vendor-neutral choice — covers most cloud providers.
- Azure Monitor is likely the primary target for this toolset's audience.
- Conditional registration (only wire if config key is present) keeps the default zero-config experience clean.

**Packages**:
- `OpenTelemetry.Exporter.OpenTelemetryProtocol` v1.12.0
- `Azure.Monitor.OpenTelemetry.AspNetCore` v1.4.0 (for the Migration Agent / Control Plane)
- For the .NET 4.8 subprocess: no new packages; metrics are relayed as `MetricSnapshot` fields in `ProgressEvent`.

---

## 5. TUI: SSE vs Polling

**Decision**: Polling for Phase 1. SSE (Server-Sent Events) for Phase 2 (optional upgrade).

**Rationale**:
- The TUI already polls `GET /jobs/{jobId}/progress` for cursor state. Adding `GET /jobs/{jobId}/telemetry` as a sibling polling endpoint is consistent with the existing TUI pattern.
- The TUI docs explicitly state "TUI has no persistent connection to the job" — polling matches this.
- SSE (or SignalR) is more complex to test and operate, and the 5-second snapshot interval makes the latency difference negligible.
- SSE can be added later as a `GET /jobs/{jobId}/telemetry/stream` endpoint without changing the polling endpoint.

**Polling interval**: 5 seconds (matching snapshot interval).

---

## 6. What Metrics to Surface in the TUI

**Decision**: Surface the following derived values from `WorkItemExportMetrics` and `AttachmentDownloadMetrics`:

| Display Label             | Source instrument                        |
|---------------------------|------------------------------------------|
| Work Items Exported       | `work_item_exported_total`               |
| Revisions Exported        | `revision_exported_total`                |
| Revision Errors           | `revision_export_errors_total`           |
| Links Exported            | `link_exported_total`                    |
| Link Errors               | `link_export_errors_total`               |
| Attachments Attempted     | `attachment_download_attempt_total`      |
| Attachments Succeeded     | `attachment_download_success_total`      |
| Attachments Failed        | `attachment_download_failure_total`      |
| Avg Work Item Duration ms | `work_item_export_duration_ms` — mean    |
| Avg Revision Duration ms  | `revision_export_duration_ms` — mean     |
| Total Export Duration ms  | `export_total_duration_ms`               |

These fit in a single Terminal.Gui table panel below the module/stage progress table.

---

## 7. Subprocess Metric Relay via ProgressEvent

**Decision**: Add optional `MetricSnapshot? Metrics` field to `ProgressEvent`. Populated by `WorkItemExportService` every N revisions or on a time interval (configurable, default every 100 revisions). `TfsExporterProcessAdapter` extracts the snapshot and forwards it as part of the telemetry pipeline.

**Rationale**:
- Zero new IPC mechanism required — the NDJSON stdout channel already exists.
- `ProgressEvent` is already `multi-targeted` via `Abstractions` which targets `netstandard2.0`, so the new field is available in both .NET 4.8 and .NET 10.
- This is additive — existing consumers that don't read `Metrics` continue to work.

**Alternatives considered**: Separate NDJSON message type on stdout — adds parsing complexity with no benefit at this scale.

---

## 8. Constitution Compliance

| Principle | Impact | Verdict |
|---|---|---|
| I. Package-First | None — telemetry is not migration data | ✅ N/A |
| II. Streaming | None | ✅ N/A |
| III. WorkItems Layout | None | ✅ N/A |
| IV. Checkpointing | None | ✅ N/A |
| V. Module Isolation | New sinks/services use `IProgressSink` + DI only | ✅ Compliant |
| VI. Separation of Planes | Agent pushes to CP via HTTP; TUI reads from CP; no migration logic in TUI | ✅ Compliant |
| VII. Determinism | N/A for telemetry | ✅ N/A |
| IX. SOLID & DI | All new services: constructor injection, `IOptions<TelemetryOptions>` | ✅ Must enforce |
