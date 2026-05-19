# Lease Coordination Contract

Canonical contract for lease polling, job dispatch, and terminal signaling.

## Contract Surface

- `AgentWorkerBase`
- `JobAgentWorker`
- `ModulePipelineWorkerBase`
- `AgentControlPlaneClientAdapter`
- `ActiveLeaseState`
- `ActivePackageState`

## Required Semantics

1. Worker polls control plane lease endpoint and dispatches leased jobs.
2. Lease state is set before dispatch and cleared after completion/failure.
3. Terminal lease status must be reported explicitly (`complete` or `fail`).

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

