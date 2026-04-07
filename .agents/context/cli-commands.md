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
| `prepare` | `PrepareCommandSettings` | Validate config, compute `configHash`, print planned modules. **No job submitted.** |
| `export` | `ExportCommandSettings` | Submit an export-only job. Writes package to `artefacts.packageUri`. |
| `import` | `ImportCommandSettings` | Submit an import-only job. Reads package from `artefacts.packageUri`. |
| `validate` | `ValidateCommandSettings` | Run pre-flight validation on an existing package. |
| `migrate` | `MigrateCommandSettings` | Full lifecycle: export → validate → import in one orchestrated run. |

### 2. Job Management Commands (`manage`)

Query and control existing jobs. Registered as a Spectre.Console branch named `manage`.

| Command | Settings Key | Description |
|---------|-------------|-------------|
| `manage list` | `ManageListCommandSettings` | List all jobs visible to the authenticated user with status and progress. |
| `manage status` | `ManageStatusCommandSettings` | Display job state and per-module progress for a specific job. Requires `--job <id>`. |
| `manage logs` | `ManageLogsCommandSettings` | Fetch or stream `ProgressEvent` records. `--follow` opens SSE stream. Requires `--job <id>`. |
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
| `--dry-run` | — | `false` | Parse and validate but perform no writes or job submissions. |

---

## Command Registration Pattern

Commands are registered in `Program.cs` using Spectre.Console's fluent API:

```csharp
// Top-level commands
config.AddCommand<PrepareCommand>("prepare");
config.AddCommand<ExportCommand>("export");
config.AddCommand<ImportCommand>("import");
config.AddCommand<ValidateCommand>("validate");
config.AddCommand<MigrateCommand>("migrate");

// manage branch
config.AddBranch("manage", branch => {
    branch.AddCommand<ManageListCommand>("list");
    branch.AddCommand<ManageStatusCommand>("status");
    branch.AddCommand<ManageLogsCommand>("logs");
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
devopsmigration export   --config migration.json
devopsmigration import   --config migration.json
devopsmigration validate --config migration.json
devopsmigration migrate  --config migration.json

devopsmigration manage list
devopsmigration manage status  --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage logs    --job 550e8400-e29b-41d4-a716-446655440000 --follow
devopsmigration manage pause   --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage resume  --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage cancel  --job 550e8400-e29b-41d4-a716-446655440000
devopsmigration manage login   --url https://migration.example.com
devopsmigration manage logout  --url https://migration.example.com

devopsmigration discovery inventory --config migration.json
devopsmigration discovery inventory --config migration.json --all-projects
devopsmigration discovery inventory --config migration.json --output ./reports

devopsmigration tui
devopsmigration tui --url https://migration.example.com
```

---

## Constraints

- The `manage` and `discovery` branches are registered as Spectre.Console `AddBranch` entries — they are not standalone commands.
- `discovery *` commands must never submit a `MigrationJob` to the control plane.
- `manage login` / `manage logout` store and revoke credentials only; they do not trigger any job operations.
- All commands inherit from `CommandBase<T>`, which injects `IServiceProvider`, `IHostApplicationLifetime`, `ILogger`, and `ActivitySource`.
- No command may contain migration execution logic — see [system-architecture.md](../guardrails/system-architecture.md) Rule 16.
