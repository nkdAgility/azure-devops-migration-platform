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

### Local Commands

| Command | Description |
|---|---|
| `prepare` | Validate the config, compute `configHash`, print a job summary and planned modules. No execution. |
| `export` | Run export via `LocalJobRunner`. Writes the package to the URI in `artefacts.packageUri`. |
| `import` | Run import via `LocalJobRunner`. Reads the package from `artefacts.packageUri`. |
| `both` | Run export → validate → import via `LocalJobRunner`. |
| `validate` | Run pre-flight validation on an existing package. See [docs/validation.md](validation.md). |
| `pack` | Compress `PackageRoot/` into a zip file. See [docs/packaging-zip.md](packaging-zip.md). |
| `unpack` | Extract a zip file into `PackageRoot/`. |

```
migrate prepare --config migration.json
migrate export  --config migration.json
migrate both    --config migration.json
migrate validate --package file:///D:/exports/run-001
migrate pack    --package file:///D:/exports/run-001 --out run-001.zip
```

All commands accept `--local` to force in-process execution regardless of environment config.

### Remote Commands (Control Plane)

| Command | Description |
|---|---|
| `queue` | Convert config to `MigrationJob`, submit to control plane, return `jobId`. |
| `status` | Display job state and per-module progress from the control plane. |
| `logs` | Tail or page job logs from the control plane. |
| `pause` | Signal the running Migration Agent to checkpoint and pause. |
| `resume` | Resume a paused job (re-queues it for Migration Agent pickup). |
| `cancel` | Cancel a queued or running job. |

```
migrate queue  --config migration.json
migrate status --job 550e8400-e29b-41d4-a716-446655440000
migrate logs   --job 550e8400-e29b-41d4-a716-446655440000 --follow
migrate pause  --job 550e8400-e29b-41d4-a716-446655440000
migrate resume --job 550e8400-e29b-41d4-a716-446655440000
migrate cancel --job 550e8400-e29b-41d4-a716-446655440000
```

All remote commands call `ControlPlaneClient`. In Phase 1 they print `"Remote execution not yet implemented"` and exit with code 2.

---

## Mode Selection

| Condition | Behaviour |
|---|---|
| `--local` flag | Always uses `LocalJobRunner`. |
| `--remote` flag | Always uses `ControlPlaneClient` (fails if `MIGRATION_API_URL` not set). |
| `MIGRATION_API_URL` set | Defaults to `ControlPlaneClient` for `export`/`import`/`both`. |
| Neither flag nor env var | Defaults to `LocalJobRunner`. |

This means a developer can run `migrate both --config migration.json` locally without any cloud infrastructure, and the same config in a CI/CD pipeline with `MIGRATION_API_URL` set will submit to the control plane.

---

## Config → MigrationJob Conversion

Before any execution, the TUI converts the local config file into a `MigrationJob`:

1. Read and validate the config file schema.
2. Compute `configHash` (SHA-256 of the normalised config JSON).
3. Generate a fresh `jobId` (UUID v4).
4. Normalise `artefacts.path` to a URI (`file:///` prefix if a bare filesystem path is given).
5. Replace inline credentials with Key Vault URI references (remote mode only).
6. Construct the `MigrationJob` with `guardrails` set to their required values.

The local config file is never sent directly anywhere. The `MigrationJob` is the only artefact that crosses boundaries. See [docs/job-contract.md](job-contract.md).

---

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
