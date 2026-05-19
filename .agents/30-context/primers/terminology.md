# Canonical Terminology

Use these terms consistently throughout code, docs, and tests.

---

## Pipeline Phases

| Term | Definition |
|---|---|
| **Inventory** | Counts and catalogues source items without writing to the package |
| **Export** | Reads source items and writes them to the package |
| **Prepare** | Validates and provisions target readiness before import begins |
| **Import** | Reads the package and pushes items to the target |
| **Validate** | Compares source and target to verify completeness |
| **Migrate** | Convenience mode that chains all five phases in order |

---

## Core Concepts

| Term | Definition |
|---|---|
| **Package** | The intermediate filesystem directory; the source of truth between Export and Import |
| **Artefact** | A file or binary object stored in the package |
| **Module** | A self-contained unit of migration logic for a specific data type (e.g. WorkItems, Teams, Nodes) |
| **Analyser** | A cross-cutting analysis component that runs after inventory modules complete, reads package data, and writes analysis artefacts without connecting to source or target |
| **Tool** | A stateless transformation or lookup service declared at the config root under `Tools.*`; applies pure data transforms during export and import with no I/O |
| **Connector** | An adapter to an external system; every feature requires Simulated, AzureDevOps, and TFS variants |
| **Source** | The system being migrated from |
| **Target** | The system being migrated to |

---

## Jobs & Scheduling

| Term | Definition |
|---|---|
| **Job** | A unit of work submitted to the Control Plane with a lifecycle of Queued → Running → Complete/Failed |
| **Job Task** | A planned step within a job, one module+phase combination |
| **Control Plane** | The coordination service that manages jobs, leases, telemetry, and progress |
| **Agent** | The worker that executes migration phases |
| **TFS Export Agent** | The net481 subprocess worker for TFS Object Model sources |
| **Lease** | A time-limited exclusive claim on a job by a single agent |
| **Heartbeat** | A periodic signal from the agent to renew its lease |
| **Entitlement** | The licence snapshot enforced at job admission and renewal |

---

## Checkpointing & Resumability

| Term | Definition |
|---|---|
| **Checkpoint** | A cursor-based durable progress marker for a module+phase |
| **Cursor** | A string identifying the last successfully processed item |
| **IdMap** | The database that records source-to-target work item and attachment ID mappings |
| **Provenance Marker** | A value written to the target after creation so the mapping is discoverable in future runs |
| **Revision-Level Watermark** | The per-work-item last-applied revision index; skips already-applied revisions on re-run |

---

## Work Item Patterns

| Term | Definition |
|---|---|
| **Work Item Iteration Strategy** | Splits a large query into successive date-range windows to stay under the WIQL 20,000-item hard limit |
| **Work Item Revision Source** | Streams work item revisions one at a time from a source system; must be lazy and never buffer |
| **Work Item Fetch Scope** | Encapsulates field projection, filters, an optional query clause, and resume state for a fetch operation |
| **Work Item Resolution Strategy** | Discovers existing source-to-target mappings from the target at import startup; also writes provenance markers after creation |
| **Revision** | A single historical snapshot of a work item; the atomic unit of export and import |
| **Query Window** | A single time-bounded slice of work item IDs produced by the iteration strategy |

---

## Telemetry

| Term | Definition |
|---|---|
| **Three-Channel Model** | The mandatory telemetry architecture: SSE events (state changes), polled metrics (aggregate counters), slow-polled snapshots (per-project breakdowns) |
| **Progress Event** | A structured event emitted by a module or the job engine; carries stage, message, sequence number, and optional aggregate counters |
| **Progress Sink** | The interface modules use to emit progress events; always injected as optional |
| **Job Metrics** | Aggregate counters for a running job, polled at ~5 s by the CLI and TUI |
| **Migration Counters** | The per-module aggregate counts within Job Metrics: work items, teams, nodes, identities |
| **Job Snapshot** | Per-project breakdown of counters, polled slowly (~5 min); the only place for high-cardinality per-project data |
| **Job Bootstrap** | The initial payload when a client connects to a job; provides metrics, snapshot, and last event sequence to render initial state |
| **Event Sequence** | A monotonic per-job integer on every progress event; used for SSE reconnect and deduplication |
| **Stage** | A string label on a progress event naming the current processing step; must match cursor stage values |
| **Platform Metrics** | OTel-recorded throughput, latency, and error metrics; flow to Azure Monitor only — do not feed CLI/TUI |

---

## Terms to Avoid

| Avoid | Use instead |
|---|---|
| "Migration" (generic noun) | "Migrate" (the mode) or the specific phase name |
| Direct migration | Source → Files → Target — direct Source → Target is not permitted |
| Progress database | Checkpoints folder / cursor file |
| Watermark table | Checkpoint / cursor |



