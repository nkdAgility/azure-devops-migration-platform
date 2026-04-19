# Quickstart: Verify OTel Observability and Live Progress Streaming

**Feature**: `002-otel-observability-phase2`

This guide shows how to verify the two capabilities of this feature end-to-end in a local Aspire environment. It assumes all P1 and P2 implementation is complete.

---

## Prerequisites

- .NET 10 SDK
- `dotnet run` access to `DevOpsMigrationPlatform.AppHost` (Aspire orchestrator)
- A valid `migration.json` configuration for a TFS source (or a short mock job)
- Optional: Azure Monitor connection string for CLI telemetry validation

---

## 1 — Verify CLI OTel (User Story 1)

### 1a. Without Azure Monitor (smoke test)

Run any CLI command. It should complete normally with no errors, even without an Azure Monitor connection string:

```powershell
cd src/DevOpsMigrationPlatform.CLI.Migration
dotnet run -- discover --config migration.json
```

**Expected**: Command runs and exits. No OTel errors in stderr. No delay beyond normal startup.

### 1b. With Azure Monitor

Set the connection string in `appsettings.json` or as an environment variable:

```json
// appsettings.json
{
  "Telemetry": {
    "AzureMonitorConnectionString": "InstrumentationKey=00000000-..."
  }
}
```

Or via environment variable:
```powershell
$env:Telemetry__AzureMonitorConnectionString = "InstrumentationKey=00000000-..."
dotnet run -- discover --config migration.json
```

**Expected**: Within ~60 seconds, a trace span named after the command (e.g. `discover`) appears in Application Insights → Live Metrics or Transaction Search.

---

## 2 — Verify Progress Streaming (User Story 2)

### 2a. Start the platform

```powershell
cd src/DevOpsMigrationPlatform.AppHost
dotnet run
```

Aspire opens the dashboard at `http://localhost:18888`. The Control Plane runs at `http://localhost:5100` and the Migration Agent at `http://localhost:5101`.

### 2b. Submit a job and get the job ID

```powershell
$jobId = (Invoke-RestMethod -Method POST -Uri http://localhost:5100/jobs `
  -ContentType application/json `
  -Body (Get-Content migration.json -Raw)).jobId

Write-Host "Job ID: $jobId"
```

### 2c. Wait a few seconds, then check the snapshot

```powershell
Invoke-RestMethod -Uri "http://localhost:5100/jobs/$jobId/logs" | ConvertTo-Json -Depth 4
```

**Expected**: A JSON array containing at least one `ProgressEvent` object with `module`, `stage`, `workItemsProcessed`, and `timestamp` fields populated.

---

## 3 — Verify `migrate logs` Command (User Story 3)

### 3a. Snapshot mode

```powershell
cd src/DevOpsMigrationPlatform.CLI.Migration
dotnet run -- logs --job $jobId
```

**Expected**: One compact JSON object per line (NDJSON) printed to stdout, then the process exits with code 0.

### 3b. Follow mode (live tail)

While the job is still running:

```powershell
dotnet run -- logs --job $jobId --follow
```

**Expected**:
- New NDJSON lines appear in the terminal as the agent processes revisions.
- When the job completes, the stream closes and the CLI exits with code 0.
- Pressing Ctrl+C during streaming exits the CLI without affecting the running job (verify via `GET /jobs/{jobId}` — job state should still be `Running`).

### 3c. Unknown job

```powershell
dotnet run -- logs --job 00000000-0000-0000-0000-000000000000
```

**Expected**: Error message printed to stderr, exit code non-zero.

---

## 4 — Verify Ring Buffer Eviction

Submit a job and flood it with synthetic POST events using the agent protocol:

```powershell
$leaseId = "<leaseId-from-GET-agents-lease>"

1..1200 | ForEach-Object {
    $body = @{ module = "WorkItems"; stage = "AppliedFields"; workItemsProcessed = $_; timestamp = (Get-Date).ToUniversalTime().ToString("o") } | ConvertTo-Json
    Invoke-RestMethod -Method POST -Uri "http://localhost:5100/agents/lease/$leaseId/progress" -ContentType application/json -Body $body
}

$snapshot = Invoke-RestMethod -Uri "http://localhost:5100/jobs/$jobId/logs"
Write-Host "Snapshot count: $($snapshot.Count)"
```

**Expected**: `snapshot.Count` is 1000 (not 1200) — the oldest 200 events were evicted. The most recent 1000 events are retained.

---

## 5 — Check the Aspire Dashboard

Open `http://localhost:18888`:

- **Traces**: The `CLI.Migration` process should emit spans visible in the OpenTelemetry trace view (if the Aspire OTLP endpoint is configured).
- **Metrics**: The Migration Agent's work item export meters should appear in the Metrics tab.
- **Logs**: Structured log entries from all services should be aggregated in the Logs tab.

---

## Troubleshooting

| Symptom | Check |
|---|---|
| `GET /logs` returns empty array | Verify agent is running and `ControlPlane:BaseUrl` is set in `appsettings.json` for the agent |
| `migrate logs --follow` exits immediately | Job may already be in a terminal state; check `GET /jobs/{jobId}` |
| No spans in Azure Monitor | Verify `Telemetry:AzureMonitorConnectionString` is set and non-empty |
| CLI exits with error on OTel init | Check that the `Azure.Monitor.OpenTelemetry.Exporter` package is referenced in `CLI.Migration.csproj` |
