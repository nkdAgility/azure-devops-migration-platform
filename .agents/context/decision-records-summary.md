# Decision Records Summary

Compressed summary of Architecture Decision Records (ADRs). Full records in `docs/adr/`.

## ADR 0001 — Source → Files → Target

**Status:** Accepted

All migration data flows through the filesystem package. Source and Target never communicate directly. Export writes to the package. Import reads from the package.

**Current implication:** Never route data directly from source to target. Every module must write to `IArtefactStore` on export and read from `IArtefactStore` on import.

## ADR 0002 — Filesystem Package as Source of Truth

**Status:** Accepted

The package is the single source of truth. No external databases or memory structures are authoritative for migration state.

**Current implication:** All persistent state goes through `IArtefactStore` (artefacts) or `IStateStore` (transient state). Both are backed by the package working directory.

## ADR 0003 — Cursor-Based Checkpointing

**Status:** Accepted

Checkpoints are cursor strings (the last successfully processed artefact store path). No count-based progress tracking.

**Current implication:** No watermark tables, no in-memory counts as resume state. Resume = seek to cursor in `EnumerateAsync`.

## ADR 0004 — Control Plane Does Not Execute Migrations

**Status:** Accepted

The Control Plane coordinates but never executes migration phases. Migration logic runs exclusively in agents.

**Current implication:** No migration method calls in Control Plane code. No package writes from Control Plane.

## ADR 0005 — Agent-Only Package Write Access

**Status:** Accepted — amended by ADR-0008

Only Migration Agent and TFS Export Agent may write to the package. CLI, TUI, Control Plane, and ControlPlaneHost are read-only.

**Current implication:** Reject any code that calls `IArtefactStore` write methods from CLI, TUI, or Control Plane code. The CLI may serialise config into the job token, but the agent performs the package write.

## ADR 0006 — Three-Channel Observability

**Status:** Accepted

OTel signals (O-1), `IProgressSink` progress events (O-2), and `ILogger` diagnostics (O-3) are three distinct channels that must not be conflated. O-2 is stored as `.migration/runs/<runId>/logs/progress.ndjson` and streamed via SSE. O-3 is stored as `.migration/runs/<runId>/logs/diagnostics.ndjson` and streamed via SSE. O-1 is exported via OTLP.

**Current implication:** Every module must emit progress, traces, metrics, and structured logs through the defined channels. CLI/TUI read metrics from `GET /jobs/{id}/telemetry` and progress from `GET /jobs/{id}/progress?follow=true` — never from an in-process sink.

## ADR 0007 — Compiler-Enforced Project Boundary Topology

**Status:** Accepted

Project reference topology enforces layer isolation at compile time. CLI may only reference `Abstractions` and base `Infrastructure`. ControlPlane adds `Abstractions.ControlPlane`. Agent adds `Abstractions.Agent` and `Infrastructure.Agent`. Violations are build errors.

**Current implication:** CLI must not reference `MigrationAgent`, `ControlPlane`, or any infrastructure connector assembly. `LocalStackHost` (in-process fallback) is deleted. The in-process fallback is replaced by `ChildProcessHost`.

## ADR 0008 — Configuration Travels in the Package

**Status:** Accepted

The CLI serialises config into `Job.ConfigPayload`. The agent writes `migration-config.json` to the package after lease acquisition and builds the per-job `IOptions<T>` scope from that materialised file.

**Current implication:** Module options are rebuilt from agent-materialised package config. The Control Plane routes opaque config payload but does not inspect or proxy configuration fields.

## ADR 0009 — Single Job Class with Kind Discriminator

**Status:** Accepted

`MigrationJob` and `DiscoveryJob` are replaced by a single `Job` record. `Job.Kind` (`Export | Import | Migrate | Prepare | Inventory | Dependencies`) is the dispatch discriminator.

**Current implication:** All code that switched on `MigrationJob` vs `DiscoveryJob` switches on `Job.Kind`. Adding a new job kind requires adding an enum value and an Agent dispatch case — no structural change to the job model.

## ADR 0010 — Plan-Driven DAG Execution

**Status:** Accepted

The Agent builds an execution plan from `IModule.DependsOn`, persists it to `.migration/Checkpoints/plan.json`, and drives all execution from the plan. Independent modules run concurrently. The plan enables task-level resume without re-executing completed modules.

**Current implication:** `IModule.DependsOn` is authoritative — the plan executor enforces it. A crashed agent resumes from persisted plan state, skipping completed tasks. Circular dependencies fail the job before any module executes.

## ADR 0011 — Unified `platform.*` Metric Namespace

**Status:** Accepted

All metric strings across Agent, ControlPlane, and CLI use `platform.<domain>.<phase>.<measure>`. `IDiscoveryMetrics` + `IMigrationMetrics` are merged into `IPlatformMetrics`.

**Current implication:** No metric string may begin with `discovery.*`, `migration.*`, `controlplane.*`, or `cli.*`. High-cardinality identifiers (`WorkItemId`, `RevisionIndex`) must not appear as metric tags.

## ADR 0012 — IModule Five-Phase Contract

**Status:** Accepted

`IModule` exposes all five phases: `InventoryAsync`, `ExportAsync`, `PrepareAsync`, `ImportAsync`, `ValidateAsync`. Standalone `InventoryModule`, `InventoryDiscoveryModule`, and `DependencyDiscoveryModule` are eliminated. `IAnalyser` handles cross-cutting analysis operations.

**Current implication:** Every domain module implements all five phase methods. Phase methods with no behaviour return `Task.CompletedTask` and emit a `Debug` log. The plan executor calls the correct phase method based on `Job.Kind`.

## ADR 0013 — Simulated Connector as First-Class CI Infrastructure

**Status:** Accepted

`Infrastructure.Simulated` is a production-quality connector, not a test stub. Simulated sources must yield ≥ 2 items. Simulated targets must record received data for assertion. Every module must have a `SystemTest_Simulated` test that asserts artefact content (not just absence of exceptions).

**Current implication:** A zero-item simulated source is a test violation. An import test that asserts `count >= 0` is a test violation. A `SystemTest_Simulated` that only asserts `Assert.IsNotNull(result)` is a test violation.

## ADR 0016 — Unified Package Access

**Status:** Accepted

`IPackageAccess` is the canonical caller-facing package boundary for runtime package operations. `IPackageContentAddress` supplies module-owned relative content addressing beneath that boundary.

**Current implication:** Runtime modules, orchestrators, workers, checkpointing, phase tracking, and package-backed logging should use `IPackageAccess` for package-facing reads and writes instead of rebuilding path logic directly over `IArtefactStore` or `IStateStore`.