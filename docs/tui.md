# TUI (Terminal UI)

## Purpose

The TUI is the operator's entry point to the migration platform. It is a **thin shell** — it parses arguments, builds a `MigrationJob`, chooses a transport, and renders progress. It contains no migration logic.

Migration logic lives exclusively in the **Job Engine**. The TUI calls the Job Engine through one of two transports:

- `LocalJobRunner` — executes the Job Engine in-process (no control plane required).
- `ControlPlaneClient` — submits the job to the control plane and polls for progress.

Both transports accept the same `MigrationJob` payload. Adding the control plane later requires no changes to the Job Engine or to command parsing.

---

## Three-Layer Architecture

```
┌─────────────────────────────────────────┐
│  TUI Shell                              │
│  - Parses args                          │
│  - Loads config                         │
│  - Builds MigrationJob                  │
│  - Renders progress via IProgressSink   │
└────────────────┬────────────────────────┘
                 │  MigrationJob
       ┌─────────┴───────────┐
       │                     │
┌──────▼──────┐   ┌──────────▼──────────┐
│ LocalJob    │   │ ControlPlane        │
│ Runner      │   │ Client              │
│ (in-process)│   │ (stub → Phase 2)    │
└──────┬──────┘   └──────────┬──────────┘
       │                     │
       └────────┬────────────┘
                │
┌───────────────▼─────────────────────────┐
│  Job Engine                             │
│  - Validates job                        │
│  - Resolves module dependency graph     │
│  - Runs Export / Import / Both          │
│  - Writes package via IArtefactStore    │
│  - Writes checkpoints via IStateStore   │
│  - Emits progress via IProgressSink     │
└─────────────────────────────────────────┘
```

The Job Engine has no reference to the TUI, the console, or any progress renderer. It emits structured progress events; the TUI subscribes and renders them.

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

## Execution Modes

### Local Mode

The TUI calls `LocalJobRunner`, which executes the Job Engine directly in-process.

- `IArtefactStore` is `FileSystemArtefactStore`.
- `IStateStore` is `PackageCheckpointStateStore` (writes `Checkpoints/` inside the package).
- No control plane required.
- Suitable for development, testing, and offline migrations.

### Remote Mode

The TUI calls `ControlPlaneClient`, which submits the job to the control plane.

- The Job Engine runs inside a Migration Agent container.
- `IArtefactStore` is `AzureBlobArtefactStore` (or any URI-addressable store).
- Requires a configured control plane endpoint (`MIGRATION_API_URL` or equivalent).
- Progress is rendered by polling the control plane.

**Phase 1:** `ControlPlaneClient` is a stub that returns `NotImplementedException`. The command parses correctly; only execution is deferred.

---

## Commands

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

## Local Execution as "Both" Mode

Direct Azure DevOps → Azure DevOps migration in local mode:

```
migrate both --config migration.json
```

This runs the full `Source → Files → Target` pipeline in-process:

1. `LocalJobRunner` receives the `MigrationJob`.
2. Job Engine runs `ExportAsync` for each module.
3. Job Engine runs the validation pass.
4. Job Engine runs `ImportAsync` for each module.

No control plane. No Migration Agent. Same job contract, same engine, same cursors, same package format.

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


1. Reads and validates the local config file.
2. Computes `configHash`.
3. Constructs a job contract ([docs/job-contract.md](job-contract.md)) from the config.
4. Posts the job contract to `POST /jobs` on the control plane.
5. Displays the `jobId` and returns.

The local config file is not sent directly; it is transformed into a job contract. Secret values are replaced by Key Vault URI references before submission.
