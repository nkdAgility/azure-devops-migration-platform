# CLI Commands — Canonical Reference

This is the authoritative command list for the `devopsmigration` binary.
All documentation, agent instructions, and implementation must reflect this list.

Human-readable narrative: [docs/cli-guide.md](../../docs/cli-guide.md)

---

## Binary

The CLI binary is named **`devopsmigration`** (assembly name set in `DevOpsMigrationPlatform.CLI.Migration.csproj`).

---

## Command Groups

### 1. Migration Commands

Submit and drive migration jobs via the control plane. Each command creates or queries a `Job`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `prepare` | `MigrationCommandSettings` | Submit a Prepare job through the full pipeline (CLI → Control Plane → Agent). The agent reads the exported package, connects to the target, and runs each module's `PrepareAsync` to cross-validate before import. Produces validation artefacts (identity mapping reports, node validation, field mapping reports) in each module's package folder for operator review. Any unresolved issue is blocking unless the operator adds an explicit skip. Idempotent — re-running overwrites Prepare output but preserves operator-edited mapping files. Requires a completed Export (package with `manifest.json`). |
| `queue` | `QueueCommandSettings` | Submit a job. Behaviour is determined by the `mode` field in the config (`Inventory`, `Dependencies`, `Export`, `Prepare`, `Import`, or `Migrate`). `--follow` streams diagnostic logs inline. `--level` sets the agent's diagnostic minimum level. `--force-fresh` deletes module cursor(s) before running so enumeration restarts from the beginning (identity map preserved). Phase gates apply automatically: Export auto-runs Inventory if missing; Import auto-runs Prepare if missing. |

### 2. Job Management Commands (`manage`)

Query and control existing jobs. Registered as a Spectre.Console branch named `manage`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `manage list` | `ManageListCommandSettings` | List all jobs visible to the authenticated user with status and progress. |
| `manage status` | `ManageStatusCommandSettings` | Display job state and per-module progress for a specific job. Requires `--job <id>`. |
| `manage progress` | `ManageProgressCommandSettings` | Fetch a snapshot of `ProgressEvent` records from the job ring buffer as NDJSON. Requires `--job <id>`. |
| `manage diagnostics` | `ManageDiagnosticsCommandSettings` | Download package diagnostic log files (`.migration/Logs/agent.jsonl`) for a completed job. Accepts `--level` filter. Requires `--job <id>`. |
| `manage pause` | `ManagePauseCommandSettings` | Signal the agent to checkpoint and pause. Requires `--job <id>`. |
| `manage resume` | `ManageResumeCommandSettings` | Re-queue a paused job for agent pickup. Requires `--job <id>`. |
| `manage cancel` | `ManageCancelCommandSettings` | Cancel a queued or running job. Requires `--job <id>`. |
| `manage login` | `ManageLoginCommandSettings` | Authenticate with a control plane endpoint and cache the session token. Requires `--url`. |
| `manage logout` | `ManageLogoutCommandSettings` | Revoke the cached session token for a control plane endpoint. Requires `--url`. |

### 3. Configuration Management (`config`)

Manage user preferences and create migration configuration files. Registered as a Spectre.Console branch named `config`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `config new` | `ConfigNewCommandSettings` | Interactive wizard to create a new migration configuration file. Accepts `--output` and `--force`. |
| `config set <key> <value>` | `ConfigSetCommandSettings` | Set a user-level preference in `preferences.json`. |
| `config get <key>` | `ConfigGetCommandSettings` | Read a user-level preference value from `preferences.json`. |

Supported preference keys:

| Key | Type | Description |
|-----|------|-------------|
| `scenario-folder` | path | Default folder scanned when `--config` is omitted. |

### 4. Terminal UI

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `tui` | `TuiCommandSettings` | Open the interactive Terminal UI showing live job state. |

### 5. Control Plane Management (`controlplane`)

Manage the local Control Plane host process. Registered as a Spectre.Console branch named `controlplane`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `controlplane start` | `ControlPlaneStartCommand.Settings` | Start the bundled Control Plane host (`ControlPlane/DevOpsMigrationPlatform.ControlPlaneHost[.exe]`) in the current terminal. Accepts `--port <port>` (default: `5100`); the port is passed to the child process via `ASPNETCORE_URLS`. The binary is resolved by convention from the CLI's install directory. Only available in the packaged (zip) distribution. Ctrl+C stops the process. |

---

## Global Options (all commands)

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--config` | `-c` | (interactive resolution — see below) | Path to the migration configuration file. |
| `--verbose` | `-v` | `false` | Enable verbose console output. |
| `--disable-telemetry` | — | `false` | Suppress all telemetry export. |

### Control Plane Options (commands that contact the control plane)

These options are available on all commands that derive from `ControlPlaneBaseCommandSettings`:
`queue`, `prepare`, `manage *`, `tui`.

| Option | Default | Description |
|--------|---------|-------------|
| `--port` | `5100` | Port for the local control plane in standalone mode. When specified, overrides `ControlPlane.BaseUrl` to `http://localhost:{port}`, enabling multiple concurrent standalone runs on different ports. |

### Default `--config` Resolution

When `--config` is not supplied, the CLI resolves a configuration file using the following precedence:

| Priority | Source | Behaviour |
|----------|--------|-----------|
| 1 | `--config <path>` | Use the supplied path directly, no scan. |
| 2 | `$Env:MigrationPlatform_Scenario_Folder` | Scan that folder for `*.json` files and present a selection prompt. |
| 3 | `preferences.json` → `scenario-folder` | Same scan-and-prompt behaviour. |
| 4 | `./scenarios` subfolder of cwd | Dev default — the repo ships this folder. |
| 5 | `*.json` files in cwd | Last fallback scan. |
| 6 | Nothing found | Warning message with guidance. |

When multiple JSON files are found, a `SelectionPrompt<string>` lets the operator pick interactively. When exactly one file is found, it is used automatically.

The interactive prompt runs inside the command's `ExecuteInternalAsync` (before `CreateHost` is called). `MigrationPlatformHost.ExtractConfigFileArg` remains pure file-system logic and cannot prompt.

## Resume Options (`queue`)

| Option | Default | Description |
|--------|---------|-------------|
| `--force-fresh` | `false` | Delete module cursor file(s) (and the job phase record for `Migrate` mode) before job execution. Enumeration restarts from the beginning. The identity map (`.migration/Checkpoints/idmap.json`) is **not** deleted so no duplicate items are created in the target. |

## Control Plane Endpoint Resolution (control-plane commands only)

Commands that contact the control plane (`queue`, `prepare`, `manage *`, `tui`) resolve the control plane URL from configuration:

- `MigrationPlatform:Environment:ControlPlane:BaseUrl` — bound to `EnvironmentOptions` via `IOptions<T>`.
- When `Environment` is absent or `Type` is `Standalone`, defaults to `http://localhost:5100` and the CLI starts `LocalStackHost` in-process. Use `--port <port>` to override the port (e.g. `--port 5200` to run a second concurrent job).
- When `Type` is `Hosted`, the CLI connects to the configured `BaseUrl` directly.

The config file is the single source of truth for the base URL. The `--port` flag overrides the port in standalone mode only. There is no `--url` CLI flag or `MIGRATION_API_URL` environment variable.

---

## Constraints

- The `manage`, `config`, and `controlplane` branches are registered as Spectre.Console `AddBranch` entries — they are not standalone commands.
- `controlplane start` resolves the sibling binary by convention (`ControlPlane/` subdirectory of `AppContext.BaseDirectory`). Accepts `--port <port>` (default: `5100`); the value overrides the child process's listen address via the `ASPNETCORE_URLS` environment variable. Only available in the packaged zip distribution; in a dev/source build it prints an informative error and returns exit code 1.
- `manage login` / `manage logout` store and revoke credentials only; they do not trigger any job operations.
- `config set` / `config get` read and write user-level preferences only; they do not affect migration configuration files.
- All commands inherit from `CommandBase<T>`, which injects `IServiceProvider`, `IHostApplicationLifetime`, `ILogger`, and `ActivitySource`.
- No command may contain migration execution logic — see [architecture-boundaries.md](../guardrails/architecture-boundaries.md) Rule 16.

Implementation examples and operator-facing invocation samples live in [docs/cli-guide.md](../../docs/cli-guide.md).
