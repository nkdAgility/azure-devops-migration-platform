# OTel Exporter Configuration Contract

## `appsettings.json` — `Telemetry` section

```json
{
  "Telemetry": {
    "OtlpEndpoint": "http://localhost:4317",
    "AzureMonitorConnectionString": "InstrumentationKey=...",
    "SnapshotIntervalSeconds": 5,
    "SubprocessSnapshotRevisionInterval": 100
  }
}
```

| Field | Type | Required | Description |
|---|---|---|---|
| `OtlpEndpoint` | `string` | No | OTLP gRPC endpoint. If absent or empty, OTLP exporter is not registered. |
| `AzureMonitorConnectionString` | `string` | No | Application Insights connection string. If absent or empty, Azure Monitor exporter is not registered. |
| `SnapshotIntervalSeconds` | `int` | No | How often (seconds) the Migration Agent pushes a `MetricSnapshot` to the Control Plane. Default: `5`. |
| `SubprocessSnapshotRevisionInterval` | `int` | No | How often (by revision count) the TFS subprocess embeds a `MetricSnapshot` in a `ProgressEvent` on stdout. Default: `100`. |

---

## Exporter Priority

Both exporters are independent and additive. Configuring both sends telemetry to both
destinations simultaneously. Configuring neither results in metrics and traces being
collected in-process for the TUI pipeline only.

## Environment Variable Overrides (standard .NET config)

Both fields may be overridden via environment variables using the standard `__` separator:

```sh
Telemetry__OtlpEndpoint=http://otel-collector:4317
Telemetry__AzureMonitorConnectionString=InstrumentationKey=...
```

## What is Exported

| Signal | OTLP | Azure Monitor |
|---|---|---|
| Traces | ✅ `WorkItemExport`, `AttachmentDownload` activity sources | ✅ (auto-mapped to operations) |
| Metrics | ✅ `DevOpsMigrationPlatform.WorkItemExport` + `.AttachmentDownload` meters | ✅ (auto-mapped to custom metrics) |
| Logs | Not wired in Phase 1 (Serilog writes to file; OTLP log export is Phase 2) | ✅ if `AddAzureMonitorLogExporter()` added |

## Resource Attributes (always present)

Attached to every exported span and metric point:

| Attribute | Value |
|---|---|
| `service.name` | `TfsExport` (subprocess) or `MigrationAgent` (agent) |
| `session.id` | GUID suffix of the session |
| `tfs.server` | TFS server URI |
| `tfs.project` | Project name |
