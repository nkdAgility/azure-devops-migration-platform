# Agent

## Purpose

The **Migration Agent** (`DevOpsMigrationPlatform.MigrationAgent`) is a stateless worker that executes migration jobs assigned by `ControlPlaneHost`. The Migration Agent runs the Job Engine — the same execution logic used across all deployment topologies — receiving a job definition under a time-bounded lease and reporting progress back via the lease API.

Migration Agent lifecycle is managed by `ControlPlaneHost`: spawned as processes in local and self-hosted topologies, and as container instances in cloud deployments. Migration Agents are stateless by design — any Migration Agent can pick up any job and resume from the last cursor position.

The package contract, modules, and cursors are unchanged across all deployment topologies.

---

## Responsibilities

| Responsibility | Description |
|---|---|
| Poll for work | Call the control plane lease endpoint to receive a job. |
| Acquire lease | Hold a time-bounded lease on the assigned job. |
| Mount artefact store | Connect to the package URI from the job definition (filesystem or blob). See [.agents/context/artefact-store.md](../.agents/context/artefact-store.md). |
| Resolve secrets | Fetch credentials from Key Vault references in the job definition before executing. |
| Run orchestrator | Execute `ExportAsync`, `ImportAsync`, or both in sequence, exactly as in local mode. |
| Write cursors | Write checkpoint cursors into the package's `Checkpoints/` folder after each stage, as always. |
| Heartbeat | Signal liveness to the control plane at regular intervals. |
| Report progress | Push cursor positions to the control plane for status display after each cursor write. |
| Upload logs | Write logs to `Logs/` in the package. Optionally push log lines to the control plane in real time. |
| Signal completion or failure | Call the control plane's complete or fail endpoint when the job finishes. |

The Agent does **not** accept job submissions, manage other Agents, or store job state. All job coordination is `ControlPlaneHost`'s responsibility.

---

## Execution Flow

```
Poll /agents/lease
  └─ Receive leased job definition
       ├─ Resolve Key Vault secrets
       ├─ Connect to artefact store (packageUri)
       ├─ Load cursor → determine resume position
       ├─ Start heartbeat loop (background)
       └─ Run Job Engine
            ├─ ExportAsync (if mode = Export or Both)
            │    └─ After each cursor write → POST /agents/lease/{id}/progress
            ├─ Validate package (if mode = Both)
            └─ ImportAsync (if mode = Import or Both)
                 └─ After each cursor write → POST /agents/lease/{id}/progress
  ├─ Success → POST /agents/lease/{id}/complete
  └─ Failure → POST /agents/lease/{id}/fail  (cursor preserved for resume)
```

---

## Migration Agent Roles

A single Agent binary supports all three modes (`Export`, `Import`, `Both`) by reading `mode` from the job definition. There is no requirement to deploy separate export and import binaries.

However, two Agent deployments are supported for network isolation:

| Role | Mode supported | Typical deployment |
|---|---|---|
| `ExportAgent` | `Export` | Source network zone |
| `ImportAgent` | `Import` | Target network zone |
| `Agent` | `Export`, `Import`, `Both` | Any zone with access to both |

In the two-job pattern, the Export Migration Agent writes the package to the shared artefact store; the Import Migration Agent reads it. Both use the same package URI. The `manifest.json` and cursor files written by the Export Migration Agent are read by the Import Migration Agent without modification.

---

## Stateless Design

Agents are stateless. All durable state lives either:

- In the migration package (`revision.json`, cursors, `idmap.db`, `Logs/`) via `IArtefactStore` and `IStateStore`.
- In the control plane (job status, latest reported progress).

An Agent may be stopped, rescheduled, or replaced at any point. The new Agent reads the cursor to determine where to resume. This makes Agents safe to run in auto-scaling container environments.

---

## Heartbeat and Lease Expiry

- Agents send a heartbeat to `POST /agents/lease/{leaseId}/heartbeat` every N seconds (configurable; default 30 s).
- The `ControlPlaneHost` lease TTL is set to 2× the expected heartbeat interval.
- If `ControlPlaneHost` does not receive a heartbeat within the TTL, it returns the job to `Queued`.
- The next Agent to acquire the lease resumes from the last cursor position in the package.

This means a crashed Agent loses no more than one stage of work.

---

## Pause and Cancel

- **Pause:** The control plane signals `Paused` on the job record. The Migration Agent reads this signal on the next heartbeat response. It finishes the current stage, writes the cursor, releases the lease, and exits cleanly.
- **Cancel:** The control plane signals `Cancelled`. The Migration Agent finishes the current stage, writes the cursor, and exits. The job is not resumable after cancellation (though the package remains on disk for inspection).

---

## Artefact Store Access

Migration Agents access the migration package exclusively through `IArtefactStore`. They never use raw filesystem calls or raw blob SDK calls inside module code. See [.agents/context/artefact-store.md](../.agents/context/artefact-store.md) for the abstraction and implementations.

---

## Logging

Migration Agents write structured logs to both:

- `Logs/` in the package (durable, included in zip).
- The control plane (pushed in real time via the lease API for TUI tailing).

Both outputs use the same structured format (OpenTelemetry-compatible). No `Console.WriteLine` in module code.
