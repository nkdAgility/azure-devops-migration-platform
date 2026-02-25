# Architecture Overview

> This document defines architectural intent and is the primary human reference.
> In any conflict between this document and `/agents/*.md` guardrails, **the guardrails win**.
> See [agents/system-architecture.md](../agents/system-architecture.md) for the enforced rules.
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

## 13. What This System Is Now

It is **no longer**:

- A live migration tool
- A direct source-to-target copier

It **is**:

> A versioned migration package platform with streaming chronological replay.

The platform supports two execution modes:

- **Local** — The TUI runs the orchestrator in-process using `FileSystemArtefactStore`. No control plane required.
- **Remote** — The TUI submits a job to the control plane; an agent container executes it using `AzureBlobArtefactStore` or another URI-addressable store.

Both modes use the same orchestrator engine, the same modules, and the same cursor-based checkpoints. The package contract is identical. See [docs/tui.md](tui.md), [docs/control-plane.md](control-plane.md), and [docs/migration-agent.md](migration-agent.md).

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
2. Job Engine (orchestrator + modules contract + cursors)
3. `IArtefactStore` + `FileSystemArtefactStore` (`file:///` URI)
4. `IStateStore` / `PackageCheckpointStateStore` (`Checkpoints/` inside package)
5. `IProgressSink` with `ConsoleProgressSink` + `PackageProgressSink`
6. WorkItems module (REST)
7. Identity module
8. Legacy TFS export adapter
9. Teams / Permissions / Builds modules
10. TUI local commands (`prepare`, `export`, `import`, `both`, `validate`, `pack`, `unpack`)
11. `ControlPlaneClient` stub (remote commands parse, return "not implemented")

### Phase 2 — Cloud-ready

12. `AzureBlobArtefactStore` (`azureblob://` URI)
13. Control plane API (job submission, lease, status, logs)
14. Migration Agent container (poll, execute, heartbeat, report)
15. `ControlPlaneProgressSink`
16. TUI remote commands (`queue`, `status`, `logs`, `pause`, `resume`, `cancel`)

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
| 17. TUI (Terminal UI) | [docs/tui.md](tui.md) |

## Agent Guardrails

| Topic | Document |
|---|---|
| Hard architectural constraints (authoritative) | [agents/system-architecture.md](../agents/system-architecture.md) |
| WorkItems-specific rules | [agents/workitems-rules.md](../agents/workitems-rules.md) |
| Migration behaviour invariants | [agents/migration-rules.md](../agents/migration-rules.md) |
| Coding standards | [agents/coding-standards.md](../agents/coding-standards.md) |
| New module checklist | [agents/module-template.md](../agents/module-template.md) |
