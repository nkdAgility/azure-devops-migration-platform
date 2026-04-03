# Architecture Overview

> This document defines architectural intent and is the primary human reference.
> In any conflict between this document and `/.agents/guardrails/*.md` guardrails, **the guardrails win**.
> See [.agents/guardrails/system-architecture.md](../.agents/guardrails/system-architecture.md) for the enforced rules.
> See [agents.md](../agents.md) for the agent entry point that binds docs to guardrails.

## 1. System Purpose

Build a migration package platform, not just a migration tool.

The system supports three modes:

1. **Export** — Azure DevOps Services → Files, or TeamFoundationServer (via .NET 4 OM exporter) → Files
2. **Import** — Files → Azure DevOps Services, or Files → TeamFoundationServer (via .NET 4 OM importer — **not yet implemented**, see [docs/tfs-exporter.md](tfs-exporter.md#future-tfs-import-agent))
3. **Both** — Export → Import in a single orchestrated run

The Files layer is first-class. It is:

- Portable
- Auditable
- Zip-friendly
- Resumable
- Stream-importable
- Human-readable

---

## 2. Execution Model

The platform separates **job coordination** (control plane) from **job execution** (migration agent).

### Components and Responsibilities

| Component | Role |
|---|---|
| **CLI** | Operator interface. Drives Aspire to start the control plane and agents for local and server execution, or connects to a remote control plane endpoint. Submits jobs, queries status, and manages job lifecycle. Contains no migration execution logic. |
| **TUI** | Connects to any control plane endpoint — on the same machine, a dedicated server, or in the cloud — and renders live job state. Never submits jobs. |
| **ControlPlane** | Service library (`DevOpsMigrationPlatform.ControlPlane`). Contains the HTTP API controllers, job state machine, lease protocol, progress tracking, and EF Core data model. Has no entry point — it is referenced and hosted by `ControlPlaneHost`. |
| **ControlPlaneHost** | Deployable ASP.NET Core host (`DevOpsMigrationPlatform.ControlPlaneHost`). References the `ControlPlane` service library and adds: process entry point, agent lifecycle management (spawning and monitoring Agents in local and self-hosted topologies; managing container scaling in cloud deployments), and Aspire resource integration. Always reachable over HTTP. |
| **Migration Agent** | (`DevOpsMigrationPlatform.MigrationAgent`) Stateless worker that executes migration jobs. Polls `ControlPlaneHost` for assigned jobs under a time-bounded lease, runs modules via the Job Engine, writes to the package, reports progress back. Lifecycle managed by `ControlPlaneHost`. |
| **TFS Export Agent** | A .NET 4.8 standalone exporter (`CLI.TfsMigration`) spawned by the Agent when the source is TFS. Contains a `TfsExportAgent` class that is the structural parallel of `Agent`: receives a job definition, connects to TFS via the TFS Object Model, writes to the package via `IArtefactStore` (`FileSystemArtefactStore`), maintains checkpoints via `IStateStore`, and reports progress via `IProgressSink` (`StdoutProgressSink` → NDJSON on stdout). Uses the same interfaces as the .NET 10 agent via multi-targeted `Abstractions`. |
| **TFS Import Agent** *(not yet implemented)* | The structural mirror of the TFS Export Agent. A .NET 4.8 importer (`CLI.TfsMigration`) that would be spawned by the Agent when the target is TFS. Contains a `TfsImportAgent` class: receives a job definition, reads from the package via `IArtefactStore` (`FileSystemArtefactStore`), writes to TFS via the TFS Object Model, maintains checkpoints via `IStateStore`, and reports progress via `IProgressSink`. Reuses the same process isolation pattern, NDJSON protocol, and `ExternalToolRunner` as the exporter — no new infrastructure required. See [docs/tfs-exporter.md](tfs-exporter.md#future-tfs-import-agent). |

### Flow

```
Operator
  │  (config file)
  ▼
CLI
  │  Tier 0: structural validation (local, no network)
  │  Tier 1: connectivity + permission checks (network)
  │  → creates MigrationJob (assigns jobId, normalises URI, computes configHash)
  │
  ▼
ControlPlaneHost (Aspire-managed or remote — same HTTP interface)
  │  deduplication check (jobId)
  │  final schema validation
  │  assigns to available agent
  │
  ▼
Agent
  │  Tier 2: pre-flight validation (package structure, before import)
  │  runs job engine + modules
  │  writes to package
  │  [spawns TFS export agent if TFS source]
  │  [spawns TFS import agent if TFS target — not yet implemented]
  │  Tier 3: post-flight validation (counts, links, attachments)
  ▼
Package (file:/// or azureblob://)
```

### MigrationJob is the Internal Contract

The `ControlPlaneHost` receives the `MigrationJob` from the CLI. It is the fully serialisable object that `ControlPlaneHost` passes to an Agent under a lease. The config file is never passed to the Agent directly.

See [.agents/context/job-contract.md](../.agents/context/job-contract.md).

### ControlPlaneHost is Always an HTTP Service

`ControlPlaneHost` is always reachable over HTTP. The CLI always communicates with it via `ControlPlaneClient`. The difference between topologies is only where `ControlPlaneHost` is running:

- **Local / Dedicated Server**: CLI drives Aspire to start `ControlPlaneHost`, listening on `http://localhost:5100`. Any machine with network access to that endpoint can connect a TUI and monitor the migration.
- **Cloud (Self-Hosted / Managed)**: an HTTPS URL to the Azure-hosted `ControlPlaneHost`.

Switching from local to cloud requires only a config change. No code changes.

### All Stores are URI-Based

The package location is expressed as a URI in the `MigrationJob`. The Migration Agent resolves the URI to an `IArtefactStore` implementation:

| URI scheme | Implementation |
|---|---|
| `file:///` | `FileSystemArtefactStore` |
| `azureblob://` | `AzureBlobArtefactStore` |

Module code never references a concrete store implementation.

### Cross-Environment Package Handoff

Because the package is a first-class artefact identified by URI, export and import can run in completely different environments:

| Scenario | Export runs on | Import runs on | Handoff |
|---|---|---|---|
| Local / Server → Cloud | Local CLI-hosted control plane | Cloud (Self-Hosted/Managed) | Operator zips package, uploads to blob, resubmits import config pointing at `azureblob://` URI |
| Cloud → Air-gapped | Cloud | Local CLI-hosted control plane | Operator downloads package or zip, resubmits import config pointing at `file:///` URI |
| Both, same environment | Same control plane for both phases | — | Control plane chains export → import internally |

The package format is identical in all cases. See [docs/packaging-zip.md](packaging-zip.md) for the zip transfer mechanism.

### Progress is Event-Driven

The Migration Agent emits structured `ProgressEvent` records through `IProgressSink`. The TUI subscribes by polling the control plane's progress endpoint. The package log (`Logs/progress.jsonl`) is always written regardless of whether the TUI is open.

The job engine has no knowledge of where progress is rendered.

## 13. What This System Is

> A versioned migration package platform with streaming chronological replay.

Operators can run export and import as separate steps, or as a single end-to-end operation (`Both` mode). Either way, the migration package is always the intermediary — providing a complete, auditable, resumable record of every change. The package is a first-class artefact, not an internal implementation detail.

The platform has a single architecture across all hosting topologies. The same control plane, agent, and job engine run in every environment. The only variable is where the components are hosted.

| Topology | Control Plane host | Agent host | Package store | PostgreSQL |
|---|---|---|---|---|
| **Local** | Aspire-managed process on the operator's machine | Aspire-managed process(es) on the same machine | `file:///` | Aspire portable binary resource |
| **Dedicated Server** | Aspire-managed process on a server | Aspire-managed process(es) on the same server | `file:///` | Aspire portable binary resource |
| **Cloud (Self-Hosted)** | Azure Container App (customer subscription) | Azure Container App(s) | Azure Blob Storage | Azure PostgreSQL Flexible Server |
| **Cloud (Managed)** | Azure Container App (NKD Agility subscription) | Azure Container App(s) | Azure Blob Storage | Azure PostgreSQL Flexible Server |

In the Local and Dedicated Server topologies, the CLI drives Aspire programmatically to start the control plane and agents. PostgreSQL runs as an Aspire portable binary resource — no Docker, no installer required. The TUI can connect to the control plane from any machine with network access to the server.

In Cloud topologies, the CLI connects to a pre-existing HTTPS control plane endpoint. Agents are containers managed by the cloud platform.

All topologies use the same orchestrator engine, the same modules, and the same cursor-based checkpoints. The package contract is identical. See [docs/cli.md](cli.md), [docs/tui.md](tui.md), [docs/control-plane.md](control-plane.md), and [docs/migration-agent.md](migration-agent.md).

Key properties:

- Deterministic
- Resumable
- Portable
- Auditable
- Extensible
- Pluggable
- Scalable
- Memory-safe for large datasets

## 14. Implementation Priority

### Phase 1 — Local-first

1. `MigrationJob` model + schema
2. Control plane API (job submission, lease, status, logs) — embedded in CLI for local execution
3. Migration Agent worker service (poll, execute, heartbeat, report) — spawned as child process by CLI
4. Job Engine (orchestrator + modules contract + cursors)
5. `IArtefactStore` + `FileSystemArtefactStore` (`file:///` URI)
6. `IStateStore` / `PackageCheckpointStateStore` (`Checkpoints/` inside package)
7. `IProgressSink` with `ConsoleProgressSink` + `PackageProgressSink`
8. `ControlPlaneClient` (CLI always uses this to talk to the in-process or remote control plane)
9. WorkItems module (REST)
10. Identity module
11. Legacy TFS export adapter
12. Teams / Permissions / Builds modules
13. TUI commands (`prepare`, `export`, `import`, `both`, `validate`, `pack`, `unpack`, `tui`, `status`, `logs`)
14. ServiceDefaults project (shared observability for control plane + agents)

### Phase 2 — Cloud-ready

15. `AzureBlobArtefactStore` (`azureblob://` URI) with Azurite local emulator support
16. Aspire AppHost for CI/CD integration testing
17. `ControlPlaneProgressSink`
18. `azd` deployment templates for Azure Container Apps

### Phase 3 — Operational hardening

13. Key Vault integration
14. Multi-tenant isolation
15. Rate limiting per job
16. Agent scale-out rules
17. Artefact retention policies

---

## Full Reference Set

| Section | Document |
|---|---|
| 2. Package structure & manifest | [.agents/context/package-format.md](../.agents/context/package-format.md) |
| 3. WorkItems on-disk layout | [.agents/context/workitems-format.md](../.agents/context/workitems-format.md) |
| 4. Streaming import model | [.agents/context/import-streaming.md](../.agents/context/import-streaming.md) |
| 5. Cursor-based checkpointing | [.agents/context/checkpointing.md](../.agents/context/checkpointing.md) |
| 6. Module architecture | [docs/modules.md](modules.md) |
| 7. Identity & mapping | [.agents/context/identity-and-mapping.md](../.agents/context/identity-and-mapping.md) |
| 8. Source types | [docs/source-types.md](source-types.md) |
| 9. Configuration model | [docs/configuration.md](configuration.md) |
| 10. Orchestration | [docs/orchestration.md](orchestration.md) |
| 11. Zip packaging | [docs/packaging-zip.md](packaging-zip.md) |
| 12. Validation (pre-flight & post-flight) | [docs/validation.md](validation.md) |
| 13. Artefact store abstraction | [.agents/context/artefact-store.md](../.agents/context/artefact-store.md) |
| 14. Job contract | [.agents/context/job-contract.md](../.agents/context/job-contract.md) |
| 15. Control plane | [docs/control-plane.md](control-plane.md) |
| 16. Migration Agent (worker) | [docs/migration-agent.md](migration-agent.md) |
| 17. CLI | [docs/cli.md](cli.md) |
| 18. TUI (Terminal UI) | [docs/tui.md](tui.md) |

## Agent Guardrails

| Topic | Document |
|---|---|
| Hard architectural constraints (authoritative) | [.agents/guardrails/system-architecture.md](../.agents/guardrails/system-architecture.md) |
| WorkItems-specific rules | [.agents/guardrails/workitems-rules.md](../.agents/guardrails/workitems-rules.md) |
| Migration behaviour invariants | [.agents/guardrails/migration-rules.md](../.agents/guardrails/migration-rules.md) |
| Coding standards | [.agents/guardrails/coding-standards.md](../.agents/guardrails/coding-standards.md) |
| New module checklist | [.agents/guardrails/module-template.md](../.agents/guardrails/module-template.md) |
