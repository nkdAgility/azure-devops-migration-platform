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
3. Terminal signals (`Terminal` kind with `failed` flag) are sent through `UnifiedWorkerEventWriter` as part of the unified event batch channel — not as separate `/complete` or `/fail` HTTP calls.
4. A 15-second heartbeat runs in parallel with job execution via `POST /agents/lease/{leaseId}/heartbeat`. The CP uses this to distinguish "agent alive but quiet" from "agent dead".

## Sequence Diagram

```mermaid
sequenceDiagram
  participant AW as AgentWorkerBase
  participant UEW as UnifiedWorkerEventWriter
  participant CP as ControlPlane
  participant LS as ActiveLeaseState
  participant JW as JobAgentWorker

  AW->>CP: GET /agents/lease?capabilities=...
  CP-->>AW: leaseId + Job
  AW->>LS: Set current lease
  AW->>UEW: Start (BackgroundService)

  par Heartbeat loop (15 s)
    loop Every 15 s
      AW->>CP: POST /agents/lease/{leaseId}/heartbeat
      CP-->>AW: 204 No Content
    end
  and Job execution
    AW->>JW: OnJobAsync(job, leaseId)
    JW->>UEW: EnqueueTasks / Emit(ProgressEvent) / EnqueueDiagnostic
    Note over UEW: Batch ≤50 events or 500 ms
    UEW->>CP: POST /workers/{workerId}/events (WorkerEventBatch)
    CP-->>UEW: WorkerEventAck {lastAcceptedSeq}
  end

  alt Success
    AW->>UEW: EnqueueTerminal(failed: false)
    UEW->>CP: POST /workers/{workerId}/events [{kind: Terminal, failed: false}]
  else Failure
    AW->>UEW: EnqueueTerminal(failed: true)
    UEW->>CP: POST /workers/{workerId}/events [{kind: Terminal, failed: true}]
  end
  AW->>LS: Clear current lease
```

