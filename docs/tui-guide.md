# TUI (Terminal UI)

## Purpose

The TUI is the visual progress layer rendered in the terminal during a migration. It subscribes to structured progress events and metrics **from the ControlPlane API**. It contains no migration logic, no command routing, and no direct connection to any `IProgressSink`.

Command parsing, mode selection, and job dispatch are handled by the CLI shell. See [docs/cli-guide.md](cli.md).

The exact target mode-to-workspace contract lives in [ui-mode-contract.md](ui-mode-contract.md). This guide remains the operator guide for launch, authentication, data-source rules, and the current TUI shell; the UI mode contract records the intended future-state TUI workspace behaviour.

---

## Data Sourcing Contract ⛔ MANDATORY

The TUI is **never** in the same process as the Migration Agent. All display data comes exclusively from the ControlPlane API via three independent streams:

| Panel | Source | Mechanism |
|-------|--------|-----------|
| **Metrics panel** (counters, rates) | `GET /jobs/{jobId}/telemetry` | Polling every ~5 s — returns `JobMetrics` |
| **Progress table** (module stages, cursor) | `GET /jobs/{jobId}/progress?follow=true` | Server-Sent Events (SSE) push — returns `ProgressEvent` stream |
| **Log/Diagnostics panel** | `GET /jobs/{jobId}/diagnostics?follow=true` | Server-Sent Events (SSE) push — structured log records |

TUI code MUST NOT:
- Inject or subscribe to `IProgressSink` directly.
- Read aggregate counters from `ProgressEvent.Metrics` — that field is only populated by the TFS subprocess (net481) and is always null for .NET 10 agents.
- Maintain any in-process metric state. The ControlPlane is the single source of truth.

---

## Launching the TUI

```
devopsmigration tui [--job <jobId>]
```

| Flag | Description |
|---|---|
| `--job` | Jump directly to the progress view for a specific job, bypassing the job list. |

The TUI always requires a control plane connection. The control plane URL is resolved from the `Environment.ControlPlane.BaseUrl` configuration section. When `Environment.Type` is `Standalone` (the default), `LocalStackHost` starts the control plane at `http://localhost:5100` (preferring process-per-component mode when published binaries are found, with in-process fallback otherwise) — the TUI connects there automatically. If no control plane is reachable the TUI exits with an actionable error.

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
| `PackageProgressSink` | Writes structured events to `.migration/Logs/progress.jsonl` in the package (always active). |
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

## Single-Screen Three-Panel Layout

The TUI uses a **single-screen three-panel layout** — all panels are always visible in one `Window`. There is no separate detail screen or navigation stack.

This is the current implementation shell. The target mode-driven workspaces that should occupy that shell are defined in [ui-mode-contract.md](ui-mode-contract.md).

| Panel | Approximate Width | Content |
|---|---|---|
| **Job List** (left) | ~30% | Scrollable table of jobs: short Job ID, state, mode, submission timestamp |
| **Metrics Panel** (center) | ~35% | Per-module counters, throughput rates, and duration means for the selected job |
| **Log Panel** (right) | ~35% | Live `ProgressEvent` stream or structured diagnostic log stream for the selected job |

**Selecting a job** (arrow keys + Enter in the Job List) updates the Metrics and Log panels in place. No navigation occurs. **Deselecting** (Escape from the Job List) cancels all active SSE subscriptions immediately and clears both panels — no background threads remain subscribed.

**Log Panel toggle**: Pressing Tab within the Log Panel switches between:
- `[Progress]` mode — subscribes to `GET /jobs/{jobId}/progress?follow=true` (SSE)
- `[Diagnostics]` mode — subscribes to `GET /jobs/{jobId}/diagnostics?follow=true` (SSE)

The current mode is shown in the panel header. Toggling cancels the current SSE stream and starts the new one.

**Terminal state behaviour**: When a job reaches a terminal state (Completed, Failed, Cancelled), the Log Panel appends a final status marker (e.g. `── Job Completed ──`) and ceases all reconnection attempts. The Metrics Panel retains the last polled values.

---



## Status Display (Remote Mode)

When watching a remote job, the TUI renders **three independent data streams**:

| Stream | Endpoint | Update mechanism |
|---|---|---|
| **Metrics panel** (counts, rates) | `GET /jobs/{jobId}/telemetry` | Polling (interval configurable, default 5 s) |
| **Progress table** (module stages, last processed) | `GET /jobs/{jobId}/progress?follow=true` | Server-Sent Events (SSE) — push |
| **Diagnostics panel** (structured diagnostic logs) | `GET /jobs/{jobId}/diagnostics?follow=true` | Server-Sent Events (SSE) — push |

The progress table subscribes to the SSE stream on job entry. The TUI reconnects automatically with exponential back-off (max 30 s) on connection loss. Each `ProgressEvent` arriving on the stream updates the matching module row in the table.

When watching a remote job, the TUI renders:

```
Job:      550e8400-e29b-41d4-a716-446655440000
Mode:     Migrate
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

Reconnecting is always safe and requires only the `jobId`. The TUI will re-subscribe to the SSE stream and receive events from the ring buffer (last 1000 events) on reconnect. See [docs/cli-guide.md](cli.md) for the `status` and `logs` commands.
