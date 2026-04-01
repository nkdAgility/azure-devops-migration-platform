# Architecture Overview

> This document defines architectural intent and is the primary human reference.
> In any conflict between this document and `/ai/guardrails/*.md` guardrails, **the guardrails win**.
> See [ai/guardrails/system-architecture.md](../ai/guardrails/system-architecture.md) for the enforced rules.
> See [agents.md](../agents.md) for the agent entry point that binds docs to guardrails.

## 1. System Purpose

Build a migration package platform, not just a migration tool.

The system supports three modes:

1. **Export** — Azure DevOps Services → Files, or TeamFoundationServer (via .NET 4 OM exporter) → Files
2. **Import** — Files → Azure DevOps Services
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

The platform separates **job coordination** from **job execution**.

### MigrationJob is the Universal Contract

A `MigrationJob` is a fully serialisable object created by the TUI from a local config file. It is the only thing that crosses boundaries between the TUI, the control plane, and Migration Agents. The config file is never passed directly to any executor.

See [docs/job-contract.md](job-contract.md).

### Local Runner vs Control Plane Agent

The TUI routes a `MigrationJob` to one of two transports:

| Transport | What it does |
|---|---|
| `LocalJobRunner` | Executes the Job Engine in-process. No control plane required. |
| `ControlPlaneClient` | Submits the job to the control plane. A Migration Agent executes it remotely. |

Both transports call the same Job Engine contract. Switching from local to cloud requires no changes to the Job Engine or module code.

### Microsoft Aspire Orchestration

The Control Plane and Migration Agent(s) are orchestrated by Microsoft Aspire in both local development and cloud deployment scenarios:

- **Local**: Aspire AppHost runs Control Plane API, Migration Agent(s), PostgreSQL, and Azurite (blob emulator) on the developer's machine
- **Cloud**: Aspire deploys the same components to Azure Container Apps with PostgreSQL Flexible Server and Azure Blob Storage

The TUI always runs locally as a standalone CLI and is never orchestrated by Aspire. It connects to the Control Plane via configuration (localhost for local dev, cloud URL for production).

See [docs/aspire-integration.md](aspire-integration.md) for the complete orchestration model.

### All Stores are URI-Based

The package location is expressed as a URI in the `MigrationJob`. The Job Engine resolves the URI to an `IArtefactStore` implementation:

| URI scheme | Implementation |
|---|---|
| `file:///` | `FileSystemArtefactStore` |
| `azureblob://` | `AzureBlobArtefactStore` |

Module code never references a concrete store implementation.

### Progress is Event-Driven

The Job Engine emits structured `ProgressEvent` records through `IProgressSink`. The TUI subscribes; so does the package log (`Logs/progress.jsonl`). In cloud mode, the Migration Agent subscribes a `ControlPlaneProgressSink` instead.

The Job Engine has no knowledge of where progress is rendered.

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
| Hard architectural constraints (authoritative) | [ai/guardrails/system-architecture.md](../ai/guardrails/system-architecture.md) |
| WorkItems-specific rules | [ai/guardrails/workitems-rules.md](../ai/guardrails/workitems-rules.md) |
| Migration behaviour invariants | [ai/guardrails/migration-rules.md](../ai/guardrails/migration-rules.md) |
| Coding standards | [ai/guardrails/coding-standards.md](../ai/guardrails/coding-standards.md) |
| New module checklist | [ai/guardrails/module-template.md](../ai/guardrails/module-template.md) |
