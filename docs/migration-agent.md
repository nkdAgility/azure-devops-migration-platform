# Agent

## Purpose

The **Migration Agent** (`DevOpsMigrationPlatform.MigrationAgent`) is a stateless worker that executes migration jobs assigned by `ControlPlaneHost`. The Migration Agent runs the Job Engine — the same execution logic used across all deployment topologies — receiving a job definition under a time-bounded lease and reporting progress back via the lease API.

Migration Agent lifecycle is managed by `ControlPlaneHost` via `IAgentLauncher`. The same agent binary and container image are used across all topologies. Migration Agents are stateless by design — any agent instance can pick up any job and resume from the last cursor position.

The package contract, modules, and cursors are unchanged across all deployment topologies.

---

## Responsibilities

| Responsibility | Description |
|---|---|
| Poll for work | Call the control plane lease endpoint to receive a job. |
| Acquire lease | Hold a time-bounded lease on the assigned job. |
| Mount artefact store | Connect to the package URI from the job definition (filesystem or blob). See [.agents/context/artefact-store.md](../.agents/context/artefact-store.md). |
| Read credentials | Extract credentials from the job definition as provided by the operator's config. |
| Run orchestrator | Execute `ExportAsync`, `ImportAsync`, or both in sequence, exactly as in local mode. |
| Write cursors | Write checkpoint cursors into the package's `.migration/Checkpoints/` folder after each stage, as always. |
| Heartbeat | Signal liveness to the control plane at regular intervals. |
| Report progress | Emit `ProgressEvent` via `IProgressSink` after each stage. Three sinks run simultaneously: `ConsoleProgressSink` (terminal), `PackageProgressSink` (`.migration/Logs/progress.jsonl`), and `ControlPlaneProgressSink` (POST to control plane ring buffer for live TUI streaming). |
| Record metrics | Record OTel metrics via `IMigrationMetrics` during job execution (execution counters, payload histograms, duration). Metric aggregates are pushed to the control plane via `ControlPlaneTelemetryTimer`. |
| Write package logs | Write structured logs to `.migration/Logs/` in the package via `IArtefactStore`. |
| Signal completion or failure | Call the control plane's complete or fail endpoint when the job finishes. |

The Agent does **not** accept job submissions, manage other Agents, or store job state. All job coordination is `ControlPlaneHost`'s responsibility.

---

## Execution Flow

```
Poll /agents/lease
  └─ Receive leased job definition
       ├─ Extract credentials from job definition
       ├─ Connect to artefact store (packageUri)
       ├─ Load cursor → determine resume position
       ├─ Start heartbeat loop (background)
       ├─ Register IProgressSink composite:
       │    ├─ ConsoleProgressSink     (NDJSON to terminal)
       │    ├─ PackageProgressSink     (.migration/Logs/progress.jsonl in package)
       │    └─ ControlPlaneProgressSink (POST /agents/lease/{id}/progress)
       └─ Run Job Engine
            ├─ ExportAsync (if mode = Export or Both)
            │    └─ After each cursor write → Emit(ProgressEvent) via all sinks
            ├─ Validate package (if mode = Both)
            └─ ImportAsync (if mode = Import or Both)
                 └─ After each cursor write → Emit(ProgressEvent) via all sinks
  ├─ Success → POST /agents/lease/{id}/complete
  └─ Failure → POST /agents/lease/{id}/fail  (cursor preserved for resume)
```

---

## Deployment and Zone Isolation

A single agent binary and container image supports all three modes (`Export`, `Import`, `Both`) by reading `mode` from the job definition.

For network zone isolation — where source and target systems are in different network zones — `ControlPlaneHost` can deploy the same agent image to different target contexts via `ContainerAgentLauncher` configuration. One deployment runs in the source network zone (mode `Export`); another runs in the target network zone (mode `Import`). Both use the same package URI in the shared artefact store. The `manifest.json` and cursor files written by the export-mode agent are read by the import-mode agent without modification.

---

## Stateless Design

Agents are stateless. All durable state lives either:

- In the migration package (`revision.json`, cursors, `idmap.db`, `.migration/Logs/`) via `IArtefactStore` and `IStateStore`.
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

### Exclusive Write Access (Data Residency)

The Migration Agent (and TFS Export Agent for TFS sources) is the **only** component with write access to the working directory and package files. No other component — CLI, TUI, Control Plane, or ControlPlaneHost — may create, modify, or delete files in the package. This is a **data residency** requirement: customer data (work item content, attachments, identities) must remain under the exclusive control of the Agent, which runs in the operator's chosen infrastructure.

The CLI may perform **read-only** access to package files (e.g. reading `dependencies.csv` or `inventory.json`) for post-job display purposes. This does not violate data residency because it does not move or copy customer data outside the operator's infrastructure.

See [docs/architecture.md — Data Residency](architecture.md#data-residency--agent-only-write-access) for the full access matrix and rationale.

---

## Logging

Migration Agents write structured logs to both:

- `.migration/Logs/` in the package (durable, included in zip).
- The control plane (pushed in real time via the lease API for TUI tailing).

Both outputs use the same structured format (OpenTelemetry-compatible). No `Console.WriteLine` in module code.
