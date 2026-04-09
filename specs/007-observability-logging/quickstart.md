# Quickstart: Three-Channel Observability

## What Changed

The Migration Agent now produces three independent observability streams during job execution:

| Channel | What it answers | Where it's stored | How to view live |
|---|---|---|---|
| **Event Progress** | "What has been done? What's the current state?" | `Logs/progress.jsonl` in package | TUI progress table |
| **Diagnostics** | "What went wrong? Why? What should I do?" | `Logs/agent.jsonl` in package | TUI diagnostics panel, or `export --follow` |
| **OTel Signals** | "How long did it take? Where are the bottlenecks?" | OTLP / Azure Monitor | Aspire Dashboard, Azure Monitor, Grafana |

## For Operators

### Run an export with diagnostic logging

```bash
# Standalone mode (local Aspire) — --follow is implicit, logs stream to console
devopsmigration export --config migration.json --level Warning

# Remote control plane — submit and follow logs inline
devopsmigration export --config migration.json --url https://migration.example.com --follow --level Warning

# Remote control plane — submit and exit (fire-and-forget)
devopsmigration export --config migration.json --url https://migration.example.com
# Prints: Job 550e8400-e29b-41d4-a716-446655440000 submitted. Use TUI to watch.
```

### Debug-level export (more detail in the package)

```bash
# Agent writes Debug+ to package; standalone CP also at Debug
devopsmigration export --config migration.json --level Debug

# Remote CP: agent writes Debug+ to package, but CP only buffers Warning+
# Use manage diagnostics after completion to get full Debug detail from package
devopsmigration export --config migration.json --url https://migration.example.com --follow --level Debug
```

### View diagnostics from a completed job

```bash
# Download diagnostic logs from the package via control plane
devopsmigration manage diagnostics --job 550e8400-e29b-41d4-a716-446655440000 --level Warning

# Or read the package file directly (local mode)
cat output/0.0.1-007-export.1/Logs/agent.jsonl | jq 'select(.level == "Error" or .level == "Warning")'
```

### View progress events snapshot

```bash
devopsmigration manage progress --job 550e8400-e29b-41d4-a716-446655440000
```

### Watch live jobs, events, and logs

All live observation is through the TUI:

```bash
# Opens the TUI with job list, metrics, progress, and live diagnostics panel
devopsmigration tui
devopsmigration tui --url https://migration.example.com
```

### Download log files from a remote job

```bash
# Download progress events
curl -o progress.jsonl "https://migration.example.com/jobs/{jobId}/logs/download?type=progress"

# Download diagnostic logs
curl -o agent.jsonl "https://migration.example.com/jobs/{jobId}/logs/download?type=diagnostics"
```

## For Developers

### New types

- `DiagnosticLogRecord` in `DevOpsMigrationPlatform.Abstractions` — structured log record with level, category, message, exception, traceId.
- `PackageLoggerProvider` in `DevOpsMigrationPlatform.Infrastructure` — `ILoggerProvider` that buffers and writes NDJSON to `IArtefactStore`.
- `ControlPlaneLoggerProvider` in `DevOpsMigrationPlatform.Infrastructure` — `ILoggerProvider` that buffers and POSTs batches to the control plane.
- `DiagnosticLogStore` in `DevOpsMigrationPlatform.ControlPlane` — in-memory ring buffer for diagnostic records per job.
- `DiagnosticsController` in `DevOpsMigrationPlatform.ControlPlane` — REST endpoints for diagnostic record push and streaming.

### IArtefactStore change

`AppendAsync(string path, string content, CancellationToken ct)` added to `IArtefactStore`. Both `FileSystemArtefactStore` and `AzureBlobArtefactStore` must implement it. This is additive — no existing callers are affected.

### Endpoint renames

| Old | New | Notes |
|---|---|---|
| `GET /jobs/{jobId}/logs` | `GET /jobs/{jobId}/progress` | Progress events |
| `GET /jobs/{jobId}/logs?follow=true` | `GET /jobs/{jobId}/progress?follow=true` | Progress SSE (TUI) |
| `ILogsClient` | `IProgressClient` | |
| `ManageLogsCommand` (`manage logs`) | `ManageDiagnosticsCommand` (`manage diagnostics`) | Downloads from completed job package |
| *(new)* | `ManageProgressCommand` (`manage progress`) | Progress snapshot, no `--follow` |
| *(new)* | `ExportCommand --follow --level` | Inline diagnostic streaming during export |
