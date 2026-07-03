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

**Status:** Accepted — amended by iron-comms (Phases A–E, 2026-06-30); wire transport superseded by ADR-0020

OTel signals (O-1), `IProgressSink` progress events (O-2), and `ILogger` diagnostics (O-3) are three distinct channels that must not be conflated. O-2 is stored as `.migration/runs/<runId>/logs/progress.ndjson`; O-3 as `.migration/runs/<runId>/logs/diagnostics.ndjson`. O-1 is exported via OTLP.

**Current implication:** Every module must emit progress, traces, metrics, and structured logs through the defined channels. The logical channels are unchanged, but the wire transport is unified (see ADR 0020): agents send all telemetry via `POST /workers/{workerId}/events`; CLI/TUI read metrics from `GET /jobs/{id}/telemetry` and subscribe to the unified `GET /jobs/{id}/stream?from={seq}` SSE stream — never an in-process sink, never the removed per-signal endpoints.

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

**Status:** Accepted — amended by iron-comms (2026-07-01): task-list push flows through the unified worker-event channel

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

## ADR 0014 — ICapture: Unified Capture Contract

**Status:** Accepted — amends ADR-0012

`ICapture` (`Name`, `CaptureAsync(InventoryContext, ct)`) is a standalone interface; `IModule` extends it instead of declaring `InventoryAsync`. The plan executor dispatches all `capture.*` tasks through one `captureHandlersByName` dictionary covering both modules and pure capture handlers (e.g. `DependencyCapture`). `IProjectAnalyser` is deleted.

**Current implication:** Modules implement `CaptureAsync` (not `InventoryAsync`). Pure capture handlers register as `ICapture` only — no new executor branches, no `IProjectAnalyser` references.

## ADR 0015 — Mode-Driven CLI and TUI UI Contract

**Status:** Accepted

Job `Kind` selects the view family for CLI and TUI progress surfaces. `Export`/`Prepare`/`Import`/`Migrate` share one migration task view; `Inventory` and `Dependencies` each have a mandatory table-based view plus tasks. `queue --follow` and `manage status` use the same mode-to-view mapping. The exact contract is `docs/ui-mode-contract.md`.

**Current implication:** CLI/TUI presentation changes must be evaluated against `docs/ui-mode-contract.md` before completion. Raw inspection commands (`manage progress`, `manage diagnostics`) stay raw.

## ADR 0016 — Unified Package Access

**Status:** Accepted

`IPackageAccess` is the canonical caller-facing package boundary for runtime package operations. `IPackageContentAddress` supplies module-owned relative content addressing beneath that boundary.

**Current implication:** Runtime modules, orchestrators, workers, checkpointing, phase tracking, and package-backed logging should use `IPackageAccess` for package-facing reads and writes instead of rebuilding path logic directly over `IArtefactStore` or `IStateStore`.

## ADR 0017 — Capability Seam Ethos and TDD Architecture Governance

**Status:** Accepted

Every concern uses one canonical seam and one reusable public runtime surface. Adapters/extensions remain thin policy facades; concern engines stay centralized behind the seam.

**Current implication:** Design artifacts must include a Capability Seam Decision before implementation. Test-first workflow and DoD checks enforce seam integrity early so architecture-review tenets apply during creation, not only after implementation.

## ADR 0018 — Compatibility-Only Guard Clauses

**Status:** Accepted

Runtime guard clauses are allowed only for genuine `net481` vs modern .NET crash-prevention boundaries. Defensive null-service checks, enablement guards, and generic fail-fast checks in module/orchestrator/service runtime code are prohibited; validation belongs in canonical validation surfaces (schema, `IValidateOptions<T>`, `ValidateAsync`, plan-level flows).

**Current implication:** Reject new non-compatibility guard clauses. Remove existing ones when their surrounding code is touched. Guards must never skip or degrade functionality on `net481` — features are implemented, not guarded away.

## ADR 0019 — WorkItems Extension Seam and Staged Cursor Pipeline

**Status:** Accepted

Per-revision WorkItems capabilities flow through the single `IModuleExtension` seam owned by `WorkItemResolutionProcessor` (per-revision sub-orchestrator: loop, cursor, metrics, progress). Cursor dispatch is name-keyed; the on-disk cursor format is preserved (new capabilities add marker strings additively). Revision save is a single atomic PATCH. The Extension Seam Ethos gates what may be an extension: distinct domain object, core entity complete without it, separate write operation. Links and attachments are core (not extensions); `CommentsWorkItemExtension` is the only valid WorkItems extension.

**Current implication:** New work-item capabilities that pass the seam ethos test are added as extensions with no core edit; concerns that fail the test go inline in the core pipeline. Orchestrators receive extensions via DI (`IEnumerable<IModuleExtension>`) — never `new` or `?? new` fallbacks.

## ADR 0020 — Unified Worker-Event Channel

**Status:** Accepted — amends ADR-0006 and ADR-0010

All agent telemetry (progress, diagnostics, metrics snapshots, task lists, heartbeat payloads, terminal signals) is batched by `UnifiedWorkerEventWriter` into sequence-numbered `WorkerEventBatch`es POSTed to `POST /workers/{workerId}/events` — the sole ingestion endpoint. CP stores are append-only per job (warned cap 50,000). Clients consume the unified replayable SSE stream `GET /jobs/{jobId}/stream?from={seq}` with auto-reconnect from the last sequence. The seven legacy per-lease endpoints and their client classes are deleted with no shims.

**Current implication:** Agent code never bypasses `UnifiedWorkerEventWriter` for telemetry. CLI/TUI never consume per-signal endpoints. Adding a telemetry kind = new `WorkerEventKind` + CP dispatch case only. Wire schema: `.agents/10-contracts/specs/observability-transport-contract.md`.

## ADR 0021 — Four-Tier Validation Model

**Status:** Accepted

Validation runs at four fixed lifecycle points: Tier 0 Structural (CLI, no network), Tier 1 Connectivity (CLI, network), Tier 2 Pre-flight (agent, before import), Tier 3 Post-flight (agent, after import / standalone `Validate`). Fail-fast is the default; continue-on-error is explicit config. The Control Plane only deduplicates and schema-validates at submission.

**Current implication:** New validation checks are added to the owning tier, never scattered. Module `ValidateAsync` is a side-effect-free pre-flight participant. Full check tables: `docs/validation.md`.




