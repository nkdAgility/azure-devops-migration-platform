# CLI

## Purpose

The CLI is the operator's entry point to the migration platform. It is a **thin shell** — it parses arguments, builds a `MigrationJob`, chooses a transport, and delegates execution to the Job Engine. It contains no migration logic.

Migration logic lives exclusively in the **Job Engine**. The CLI calls the Job Engine through one of two transports:

---

## Technology

The CLI is built with **[Spectre.Console](https://spectreconsole.net/)** (`Spectre.Console.Cli`). All command definitions, argument/option parsing, help text, and console output formatting use Spectre.Console primitives.

Spectre.Console is the only permitted CLI library in command-layer code. Do not reference `System.CommandLine`, `McMaster.Extensions.CommandLineUtils`, or any other argument-parsing library in this layer.

- `LocalJobRunner` — executes the Job Engine in-process (no control plane required).
- `ControlPlaneClient` — submits the job to the control plane and polls for progress.

Both transports accept the same `MigrationJob` payload. Adding the control plane later requires no changes to the Job Engine or to command parsing.

See [docs/tui.md](tui.md) for how progress is rendered in the terminal.

---

## Architecture

```
┌─────────────────────────────────────────┐
│  CLI Shell                              │
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

The Job Engine has no reference to the CLI, the console, or any progress renderer. It emits structured progress events; sinks consume them.

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
migrate prepare  --config migration.json
migrate export   --config migration.json
migrate both     --config migration.json
migrate validate --package file:///D:/exports/run-001
migrate pack     --package file:///D:/exports/run-001 --out run-001.zip
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

Before any execution, the CLI converts the local config file into a `MigrationJob`:

1. Read and validate the config file schema.
2. Compute `configHash` (SHA-256 of the normalised config JSON).
3. Generate a fresh `jobId` (UUID v4).
4. Normalise `artefacts.path` to a URI (`file:///` prefix if a bare filesystem path is given).
5. Replace inline credentials with Key Vault URI references (remote mode only).
6. Construct the `MigrationJob` with `guardrails` set to their required values.

The local config file is never sent directly anywhere. The `MigrationJob` is the only artefact that crosses boundaries. See [.agents/context/job-contract.md](../.agents/context/job-contract.md).

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

## Execution Modes

### Local Mode

The CLI calls `LocalJobRunner`, which executes the Job Engine directly in-process.

- `IArtefactStore` is `FileSystemArtefactStore`.
- `IStateStore` is `PackageCheckpointStateStore` (writes `Checkpoints/` inside the package).
- No control plane required.
- Suitable for development, testing, and offline migrations.

### Remote Mode

The CLI calls `ControlPlaneClient`, which submits the job to the control plane.

- The Job Engine runs inside a Migration Agent container.
- `IArtefactStore` is `AzureBlobArtefactStore` (or any URI-addressable store).
- Requires a configured control plane endpoint (`MIGRATION_API_URL` or equivalent).
- Progress is rendered by polling the control plane.

**Phase 1:** `ControlPlaneClient` is a stub that returns `NotImplementedException`. The command parses correctly; only execution is deferred.

---

## Reconnecting to a Remote Job

When the CLI process exits or loses connectivity, the job continues running unaffected. The Migration Agent holds the lease independently of the CLI process. To reconnect:

```
migrate status --job 550e8400-e29b-41d4-a716-446655440000
migrate logs   --job 550e8400-e29b-41d4-a716-446655440000 --follow
```

The `jobId` is the only thing needed. It is printed by `queue` at submission time. Keep it.

### If You Lost the jobId

If the `jobId` was not recorded, retrieve it from the control plane by config hash:

```
migrate status --config migration.json
```

The CLI recomputes `configHash` from the config file and queries the control plane for the most recent job with that hash. If more than one job matches, all are listed with their state and timestamp.

### Notes

- `status` is a read-only poll — it never affects the running job.
- `logs` tails from the point the control plane has buffered — earlier lines may be in `Logs/` in the package.
- `pause`, `resume`, `cancel` are the only commands that change job state.

### Local Mode Has No Reconnection

In local mode the Job Engine runs in the same process as the CLI. If the process exits, the Job Engine stops. Resume requires re-running the command:

```
migrate both --config migration.json
```

The cursor in the package ensures the Job Engine picks up from the last completed stage. Nothing is lost beyond the current stage.
