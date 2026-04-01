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

If no URL is available and no control plane is configured, the TUI falls back to local mode and displays the most recent local package logs from the filesystem.

### Authentication

The TUI forwards the same credential as all other CLI commands.

| Environment | Auth method |
|---|---|
| Entra ID (cloud or Entra-joined) | Bearer token acquired by `devopsmigration login`. Token is refreshed automatically. |
| On-premises Active Directory | Windows Integrated Auth (Negotiate). No explicit login step. |
| Local mode | No auth. No control plane. |

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

| Sink | Description | Phase |
|---|---|---|
| `ConsoleProgressSink` | Renders a live progress table in the terminal. | Phase 1 |
| `PackageProgressSink` | Writes structured events to `Logs/progress.jsonl` in the package. | Phase 1 |
| `ControlPlaneProgressSink` | Pushes events to the control plane for remote TUI display. | Phase 2 |

Phase 1 runs both `ConsoleProgressSink` and `PackageProgressSink` simultaneously. The Job Engine sees only `IProgressSink`; it does not know which sinks are active.

### ProgressEvent schema

```json
{
  "jobId": "550e8400-e29b-41d4-a716-446655440000",
  "module": "WorkItems",
  "stage": "AppliedFields",
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "timestamp": "2026-02-25T18:12:34Z"
}
```

---

## Status Display (Remote Mode)

When watching a remote job, the TUI polls the control plane and renders:

## Status Display (Remote Mode)

When watching a remote job, the TUI polls the control plane and renders:

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

The TUI has no persistent connection to the job. It only reads from the control plane on demand. When the TUI process exits or loses connectivity, the job continues running unaffected — the Migration Agent holds the lease independently.

Reconnecting is always safe and requires only the `jobId`. See [docs/cli.md](cli.md) for the `status` and `logs` commands.

In local mode the Job Engine runs in the same process as the CLI. If the process exits, the Job Engine stops. The cursor in the package ensures the Job Engine picks up from the last completed stage when the command is re-run.
