# Control Plane

## Purpose

The control plane is an **ASP.NET Core Web API** that coordinates migration jobs without executing them. Execution always happens inside a Migration Agent. The control plane's role is to accept, validate, track, and assign work — not to perform it.

---

## Responsibilities

| Responsibility | Description |
|---|---|
| Job submission | Accept job definitions from the TUI or API clients. Validate config schema before accepting. |
| Job storage | Persist job definitions and status. |
| Lease management | Assign jobs to available Migration Agents via time-bounded leases. Reassign if a Migration Agent stops heartbeating. |
| Progress tracking | Record per-module, per-cursor, per-stage progress as reported by Migration Agents. |
| Status and logs API | Expose job status, progress, and logs to the TUI and other clients. |
| Pause / resume / cancel | Allow operators to signal state changes to Migration Agents via the job record. |
| Artefact URLs | Provide Migration Agents with the package URI (`packageUri`) for the job. |
| Secrets references | Store references to Key Vault secrets; never unwrap or proxy secrets. |

The control plane does **not** run the orchestrator, call source or target APIs, or read or write the migration package directly.

---

## API Surface

### Job Lifecycle

| Method | Path | Description |
|---|---|---|
| `POST` | `/jobs` | Submit a new job. Body is a job definition (see [docs/job-contract.md](job-contract.md)). Returns `jobId`. |
| `GET` | `/jobs/{jobId}` | Get job status and metadata. |
| `GET` | `/jobs/{jobId}/progress` | Get per-module, per-stage progress as last reported by the Migration Agent. |
| `POST` | `/jobs/{jobId}/cancel` | Cancel a running or queued job. Migration Agent will receive the signal on next heartbeat. |
| `POST` | `/jobs/{jobId}/pause` | Pause a running job. Migration Agent will checkpoint and release its lease. |
| `POST` | `/jobs/{jobId}/resume` | Resume a paused job (makes it eligible for lease pickup). |
| `GET` | `/jobs/{jobId}/logs` | Tail or fetch logs uploaded by the Migration Agent. |

### Migration Agent Protocol

| Method | Path | Description |
|---|---|---|
| `GET` | `/agents/lease` | Migration Agent polls for available work. Returns a leased job if one is available. |
| `POST` | `/agents/lease/{leaseId}/heartbeat` | Migration Agent signals it is alive. Lease expiry is extended on each heartbeat. |
| `POST` | `/agents/lease/{leaseId}/progress` | Migration Agent reports cursor position and stage for a module. |
| `POST` | `/agents/lease/{leaseId}/complete` | Migration Agent signals successful job completion. |
| `POST` | `/agents/lease/{leaseId}/fail` | Migration Agent signals non-recoverable failure with error detail. |
| `POST` | `/agents/lease/{leaseId}/release` | Migration Agent releases lease without completing (e.g. on pause). |

---

## Job States

```
Queued → Leased → Running → Completed
                          → Failed
                ↓
              Paused → Queued (resume)
                     → Cancelled
         ↑
       Cancelled (from Queued)
```

| State | Description |
|---|---|
| `Queued` | Waiting for an agent to pick up. |
| `Leased` | Assigned to an agent but not yet executing. |
| `Running` | Agent is actively executing. |
| `Paused` | Agent has checkpointed and released the lease. Job is resumable. |
| `Completed` | All modules completed successfully. |
| `Failed` | A non-recoverable error occurred. Cursor state is preserved for investigation. |
| `Cancelled` | Operator cancelled the job. |

---

## Lease Protocol

1. Migration Agent calls `GET /agents/lease` (long-poll or short-poll).
2. Control plane returns a lease containing the job definition and a `leaseId`.
3. Migration Agent sends `POST /agents/lease/{leaseId}/heartbeat` on a configurable interval (default: every 30 seconds).
4. If the control plane does not receive a heartbeat within `leaseExpiry` (default: 2× heartbeat interval), the job is returned to `Queued` and another Migration Agent may pick it up.
5. The cursor in the package ensures the new Migration Agent resumes from where the previous one stopped.

---

## Progress Reporting

Migration Agents push module progress after each cursor write:

```json
{
  "module": "WorkItems",
  "lastProcessed": "WorkItems/2026-02-25/638760123456789012-12345-17",
  "stage": "AppliedFields",
  "updatedAt": "2026-02-25T18:12:34Z"
}
```

This mirrors the cursor schema ([docs/checkpointing.md](checkpointing.md)). The control plane stores the latest value per module for status display. The cursor in the package remains the authoritative resume state.

---

## Isolation Rule

The control plane must not:

- Call source or target Azure DevOps APIs.
- Read or write the migration package.
- Execute orchestrator logic.
- Unwrap or cache secrets from Key Vault.

Violating any of these rules breaks the Migration Agent / control-plane separation and couples execution to coordination.

Violating any of these rules breaks the agent/control-plane separation and couples execution to coordination.

---

## Data Store

The control plane persists:

- Job definitions (serialised job contract)
- Job states and state transitions
- Latest progress per module (for display; not authoritative for resume)
- Lease records
- Log references (URIs into blob storage; logs themselves are stored in the package's `Logs/` folder by the Migration Agent)

---

## Multi-Tenant Considerations (Phase 3)

- Each tenant's jobs are isolated by a `tenantId` claim on the JWT.
- Migration Agents may be scoped to a tenant or shared across tenants with RBAC controls.
- Rate limits are applied per tenant to prevent one tenant starving others.
- Artefact retention policies are configurable per tenant.

See [docs/architecture.md](architecture.md) for the overall system context.
