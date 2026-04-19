# Implementation Plan: Telemetry Pipeline — Cloud Export + TUI Live Feed

**Branch**: `first-coding-session` | **Date**: 2026-04-02 | **Spec**: user request (no spec.md)
**Input**: Direct user request: "send telemetry to a cloud provider and intercept for TUI via AgentJobService → ControlPlaneService → HTTP → TUI"

## Summary

Wire a complete observability pipeline that (a) exports OTel metrics and traces to cloud
providers (OTLP-compatible endpoints and Azure Monitor) via configuration, and (b) routes
live metric snapshots from the Migration Agent and TFS export subprocess through the Control
Plane HTTP API to the TUI, where they are rendered as a live metrics panel alongside the
existing module/stage progress table.

The approach extends the existing `ProgressEvent` model with an optional `MetricSnapshot`
field for subprocess relay, adds a `ControlPlaneTelemetryClient` that pushes snapshots on a
5-second timer, adds two new Control Plane endpoints (`POST /agents/lease/{id}/telemetry`
and `GET /jobs/{jobId}/telemetry`), and adds a `TelemetryPanel` Terminal.Gui view to the TUI.

## Technical Context

**Language/Version**: C# 10+, .NET 9/10 (Migration Agent, Control Plane, CLI); .NET 4.8 (TFS export subprocess, multi-targeted via `netstandard2.0` Abstractions)
**Primary Dependencies**: OpenTelemetry 1.12.0, `OpenTelemetry.Exporter.OpenTelemetryProtocol` 1.12.0, `Azure.Monitor.OpenTelemetry.AspNetCore` 1.4.0, Serilog 4.x, Terminal.Gui (TUI rendering), ASP.NET Core Web API (Control Plane), `Microsoft.Extensions.Hosting` (Migration Agent)
**Storage**: In-memory `ConcurrentDictionary<Guid, MetricSnapshot>` in the Control Plane (Phase 1). No package writes. No database.
**Testing**: MSTest + Reqnroll (ATDD), Moq with `MockBehavior.Strict`
**Target Platform**: .NET 9/10 (Linux/Windows container + local); .NET 4.8 subprocess (Windows only)
**Project Type**: Library/service additions to an existing multi-project solution
**Performance Goals**: Snapshot push < 1 ms in-process; control plane endpoint < 10 ms p99 (in-memory store, no I/O)
**Constraints**: No in-memory sort; no direct filesystem access in module code; subprocess communicates only via stdout NDJSON; TUI must not contain migration logic; no migration logic in Control Plane
**Scale/Scope**: Snapshots every 5 s × job duration (hours). Control plane stores 1 snapshot per job, overwritten each push — no accumulation.


## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- [x] **Package-First (I):** No direct source-to-target migration. All reads/writes go via the on-disk package through `IArtefactStore`. — **N/A: telemetry is not migration data; no package reads or writes**
- [x] **Streaming (II):** Import processes one revision folder at a time. No in-memory list/array of all revisions. — **N/A: no import logic**
- [x] **WorkItems Layout (III):** Folder structure preserved. — **N/A: no package folder changes**
- [x] **Checkpointing (IV):** Module uses cursor file. — **N/A: telemetry is stateless**
- [x] **Module Isolation (V):** All new services use `IArtefactStore`/`IStateStore` only for migration data. New telemetry services are DI-injected via abstractions (`IMetricSnapshotStore`, `IControlPlaneTelemetryClient`). No concrete store references in module code.
- [x] **Separation of Planes (VI):** Migration Agent pushes snapshots to Control Plane via HTTP. TUI reads from Control Plane only. Control Plane stores metric snapshots — no migration logic. No `Console` writes from the Job Engine.
- [x] **Determinism (VII):** Metric snapshots are observability data — not part of the package; skipped.
- [x] **ATDD-First (VIII):** Each component below has at least one Given/When/Then acceptance scenario defined before implementation begins.
- [x] **SOLID & DI (IX):** All new services: constructor injection. Config via `IOptions<TelemetryOptions>` with sealed options class and `SectionName` constant. Interfaces in `DevOpsMigrationPlatform.Abstractions`. Registration via dedicated `AddTelemetryServices()` extension method.

**Constitution check: PASSED**


## Project Structure

### Documentation (this feature)

```text
specs/first-coding-session/
├── plan.md              # This file
├── research.md          # Phase 0 — technology decisions, rejected alternatives
├── data-model.md        # Phase 1 — MetricSnapshot, TelemetryOptions, interfaces
├── quickstart.md        # Phase 1 — how to configure and use the telemetry pipeline
├── contracts/
│   ├── telemetry-api.md        # Phase 1 — HTTP contract: POST /telemetry + GET /telemetry
│   └── otel-exporter-config.md # Phase 1 — appsettings.json Telemetry section contract
└── tasks.md             # Phase 2 output (/speckit.tasks — NOT yet created)
```

### Source Code Changes

```text
src/DevOpsMigrationPlatform.Abstractions/
├── Models/
│   └── MetricSnapshot.cs                     # NEW — point-in-time metric aggregates
│   └── ProgressEvent.cs                      # MODIFY — add optional Metrics property
├── Telemetry/
│   └── TelemetryOptions.cs                   # NEW — IOptions config (SectionName = "Telemetry")
│   └── IMetricSnapshotStore.cs               # NEW — passive store written by OTel exporter, read by push timer
│   └── IControlPlaneTelemetryClient.cs       # NEW — HTTP push interface

src/DevOpsMigrationPlatform.Infrastructure/
├── Telemetry/
│   └── InMemoryMetricSnapshotStore.cs        # NEW — volatile-field impl of IMetricSnapshotStore
│   └── SnapshotMetricExporter.cs             # NEW — BaseExporter<Metric>: writes to IMetricSnapshotStore
│   └── ControlPlaneTelemetryClient.cs        # NEW — HttpClient-based IControlPlaneTelemetryClient
│   └── TelemetryServiceExtensions.cs         # NEW — AddTelemetryServices(): wires MeterProvider fan-out

src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
├── WorkItemExportService.cs                  # MODIFY — embed MetricSnapshot in ProgressEvent every N revisions
├── MigrationPlatformHost.cs                  # MODIFY — wire OTLP/Azure Monitor exporters from TelemetryOptions

src/DevOpsMigrationPlatform.Infrastructure.TfsLegacy/
├── TfsExporterProcessAdapter.cs              # MODIFY — extract MetricSnapshot from ProgressEvent, push to IControlPlaneTelemetryClient

src/DevOpsMigrationPlatform.MigrationAgent/
├── MigrationAgentWorker.cs                   # MODIFY — start ControlPlaneTelemetryTimer background loop
├── ControlPlaneTelemetryTimer.cs             # NEW — BackgroundService pushing MetricSnapshot to Control Plane
├── ActiveLeaseState.cs                       # NEW — singleton carrying current leaseId between worker and timer

src/DevOpsMigrationPlatform.ControlPlane/
├── Controllers/
│   └── TelemetryController.cs               # NEW — POST /agents/lease/{id}/telemetry + GET /jobs/{id}/telemetry
├── Services/
│   └── JobTelemetryStore.cs                 # NEW — ConcurrentDictionary<Guid, MetricSnapshot>

src/DevOpsMigrationPlatform.CLI.Migration/
├── Views/
│   └── TelemetryPanel.cs                    # NEW — Terminal.Gui panel for live metrics display
│   └── TelemetryPoller.cs                   # NEW — polls GET /jobs/{id}/telemetry, feeds TelemetryPanel

tests/DevOpsMigrationPlatform.Infrastructure.Tests/
├── Telemetry/
│   └── SnapshotMetricExporterTests.cs        # NEW — unit tests for OTel BaseExporter<Metric> + IMetricSnapshotStore
│   └── ControlPlaneTelemetryClientTests.cs   # NEW — unit tests for HTTP push

features/platform/
└── telemetry/
    └── metric-snapshot-relay.feature         # NEW — ATDD acceptance scenarios
    └── otel-cloud-export.feature             # NEW — ATDD acceptance scenarios
    └── tui-metrics-panel.feature             # NEW — ATDD acceptance scenarios
```

## Complexity Tracking

*No constitution violations. No additional complexity beyond what the architecture already prescribes.*
