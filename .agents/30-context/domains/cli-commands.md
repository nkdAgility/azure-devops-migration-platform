# CLI Commands — Canonical Summary

Compressed agent-facing command contract. Human narrative and examples: [docs/cli-guide.md](../../../docs/cli-guide.md).

## Binary

- CLI executable: `devopsmigration`

## Command Groups

### Migration submission

| Command | Purpose |
|---|---|
| `queue` | Submit job kinds: `Inventory`, `Dependencies`, `Export`, `Prepare`, `Import`, `Migrate`. |
| `prepare` | Explicit Prepare submission through control plane + agent flow. |

Key queue options:
- `--follow`: stream live progress/diagnostics
- `--level`: diagnostic minimum level for the job
- `--force-fresh`: remove cursors/phase markers before run (preserve id map)
- `--diagnostics`: emit detailed communication traces to diagnostics folder

### Job management (`manage`)

| Command | Purpose |
|---|---|
| `manage list` | List visible jobs |
| `manage status` | Summarized state for one job |
| `manage progress` | Progress snapshot stream payloads |
| `manage diagnostics` | Package diagnostics retrieval |
| `manage pause` / `manage resume` / `manage cancel` | Lifecycle controls |
| `manage login` / `manage logout` | Control-plane auth session management |

### Config management (`config`)

| Command | Purpose |
|---|---|
| `config new` | Interactive config file creation |
| `config set <key> <value>` | Set user preference |
| `config get <key>` | Read user preference |

Supported preference key:
- `scenario-folder`

### TUI and control plane

| Command | Purpose |
|---|---|
| `tui` | Interactive terminal dashboard |
| `controlplane start` | Start bundled local control-plane host |

## Global Options

| Option | Notes |
|---|---|
| `--config` / `-c` | Config file path (interactive fallback resolution when omitted) |
| `--verbose` | Verbose console output |
| `--disable-telemetry` | Suppress telemetry export |
| `--port` | Control-plane port for standalone control-plane commands |

Default config resolution precedence:
1. explicit `--config`
2. `$Env:MigrationPlatform_Scenario_Folder`
3. `preferences.json` `scenario-folder`
4. `./scenarios`
5. `*.json` in cwd

## Canonical Constraints

- `manage`, `config`, and `controlplane` are branch commands.
- CLI commands submit/query via control plane; no command may execute migration logic directly.
- Counters are sourced from control-plane telemetry endpoints (not in-process sinks).
- CLI/TUI behavior constraints are enforced by [cli-tui-rules.md](../../20-guardrails/domains/cli-tui-rules.md).




