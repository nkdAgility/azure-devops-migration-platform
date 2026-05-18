# Reconciliation Plan — 023.5 (2026-05-17)

## Current status

- Spec intent is largely implemented in code, but the spec text still contains historical assumptions and stale references.
- Reconciliation scope is documentation/spec truth alignment (Class A).

## Remaining incomplete work

1. Multi-agent standalone spawning in `AgentLifecycleService` (or explicit decision to keep manual TFS agent launch).
2. Architecture docs alignment with current lifecycle behavior.
3. Replace stale doc references with existing canonical docs.

## Completed because superseded

1. `source_type` DB-column lease routing design superseded by connector capability routing via `Job.Connectors`.
2. Dedicated TFS-specific control-plane client/progress sink superseded by shared agent infrastructure.
3. Discovery routing extension via extra job shape superseded by unified job contract flow.

## Contradictions and reconciliation

- The spec states both agents are auto-spawned; implementation currently auto-spawns only `MigrationAgent`.
- The spec caveats for net481 hosting/HTTP factory are obsolete versus actual implementation.
- The spec references a missing `docs/tfs-exporter.md` file.

## Verification evidence

- `src/DevOpsMigrationPlatform.TfsMigrationAgent/Program.cs`
- `src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs`
- `src/DevOpsMigrationPlatform.ControlPlaneHost/AgentLifecycle/AgentLifecycleService.cs`
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/AgentWorkerBase.cs`
- `src/DevOpsMigrationPlatform.ControlPlane/Controllers/AgentLeaseController.cs`
- `src/DevOpsMigrationPlatform.ControlPlane/Jobs/JobStore.cs`
- `src/DevOpsMigrationPlatform.CLI.Migration/Commands/QueueCommand.cs`
- `build.ps1`
- `.vscode/tasks.json`
