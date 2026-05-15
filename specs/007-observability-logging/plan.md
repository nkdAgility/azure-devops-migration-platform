# Implementation Plan: Three-Channel Observability

**Branch**: `007-observability-logging` | **Date**: 2026-04-09 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/007-observability-logging/spec.md`

## Summary

Establish three orthogonal observability channels for the Migration Agent: (1) complete the existing `PackageProgressSink` stub so `ProgressEvent` records are persisted to `Logs/progress.jsonl` in the package, (2) add a new diagnostics channel that captures `ILogger` output as `DiagnosticLogRecord` NDJSON in `Logs/agent.jsonl` and streams it to the Control Plane for live TUI and `export --follow` consumption, and (3) rename the misnamed `/logs` endpoints to `/progress` and repurpose `manage logs` as `manage diagnostics`.

Key design decisions:
- **Tiered log levels**: The agent's diagnostic log level is per-job (set via `export --level`, default: Information). The control plane has an independent deployment-level minimum (default: Warning). The agent writes full detail to the package; the CP filters incoming records at its own floor before buffering, streaming via SSE, or exporting to Application Insights / OTel.
- **CLI lifecycle**: `export --follow` streams diagnostics inline (implicit in standalone mode). Without `--follow` on a remote CP, the CLI submits the job and exits. Ctrl+C during `--follow` detaches without cancelling the job.
- **Live observation**: The TUI is the primary live dashboard. The CLI provides live diagnostic streaming via `export --follow`. CLI `manage` commands are snapshot/download only — no `--follow`.
- **Command renames**: `manage logs` → `manage diagnostics` (downloads package logs for completed jobs). New `manage progress` (progress event snapshot, no `--follow`).
- **`AppendAsync`**: Research confirmed `IArtefactStore.WriteAsync` is overwrite-only, so `AppendAsync` must be added.
- **`ILoggerProvider`**: A custom `ILoggerProvider` is preferred over an OTel `BaseExporter<LogRecord>` to avoid modifying the shared OTel pipeline.

## Technical Context

**Language/Version**: C# 10+, .NET 10
**Primary Dependencies**: Microsoft.Extensions.Logging, OpenTelemetry SDK, Spectre.Console (`Spectre.Console.Cli`), Terminal.Gui
**Storage**: `IArtefactStore` (filesystem or Azure Blob), PostgreSQL (control plane, not directly touched by this feature)
**Testing**: Reqnroll.MSTest + Moq (`MockBehavior.Strict`)
**Target Platform**: Windows/Linux (CLI/Agent), Azure Container Apps (cloud)
**Project Type**: Multi-project solution (abstractions, infrastructure, agent, control plane, CLI, TUI)
**Performance Goals**: Package log sinks add < 5% overhead to job duration; diagnostics SSE delivery < 5s latency
**Constraints**: Non-blocking log writes; bounded memory (channel capacity 1024); no unbounded growth
**Scale/Scope**: Packages with 100k+ work items producing millions of log lines; single-job focus

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** All guardrail files (`architecture-boundaries.md`, `coding-standards.md`, `testing-rules.md`, `workitems-rules.md`, `migration-rules.md`, `module-rules.md`, `control-plane-rules.md`, `test-first-workflow.md`, `acceptance-test-format.md`), all context files (`migration-package-concept.md`, `package-manager.md`, `job-lifecycle.md`), and relevant docs (`architecture.md`, `cli.md`, `tui.md`, `control-plane.md`, `migration-agent.md`) have been read. Confirmed.

- [x] **Package-First (I):** All log persistence goes through `IArtefactStore.AppendAsync`. No direct filesystem access in sinks. The package is the durable record; ring buffers are volatile caches.
- [x] **Streaming (II):** Not directly applicable — this feature writes logs, not revision data. Log sinks use bounded channels with streaming writes. No in-memory sort.
- [x] **WorkItems Layout (III):** Not modified. Logs write to `Logs/` folder only. WorkItems layout unchanged.
- [x] **Checkpointing (IV):** Not applicable — log sinks are not modules. They don't maintain cursors. Progress events are not checkpoints.
- [x] **Module Isolation (V):** Sinks depend on `IArtefactStore` abstraction only. No concrete store references in any sink or provider. `DiagnosticLogRecord` defined in Abstractions.
- [x] **Separation of Planes (VI):** Control plane receives and buffers diagnostic records but does not interpret or act on them. Agent produces them. TUI renders them. CLI renders them via `export --follow`. No migration logic crosses boundaries. `manage diagnostics` downloads from the package via the CP — it does not execute migration logic.
- [x] **Determinism (VII):** Log output is non-deterministic by nature (timestamps, error conditions). This does not affect package layout determinism. Adding `AppendAsync` to `IArtefactStore` is additive (non-breaking). The rename from `/logs` to `/progress` is a breaking API change — acceptable pre-release without an upgrader (documented in assumptions).
- [x] **ATDD-First (VIII):** 6 user stories with 18+ acceptance scenarios in spec.md. Each will be implemented via ATDD inner loop (one scenario per session per commit).
- [x] **SOLID & DI (IX):** All new sinks, providers, stores, and controllers use constructor injection. Options via `IOptions<T>` with sealed classes and `SectionName` constants. Interfaces in Abstractions. Registration via dedicated `Add*Services` extension methods. No service locator. No static mutable state.

## Project Structure

### Documentation (this feature)

```text
specs/007-observability-logging/
├── plan.md              # This file
├── spec.md              # Feature specification (6 user stories, 30 FRs)
├── research.md          # Phase 0 research (7 decisions)
├── data-model.md        # Phase 1 data model
├── quickstart.md        # Phase 1 quickstart
├── contracts/
│   └── api-contracts.md # Phase 1 API contracts
├── discrepancies.md     # Architecture doc discrepancies (9 items)
└── tasks.md             # Phase 2 output (created by /speckit.tasks)
```

### Source Code (files touched by this feature)

```text
src/
  DevOpsMigrationPlatform.Abstractions/
    Storage/IArtefactStore.cs                          # Add AppendAsync method
    Models/DiagnosticLogRecord.cs                      # NEW: log record type
    Diagnostics/DiagnosticLogOptions.cs                # NEW: agent-side options class

  DevOpsMigrationPlatform.Infrastructure/
    Storage/FileSystemArtefactStore.cs                 # Implement AppendAsync
    Telemetry/PackageProgressSink.cs                   # COMPLETE: implement the stub
    Telemetry/PackageLoggerProvider.cs                 # NEW: ILoggerProvider → IArtefactStore
    Telemetry/ControlPlaneLoggerProvider.cs            # NEW: ILoggerProvider → HTTP push
    Telemetry/DiagnosticsServiceExtensions.cs          # NEW: DI registration extension

  DevOpsMigrationPlatform.ControlPlane/
    Services/DiagnosticLogStore.cs                     # NEW: ring buffer for diagnostics
    Services/DiagnosticLogStoreOptions.cs              # NEW: options class
    Controllers/ProgressController.cs                  # RENAME: /logs → /progress
    Controllers/DiagnosticsController.cs               # NEW: push + SSE endpoints (TUI + export --follow)
    Controllers/LogDownloadController.cs               # NEW: package file download (manage diagnostics backend)

  DevOpsMigrationPlatform.MigrationAgent/
    MigrationAgentServiceExtensions.cs                 # Wire new sinks and providers

  DevOpsMigrationPlatform.CLI.Migration/
    Commands/ManageLogsCommand.cs → ManageDiagnosticsCommand.cs  # RENAME: manage logs → manage diagnostics (download only)
    Commands/ManageProgressCommand.cs                  # NEW: manage progress (snapshot only, no --follow)
    Commands/MigrationExportCommand.cs                 # UPDATE: add --follow and --level options
    Settings/ExportCommandSettings.cs                  # UPDATE: add Follow and Level properties
    Settings/ManageDiagnosticsCommandSettings.cs       # NEW (replaces ManageLogsCommandSettings)
    Settings/ManageProgressCommandSettings.cs          # NEW
    JobRunners/ILogsClient.cs → IProgressClient.cs     # RENAME
    JobRunners/ControlPlaneClient.cs                   # Update endpoint paths + diagnostics SSE method
    Program.cs                                         # Update command registration
    Views/DiagnosticsPanel.cs                          # NEW: Terminal.Gui panel (P2, TUI)

tests/
  DevOpsMigrationPlatform.Infrastructure.Tests/
    Telemetry/PackageProgressSinkTests.cs              # NEW
    Telemetry/PackageLoggerProviderTests.cs            # NEW
    Storage/FileSystemArtefactStoreAppendTests.cs      # NEW

  DevOpsMigrationPlatform.ControlPlane.Tests/
    Services/DiagnosticLogStoreTests.cs                # NEW
    Controllers/DiagnosticsControllerTests.cs          # NEW
    Controllers/ProgressControllerRenameTests.cs       # NEW

  DevOpsMigrationPlatform.CLI.Migration.Tests/
    Commands/ManageDiagnosticsCommandTests.cs          # NEW (replaces LogsCommandTests)
    Commands/ManageProgressCommandTests.cs             # NEW
    Commands/ExportCommandFollowTests.cs               # NEW: --follow and --level behaviour

features/
  cli/
    export/
      export-follow-and-level.feature                  # Export --follow and --level scenarios
  platform/
    observability/
      package-progress-sink.feature                    # PackageProgressSink scenarios
      package-diagnostics-sink.feature                 # Diagnostics persistence scenarios
      diagnostics-streaming.feature                    # Control plane streaming scenarios (TUI + export --follow)
      tiered-log-levels.feature                        # Agent/CP level independence
      endpoint-rename.feature                          # API rename scenarios
      log-download.feature                             # Package log download scenarios
```

**Structure Decision**: Changes span 6 existing projects. No new projects needed. All new types fit within existing project boundaries (abstractions for interfaces/DTOs, infrastructure for implementations, control plane for API, CLI for commands, TUI views in CLI).

## Complexity Tracking

| Aspect | Decision | Rationale |
|--------|----------|-----------|
| `AppendAsync` on `IArtefactStore` | Interface extension (additive) | Research R-001: `WriteAsync` is overwrite-only; read-modify-write is memory-unsafe for large logs |
| Separate `DiagnosticLogStore` vs shared store | Separate store | Research R-004: different schemas, different filtering needs, cleaner separation |
| `ILoggerProvider` vs OTel `BaseExporter` | `ILoggerProvider` | Research R-002: avoids modifying shared OTel pipeline in ServiceDefaults |
| Tiered log levels (agent vs CP) | Independent configuration | User requirement: agent writes full detail to package; CP filters at its own floor before buffering/streaming/exporting |
| CLI lifecycle (`export --follow`) | SSE consumer in ExportCommand | Existing pattern: `ControlPlaneClient` already has SSE infrastructure for progress streaming |
| `manage logs` rename | Option C: `manage logs` → `manage diagnostics` | User decision: cleaner mapping — diagnostics command downloads diagnostic logs, new `manage progress` for events |
| Live observation model | TUI primary + `export --follow` | User clarification: TUI is the full dashboard; CLI provides inline follow via export; manage commands are snapshot/download only |

## Architecture Alignment

All design decisions checked against:
- `docs/architecture.md` — three-sink pattern already documented; diagnostics channel is an addition, not a departure
- `docs/cli-guide.md` — export command follows host builder pattern, `CommandBase<T>` inheritance, DI for all services
- `docs/tui-guide.md` — Terminal.Gui for diagnostics panel; SSE subscription pattern matches existing progress table
- `docs/control-plane.md` — ring buffer pattern matches existing `JobProgressStore`; separate `DiagnosticLogStore` parallels it
- `docs/agent-hosting.md` — three sinks (console, package, CP) already documented; adding logger providers follows same wiring
- `.agents/20-guardrails/core/architecture-boundaries.md` — rule #11 (CP no migration logic), #12 (agents stateless), #13 (IArtefactStore only), #16 (CLI no migration logic), #18 (no UI coupling in engine)
- `.agents/20-guardrails/domains/control-plane-rules.md` — standalone mode uses embedded Aspire APIs, not AppHost; ServiceDefaults for OTel
- `.agents/30-context/domains/job-lifecycle.md` — `--level` value passed via job definition to agent; no schema break (additive field)

Discrepancies with existing docs flagged in [discrepancies.md](discrepancies.md) (9 items). These will be resolved during `speckit.implement`.

## Implementation Design

### Layer 1: Abstractions (DevOpsMigrationPlatform.Abstractions)

**IArtefactStore.AppendAsync** (additive, non-breaking):
```csharp
Task AppendAsync(string relativePath, string content, CancellationToken cancellationToken);
```
Creates file + parent directories if they don't exist. Appends `content` to the end. Used by `PackageProgressSink` and `PackageLoggerProvider`.

**DiagnosticLogRecord** (new record type):
```csharp
public record DiagnosticLogRecord
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public required string Level { get; init; }
    public required string Category { get; init; }
    public required string Message { get; init; }
    public string? Exception { get; init; }
    public string? TraceId { get; init; }
    public string? SpanId { get; init; }
}
```

**DiagnosticLogOptions** (new, sealed):
```csharp
public sealed class DiagnosticLogOptions
{
    public const string SectionName = "Diagnostics";
    public string MinimumLevel { get; init; } = "Information";
    public int ChannelCapacity { get; init; } = 1024;
    public int FlushIntervalMs { get; init; } = 500;
    public int FlushBatchSize { get; init; } = 50;
}
```

### Layer 2: Infrastructure (DevOpsMigrationPlatform.Infrastructure)

**FileSystemArtefactStore.AppendAsync**:
```csharp
File.AppendAllTextAsync(fullPath, content, Encoding.UTF8, cancellationToken)
```
Creates parent directory if needed. Thread-safety: concurrent appends to the same file rely on OS-level file locking (acceptable for single-agent single-job writes).

**AzureBlobArtefactStore.AppendAsync**:
Uses `AppendBlobClient.AppendBlockAsync`. Creates the append blob on first call if it doesn't exist.

**PackageProgressSink** (complete the stub):
Follows the identical bounded-channel pattern as `ControlPlaneProgressSink`:
- `BoundedChannel<ProgressEvent>` with capacity 100, `DropOldest`
- `BackgroundService` drain loop: batch up to 50 records or flush every 500ms
- Serialize each `ProgressEvent` as JSON line + `\n`
- Call `IArtefactStore.AppendAsync("Logs/progress.jsonl", batch, ct)`
- Failures: catch, count dropped records, log at Debug to console

**PackageLoggerProvider** (new `ILoggerProvider`):
- Implements `ILoggerProvider`, `IDisposable`
- Creates `PackageLogger` instances per category
- `PackageLogger.Log<TState>()` maps `LogLevel`, category, message, exception, and `Activity.Current?.TraceId`/`SpanId` to a `DiagnosticLogRecord`
- Writes to `BoundedChannel<DiagnosticLogRecord>` (capacity from `DiagnosticLogOptions.ChannelCapacity`, `DropOldest`)
- `BackgroundService` drain: batch + flush to `IArtefactStore.AppendAsync("Logs/agent.jsonl", ...)`
- Minimum level from `DiagnosticLogOptions.MinimumLevel` (set from job's `--level`)

**ControlPlaneLoggerProvider** (new `ILoggerProvider`):
- Same channel pattern as `PackageLoggerProvider`
- Drain loop POSTs batches to `POST /agents/lease/{leaseId}/diagnostics`
- Failures: catch, count dropped, log at Debug. Never block the agent.
- Minimum level from `DiagnosticLogOptions.MinimumLevel` (same as package provider — both get the agent's per-job level)

**DiagnosticsServiceExtensions** (new):
```csharp
public static class DiagnosticsServiceExtensions
{
    public static IServiceCollection AddDiagnosticsServices(this IServiceCollection services)
    {
        // Register options, PackageLoggerProvider, ControlPlaneLoggerProvider
    }
}
```

### Layer 3: Control Plane (DevOpsMigrationPlatform.ControlPlane)

**DiagnosticLogStore** (new, mirrors `JobProgressStore`):
- `ConcurrentDictionary<Guid, JobEntry>` where `JobEntry` has:
  - `ConcurrentQueue<DiagnosticLogRecord>` (bounded, default capacity 1000 from `DiagnosticLogStoreOptions`)
  - `List<ChannelWriter<DiagnosticLogRecord>>` (SSE subscribers)
- `Add(Guid jobId, IEnumerable<DiagnosticLogRecord> records)`: filters by CP's deployment-level minimum, enqueues, notifies subscribers
- `GetSnapshot(Guid jobId, LogLevel? minimumLevel)`: returns filtered array
- `Subscribe(Guid jobId, LogLevel? minimumLevel)`: returns `ChannelReader<DiagnosticLogRecord>` for SSE

**DiagnosticLogStoreOptions** (new, sealed):
```csharp
public sealed class DiagnosticLogStoreOptions
{
    public const string SectionName = "DiagnosticLogStore";
    public int Capacity { get; init; } = 1000;
    public string MinimumLevel { get; init; } = "Warning"; // CP deployment-level floor
}
```

**DiagnosticsController** (new):
- `POST /agents/lease/{leaseId}/diagnostics` — accepts `DiagnosticLogRecord[]`, validates lease, calls `DiagnosticLogStore.Add()`
- `GET /jobs/{jobId}/diagnostics` — snapshot from `DiagnosticLogStore.GetSnapshot()`, optional `?level=` filter
- `GET /jobs/{jobId}/diagnostics?follow=true` — SSE stream from `DiagnosticLogStore.Subscribe()`, optional `?level=` filter

**ProgressController** (rename):
- `GET /jobs/{jobId}/logs` → `GET /jobs/{jobId}/progress`
- `GET /jobs/{jobId}/logs?follow=true` → `GET /jobs/{jobId}/progress?follow=true`

**LogDownloadController** (new):
- `GET /jobs/{jobId}/logs/download?type=progress` — reads `Logs/progress.jsonl` from package via `IArtefactStore`
- `GET /jobs/{jobId}/logs/download?type=diagnostics` — reads `Logs/agent.jsonl` from package via `IArtefactStore`
- Resolves job's `packageUri` to construct the appropriate `IArtefactStore` instance

### Layer 4: Migration Agent (DevOpsMigrationPlatform.MigrationAgent)

**MigrationAgentServiceExtensions** (update):
- Register `PackageLoggerProvider` and `ControlPlaneLoggerProvider` via `builder.Logging.AddProvider()`
- Bind `DiagnosticLogOptions` from job definition's `--level` value
- In standalone mode: also configure `DiagnosticLogStoreOptions.MinimumLevel` to match `--level`

### Layer 5: CLI (DevOpsMigrationPlatform.CLI.Migration)

**MigrationExportCommand** (update):
- Add `--follow` option to `ExportCommandSettings` (bool, default `false`)
- Add `--level` option to `ExportCommandSettings` (string, default `"Information"`, validated)
- After job submission:
  - **Standalone (no `--url`)**: `--follow` is implicit. Start Aspire services with CP's `DiagnosticLogStoreOptions.MinimumLevel` set to `--level`. Stream diagnostics SSE to console. On job terminal state → print summary, exit.
  - **Remote + `--follow`**: Submit job, then stream diagnostics SSE to console. On Ctrl+C → detach, print "Job continues. Use TUI to watch.", exit. On job terminal state → print summary, exit.
  - **Remote, no `--follow`**: Submit job, print `jobId`, exit.
- `--level` is passed to the job definition for the agent to configure its sinks

**ManageDiagnosticsCommand** (replaces ManageLogsCommand):
- `manage diagnostics --job <id> [--level <level>]`
- Calls `GET /jobs/{jobId}/logs/download?type=diagnostics` via `ControlPlaneClient`
- Filters results client-side by `--level` (since package may have more detail than CP)
- Outputs NDJSON to stdout
- No `--follow`

**ManageProgressCommand** (new):
- `manage progress --job <id>`
- Calls `GET /jobs/{jobId}/progress` via `ControlPlaneClient`
- Outputs `ProgressEvent` records to stdout
- No `--follow`

**Program.cs** (update registration):
```csharp
config.AddBranch("manage", branch => {
    branch.AddCommand<ManageListCommand>("list");
    branch.AddCommand<ManageStatusCommand>("status");
    branch.AddCommand<ManageProgressCommand>("progress");    // NEW (replaces logs)
    branch.AddCommand<ManageDiagnosticsCommand>("diagnostics"); // NEW (was logs)
    branch.AddCommand<ManagePauseCommand>("pause");
    branch.AddCommand<ManageResumeCommand>("resume");
    branch.AddCommand<ManageCancelCommand>("cancel");
    branch.AddCommand<ManageLoginCommand>("login");
    branch.AddCommand<ManageLogoutCommand>("logout");
});
```

**ControlPlaneClient** (update):
- Rename `GetProgressEventsAsync` (was `GetLogsAsync` or similar) to use `/progress` path
- Add `StreamDiagnosticsAsync(Guid jobId, LogLevel? level, CancellationToken ct)` returning `IAsyncEnumerable<DiagnosticLogRecord>` for SSE consumption
- Add `DownloadDiagnosticsAsync(Guid jobId)` returning `Stream` for file download

### Layer 6: TUI (Terminal.Gui panel in CLI)

**DiagnosticsPanel** (new, P2):
- Terminal.Gui `View` subclass
- Subscribes to `GET /jobs/{jobId}/diagnostics?follow=true&level=Warning`
- Renders log records in a scrolling list with level-based coloring
- Level filter toggle (Warning ↔ Information)
- Sits alongside existing metrics panel and progress table

### Tiered Log Level Data Flow

```
Operator: devopsmigration export --level Debug

┌─────────────────────────────────────┐
│ CLI                                  │
│  --level Debug → job definition      │
│  --follow → SSE consumer             │
└────────────┬────────────────────────┘
             │ POST /jobs (includes level=Debug)
             ▼
┌─────────────────────────────────────┐
│ Control Plane                        │
│  Deployment config: Warning          │
│  (standalone: adopts Debug)          │
│  Filters incoming < Warning          │
│  Ring buffer: Warning+ only          │
│  SSE: Warning+ only                  │
│  App Insights: Warning+ only         │
└────────────┬────────────────────────┘
             │ Lease (includes level=Debug)
             ▼
┌─────────────────────────────────────┐
│ Migration Agent                      │
│  PackageLoggerProvider: Debug+       │
│  → Logs/agent.jsonl: Debug+ records  │
│  ControlPlaneLoggerProvider: Debug+  │
│  → POST to CP (CP filters at Warning)│
└─────────────────────────────────────┘

Package has full Debug detail.
Live view (TUI/CLI) shows Warning+ only.
Post-mortem: manage diagnostics downloads full package.
```

### Job Definition Extension

Add an optional `diagnostics` section to the job contract:

```json
{
  "jobId": "...",
  "mode": "Export",
  "diagnostics": {
    "minimumLevel": "Debug"
  }
}
```

This is additive — existing jobs without this field default to `"Warning"`. No schema version bump needed.

## .vscode/launch.json Entries Required

The following debug profiles must be added or updated:
- `manage diagnostics` — runs `manage diagnostics --job <test-job-id> --level Warning`
- `manage progress` — runs `manage progress --job <test-job-id>`
- Update existing `export` profile with `--follow` and `--level` options

## Post-Design Constitution Re-Check

- [x] **Package-First (I):** Confirmed — `AppendAsync` goes through `IArtefactStore` abstraction. Both `FileSystemArtefactStore` (`File.AppendAllTextAsync`) and `AzureBlobArtefactStore` (`AppendBlobClient.AppendBlockAsync`) implement it.
- [x] **Module Isolation (V):** Confirmed — no module code is touched. Sinks are infrastructure. `DiagnosticLogRecord` in Abstractions. No concrete store references.
- [x] **Separation of Planes (VI):** Confirmed — `DiagnosticsController` only buffers/streams. `PackageLoggerProvider` only writes. `export --follow` consumes SSE from CP (read-only). `manage diagnostics` downloads from package via CP (read-only). No cross-layer logic.
- [x] **SOLID & DI (IX):** Confirmed — `DiagnosticLogOptions` is sealed with `SectionName = "Diagnostics"`. `DiagnosticLogStoreOptions` is sealed with `SectionName = "DiagnosticLogStore"`. All registration via `AddDiagnosticsServices()` and `AddDiagnosticLogStore()` extension methods. Interfaces in Abstractions. Options validated with `[Required]` where applicable.
- [x] **Tiered Log Levels:** Confirmed — agent's per-job `--level` is independent of CP's deployment config. In standalone mode, CP adopts operator's level. In non-standalone, CP uses its own. Package always gets full agent-level detail. CP filters before buffering. App Insights / OTel export at CP level.
- [x] **CLI constraints (Rule #16):** Confirmed — `export --follow` streams from the CP via SSE (read-only). It does not call modules, write cursors, or access `IArtefactStore`. `manage diagnostics` downloads files via CP API. `manage progress` reads from CP ring buffer. All through `ControlPlaneClient`.
- [x] **Build & Test gates:** Plan includes: `dotnet clean && dotnet build --no-incremental` must pass; `dotnet test` all tests must pass; at least one scenario config run via `launch.json` debug profile. New `launch.json` entries required for `manage diagnostics` and `manage progress`. New `[TestCategory("SystemTest")]` tests for `export --follow` and `manage diagnostics`.

