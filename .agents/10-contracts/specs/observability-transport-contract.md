# Observability Transport Contract

Canonical contract for runtime observability transport channels.

## Contract Surface

- `IProgressSink`
- `CompositeProgressSink`
- `PackageProgressSink`
- `ControlPlaneProgressSink`
- `ControlPlaneTelemetryClient`
- `ControlPlaneTelemetryTimer`
- `ControlPlaneLoggerProvider`
- `PackageLoggerProvider`
- `PlatformMetrics`

## Required Semantics

1. Subsystems emit progress, diagnostics, traces, and metric snapshots through the canonical transport surfaces.
2. Progress is transported to both control-plane and package run logs.
3. Diagnostics are transported to control-plane diagnostics stream and package diagnostics log stream.
4. Telemetry snapshots are transported to control-plane telemetry endpoints.
5. Transport contract is cross-cutting and must preserve O-1..O-5 requirements.

## Sequence Diagram

```mermaid
sequenceDiagram
  participant SUB as AnySubsystem
  participant CPS as ControlPlaneProgressSink
  participant PPS as PackageProgressSink
  participant CLP as ControlPlaneLoggerProvider
  participant CTT as ControlPlaneTelemetryTimer
  participant CP as ControlPlane

  SUB->>CPS: Emit ProgressEvent
  SUB->>PPS: Emit ProgressEvent
  SUB->>CLP: ILogger records
  SUB->>CTT: IMigrationMetrics instruments
  CPS->>CP: POST progress event
  CLP->>CP: POST diagnostics record
  CTT->>CP: POST telemetry snapshot
  PPS-->>PPS: Append progress.ndjson
```

