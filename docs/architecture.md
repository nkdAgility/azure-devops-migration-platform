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
| **CLI** | Operator interface. Submits config to the control plane, queries status, cancels jobs. Does no execution. |
| **TUI** | Terminal UI for monitoring a running migration and light interaction. Never submits jobs. |
| **Control Plane** | Always a separate service. Accepts config from the CLI, creates a `MigrationJob`, assigns it to an available Migration Agent. Runs locally (localhost via Aspire AppHost) or in the cloud (Azure Container Apps). |
| **Migration Agent** | Executes the job engine. Polls the control plane for assigned jobs, runs modules, writes to the package, reports progress back. |
| **TFS Export Agent** | A .NET 4.8 standalone exporter (`CLI.TfsMigration`) spawned by the Migration Agent when the source is TFS. Contains a `TfsExportAgent` class that is the structural parallel of `MigrationAgent`: receives a job definition, connects to TFS via the TFS Object Model, writes to the package via `IArtefactStore` (`FileSystemArtefactStore`), maintains checkpoints via `IStateStore`, and reports progress via `IProgressSink` (`StdoutProgressSink` → NDJSON on stdout). Uses the same interfaces as the .NET 10 agent via multi-targeted `Abstractions`. |
| **TFS Import Agent** *(not yet implemented)* | The structural mirror of the TFS Export Agent. A .NET 4.8 importer (`CLI.TfsMigration`) that would be spawned by the Migration Agent when the target is TFS. Contains a `TfsImportAgent` class: receives a job definition, reads from the package via `IArtefactStore` (`FileSystemArtefactStore`), writes to TFS via the TFS Object Model, maintains checkpoints via `IStateStore`, and reports progress via `IProgressSink`. Reuses the same process isolation pattern, NDJSON protocol, and `ExternalToolRunner` as the exporter — no new infrastructure required. See [docs/tfs-exporter.md](tfs-exporter.md#future-tfs-import-agent). |

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
Control Plane (local or cloud)
  │  deduplication check (jobId)
  │  final schema validation
  │  assigns to available agent
  │
  ▼
Migration Agent
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

The control plane creates a `MigrationJob` from the operator's config. It is the fully serialisable object that the control plane passes to a Migration Agent. The config file is never passed to the agent directly.

See [docs/job-contract.md](job-contract.md).

### The Control Plane is Always a Service

The control plane is always a separate process reachable over HTTP. The CLI talks to it via a configured endpoint:

- **Standalone**: `http://localhost:5100` — the Aspire AppHost starts the control plane as a local process on the same machine
- **Self-Hosted / Managed**: an HTTPS URL to the Azure-hosted control plane

Switching from local to cloud requires only a config change in the CLI. No code changes.

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
| Standalone → Cloud | Local (Standalone control plane) | Cloud (Self-Hosted/Managed) | Operator zips package, uploads to blob, resubmits import config pointing at `azureblob://` URI |
| Cloud → Air-gapped | Cloud | Local (Standalone) | Operator downloads package or zip, resubmits import config pointing at `file:///` URI |
| Both, same environment | Same control plane for both phases | — | Control plane chains export → import internally |

The package format is identical in all cases. See [docs/packaging-zip.md](packaging-zip.md) for the zip transfer mechanism.

### Progress is Event-Driven

The Migration Agent emits structured `ProgressEvent` records through `IProgressSink`. The TUI subscribes by polling the control plane's progress endpoint. The package log (`Logs/progress.jsonl`) is always written regardless of whether the TUI is open.

The job engine has no knowledge of where progress is rendered.

## 13. What This System Is

> A versioned migration package platform with streaming chronological replay.

Operators can run export and import as separate steps, or as a single end-to-end operation (`Both` mode). Either way, the migration package is always the intermediary — providing a complete, auditable, resumable record of every change. The package is a first-class artefact, not an internal implementation detail.

The platform supports three operational modes:

All three modes run the same stack (control plane + PostgreSQL + migration agent). The difference is topology — where that stack runs.

| Mode | Where the stack runs | Package Store |
|---|---|---|
| **Standalone** | Single local machine (Aspire manages all services on-device) | `file:///` |
| **Self-Hosted** | Customer's own Azure subscription (`azd up`, customer-operated) | Azure Blob Storage |
| **Managed** | NKD Agility's Azure subscription (`azd up`, NKD-operated) | Azure Blob Storage |

**Standalone mode** runs the full stack — control plane, migration agent, and PostgreSQL — on a single local machine with zero external dependencies. PostgreSQL ships as a portable bundled binary started by the Aspire AppHost; no Docker installation or external PostgreSQL is required. Package storage uses the local filesystem (`file:///`). Every service binds to localhost. This is the closest mode to the original tool: run one command, migration executes locally.

**Self-Hosted mode** is architecturally identical to Managed mode. The customer runs `azd up` in their own Azure subscription, provisioning the same stack — Container Apps, PostgreSQL Flexible Server, and Azure Blob Storage. The control plane is reachable by multiple operators, and multiple migration agents can run concurrently. The only difference from Managed mode is who provisions and operates the Azure infrastructure.

**Managed mode** is the hosted service offering. NKD Agility runs `azd up` in its own Azure subscription and operates the resulting infrastructure on the customer's behalf. The stack is identical to Self-Hosted; the only difference is who provisions and operates it. Organisations use this without managing any infrastructure themselves.

All three modes use the same orchestrator engine, the same modules, and the same cursor-based checkpoints. The package contract is identical. See [docs/cli.md](cli.md), [docs/tui.md](tui.md), [docs/control-plane.md](control-plane.md), and [docs/migration-agent.md](migration-agent.md).

> **Development and CI** is not a fourth operational mode — it is an AppHost profile used by engineers building the platform and by every CI/CD pipeline stage. It has two subprofiles (`dev-portable` and `dev-docker`) that validate the Standalone architecture and the Self-Hosted/Managed architecture respectively. Both must pass in every pipeline run. See [docs/aspire-integration.md](aspire-integration.md#development--ci-apphost).

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
2. Aspire AppHost + ServiceDefaults projects
3. Job Engine (orchestrator + modules contract + cursors)
4. `IArtefactStore` + `FileSystemArtefactStore` (`file:///` URI)
5. `IStateStore` / `PackageCheckpointStateStore` (`Checkpoints/` inside package)
6. `IProgressSink` with `ConsoleProgressSink` + `PackageProgressSink`
7. WorkItems module (REST)
8. Identity module
9. Legacy TFS export adapter
10. Teams / Permissions / Builds modules
11. TUI local commands (`prepare`, `export`, `import`, `both`, `validate`, `pack`, `unpack`)
12. `ControlPlaneClient` stub (remote commands parse, return "not implemented")

### Phase 2 — Cloud-ready

13. `AzureBlobArtefactStore` (`azureblob://` URI) with Azurite local emulator support
14. Control plane API (job submission, lease, status, logs)
15. Migration Agent worker service (poll, execute, heartbeat, report)
16. Aspire orchestration for local multi-service testing
17. `ControlPlaneProgressSink`
18. TUI remote commands (`queue`, `status`, `logs`, `pause`, `resume`, `cancel`)
19. `azd` deployment templates for Azure Container Apps

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
| 2. Package structure & manifest | [docs/package-format.md](package-format.md) |
| 3. WorkItems on-disk layout | [docs/workitems-format.md](workitems-format.md) |
| 4. Streaming import model | [docs/import-streaming.md](import-streaming.md) |
| 5. Cursor-based checkpointing | [docs/checkpointing.md](checkpointing.md) |
| 6. Module architecture | [docs/modules.md](modules.md) |
| 7. Identity & mapping | [docs/identity-and-mapping.md](identity-and-mapping.md) |
| 8. Source types | [docs/source-types.md](source-types.md) |
| 9. Configuration model | [docs/configuration.md](configuration.md) |
| 10. Orchestration | [docs/orchestration.md](orchestration.md) |
| 11. Zip packaging | [docs/packaging-zip.md](packaging-zip.md) |
| 12. Validation (pre-flight & post-flight) | [docs/validation.md](validation.md) |
| 13. Artefact store abstraction | [docs/artefact-store.md](artefact-store.md) |
| 14. Job contract | [docs/job-contract.md](job-contract.md) |
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
