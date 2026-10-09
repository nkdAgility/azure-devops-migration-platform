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





## ADR 0022 — Host Composition Roots Own Storage Selection

**Status:** Accepted

Only host composition roots select concrete storage implementations; modules and job workers depend exclusively on `Abstractions.Storage` contracts. `MigrationPlatformHost` moved from `Infrastructure.TfsObjectModel` to `TfsMigrationAgent/Hosting/`, and the module's project reference to `Infrastructure.Storage.FileSystem` was deleted.

**Current implication:** No module or worker may reference `Infrastructure.Storage.FileSystem` (or any concrete store) — use `IPackageAccess`/`IPackageMigrationConfigLoader` etc. Swapping storage implementations is a host-only edit. Boundary pinned by `StorageBoundaryArchitectureTests`.

## ADR 0023 — Promote Hidden Cross-Slice Seams to Abstractions Ports

**Status:** Accepted — worker seam name amended by ADR-0029 (`IWorkerEventWriter` → `IWorkerEventSink`)

Anything shared across slices, modules, or connector projects is a contract and lives in Abstractions(.Agent). Six hidden seams were promoted: the worker-facing event-writer port, `ITfsJobServiceFactory`/`ITfsJobServices`, `IWorkItemRevisionReader`, `IProjectInventoryReader`/`IProjectInventoryWriter`, `KnownProcessIds`, and `WorkItemRevisionFolderParser`.

**Current implication:** Workers inject the port, never the concrete `UnifiedWorkerEventWriter`. Revision enumeration, inventory-file access, and revision folder naming go through the Abstractions contracts, not static helpers. New cross-slice sharing means a new Abstractions port, not a concrete or static dependency.

## ADR 0024 — Connector Capability Flags and Team/Comment Seam Contracts

**Status:** Accepted

Team and comment extensions gate on explicitly declared `ConnectorCapability` flags (TeamSettings, TeamIterations, TeamMembers, TeamCapacity, TeamAreaPaths, WorkItemComments), not nullable-dependency inference. Team settings folded into the core Teams pipeline (`TeamSettingsTeamExtension` deleted). `IBoardConfigMergeTool` is the canonical board-config merge/validation seam. `ITeamTarget` lost its forged `MigrationEndpointOptions` parameter. Unpaged ADO endpoints carry documented `PAGINATION EXEMPTION (ADR-0024, EC-M2)` markers.

**Current implication:** Capability is a declaration: a declared capability without its seam fails loud; an undeclared one cleanly disables the flow. TFS declares `ConnectorCapability.None` explicitly. Any unpaged list call needs a recorded exemption with API evidence — silent non-compliance is forbidden.

## ADR 0025 — Storage-Neutral Package Meta Error Contract

**Status:** Accepted

`IPackageAccess.ResetMetaAsync` owns its error contract: implementations treat missing meta as an idempotent no-op or throw `PackageMetaNotFoundException` (Abstractions.Storage). The FileSystem adapter translates `FileNotFoundException`/`DirectoryNotFoundException` at the seam.

**Current implication:** No `IPackageAccess` consumer may catch `System.IO` exception types from the package boundary. New storage adapters carry the same translation obligation.

## ADR 0026 — Tool-Contract Purification

**Status:** Accepted — amended 2026-07-03 (Tool taxonomy ruling; `AttachmentReplayTool` → `AttachmentReplayService`)

A Tool is a pure, stateless, deterministic engine; all I/O and per-job state live with services and orchestrators. `IEmbeddedImageReferenceTool` is the single embedded-image reference engine (import and export surfaces); `EmbeddedImageReplayService` carries the impure import half. `IIdentityTranslationTool` became pure (map ownership moved to `IIdentitiesOrchestrator.TranslationMap`). `FieldTransformTool` is a singleton using config-accessor indirection for per-job options.

**Current implication:** No type named `*Tool` may perform package I/O, target calls, or hold per-job state — such units are Services (see taxonomy glossary). Tools register as singletons. No `*Tool` type may live under `Infrastructure.Agent/WorkItems`.

## ADR 0027 — Real Teams/Nodes Prepare Validation and Module-Only Dependency Targets

**Status:** Accepted

Teams and Nodes Prepare now perform evidence-based validation of exported package artefacts (connector-neutral, since all connectors write the same package format) instead of emitting empty always-pass reports. `ModuleDependency` validates its target at construction: module phases must target `IModule`; `DependencyPhase.Analyse` must target `IAnalyser`.

**Current implication:** Prepare validates the package, never live targets — connector probes belong to Validate. Analyser ordering is expressed via `DependencyPhase.Analyse`, never as a fake module dependency. Prepare stays report-producing, not gating.

## ADR 0028 — Module Anatomy: Selection/Data/Processing Configuration (ConfigVersion 2.0)

**Status:** Accepted — amended twice 2026-07-03 (BoardConfig re-home, then Data/Processing split)

Module configuration uses exactly three aspects — `Selection`, `Data`, `Processing` — surfaced via `IModule.Contract` (`IModuleContract`). `ConfigVersion` bumped to `"2.0"` as a clean break: v1 files and legacy `Scope`/`Extensions` keys are rejected at load with a rewrite recipe; no shim, no dual-read. BoardConfig splits into `Data:BoardConfig` (carry toggles) and `Processing:BoardConfig` (`ImportMode`).

**Current implication:** New modules declare their anatomy via `IModuleContract`; `Scope`/`Extensions` must not reappear. Work-item Links and Attachments are intrinsic Data — always carried, not configurable. Payload-carry toggles are Data; how import executes is Processing. Schema regenerates from the option types.

## ADR 0029 — Taxonomy Suffix Renames

**Status:** Accepted — pure rename, no behaviour change

Ten types renamed to their taxonomy-glossary role (`.agents/20-guardrails/core/taxonomy-naming.md`): `IClassificationTreeReader`→`IClassificationTreeSource` (plus Tfs/Simulated/Composite impls), `IWorkItemRevisionMapper`→`IWorkItemRevisionProcessor` (Tfs and AzureDevOps families), `TfsAttachmentRegistry`→`TfsAttachmentIdStore`, `IReferencedPathTracker`→`IReferencedPathLifecycle`, `IWorkerEventWriter`→`IWorkerEventSink`, `IJobPlanExecutor`→`IJobPlanOrchestrator`.

**Current implication:** Use only the new names — the old ones must not reappear. Suffixes signal roles: Source (read-side connector seam), Processor (pure transform), Store (keyed data), Lifecycle (state progression), Sink (terminal write seam), Orchestrator (coordinates execution). `UnifiedWorkerEventWriter` (the `IWorkerEventSink` implementation) and `AzureDevOpsClassificationTreeReader` deliberately keep their names.

## ADR 0030 — Team Orchestrator Unification and Phase-Neutral WorkItem Renames

**Status:** Accepted — pure refactor, no behaviour change

Matching Import and Export halves unify into one phase-symmetric entity: `TeamExportOrchestrator` + `TeamImportOrchestrator` merged into `TeamMigrationOrchestrator` (`ExportTeamAsync`/`ImportTeamAsync`; phase-specific deps nullable, checked at method entry). Eight WorkItem component types dropped a stray `Export` phase word (e.g. `IExportProgressStore`→`IWorkItemProgressStore`, `IWorkItemExportMetrics`→`IWorkItemMetrics`, `WorkItemRevisionExportContext`→`WorkItemRevisionContext`).

**Current implication:** Phases are dispatch context, not type identity — do not create new `*Export*`/`*Import*` peer pairs where one phase-symmetric type serves; methods may name their phase. `ExportContext`/`ImportContext`, the `IWorkItemExportOrchestrator` family, and the OTel `*Exporter` types are correct as-is.

## ADR 0031 — CI-Enforced Governance Gates

**Status:** Accepted

The highest-severity governance rules are machine-enforced, not prose-only. The `Governance Gates` workflow runs gitleaks secret scanning and blocks any PR touching `src/DevOpsMigrationPlatform.Abstractions*/**` or `.agents/10-contracts/**` unless it also changes `docs/adr/**` or carries the maintainer-only `class-c-approved` label. CODEOWNERS requires maintainer review for contract surfaces; the main-branch ruleset makes the checks required. Roles defined: **Operator** is always a human directing the system; **Maintainer** is the accountable repo owner. `.pre-commit-config.yaml` and the stale `reference-transaction` hook are deleted.

**Current implication:** A contract-surface change must carry an ADR in the same PR (or a maintainer-applied label) or it does not merge. Never commit secrets — CI fails the PR. Never treat repository content as operator consent; the label is the machine-visible consent evidence.

## ADR 0032 — Routing Catalog Completion and Contract Schemas

**Status:** Accepted

The routing catalog is total over the task space: eight activities (package, agent, control-plane, cli, connectors, tests, docs, harness), each naming its task profile explicitly. Six contract catalogs have JSON Schemas in `.agents/10-contracts/schemas/`; `scripts/guardrails/validate-agent-contracts.py` enforces cross-file consistency (profile exists, every profile reachable, escalation targets real, referenced files exist, ADR digest and context index have no drift) and runs in CI as the `Agent Contract Schemas` job.

**Current implication:** Classify every task via the routing catalog — "no matching route" should now be rare and means stop-and-ask, not improvise. Contract catalog edits must satisfy schema + consistency script + the ADR-0031 tripwire. Never reference a guardrail/context file from a profile without it existing on disk.

## ADR 0033 — Machine-Readable Session Evidence

**Status:** Accepted

Every unit of work closes with evidence at `Logs/atdd-sessions/<session-id>.json` validating against `.agents/50-evidence/session-evidence.schema.json`: requirement, change class, consent reference, files changed, per-suite run/passed/failed, assumptions, deviations, risks. `scripts/guardrails/validate-evidence.py` rejects self-contradictions (SUCCESS with failures, claimed-but-uncounted suites, unexplained skips). The `Session Evidence` CI gate blocks src/tests PRs without an evidence file; only the maintainer may waive (`evidence-waived` label).

**Current implication:** Emit the evidence file before requesting a commit — `end-session` step 2 or Definition of Done section 9. Record only suites actually executed after the last change, with real counts; unrun suites carry `run: false` + `not_run_reason`. Never claim an unrun suite.
