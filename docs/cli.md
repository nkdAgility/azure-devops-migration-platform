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
│  - Runs Export / Import / Both          │
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
- Default config file resolution: `migration.json` in current working directory when `--config` not specified

### Error Handling
- **Malformed JSON**: Clear error messages with file location and JSON parsing details
- **Missing sections**: Validation errors identifying required configuration sections
- **File not found**: Helpful error messages suggesting correct file paths
- **DI container failures**: Configuration binding errors reported during startup

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
| `logs` | Fetch or stream `ProgressEvent` records from the job ring buffer. Without `--follow`: prints buffered events as NDJSON and exits. With `--follow`: opens the SSE stream and prints arriving events as NDJSON until the job ends or Ctrl+C. |
| `pause` | Signal the running Migration Agent to checkpoint and pause. |
| `resume` | Resume a paused job (re-queues it for Migration Agent pickup). |
| `cancel` | Cancel a queued or running job. |
| `discovery inventory` | Count work items and revisions per project. Read-only pre-flight operation. Does **not** submit a `MigrationJob` to the control plane — results are written directly to `discovery-summary.csv`. |

```
migrate prepare  --config migration.json
migrate export   --config migration.json
migrate both     --config migration.json
migrate validate --package file:///D:/exports/run-001
migrate pack     --package file:///D:/exports/run-001 --out run-001.zip
migrate tui      [--url <control-plane-url>] [--job <jobId>]
migrate status   --job 550e8400-e29b-41d4-a716-446655440000
migrate logs     --job 550e8400-e29b-41d4-a716-446655440000 --follow
devopsmigration discovery inventory --config migration.json
devopsmigration discovery inventory --config migration.json --all-projects
devopsmigration discovery inventory --config migration.json --output ./reports
```

> **Note**: `discovery *` commands do not submit a `MigrationJob` to the control plane.
> They run locally, reading the config directly.  Results are written to `discovery-summary.csv`
> in the `--output` directory (default: current working directory).

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
