# Task Plan Contract

Canonical contract for building and persisting `JobTaskList` execution plans.

## Contract Surface

- `JobExecutionPlanBuilder`
- `IJobExecutionPlanBuilder`
- `JobTaskList`
- `JobTask`

## Required Semantics

1. Build ordered plans from job kind, enabled modules, and dependency graph.
2. Persist and reuse compatible `plan.json` for resume.
3. Expose both flat ordered task rows and phase summaries for canonical stage rendering.

## Sequence Diagram

```mermaid
sequenceDiagram
  participant JW as JobAgentWorker
  participant TB as JobExecutionPlanBuilder
  participant ST as IStateStore
  participant CP as ControlPlane

  JW->>TB: BuildAndSaveAsync(packageConfig, kind)
  TB->>ST: Read plan.json (LoadOrReset)
  alt Existing compatible plan
    TB-->>JW: Return persisted JobTaskList
  else Build fresh plan
    TB->>TB: BuildPlanAsync()
    TB->>ST: Write plan.json
    TB-->>JW: Return fresh JobTaskList
  end
  JW->>CP: EnqueueTasks via UnifiedWorkerEventWriter → POST /workers/{workerId}/events (Tasks kind)
```

