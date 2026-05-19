# Implementation Plan: Work Item OpenTelemetry Metrics

**Branch**: `018-workitem-otel-metrics` | **Date**: 2026-04-19 | **Spec**: [specs/018-workitem-otel-metrics/spec.md](spec.md)
**Input**: Feature specification from `/specs/018-workitem-otel-metrics/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Expand the platform's OpenTelemetry instrumentation from 13 export-centric metrics under two meters to 24 instruments under a single consolidated `DevOpsMigrationPlatform.Migration` meter. The new instruments cover execution counters, payload complexity histograms, count-parity correctness metrics (Tier 3 post-flight), in-flight concurrency gauges, and reserved idempotency counters. All metric names are renamed from underscore-separated (`work_item_exported_total`) to dot-separated (`migration.workitems.attempted`) with mandatory `job.id`, `operation`, and `module` dimension tags. The `MetricSnapshot` DTO is expanded to carry all new instrument aggregates, with nullable properties for deferred (mapping-store-dependent) metrics.

## Reconciliation Addendum (2026-05-17)

### Current status

- This plan reflects an earlier telemetry architecture and is now partially stale.
- Runtime implementation has moved to the platform metrics contract (`IPlatformMetrics`, `WellKnownAgentMetricNames`, `WellKnownMeterNames.Agent`) per `specs/031-platform-metrics-unification` and ADR 0011.
- Reconciled task truth is recorded in `tasks.md` with evidence-bearing statuses.
- Canonical task format has been normalised in `tasks.md` to `[X]/[ ]` plus `— Status: ...`.

### Remaining incomplete work (IDs)

`T010, T011, T012, T022, T023, T026, T027, T028, T030, T032, T033, T037, T047, T048, T049, T050, T052`

### Completed because superseded (IDs + source)

`T001, T002, T003, T004, T005, T006, T007, T008, T009, T013, T014, T016, T017, T018, T019, T021, T025, T038, T039, T040, T041, T042, T043, T044, T051`

Superseding source: `specs/031-platform-metrics-unification/spec.md` and `docs/adr/0011-unified-platform-metric-namespace.md`.

### Contradictions and reconciliation

- Planned artifacts `IMigrationMetrics`, `MigrationMetrics`, `WellKnownMetricNames`, `DevOpsMigrationPlatform.Migration`, and `MetricSnapshot` are not the current canonical runtime surface.
- Current runtime surfaces are `IPlatformMetrics`, `PlatformMetrics`, `WellKnownAgentMetricNames`, `WellKnownMeterNames.Agent`, and control-plane telemetry DTOs (`JobMetrics`, `MigrationDiagnostics`).
- `SnapshotMetricExporterTests.cs` path listed in this plan does not exist; related verification tasks remain incomplete.

### Verification evidence

- Implemented contract and meter sources:
  - `src/DevOpsMigrationPlatform.Abstractions.Agent/Telemetry/IPlatformMetrics.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/PlatformMetrics.cs`
  - `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownAgentMetricNames.cs`
  - `src/DevOpsMigrationPlatform.Abstractions/WellKnownMeterNames.cs`
- Wiring/orchestrator sources:
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/TelemetryServiceExtensions.cs`
  - `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Export/WorkItemExportOrchestrator.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.Agent/Import/WorkItemImportOrchestrator.cs`
- Incomplete-gap sources:
  - `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/WorkItemExportMetrics.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Telemetry/AttachmentDownloadMetrics.cs`
- Reconciliation command evidence:
  - `/speckit.analyze` run against `specs/018-workitem-otel-metrics`
  - `/speckit.checklist` run (dry-run) against `specs/018-workitem-otel-metrics`

## Technical Context

**Language/Version**: C# 10+, targeting .NET 10 (multi-target `net481;net10.0` for Abstractions)
**Primary Dependencies**: OpenTelemetry 1.14.0 (Counter, Histogram, UpDownCounter, ObservableGauge — all already in `Directory.Packages.props`)
**Storage**: N/A — metrics are purely observational, no package writes
**Testing**: Reqnroll.MSTest + Moq (MockBehavior.Strict); MSTest for unit tests
**Target Platform**: .NET 10 hosts + .NET 4.8 TFS subprocess (via multi-targeted Abstractions)
**Project Type**: Cross-cutting platform library (observability)
**Performance Goals**: Zero measurable overhead on the migration hot path — OTel counters and histograms are lock-free
**Constraints**: `net481` compatibility for constants and interfaces in Abstractions; no new NuGet packages required
**Scale/Scope**: 24 metric instruments across 5 categories, 5 user stories, MetricSnapshot expansion with ~25 new properties

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Confirmed ALL files in `/.agents/20-guardrails/` (system-architecture, coding-standards, testing-standards, workitems-rules, migration-rules, module-template, aspire-integration, atdd-workflow, acceptance-test-format), ALL relevant files in `/.agents/30-context/` (checkpointing), and relevant `/docs/` files (architecture, validation, modules, orchestration, migration-agent, configuration) have been read.

- [x] **Package-First (I):** Not affected. Metrics are observational — they record measurements about the migration process. No reads/writes to the package are introduced by this feature. Existing package I/O paths are unchanged.
- [x] **Streaming (II):** Not affected. No import logic is altered. Metrics are recorded inline during existing streaming processing; they do not buffer or materialise data.
- [x] **WorkItems Layout (III):** Not affected. No folder structure changes.
- [x] **Checkpointing (IV):** Not affected. No checkpoint changes. Metrics are ephemeral per-process measurements, not durable state.
- [x] **Module Isolation (V):** Compliant. The new `IMigrationMetrics` interface is defined in `DevOpsMigrationPlatform.Abstractions`. Constants (`WellKnownMetricNames`, `WellKnownMeterNames`) remain in `Abstractions`. The concrete implementation lives in `DevOpsMigrationPlatform.Infrastructure`. Modules receive the metrics interface via constructor injection.
- [x] **Separation of Planes (VI):** Compliant. `MetricSnapshot` flows through the existing `ControlPlaneTelemetryClient` → control plane → TUI pipeline. No migration logic is added to the control plane. The TUI reads the expanded DTO for display only.
- [x] **Determinism (VII):** Not affected. Metric recording is observational and does not influence migration output. `MetricSnapshot` expansion is additive (new nullable properties), not a breaking change.
- [x] **ATDD-First (VIII):** Compliant. The spec contains 5 user stories with 12 acceptance scenarios (Given/When/Then). Each will be implemented via the ATDD inner loop — one scenario per session per commit. **Infrastructure exemption**: Phases 1–2 create shared plumbing (interfaces, constants, DI wiring, MetricSnapshot expansion) that has no user-visible behaviour on its own. These foundational tasks are exempt from per-scenario Gherkin gating because they are prerequisites for all user stories and cannot be expressed as independent acceptance scenarios. Gherkin feature files are created starting in Phase 3, one per user story, before that story’s implementation tasks.
- [x] **SOLID & DI (IX):** Compliant. New `IMigrationMetrics` interface in Abstractions. `MigrationMetrics` concrete class receives `Meter` via DI factory. Configuration uses existing `TelemetryOptions` (sealed, `init`-only, `SectionName` constant). Registration in a dedicated `AddMigrationMetrics` extension method. No raw `IConfiguration` access.

## Project Structure

### Documentation (this feature)

```text
specs/018-workitem-otel-metrics/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── metric-instruments.md
├── discrepancies.md     # Architecture discrepancies (from speckit.specify)
└── tasks.md             # Phase 2 output (speckit.tasks — NOT created by speckit.plan)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Models/
│   │   └── MetricSnapshot.cs                          # MODIFY — expand with ~25 new properties
│   └── Telemetry/
│       ├── WellKnownMetricNames.cs                    # MODIFY — rename existing + add 28 new constants
│       ├── WellKnownMeterNames.cs                     # MODIFY — add Migration, deprecate two old names
│       ├── IMigrationMetrics.cs                       # NEW — unified recording interface
│       ├── IWorkItemExportMetrics.cs                  # MODIFY — mark obsolete, delegate to IMigrationMetrics
│       └── IAttachmentDownloadMetrics.cs              # MODIFY — mark obsolete, delegate to IMigrationMetrics
├── DevOpsMigrationPlatform.Infrastructure/
│   └── Telemetry/
│       ├── MigrationMetrics.cs                        # NEW — concrete Meter + instrument registration
│       ├── SnapshotMetricExporter.cs                  # MODIFY — handle all new instrument names
│       ├── InMemoryMetricSnapshotStore.cs             # UNCHANGED
│       └── TelemetryServiceExtensions.cs              # MODIFY — register new meter + IMigrationMetrics
├── DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
│   └── Telemetry/
│       ├── WorkItemExportMetrics.cs                   # MODIFY — delegate to IMigrationMetrics
│       └── AttachmentDownloadMetrics.cs               # MODIFY — delegate to IMigrationMetrics
└── DevOpsMigrationPlatform.MigrationAgent/
    └── MigrationAgentServiceExtensions.cs             # MODIFY — register new meter name

tests/
├── DevOpsMigrationPlatform.Infrastructure.Tests/
│   └── Telemetry/
│       ├── SnapshotMetricExporterTests.cs             # MODIFY — test all new instruments
│       ├── MigrationMetricsTests.cs                   # NEW — unit tests for instrument registration + recording
│       └── MetricSnapshotSerializationTests.cs        # NEW — JSON round-trip tests
└── DevOpsMigrationPlatform.Abstractions.Tests/
    └── Telemetry/
        └── WellKnownMetricNamesTests.cs               # NEW — validate naming convention compliance

features/
├── export/work-items/
│   └── export-execution-metrics.feature               # NEW — US1 scenarios
├── export/work-items/
│   └── export-payload-metrics.feature                 # NEW — US2 scenarios
├── platform/validation/
│   └── post-flight-correctness-metrics.feature        # NEW — US3 scenarios
├── platform/telemetry/
│   ├── in-flight-concurrency-metrics.feature          # NEW — US4 scenarios
│   └── idempotency-metric-registration.feature        # NEW — US5 scenario
```

**Structure Decision**: This feature modifies existing projects only — no new projects are created. Changes span four layers (Abstractions → Infrastructure → TfsObjectModel → MigrationAgent) plus test projects. All changes follow the existing project boundaries.

## Complexity Tracking

No constitution violations. All changes fit within existing project boundaries and established patterns.

