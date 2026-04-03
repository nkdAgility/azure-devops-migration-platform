# CLI

## Purpose

The CLI is the operator's entry point to the migration platform. It is a **thin shell** — it parses arguments, builds a `MigrationJob`, hosts or connects to the control plane, and delegates execution to Migration Agents. It contains no migration logic.

Migration logic lives exclusively in the **Job Engine**, which runs inside Migration Agents. The CLI always communicates with the control plane via `ControlPlaneClient`. For local and server execution, the CLI drives Aspire to start the control plane, agents, and PostgreSQL before submitting the job.

See [docs/tui.md](tui.md) for how progress is rendered in the terminal.

---

## Technology

The CLI is built with **[Spectre.Console](https://spectreconsole.net/)** (`Spectre.Console.Cli`). All command definitions, argument/option parsing, help text, and console output formatting use Spectre.Console primitives.

Spectre.Console is the only permitted CLI library in command-layer code. Do not reference `System.CommandLine`, `McMaster.Extensions.CommandLineUtils`, or any other argument-parsing library in this layer.

---

## Architecture

```
┌─────────────────────────────────────────┐
│  CLI Shell                              │
│  - Parses args                          │
│  - Loads config                         │
│  - Builds MigrationJob                  │
│  - Drives Aspire (if local/server)      │
│    or connects to remote                │
└────────────────┬────────────────────────┘
                 │  MigrationJob
                 │
        ┌────────▼────────┐
        │ ControlPlane    │
        │ Client          │
        │ (always active) │
        └────────┬────────┘
                 │  HTTP
                 │
┌────────────────▼────────────────────────┐
│  Control Plane (Aspire-managed or       │
│  remote)                                │
│  - Deduplicates job                     │
│  - Assigns to available agent           │
│  - Tracks state and progress            │
└────────────────┬────────────────────────┘
                 │  Lease
                 │
┌────────────────▼────────────────────────┐
│  Agent (Aspire-managed process or       │
│  container)                              │
│  - Runs Job Engine                      │
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

| Command | Description |
|---|---|
| `prepare` | Validate the config, compute `configHash`, print a job summary and planned modules. No execution. |
| `export` | Submit an export job to the control plane. Writes the package to the URI in `artefacts.packageUri`. |
| `import` | Submit an import job to the control plane. Reads the package from `artefacts.packageUri`. |
| `both` | Submit an export → validate → import job to the control plane. |
| `validate` | Run pre-flight validation on an existing package. See [docs/validation.md](validation.md). |
| `pack` | Compress `PackageRoot/` into a zip file. See [docs/packaging-zip.md](packaging-zip.md). |
| `unpack` | Extract a zip file into `PackageRoot/`. |
| `tui` | Open the interactive Terminal UI showing jobs visible to the current user. |
| `status` | Display job state and per-module progress from the control plane. |
| `logs` | Tail or page job logs from the control plane. |
| `pause` | Signal the running Migration Agent to checkpoint and pause. |
| `resume` | Resume a paused job (re-queues it for Migration Agent pickup). |
| `cancel` | Cancel a queued or running job. |

```
migrate prepare  --config migration.json
migrate export   --config migration.json
migrate both     --config migration.json
migrate validate --package file:///D:/exports/run-001
migrate pack     --package file:///D:/exports/run-001 --out run-001.zip
migrate tui      [--url <control-plane-url>] [--job <jobId>]
migrate status   --job 550e8400-e29b-41d4-a716-446655440000
migrate logs     --job 550e8400-e29b-41d4-a716-446655440000 --follow
migrate pause    --job 550e8400-e29b-41d4-a716-446655440000
migrate resume   --job 550e8400-e29b-41d4-a716-446655440000
migrate cancel   --job 550e8400-e29b-41d4-a716-446655440000
```

### Auth Commands

| Command | Description |
|---|---|
| `login` | Authenticate with the control plane using Entra ID (device-code flow). Caches the token locally. Not needed for Windows Integrated Auth. |
| `logout` | Clear the cached credential. |

```
migrate login  [--url <control-plane-url>]
migrate logout
```

For on-premises Active Directory deployments, Windows Integrated Auth is used automatically via Negotiate (Kerberos/NTLM). No `login` step is required.

---

## Job Visibility

When queuing a job to the control plane, the `--visibility` flag controls who can see it:

| Value | Who can see the job |
|---|---|
| `user` (default) | Only the submitter and Control Plane Admins |
| `tenant` | Any authenticated user in the same Entra tenant or AD domain (read-only), plus the submitter and admins |

```
migrate queue --config migration.json --visibility tenant
```

The visibility setting is immutable after the job is submitted. If omitted, `user` is applied. This is the safest default — it does not expose job configuration or progress to colleagues until the submitter explicitly opts in.

---

## Control Plane Endpoint

The CLI always communicates with the control plane via `ControlPlaneClient`. The endpoint is determined as follows:

| Condition | Behaviour |
|---|---|
| `MIGRATION_API_URL` not set | CLI drives Aspire to start the control plane on `http://localhost:5100`, agents, and PostgreSQL |
| `MIGRATION_API_URL` set | CLI connects to the specified remote endpoint; no in-process hosting |
| `--url` flag | Overrides `MIGRATION_API_URL` for that invocation |

Running `migrate export --config migration.json` on a local machine with no environment variables configured will drive Aspire to start the control plane, spawn agents, execute the job, and exit — all from a single command.

---

## Config → MigrationJob Conversion

Before any execution, the CLI converts the local config file into a `MigrationJob`:

1. Read and validate the config file schema.
2. Compute `configHash` (SHA-256 of the normalised config JSON).
3. Generate a fresh `jobId` (UUID v4).
4. Normalise `artefacts.path` to a URI (`file:///` prefix if a bare filesystem path is given).
5. Replace inline credentials with Key Vault URI references (cloud deployments only).
6. Construct the `MigrationJob` with `guardrails` set to their required values.

The local config file is never sent directly anywhere. The `MigrationJob` is the only artefact that crosses boundaries. See [.agents/context/job-contract.md](../.agents/context/job-contract.md).

---

## Execution Topologies

### Aspire-Managed (Local / Server)

When no remote control plane endpoint is configured, the CLI drives Aspire programmatically — building a `DistributedApplication` with the same resources defined in the AppHost. PostgreSQL starts as an Aspire portable binary resource (no Docker, no installer required).

- Control plane starts on `http://localhost:5100` as an Aspire-managed process.
- Agents run as Aspire-managed processes, using Aspire service discovery to locate `ControlPlaneHost`.
- `IArtefactStore` is `FileSystemArtefactStore`.
- `IStateStore` is `PackageCheckpointStateStore` (writes `Checkpoints/` inside the package).
- Any machine with network access to the host can attach a TUI and monitor the migration.

### Remote (Cloud)

When `MIGRATION_API_URL` is set, the CLI connects to the specified control plane endpoint.

- The control plane and agents run as containers managed by `ControlPlaneHost` in the cloud.
- `IArtefactStore` is `AzureBlobArtefactStore`.
- The CLI process can exit after submission; the job continues running on the remote agents.

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
