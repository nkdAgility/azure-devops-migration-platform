# Tasks — 023.5 Reconciliation (2026-05-17)

## Phase 1: Control Plane Capability Routing

- [X] P1-T1 Add `jobs.source_type` column + EF migration — Status: complete/superseded; completed because superseded by connector capability routing on `Job.Connectors` (`specs/025.1-fold-to-job/spec.md`, `src/DevOpsMigrationPlatform.ControlPlane/Jobs/JobStore.cs`).
  - Evidence (superseded): control plane routing now evaluates `job.Connectors` against agent capabilities (`JobStore.DequeueAsync(...capabilities...)`); no `source_type` storage path exists in current code.
- [X] P1-T2 Add `?capabilities=` filter to `GET /agents/lease` — Status: complete
- [X] P1-T3 `MigrationAgent` polls with `?capabilities=ado,simulated` — Status: complete
- [X] P1-T4 Existing ADO/Simulated behavior preserved while routing changed — Status: complete

## Phase 2: Build `TfsMigrationAgent`

- [X] P2-T1 Create `DevOpsMigrationPlatform.TfsMigrationAgent` project and polling worker — Status: complete
- [X] P2-T2 Reuse/move TFS execution stack into agent-hosted path — Status: complete
- [X] P2-T3 Implement net481 progress/control-plane reporting via dedicated `TfsControlPlaneProgressSink` — Status: complete/superseded; completed because superseded by shared control-plane progress pipeline (`AddCoreAgentServices` + shared sink plumbing) used by `TfsJobAgentWorker`.
  - Evidence (superseded): no dedicated `TfsControlPlaneProgressSink` class exists; `TfsJobAgentWorker` inherits shared agent worker base and posts through shared infrastructure.
- [X] P2-T4 `TfsMigrationAgent` polls with `?capabilities=tfs` — Status: complete
- [ ] P2-T5 `AgentLifecycleService` spawns both `MigrationAgent` and `TfsMigrationAgent` in standalone mode — Status: incomplete
  - Evidence (incomplete): `src/DevOpsMigrationPlatform.ControlPlaneHost/AgentLifecycle/AgentLifecycleService.cs` resolves and spawns only `../MigrationAgent/DevOpsMigrationPlatform.MigrationAgent[.exe]`.

## Phase 3: Remove CLI Subprocess Bridge

- [X] P3-T1 Delete `TfsExportRunner`, `ExternalToolRunner`, `TfsExporterProcessAdapter`, and `IExternalToolRunner` — Status: complete
- [X] P3-T2 Simplify `QueueCommand` so all source types submit via control plane — Status: complete
- [X] P3-T3 Delete old `CLI.TfsMigration` project — Status: complete
- [X] P3-T4 Update packaging layout to `TfsMigrationAgent/` in win-x64 artefact — Status: complete

## Phase 4: Update Docs and Guardrails

- [ ] P4-T1 Rewrite `docs/tfs-exporter.md` for agent-based model — Status: incomplete
  - Evidence (incomplete): `docs/tfs-exporter.md` does not exist in current repository.
- [ ] P4-T2 Update all architecture docs to match actual lifecycle behavior — Status: incomplete
  - Evidence (incomplete): docs contain lifecycle assertions not matched by `AgentLifecycleService` current implementation.
- [X] P4-T3 Update guardrails for TFS agent topology/isolation model — Status: complete

## Cross-cutting superseded tasks discovered during reconciliation

- [X] X-T1 Add `source` to `DiscoveryJob` for TFS routing — Status: complete/superseded; completed because superseded by unified `Job` contract with `Connectors`-based capability routing.
  - Evidence (superseded): lease routing and queue submission flow use `Job.Connectors`; no standalone `DiscoveryJob` routing extension required in this path.
