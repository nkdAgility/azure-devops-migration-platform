# CLI

## Purpose

The CLI is the operator's entry point to the migration platform. It is a **thin shell** — it parses arguments, builds a `MigrationJob`, and delegates execution to the control plane via HTTP. It contains no migration logic.

Migration logic lives exclusively in the **Job Engine**, which runs inside Migration Agents. CLI commands manage their own hosting lifecycle — starting or connecting to the required services as needed before submitting the job. The CLI always communicates with the control plane via `ControlPlaneClient`.

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
│  - Commands manage hosting lifecycle    │
│    (start or connect to services)       │
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
│  - Runs Export / Prepare / Import / Migrate │
│  - Writes package via IArtefactStore    │
│  - Writes checkpoints via IStateStore   │
│  - Emits progress via IProgressSink     │
└─────────────────────────────────────────┘
```

The Job Engine has no reference to the CLI, the console, or any progress renderer. It emits structured progress events; sinks consume them.

---

## Implementation Pattern

The CLI follows a **host builder pattern** to maintain proper separation of concerns:

### MigrationPlatformHost Pattern
- **Program.cs**: Contains only minimal bootstrapping logic (< 50 lines) - creates the host builder and runs the console app
- **MigrationPlatformHost**: Static factory class with `CreateDefaultBuilder(string[] args)` method that centralizes all service registration, configuration binding, and infrastructure setup
- **CommandBase<T>**: Abstract base class providing `IServiceProvider`, `IHostApplicationLifetime`, and common command functionality
- **Commands**: Inherit from `CommandBase<T>` to access DI services and manage their hosting lifecycle

### Command Lifecycle Management
Commands manage their own hosting lifecycle through dependency injection:
1. Command instantiated by Spectre.Console with dependencies injected via constructor
2. Services accessed via `Services.GetRequiredService<T>()`
3. Application lifetime controlled via `Lifetime.StopApplication()`
4. Common error handling and telemetry provided by `CommandBase<T>`

### Service Registration
All DI container setup, service registration, and infrastructure configuration is handled by `MigrationPlatformHost`, never in individual commands or `Program.cs`. This ensures:
- New commands can be added without modifying core infrastructure
- Proper separation between bootstrapping and infrastructure concerns
- Consistent service availability across all commands
- Testable architecture via dependency injection

---

## Testing

All CLI commands use **Spectre.Console.Cli.Testing** (`CommandAppTester`) for comprehensive validation:

### Automated Command Validation
Every command must have automated tests covering:
- **Valid parameter tests**: Commands execute successfully with proper inputs (exit code 0)
- **Invalid parameter tests**: Appropriate error messages and non-zero exit codes
- **Help text tests**: `--help` displays comprehensive information without errors
- **Configuration flow tests**: Config values reach internal services correctly

### Test Isolation Strategy
- **In-memory test doubles**: Configuration via `ConfigurationBuilder` with in-memory collections
- **Mock service providers**: Test-specific `IServiceProvider` implementations
- **No external dependencies**: No file system, network, or database dependencies in CLI tests
- **Clean test environment**: Each test method gets isolated configuration and services

### CommandAppTester Pattern
```csharp
[TestMethod]
public void CommandName_WithValidInputs_ReturnsSuccessCode()
{
    // Arrange
    var app = new CommandAppTester();
    app.SetDefaultCommand<CommandUnderTest>();
    
    // Act
    var result = app.Run("param1", "value1", "--param2", "value2");
    
    // Assert
    Assert.AreEqual(0, result.ExitCode);
    Assert.Contains("expected output", result.Output);
}
```

---

## Configuration Flow

The `--config` parameter flows through the system following a specific pattern to ensure configuration reaches all services:

### Parameter Extraction Pattern
1. **Pre-processing**: `--config` and `-c` parameters are extracted by `MigrationPlatformHost` before Spectre.Console processes arguments
2. **Host builder integration**: Configuration file path is used during `CreateDefaultBuilder()` to layer configuration sources
3. **Configuration layering**: command-line args → environment variables → config files (proper precedence)
4. **DI container creation**: Configuration is available during service registration phase

### IOptions Pattern Integration
All configuration flows through the `IOptions<T>` pattern:
- Configuration classes are bound during host builder setup
- Services receive configuration via dependency injection, never direct file access
- Configuration validation occurs during DI container build

### Default `--config` Resolution

When `--config` is not supplied, the CLI resolves a configuration file using the following precedence chain:

| Priority | Source | Behaviour |
|----------|--------|-----------|
| 1 | `--config <path>` | Use the supplied path directly, no scan. |
| 2 | `$Env:MigrationPlatform_Scenario_Folder` | Scan that folder for `*.json` files and present a selection prompt. |
| 3 | `preferences.json` → `scenario-folder` | Same scan-and-prompt behaviour. |
| 4 | `./scenarios` subfolder of cwd | Dev default — the repo ships this folder. |
| 5 | `*.json` files in cwd | Last fallback scan. |
| 6 | Nothing found | Warning message with guidance. |

When multiple JSON files are found, a `Spectre.Console` `SelectionPrompt` lets the operator pick one interactively. When exactly one file is found, it is used automatically.

The interactive prompt runs inside the command's `ExecuteInternalAsync` (before `CreateHost` is called). `MigrationPlatformHost.ExtractConfigFileArg` remains pure file-system logic — it cannot prompt.

### Error Handling
- **Malformed JSON**: Clear error messages with file location and JSON parsing details
- **Missing sections**: Validation errors identifying required configuration sections
- **File not found**: Helpful error messages suggesting correct file paths
- **DI container failures**: Configuration binding errors reported during startup

---

## Commands

Commands are organised into four groups. See [.agents/context/cli-commands.md](../.agents/context/cli-commands.md) for the canonical machine-readable reference.

---

### Migration Commands

These commands submit jobs to the control plane via `ControlPlaneClient`.

| Command | Description |
|---|---|
| `prepare` | Submit a Prepare job through the full pipeline (CLI → Control Plane → Agent). The agent reads the exported package, connects to the target, and runs each module's `PrepareAsync` to cross-validate before import. Produces validation artefacts (identity mapping reports, node validation, field mapping reports) in each module's package folder for operator review. Any unresolved issue is blocking unless the operator adds an explicit skip. Idempotent — re-running overwrites Prepare output but preserves operator-edited mapping files. Requires a completed Export (package with `manifest.json`). |
| `queue` | Submit a migration job. Behaviour is determined by the `mode` field in the config (`Export`, `Prepare`, `Import`, or `Migrate`). `--follow` streams diagnostic logs inline (implicit in standalone mode). `--level` sets the agent's diagnostic minimum level per job. `--force-fresh` deletes module cursor(s) before running so enumeration restarts from the beginning (identity map preserved). |

### Job Management Commands (`manage`)

All job management commands live under the `manage` sub-command.

| Command | Description |
|---|---|
| `manage list` | List all jobs visible to the authenticated user, with current status and progress. |
| `manage status` | Display job state and per-module progress for a specific job. |
| `manage progress` | Fetch a snapshot of `ProgressEvent` records from the job ring buffer. Prints buffered events as NDJSON and exits. Requires `--job`. |
| `manage diagnostics` | Download package diagnostic log files (`.migration/Logs/agent.jsonl`) for a completed job. Accepts `--level` to filter by minimum severity. Requires `--job`. |
| `manage pause` | Signal the running Migration Agent to checkpoint and pause. |
| `manage resume` | Resume a paused job (re-queues it for Migration Agent pickup). |
| `manage cancel` | Cancel a queued or running job. |
| `manage login` | Authenticate with a control plane endpoint and store the session token. |
| `manage logout` | Revoke the stored session token for a control plane endpoint. |

### Discovery Commands (`discovery`)

Discovery commands run **locally** and do **not** submit a `MigrationJob` to the control plane. Results are written directly to output files.

| Command | Description |
|---|---|
| `discovery inventory` | Count work items and revisions per project. Read-only pre-flight operation. Results written to `inventory.csv` and `inventory.json` at the output root plus per-org/per-project subfolders. Accepts `--output <dir>` to override the config's `Artefacts.WorkingDirectory`. |
| `discovery dependencies` | Analyse cross-project and cross-organisation work item links. Loads `inventory.json` (if present) for grand totals before analysis. Results written to `dependencies.csv` in the output directory. Accepts `--output <dir>` to override the config's `Artefacts.WorkingDirectory`. |

### Configuration Management (`config`)

User preference management and migration configuration file creation. Follows the `git config` / `gh config` pattern.

| Command | Description |
|---|---|
| `config new` | Interactive wizard to create a new migration configuration file. Accepts `--output` and `--force`. |
| `config set <key> <value>` | Set a user-level preference. |
| `config get <key>` | Read a user-level preference value. |

**Preference store**: `preferences.json` in the user's application-data directory:
- Windows: `%APPDATA%\nkdAgility\devopsmigration\preferences.json`
- Linux/macOS: `~/.config/devopsmigration/preferences.json`

Supported preference keys:

| Key | Type | Description |
|-----|------|-------------|
| `scenario-folder` | path | Default folder scanned when `--config` is omitted. |

### Terminal UI

| Command | Description |
|---|---|
| `tui` | Open the interactive Terminal UI showing live job state for jobs visible to the current user. See [docs/tui.md](tui.md). |

### Control Plane Management (`controlplane`)

| Command | Description |
|---|---|
| `controlplane start [--port <port>]` | Start the bundled Control Plane host (`ControlPlane/DevOpsMigrationPlatform.ControlPlaneHost[.exe]`) in the current terminal. Blocks until Ctrl+C — the control plane runs as a foreground child process. `--port` sets the listen port (default: `5100`); the value is passed to the child process via `ASPNETCORE_URLS`. **Only available in the packaged (zip) distribution.** In a dev/source build, run `dotnet run --project src/DevOpsMigrationPlatform.ControlPlaneHost --urls http://localhost:5100` instead. |

---

### Example Invocations

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
devopsmigration discovery dependencies --config migration.json
devopsmigration discovery dependencies --config migration.json --output ./reports

devopsmigration config new
devopsmigration config new --output my-migration.json
devopsmigration config set scenario-folder C:\migrations\configs
devopsmigration config get scenario-folder

devopsmigration controlplane start
devopsmigration controlplane start --port 5200

devopsmigration tui
```

> **Note**: `discovery *` commands run locally and read the config directly. They do not submit a `MigrationJob` to the control plane. Results are written to `inventory.csv` / `inventory.json` (inventory) and `dependencies.csv` (dependencies) in the `--output` directory (default: current working directory).

---

## CLI Observability

The CLI process instruments itself with OpenTelemetry. Each command that performs a job-level operation starts a child `Activity` span. Traces, metrics, and logs are exported to:

- **Azure Monitor** — when a connection string is configured (product telemetry or operator-supplied)
- **OTLP endpoint** — when `OTEL_EXPORTER_OTLP_ENDPOINT` is set (Aspire dashboard, local dev)

The `TracerProvider` and `MeterProvider` are flushed and disposed before process exit to ensure all pending telemetry is delivered. No telemetry is emitted if no exporter is configured — the command runs normally.
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

The CLI always communicates with the control plane via `ControlPlaneClient`. The endpoint is determined by the `Environment` section in the configuration file (bound to `EnvironmentOptions` via `IOptions<T>`):

| Condition | Behaviour |
|---|---|
| `Environment` absent or `Type` = `Standalone` | CLI starts **LocalStackHost** which launches `ControlPlaneHost` and `MigrationAgent` at `http://localhost:{port}` (default port `5100`). Prefers **process-per-component** mode when published binaries are found; falls back to **in-process** hosting otherwise. Use `--port <port>` to override. |
| `Type` = `Hosted` | CLI connects to `ControlPlane.BaseUrl` from config; no local services are started |

The config file is the single source of truth for the control plane URL. The `--port` CLI flag overrides the listen port in Standalone mode, enabling multiple concurrent local runs on different ports (e.g. `--port 5200`). There is no `--url` CLI flag or `MIGRATION_API_URL` environment variable override.

Running `devopsmigration export --config migration.json` on a local machine with default config will start the local stack, execute the job, and exit — all from a single command.

---

## Config → MigrationJob Conversion

Before any execution, the CLI converts the local config file into a `MigrationJob`:

1. Read and validate the config file schema.
2. Compute `configHash` (SHA-256 of the normalised config JSON).
3. Generate a fresh `jobId` (UUID v4).
4. Normalise `artefacts.path` to a URI (`file:///` prefix if a bare filesystem path is given).
5. Construct the `MigrationJob` with `guardrails` set to their required values.

The local config file is never sent directly anywhere. The `MigrationJob` is the only artefact that crosses boundaries. See [.agents/context/job-contract.md](../.agents/context/job-contract.md).

---

## Execution Topologies

### Standalone (Local / Server)

When `Environment.Type` is `Standalone` (the default), the CLI starts `LocalStackHost` which launches ControlPlane and MigrationAgent at `http://localhost:{port}` (default port `5100`). Use `--port` to run on a different port.

**Process-per-component mode (preferred):** When published ControlPlane and MigrationAgent binaries are found (installed layout or dev build output), each component runs as a separate child process. This gives each component its own `System.Diagnostics.DiagnosticListener` instance, producing correct Application Insights Application Map topology: `CLI ↔ ControlPlane ↔ Agent ↔ dev.azure.com`. Executables are resolved in this order:
1. `MIGRATION_CONTROLPLANE_EXE` / `MIGRATION_AGENT_EXE` environment variable override.
2. Installed layout: `../ControlPlane/` and `../MigrationAgent/` relative to the CLI binary.
3. Development layout: sibling project `bin/{Debug|Release}/net10.0/` directories.

**In-process fallback:** When executables are not found (e.g. `dotnet run` from source without publishing), falls back to hosting both components in the CLI process. A warning is logged about Application Map accuracy due to OpenTelemetry instrumentation bleed.

- Control plane starts on `http://localhost:{port}` (default: `5100`).
- `IArtefactStore` is `FileSystemArtefactStore`.
- `IStateStore` is `PackageCheckpointStateStore` (writes `.migration/Checkpoints/` inside the package).
- Any machine with network access to the host can attach a TUI and monitor the migration.

### Hosted (Cloud)

When `Environment.Type` is `Hosted`, the CLI connects to `Environment.ControlPlane.BaseUrl`.

- The control plane and agents run as containers managed by `ControlPlaneHost` in the cloud.
- `IArtefactStore` is `AzureBlobArtefactStore`.
- The CLI process can exit after submission; the job continues running on the remote agents.

---

## Job Submission Output

Every migration command that submits a job (`export`, `import`, `migrate`) prints the **Job ID** (full UUID) and the **resolved control plane URL** immediately after the job is accepted by the control plane. This output appears before any progress output begins.

```
Job ID  : 550e8400-e29b-41d4-a716-446655440000
Control : http://localhost:5100
```

Both values are labelled and printed on separate lines in a format suitable for copying. When submission fails, the control plane URL attempted is still shown so the operator knows where the request was directed.

The `prepare` command validates configuration and computes `configHash` but does **not** submit a job — it produces no job ID output.

---

## Reconnecting to a Remote Job

When the CLI process exits or loses connectivity, the job continues running unaffected. The Migration Agent holds the lease independently of the CLI process. To reconnect:

```
manage status --job 550e8400-e29b-41d4-a716-446655440000
manage progress --job 550e8400-e29b-41d4-a716-446655440000
```

The `jobId` is the only thing needed. It is printed by the submission command (`export`, `import`, `migrate`) immediately after the job is accepted. Keep it.

### If You Lost the jobId

If the `jobId` was not recorded, retrieve it from the control plane by config hash:

```
manage status --config migration.json
```

The CLI recomputes `configHash` from the config file and queries the control plane for the most recent job with that hash. If more than one job matches, all are listed with their state and timestamp.

### Notes

- `manage status` is a read-only poll — it never affects the running job.
- `manage progress` returns a snapshot of buffered events — earlier events may be in `.migration/Logs/progress.jsonl` in the package.
- `manage diagnostics` downloads diagnostic logs from the package's `.migration/Logs/agent.jsonl`.
- `manage pause`, `manage resume`, `manage cancel` are the only commands that change job state.
