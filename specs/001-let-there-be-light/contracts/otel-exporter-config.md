# OTel Exporter Configuration Contract

## `appsettings.json` — `Telemetry` section

```json
{
  "Telemetry": {
    "AzureMonitorConnectionString": "InstrumentationKey=...",
    "SnapshotIntervalSeconds": 5,
    "SubprocessSnapshotRevisionInterval": 100
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `AzureMonitorConnectionString` | `string` | No | Application Insights connection string. If absent or empty, Azure Monitor exporter is not registered. |
| `SnapshotIntervalSeconds` | `int` | No | How often (seconds) the Migration Agent pushes a `MetricSnapshot` to the Control Plane. Default: `5`. |
| `SubprocessSnapshotRevisionInterval` | `int` | No | How often (by revision count) the TFS subprocess embeds a `MetricSnapshot` in a `ProgressEvent` on stdout. Default: `100`. |

> **OTLP is not configured here.** Use the standard OTel environment variable:
> ```sh
> OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4317
> ```
> This is handled by `ServiceDefaults.AddOpenTelemetryExporters()` via `.UseOtlpExporter()`.
> Configuring OTLP both via `TelemetryOptions` and via the env var would register it twice.

---

## Exporter Priority

Both exporters are independent and additive. Configuring both sends telemetry to both
destinations simultaneously. Configuring neither results in metrics and traces being
collected in-process for the TUI pipeline only (via `SnapshotMetricExporter`).

## Environment Variable Overrides (standard .NET config)

`AzureMonitorConnectionString` may be overridden via environment variable:

```sh
Telemetry__AzureMonitorConnectionString=InstrumentationKey=...
```

## What is Exported

| Signal | OTLP (via env var) | Azure Monitor (via `TelemetryOptions`) |
|---|---|---|
| Traces | ✅ `WorkItemExport`, `AttachmentDownload` activity sources | ✅ (auto-mapped to operations) |
| Metrics | ✅ `DevOpsMigrationPlatform.WorkItemExport` + `.AttachmentDownload` meters | ✅ (auto-mapped to custom metrics) |
| Logs | ✅ if `OTEL_EXPORTER_OTLP_ENDPOINT` set (OTel log bridge via ServiceDefaults) | Phase 2 only |

## Resource Attributes (always present)

Attached to every exported span and metric point:

| Attribute | Value |
|---|---|
| `service.name` | `TfsExport` (subprocess) or `MigrationAgent` (agent) |
| `session.id` | GUID suffix of the session |
| `tfs.server` | TFS server URI |
| `tfs.project` | Project name |
