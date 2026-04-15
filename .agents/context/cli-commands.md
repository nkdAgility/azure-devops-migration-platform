# CLI Commands — Canonical Reference

This is the authoritative command list for the `devopsmigration` binary.
All documentation, agent instructions, and implementation must reflect this list.

Human-readable narrative: [docs/cli.md](../../docs/cli.md)

---

## Binary

The CLI binary is named **`devopsmigration`** (assembly name set in `DevOpsMigrationPlatform.CLI.Migration.csproj`).

---

## Command Groups

### 1. Migration Commands

Submit and drive migration jobs via the control plane. Each command creates or queries a `MigrationJob`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `prepare` | `MigrationCommandSettings` | Submit a lightweight probe job through the full pipeline (CLI → Control Plane → Agent → ArtefactStore) to validate that permissions, configuration, and connectivity work end-to-end. The agent writes a single probe file to the artefact store and completes. |
| `queue` | `QueueCommandSettings` | Submit a migration job. Behaviour is determined by the `mode` field in the config (`Export`, `Import`, or `Both`). `--follow` streams diagnostic logs inline. `--level` sets the agent's diagnostic minimum level. `--force-fresh` deletes module cursor(s) before running so enumeration restarts from the beginning (identity map preserved). |

### 2. Job Management Commands (`manage`)

Query and control existing jobs. Registered as a Spectre.Console branch named `manage`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `manage list` | `ManageListCommandSettings` | List all jobs visible to the authenticated user with status and progress. |
| `manage status` | `ManageStatusCommandSettings` | Display job state and per-module progress for a specific job. Requires `--job <id>`. |
| `manage progress` | `ManageProgressCommandSettings` | Fetch a snapshot of `ProgressEvent` records from the job ring buffer as NDJSON. Requires `--job <id>`. |
| `manage diagnostics` | `ManageDiagnosticsCommandSettings` | Download package diagnostic log files (`Logs/agent.jsonl`) for a completed job. Accepts `--level` filter. Requires `--job <id>`. |
| `manage pause` | `ManagePauseCommandSettings` | Signal the agent to checkpoint and pause. Requires `--job <id>`. |
| `manage resume` | `ManageResumeCommandSettings` | Re-queue a paused job for agent pickup. Requires `--job <id>`. |
| `manage cancel` | `ManageCancelCommandSettings` | Cancel a queued or running job. Requires `--job <id>`. |
| `manage login` | `ManageLoginCommandSettings` | Authenticate with a control plane endpoint and cache the session token. Requires `--url`. |
| `manage logout` | `ManageLogoutCommandSettings` | Revoke the cached session token for a control plane endpoint. Requires `--url`. |

### 3. Discovery Commands (`discovery`)

Run **locally**. Do **not** submit a `MigrationJob`. Registered as a Spectre.Console branch named `discovery`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `discovery inventory` | `InventoryCommandSettings` | Count work items and revisions per project. Results written to `discovery-summary.csv`. |

### 4. Terminal UI

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `tui` | `TuiCommandSettings` | Open the interactive Terminal UI showing live job state. |

---

## Global Options (all commands)

| Option | Short | Default | Description |
|--------|-------|---------|-------------|
| `--config` | `-c` | `migration.json` (cwd) | Path to the migration configuration file. |
| `--verbose` | `-v` | `false` | Enable verbose console output. |
| `--disable-telemetry` | — | `false` | Suppress all telemetry export. |

## Resume Options (`queue`)

| Option | Default | Description |
|--------|---------|-------------|
| `--force-fresh` | `false` | Delete module cursor file(s) (and the job phase record for `Both` mode) before job execution. Enumeration restarts from the beginning. The identity map (`Checkpoints/idmap.json`) is **not** deleted so no duplicate items are created in the target. |

## Control Plane Endpoint Resolution (control-plane commands only)

Commands that contact the control plane (`queue`, `prepare`, `manage *`, `tui`) resolve the control plane URL from configuration:

- `MigrationPlatform:Environment:ControlPlane:BaseUrl` — bound to `EnvironmentOptions` via `IOptions<T>`.
- When `Environment` is absent or `Type` is `Standalone`, defaults to `http://localhost:5100` and the CLI starts `LocalStackHost` in-process.
- When `Type` is `Hosted`, the CLI connects to the configured `BaseUrl` directly.

The config file is the single source of truth. There is no `--url` CLI flag or `MIGRATION_API_URL` environment variable.

---

## Command Registration Pattern

Commands are registered in `Program.cs` using Spectre.Console's fluent API:

```csharp
// Top-level commands
config.AddCommand<PrepareCommand>("prepare");
config.AddCommand<QueueCommand>("queue");
// manage branch
config.AddBranch("manage", branch => {
    branch.AddCommand<ManageListCommand>("list");
    branch.AddCommand<ManageStatusCommand>("status");
    branch.AddCommand<ManageProgressCommand>("progress");
    branch.AddCommand<ManageDiagnosticsCommand>("diagnostics");
    branch.AddCommand<ManagePauseCommand>("pause");
    branch.AddCommand<ManageResumeCommand>("resume");
    branch.AddCommand<ManageCancelCommand>("cancel");
    branch.AddCommand<ManageLoginCommand>("login");
    branch.AddCommand<ManageLogoutCommand>("logout");
});

// discovery branch
config.AddBranch("discovery", branch => {
    branch.AddCommand<InventoryCommand>("inventory");
});

// tui
config.AddCommand<TuiCommand>("tui");
```

---

## Canonical Example Invocations

```
devopsmigration prepare  --config migration.json
devopsmigration queue    --config migration.json
devopsmigration queue    --config migration.json --force-fresh
devopsmigration queue    --config migration.json --follow --level Warning

devopsmigration manage list
devopsmigration manage status  --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage progress --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage diagnostics --job 550e8400-e29b-41d4-a716-446655440000 --level Warning
devopsmigration manage pause   --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage resume  --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage cancel  --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage login   --url https://migration.example.com
devopsmigration manage logout  --url https://migration.example.com

devopsmigration discovery inventory --config migration.json
devopsmigration discovery inventory --config migration.json --all-projects
devopsmigration discovery inventory --config migration.json --output ./reports

devopsmigration tui
```

---

## Constraints

- The `manage` and `discovery` branches are registered as Spectre.Console `AddBranch` entries — they are not standalone commands.
- `discovery *` commands must never submit a `MigrationJob` to the control plane.
- `manage login` / `manage logout` store and revoke credentials only; they do not trigger any job operations.
- All commands inherit from `CommandBase<T>`, which injects `IServiceProvider`, `IHostApplicationLifetime`, `ILogger`, and `ActivitySource`.
- No command may contain migration execution logic — see [system-architecture.md](../guardrails/system-architecture.md) Rule 16.
