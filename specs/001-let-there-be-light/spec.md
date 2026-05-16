# Spec Reconciliation Ledger: 001-let-there-be-light

## Current status

This folder is a legacy telemetry spec baseline that has been partially implemented and substantially evolved by later specifications and runtime refactors.

Task reconciliation result from `tasks.md`:
- Complete: 15
- Incomplete: 2
- Complete/superseded: 22

## Remaining incomplete work (IDs)

- `T025` — expected subprocess metric-emission implementation was not found at the specified location or an equivalent implementation in `Infrastructure.TfsObjectModel`.
- `T034` — `SnapshotMetricExporterTests` test class/file is not present.

## Completed because superseded (IDs + source)

- Superseded by `specs/031-platform-metrics-unification`: `T001, T004, T005, T007, T008, T010, T012, T013, T014, T020, T021, T022, T023, T037, T039`
- Superseded by `specs/021.2-separation-of-concerns`: `T016, T017, T035, T036, T038`
- Superseded by `specs/023.5-tfsmigrationagent-architectural-consistency`: `T027`
- Superseded by `specs/008-tui-job-dashboard`: `T032`

## Contradictions and reconciliation

- Contract model drift: spec artifacts describe `MetricSnapshot` and `POST /agents/lease/{leaseId}/telemetry`; implementation uses `JobMetrics`/`JobSnapshot` and `POST /agents/lease/{leaseId}/metrics` plus `/snapshot`.
- Runtime host drift: tasks reference `ControlPlane/Program.cs`; current runtime host entrypoint is `ControlPlaneHost/Program.cs`.
- TFS topology drift: task expects CLI `TfsExporterProcessAdapter`; architecture now routes TFS via dedicated `TfsMigrationAgent`.
- Metrics naming drift: early examples use old metric naming/shape; current runtime uses unified platform metric conventions from later spec work.

## Verification evidence

- `src/DevOpsMigrationPlatform.ControlPlane/Controllers/TelemetryController.cs`
- `src/DevOpsMigrationPlatform.Abstractions/Streaming/ProgressEvent.cs`
- `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/JobMetrics.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryTimer.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Telemetry/ControlPlaneTelemetryClient.cs`
- `src/DevOpsMigrationPlatform.ControlPlane/Jobs/ILeaseJobResolver.cs`
- `src/DevOpsMigrationPlatform.ControlPlane/Jobs/StubLeaseJobResolver.cs`
- `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownMeterNames.cs`
- `features/platform/telemetry/*.feature`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Telemetry/ControlPlaneTelemetryClientTests.cs`

