# Implementation Plan: OpenTelemetry Observability — CLI DI and Phase 2 Live Progress Streaming

**Branch**: `002-otel-observability-phase2` | **Date**: 2026-04-03 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/002-otel-observability-phase2/spec.md`

## Summary

This feature delivers two capabilities:

1. **CLI Observability (US-1, P1)** — Wire the OTel SDK (traces, metrics, logs) into `DevOpsMigrationPlatform.CLI.Migration/Program.cs` with Azure Monitor export when a connection string is present. Each CLI command starts a child `Activity` span. `TracerProvider` and `MeterProvider` are flushed and disposed before process exit. This mirrors the OTel pattern already used in `ServiceDefaults` by `ControlPlaneHost` and `MigrationAgent`, adapted for the non-hosted CLI entry point (which uses a bare `ServiceCollection`, not `IHostApplicationBuilder`).

2. **Phase 2 Live Progress Streaming (US-2 P1, US-3 P2, US-4 P3)** — A new `ControlPlaneProgressSink` (`IProgressSink` in Infrastructure) fire-and-forgets individual `ProgressEvent` records to the Control Plane via a background `Channel`. The Control Plane stores them in a bounded per-job ring buffer (`JobProgressStore`) and exposes them via a REST snapshot (`GET /jobs/{jobId}/logs`) and an SSE stream (`GET /jobs/{jobId}/logs?follow=true`). A new `migrate logs` CLI command prints buffered or live events as NDJSON. The TUI SSE consumer (US-4, P3) is planned in the spec but deferred to its own dedicated session.

**Approach**: Extend existing patterns — `JobTelemetryStore` → `JobProgressStore`; `TelemetryController` → new `ProgressController`; `ControlPlaneTelemetryClient` pattern → `ControlPlaneProgressSink`. No new NuGet packages required in `ControlPlane` or `MigrationAgent`; two new packages (`OpenTelemetry.Extensions.Hosting` and `Azure.Monitor.OpenTelemetry.Exporter`) are added to `CLI.Migration.csproj` only.

## Technical Context

**Language/Version**: C# / .NET 10 (`net10.0`); .NET 4.8 TFS subprocess (`CLI.TfsMigration`) is out of scope for this feature  
**Primary Dependencies**:
- `OpenTelemetry`, `OpenTelemetry.Metrics`, `OpenTelemetry.Trace`, `OpenTelemetry.Logs` — already in `ServiceDefaults`; must be added directly to `CLI.Migration.csproj`
- `Azure.Monitor.OpenTelemetry.Exporter` (standalone package, **not** the `AspNetCore` variant) — for CLI OTel export
- `System.Threading.Channels` — BCL in .NET 10; no NuGet package needed
- `Microsoft.Extensions.Http` — already used for `ControlPlaneTelemetryClient`; used for `ControlPlaneProgressSink` and extended `ControlPlaneClient`
- `Spectre.Console.Cli` — already present in `CLI.Migration`
- `Reqnroll.MSTest` + `Moq` — for new test project `DevOpsMigrationPlatform.ControlPlane.Tests`

**Storage**: Per-job in-memory ring buffer (`ConcurrentQueue<ProgressEvent>` capped at capacity + per-subscriber `Channel<ProgressEvent>` for SSE fan-out). Transient — cleared on Control Plane restart. No database schema changes.  
**Testing**: Reqnroll.MSTest + `Moq MockBehavior.Strict` — same conventions as `Infrastructure.Tests`  
**Target Platform**: Linux/Windows server (`ControlPlaneHost`, `MigrationAgent`); cross-platform CLI  
**Project Type**: Service library (ControlPlane), Worker service (MigrationAgent), CLI (CLI.Migration)  
**Performance Goals**: Progress POST delivered within 1 s of `IProgressSink.Emit` call; SSE event end-to-end latency < 2 s; CLI cold start overhead < 50 ms added  
**Constraints**: Ring buffer capped at 1000 events (`DropOldest`); no event batching in v1; OTel must flush before exit; SSE heartbeat comment every 15 s  
**Scale/Scope**: Single agent per job; unlimited concurrent SSE subscribers per job in v1 (documented operational constraint)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Package-First (I):** N/A — this feature involves no reads or writes to the migration package (`IArtefactStore`). `ControlPlaneProgressSink` posts to the Control Plane HTTP API, not to the package. Progress events are ephemeral telemetry, not migration data.
- [x] **Streaming (II):** N/A — this feature introduces no import processing. The ring buffer is bounded (capacity 1000, `DropOldest`); no unbounded in-memory accumulation of events occurs at any layer.
- [x] **WorkItems Layout (III):** N/A — no changes to the WorkItems folder structure, `revision.json`, or attachment layout.
- [x] **Checkpointing (IV):** N/A — progress events are live telemetry, not module cursor state. The ring buffer is intentionally transient (in-memory, cleared on restart). No cursor files are involved.
- [x] **Module Isolation (V):** Pass — `ControlPlaneProgressSink` implements `IProgressSink` (defined in Abstractions). It lives in Infrastructure and is injected via DI. No module code references `ControlPlaneProgressSink` directly; all modules see only `IProgressSink`. No `IArtefactStore` or filesystem access in any new component.
- [x] **Separation of Planes (VI):** Pass — `ProgressController` and `JobProgressStore` receive and store events only; no migration logic. `LogsCommand` reads from the Control Plane API; no migration logic or module access. `ControlPlaneProgressSink` reports events; it does not call source/target APIs or access the package. CLI OTel wiring is at the composition root (`Program.cs`) only.
- [x] **Determinism (VII):** Pass — progress events are ephemeral telemetry. No changes to package layout, `manifest.json`, or cursor format. The ring buffer intentionally does not guarantee replay fidelity across restarts; this is documented.
- [x] **ATDD-First (VIII):** Pass — all 4 user stories have Given/When/Then acceptance scenarios in `spec.md` (4 + 5 + 6 + 4 = 19 scenarios total). Each scenario will follow the ATDD inner loop: one scenario per session per commit.
- [x] **SOLID & DI (IX):** Pass — `ControlPlaneProgressSink`, `JobProgressStore`, and `ProgressController` use constructor injection throughout. Configuration flows through `IOptions<JobProgressOptions>` (sealed class, `SectionName` constant, validation attributes). Registration via dedicated `AddProgressServices` extension method. CLI OTel wiring in `Program.cs` is acceptable at the composition root.

## Project Structure

### Documentation (this feature)

```text
specs/002-otel-observability-phase2/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
│   └── progress-api.md  # REST + SSE endpoint contracts
└── tasks.md             # Phase 2 output (/speckit.tasks command — NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
  DevOpsMigrationPlatform.Abstractions/          [UNCHANGED — IProgressSink + ProgressEvent already satisfy the contract]

  DevOpsMigrationPlatform.Infrastructure/
    Telemetry/
      ControlPlaneTelemetryClient.cs             [existing, unchanged]
      ControlPlaneProgressSink.cs                ← NEW  (IProgressSink; bounded background Channel; best-effort POST to /agents/lease/{leaseId}/progress)
      InMemoryMetricSnapshotStore.cs             [existing, unchanged]
      SnapshotMetricExporter.cs                  [existing, unchanged]
      TelemetryServiceExtensions.cs             [modified — adds AddControlPlaneProgressSink extension method]

  DevOpsMigrationPlatform.ControlPlane/
    Controllers/
      TelemetryController.cs                     [existing, unchanged]
      ProgressController.cs                      ← NEW  (POST /agents/lease/{leaseId}/progress · GET /jobs/{jobId}/logs · GET /jobs/{jobId}/logs?follow=true SSE)
    Services/
      ControlPlaneServiceExtensions.cs           [modified — registers JobProgressStore + JobProgressOptions]
      ILeaseJobResolver.cs                       [existing, unchanged]
      JobProgressOptions.cs                      ← NEW  (sealed options: Capacity=1000, SectionName="JobProgress")
      JobProgressStore.cs                        ← NEW  (per-job ring buffer ConcurrentQueue + SSE subscriber fan-out via per-subscriber Channel)
      JobTelemetryStore.cs                       [existing, unchanged]
      StubLeaseJobResolver.cs                    [existing, unchanged]

  DevOpsMigrationPlatform.MigrationAgent/
    ActiveLeaseState.cs                          [existing, unchanged]
    ControlPlaneTelemetryTimer.cs               [existing, unchanged]
    MigrationAgentWorker.cs                      [existing, unchanged]
    Program.cs                                   [modified — register ControlPlaneProgressSink as IProgressSink alongside ConsoleProgressSink + PackageProgressSink]

  DevOpsMigrationPlatform.CLI.Migration/
    Commands/
      AzureDevOpsSettings.cs                     [existing, unchanged]
      LogsCommand.cs                             ← NEW  (migrate logs --job <id> [--follow] — NDJSON output)
      TfsExportCommand.cs                        [existing, unchanged]
      Discovery/
        InventoryCommand.cs                      [existing, unchanged]
    Infrastructure/
      TypeRegistrar.cs                           [existing, unchanged]
      TypeResolver.cs                            [existing, unchanged]
    JobRunners/
      ControlPlaneClient.cs                      [modified — add GetLogsAsync (snapshot) + FollowLogsAsync (SSE) methods]
    Views/
      TelemetryPanel.cs                          [existing, unchanged]
      TelemetryPoller.cs                         [existing, unchanged]
    ExternalToolRunner.cs                        [existing, unchanged]
    Program.cs                                   [modified — add OTel DI: ActivitySource, Azure Monitor exporter, TracerProvider/MeterProvider flush on exit]
    TfsExporterProcessAdapter.cs                 [existing, unchanged]

  # P3 — deferred to its own session:
  # DevOpsMigrationPlatform.TUI/                [NOT in this feature — User Story 4 is P3, planned separately]

  DevOpsMigrationPlatform.AppHost/               [UNCHANGED]
  DevOpsMigrationPlatform.ControlPlaneHost/      [UNCHANGED]
  DevOpsMigrationPlatform.ServiceDefaults/       [UNCHANGED — already wires OTel for hosted services via AddServiceDefaults]
  DevOpsMigrationPlatform.Infrastructure.AzureDevOps/   [UNCHANGED]
  DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/ [UNCHANGED]
  DevOpsMigrationPlatform.CLI.TfsMigration/      [UNCHANGED — .NET 4.8 subprocess, out of scope]

tests/
  DevOpsMigrationPlatform.Infrastructure.Tests/  [modified — new Reqnroll acceptance test for ControlPlaneProgressSink]
    Telemetry/
      ControlPlaneTelemetryClientTests.cs        [existing, unchanged]
      SnapshotMetricExporterTests.cs             [existing, unchanged]
      ControlPlaneProgressSinkSteps.cs           ← NEW
      ControlPlaneProgressSinkContext.cs         ← NEW

  DevOpsMigrationPlatform.ControlPlane.Tests/    ← NEW PROJECT
    Progress/
      JobProgressStoreSteps.cs                   ← NEW
      JobProgressStoreContext.cs                 ← NEW
      ProgressControllerSteps.cs                 ← NEW
      ProgressControllerContext.cs               ← NEW
    DevOpsMigrationPlatform.ControlPlane.Tests.csproj  ← NEW (net10.0; Reqnroll.MSTest + Moq)

features/
  platform/
    telemetry/                                   ← NEW folder
      cli-otel.feature                           ← NEW  (US-1 scenarios)
      progress-sink.feature                      ← NEW  (US-2 scenarios)
      migrate-logs.feature                       ← NEW  (US-3 scenarios)
      job-progress-store.feature                 ← NEW  (US-2 ring buffer unit scenarios)
      progress-controller.feature                ← NEW  (US-2 endpoint scenarios)
```

**Structure Decision**: Modify 4 existing `src/` projects only (Infrastructure, ControlPlane, MigrationAgent, CLI.Migration). Add 1 new test project (`ControlPlane.Tests`). Add 5 feature files under `features/platform/telemetry/`. The P3 TUI project is deferred. Feature files sit under `platform/` because observability spans all layers and is not tied to a single export/import operation.

## Complexity Tracking

> No constitution violations identified. All principles are either N/A or pass. This table is not required.
