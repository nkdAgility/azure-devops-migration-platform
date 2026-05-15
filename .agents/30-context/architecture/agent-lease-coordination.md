# agent_lease_coordination — Lease and Worker Coordination System

- Tag: `agent_lease_coordination`
- Responsibility: Poll control plane, acquire lease, dispatch jobs, and signal terminal states.

## Core Classes

- `AgentWorkerBase`
- `JobAgentWorker`
- `ModulePipelineWorkerBase`
- `AgentControlPlaneClientAdapter`
- `ActiveLeaseState`
- `ActivePackageState`

## Validating Tests

- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerDispatchTests.cs`
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Context/JobAgentWorkerInventoryTests.cs`
- `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`

## Sequence Diagram

```mermaid
sequenceDiagram
  participant AW as AgentWorkerBase
  participant CP as ControlPlane
  participant LS as ActiveLeaseState
  participant JW as JobAgentWorker

  AW->>CP: GET /agents/lease?capabilities=...
  CP-->>AW: leaseId + Job
  AW->>LS: Set current lease
  AW->>JW: OnJobAsync(job, leaseId)
  alt Success
    AW->>CP: POST /agents/lease/{leaseId}/complete
  else Failure
    AW->>CP: POST /agents/lease/{leaseId}/fail
  end
  AW->>LS: Clear current lease
```




