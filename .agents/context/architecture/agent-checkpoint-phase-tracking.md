# agent_checkpoint_phase_tracking — Checkpoint and Phase Tracking System

- Tag: `agent_checkpoint_phase_tracking`
- Responsibility: Persist cursor and phase records (`job.phase.json`, module cursors) to enable deterministic resume and force-fresh semantics.

## Scope

This file is the subsystem map for checkpoint and phase-tracking components.
It is intentionally concise and architecture-focused.

Canonical behavior, schema, and resume semantics are defined in:

- [.agents/context/checkpointing-summary.md](../checkpointing-summary.md)
- [docs/adr/0003-cursor-based-checkpointing.md](../../../docs/adr/0003-cursor-based-checkpointing.md)
- [.agents/guardrails/migration-rules.md](../../guardrails/migration-rules.md)

Use this file to find the implementation surface.
Use the linked files above for normative cursor and phase-tracking behavior.

## Core Classes

- `CheckpointingService`
- `CheckpointingServiceFactory`
- `ICheckpointingService`
- `PhaseTrackingService`
- `PhaseTrackingServiceFactory`
- `IPhaseTrackingService`

## Validating Tests

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Checkpointing/CheckpointingServiceTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerDispatchTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant CK as CheckpointingService
  participant PH as PhaseTrackingService
  participant SS as IStateStore

  JW->>PH: ReadPhaseRecordAsync()
  PH->>SS: ReadAsync(.migration/job.phase.json)
  PH-->>JW: JobPhaseRecord
  loop During execution
    JW->>CK: WriteCursorAsync(action, module)
    CK->>SS: WriteAsync(/{org}/{project}/.migration/*.cursor.json)
  end
  JW->>PH: WritePhaseRecordAsync(updated)
  PH->>SS: WriteAsync(.migration/job.phase.json)
```
