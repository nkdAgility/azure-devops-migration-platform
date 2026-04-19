# Quickstart: Telemetry Pipeline

## Local mode (standalone — no cloud exporter)

Metrics and traces are collected in-process and forwarded to the TUI via the Control Plane.
No external configuration is required.

Start the full stack:
```sh
devopsmigration run --config migration.yml
```

The TUI displays a live metrics panel automatically. No additional setup needed.

---

## Enable OTLP export (to Jaeger, Tempo, Prometheus, etc.)

Add to `appsettings.json` (or environment variable) for the Migration Agent:
```json
{
  "Telemetry": {
    "OtlpEndpoint": "http://localhost:4317"
  }
}
```

Or via environment variable:
```sh
Telemetry__OtlpEndpoint=http://localhost:4317 devopsmigration run --config migration.yml
```

Traces and metrics will be exported over gRPC to the specified OTLP endpoint in addition to
appearing in the TUI.

---

## Enable Azure Monitor (Application Insights) export

1. Get your Application Insights connection string from the Azure portal.
2. Add to `appsettings.json`:
   ```json
   {
     "Telemetry": {
       "AzureMonitorConnectionString": "InstrumentationKey=00000000-0000-0000-0000-000000000000;..."
     }
   }
   ```

Traces, metrics, and (in Phase 2) logs will appear in your Application Insights resource.
Custom metric names:
- `work_item_exported_total`
- `revision_exported_total`
- `attachment_download_success_total`
- etc. (see `contracts/otel-exporter-config.md` for the full list)

---

## TUI Metrics Panel

When a job is running, the TUI renders a metrics panel below the module progress table:

```
Metrics (as of 10:15:30)
────────────────────────────────────────────────────────────
Work Items Exported    : 1,247     Revision Errors          : 2
Revisions Exported     : 8,943     Link Errors              : 0
Links Exported         : 12,034    Attachments Failed       : 2
Attachments Attempted  : 543       Avg Work Item Duration   : 95 ms
Attachments Succeeded  : 541       Avg Revision Duration    : 19 ms
```

The panel refreshes every 5 seconds (configurable via `Telemetry:SnapshotIntervalSeconds`).

---

## Adjusting snapshot frequency

Reduce control-plane traffic on slow connections:
```json
{
  "Telemetry": {
    "SnapshotIntervalSeconds": 30
  }
}
```

Reduce subprocess stdout chatter for very large exports:
```json
{
  "Telemetry": {
    "SubprocessSnapshotRevisionInterval": 500
  }
}
```
