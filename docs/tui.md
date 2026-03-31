# TUI (Terminal UI)

## Purpose

The TUI is the visual progress layer rendered in the terminal during a migration. It subscribes to structured progress events emitted by the Job Engine and renders them as a live progress display. It contains no migration logic and no command routing.

Command parsing, mode selection, and job dispatch are handled by the CLI shell. See [docs/cli.md](cli.md).

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
