# TUI (Terminal UI)

## Purpose

The TUI is the visual progress layer rendered in the terminal during a migration. It subscribes to structured progress events emitted by the Job Engine and renders them as a live progress display. It contains no migration logic and no command routing.

Command parsing, mode selection, and job dispatch are handled by the CLI shell. See [docs/cli.md](cli.md).

---

## Launching the TUI

```
devopsmigration tui [--url <control-plane-url>] [--job <jobId>]
```

| Flag | Description |
|---|---|
| `--url` | Override the control plane URL. Defaults to `MIGRATION_API_URL` or the value stored by `devopsmigration login`. |
| `--job` | Jump directly to the progress view for a specific job, bypassing the job list. |

The TUI always requires a control plane connection. When no `--url` flag or `MIGRATION_API_URL` environment variable is set, the CLI will have already driven Aspire to start `ControlPlaneHost` locally at `http://localhost:5100` — the TUI connects there automatically. If no control plane is reachable the TUI exits with an actionable error.

### Authentication

The TUI forwards the same credential as all other CLI commands.

| Environment | Auth method |
|---|---|
| Entra ID (cloud or Entra-joined) | Bearer token acquired by `devopsmigration login`. Token is refreshed automatically. |
| On-premises Active Directory | Windows Integrated Auth (Negotiate). No explicit login step. |

The TUI never prompts for credentials itself. Run `devopsmigration login` if the token is missing or expired before launching the TUI.

### Job List View

On launch (without `--job`), the TUI displays the job list filtered by the caller's auth context — the same rules as `GET /jobs` on the control plane:

- A regular user sees their own jobs plus any `Tenant`-visibility jobs in their tenant.
- A Control Plane Admin sees all jobs. A tenant filter is available in the UI.

Selecting a job enters the progress view.

---

## Technology

The TUI is built with **[Terminal.Gui](https://github.com/gui-cs/Terminal.Gui)**. All interactive terminal windows, panels, progress tables, and live-updating views are rendered through the `Terminal.Gui` widget model.

Terminal.Gui is the only permitted UI rendering library in TUI code. Do not use `System.Console`, ANSI escape sequences, or Spectre.Console widgets inside TUI view classes.

---

## IProgressSink

Progress is event-driven. The Job Engine emits `ProgressEvent` records; sinks consume them.

```csharp
interface IProgressSink
{
    void Emit(ProgressEvent e);
}
```

### Implementations

| Sink | Description |
|---|---|
| `ConsoleProgressSink` | Renders a live progress log in the terminal (local CLI output). |
| `PackageProgressSink` | Writes structured events to `Logs/progress.jsonl` in the package (always active). |
| `ControlPlaneProgressSink` | POSTs each event to `POST /agents/lease/{leaseId}/progress` for real-time TUI streaming. |

All three sinks run simultaneously when the Migration Agent holds a lease. The Job Engine sees only `IProgressSink`; it does not know which sinks are active.

`ControlPlaneProgressSink` is best-effort — transient failures are dropped and logged at debug level. Job execution is never blocked by a sink failure.

### ProgressEvent schema

```json
{
  "module": "WorkItems",
  "stage": "AppliedFields",
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "totalWorkItems": 1500,
  "workItemsProcessed": 312,
  "revisionsProcessed": 874,
  "workItemId": 12345,
  "message": "Exporting revision 17",
  "timestamp": "2026-02-25T18:12:34Z"
}
```

The `jobId` is not part of the `ProgressEvent` record — it is carried by the lease endpoint that receives the event (`POST /agents/lease/{leaseId}/progress`), allowing the control plane to resolve the job from the lease.

---

## Status Display (Remote Mode)

When watching a remote job, the TUI renders two independent data streams:

| Stream | Endpoint | Update mechanism |
|---|---|---|
| **Metrics panel** (counts, rates) | `GET /jobs/{jobId}/telemetry` | Polling (interval configurable, default 5 s) |
| **Progress table** (module stages, last processed) | `GET /jobs/{jobId}/progress?follow=true` | Server-Sent Events (SSE) — push |
| **Diagnostics panel** (structured diagnostic logs) | `GET /jobs/{jobId}/diagnostics?follow=true` | Server-Sent Events (SSE) — push |

The progress table subscribes to the SSE stream on job entry. The TUI reconnects automatically with exponential back-off (max 30 s) on connection loss. Each `ProgressEvent` arriving on the stream updates the matching module row in the table.

When watching a remote job, the TUI renders:

```
Job:      550e8400-e29b-41d4-a716-446655440000
Mode:     Both
State:    Running
Agent:    migration-agent-pod-7d9f4

Module    Stage                Last Processed
────────  ───────────────────  ──────────────────────────────────────────────────
WorkItems AppliedFields        WorkItems/2026-02-25/638760123456789012-12345-17
Teams     Completed            Teams/team-0045
```

Progress data comes from the control plane's latest cursor mirror. The authoritative resume state remains the cursor in the package.

---

## TUI Disconnection

The TUI holds an SSE connection for the progress table (see Status Display above) but this connection does not affect the running job. The Migration Agent holds the lease independently of any connected TUI. When the TUI process exits or loses connectivity, the job continues running unaffected.

Reconnecting is always safe and requires only the `jobId`. The TUI will re-subscribe to the SSE stream and receive events from the ring buffer (last 1000 events) on reconnect. See [docs/cli.md](cli.md) for the `status` and `logs` commands.
