# ADR 0023 — Promote Hidden Cross-Slice Seams to Canonical Abstractions Ports

## Status

Accepted

Executes architecture-audit items **CA-C1**, **CA-H1** (and its duplicate **HX-M1**), **VS-H1**, **VS-H2**, **VS-H3**, and **VS-M3** (analysis/archcheck/report.md) as one themed Class C change under explicit operator consent, with contract compatibility tests and a test-first trace as required by `.agents/20-guardrails/core/change-governance.md`.

## Context

The architecture audit confirmed six related violations sharing one root cause: seams that are shared across slices, modules, or projects were hidden behind concrete types or static helpers instead of living as canonical contracts in the Abstractions inner ring:

1. **CA-C1** — `JobAgentWorker` and `TfsJobAgentWorker` (plus `AgentWorkerBase`/`ModulePipelineWorkerBase`) injected the concrete `UnifiedWorkerEventWriter` (Infrastructure.Agent.Telemetry) to push task lists and terminal signals, binding the use-case ring to a concrete telemetry channel.
2. **CA-H1 / HX-M1** — `ITfsJobServiceFactory` was declared inside `Infrastructure.TfsObjectModel` but consumed cross-project by the `TfsMigrationAgent` worker: a port owned by an infrastructure module rather than the inner ring.
3. **VS-H1** — the static `WorkItemsPrepareRevisionReader` helper shared revision-enumeration business logic across seven slices (Prepare, Import-failure patterns, Node validation, WorkItemType validation).
4. **VS-H2** — the static `ProjectInventoryFile.MergeAsync/ReadAsync` governed the `inventory.json` file format shared by seven slices (Inventory/Discovery, Analysis, Identities, Nodes, Teams, WorkItems, JobPlanExecutor).
5. **VS-H3** — `KnownProcessIds` lived in `Infrastructure.Agent` but was referenced by all three connector projects (Simulated, AzureDevOps, TfsObjectModel), coupling connectors to another module's internals.
6. **VS-M3** — the static `WorkItemRevisionFolderParser` (the `{ticks}-{workItemId}-{revisionIndex}` package folder-naming contract) was consumed by both the WorkItems Revisions pipeline and the Nodes `ReferencedPathsFromWorkItemsStrategy`, creating hidden cross-slice coupling.

## Decision

**Anything shared across slices, modules, or connector projects is a contract, and contracts live in Abstractions(.Agent).** The six seams were promoted as follows; behaviour is byte-for-byte preserved — these are seam promotions, not behaviour changes.

1. **`IWorkerEventWriter`** (`DevOpsMigrationPlatform.Abstractions.Agent.Telemetry`) — the worker-facing surface of the unified event channel (`EnqueueTasks`, `EnqueueTerminal`, `FlushAsync`). `UnifiedWorkerEventWriter` implements it (`EnqueueTerminal` widened from `internal` to `public`); all four worker classes now inject the port. Both registration sites (`CoreAgentServiceExtensions.AddControlPlaneIntegration`, `TelemetryServiceExtensions.AddUnifiedWorkerEventWriter`) map the port to the same singleton. The concrete registration is retained for the hosted-service/`IFlushable` wiring and for infrastructure-internal consumers (`ControlPlaneTelemetryTimer`, `ControlPlaneLoggerProvider`, `CompositeProgressSink`), which legitimately use the wider concrete surface (`EnqueueSnapshot`, `EnqueueMetrics`, `EnqueueDiagnostic`, `Emit`) inside the same module — only the worker seam was mandated and promoted.
2. **`ITfsJobServiceFactory` + `ITfsJobServices`** (`DevOpsMigrationPlatform.Abstractions.Agent.TfsExecution`) — the factory port moved to Abstractions.Agent, and a companion `ITfsJobServices` contract was introduced so the factory's return type does not drag TFS SDK types into the inner ring: it exposes only the thirteen Abstractions-owned seam properties (revision/attachment sources, node creator, tree reader, discovery/fetch services, identity/team sources, lifecycle service, metrics, and `Endpoint` typed as the base `MigrationEndpointOptions`). The concrete `TfsJobServices` keeps the TFS SDK `WorkItemStore` off-contract; the two in-module consumers that genuinely need SDK types (`TfsActiveJobWorkItemTargetFactory`, `TfsActiveJobWorkItemTypeReadinessTargetFactory`) downcast inside `Infrastructure.TfsObjectModel`, which owns the concrete type. `ActiveTfsJobServices` now carries `ITfsJobServices`.
3. **`IWorkItemRevisionReader` + `ParsedWorkItemRevision`** (`DevOpsMigrationPlatform.Abstractions.Agent.WorkItems`) — the static helper became the injectable `WorkItemsPrepareRevisionReader` implementation (Infrastructure.Agent), injected into the seven consumers (`MissingRevisionArtefactImportFailurePattern`, `InvalidRevisionPayloadImportFailurePattern`, `FieldTransformCompatibilityImportFailurePattern`, `MissingAttachmentBinaryImportFailurePattern`, `MissingEmbeddedImageBinaryImportFailurePattern`, `NodePathValidator`, `WorkItemTypeValidator`).
4. **`IProjectInventoryReader` / `IProjectInventoryWriter` + `ProjectInventoryData`** (`DevOpsMigrationPlatform.Abstractions.Agent.Discovery`) — the static `ProjectInventoryFile` became the single `ProjectInventoryFileStore` implementation (Infrastructure.Agent), injected into the seven consuming slices (`InventoryOrchestrator`, `InventoryAnalyser`, `JobPlanExecutor`, `IdentitiesOrchestrator`, `NodesOrchestrator`, `TeamsOrchestrator`, `WorkItemsOrchestrator`). Inventory file-format changes are now made behind a contract.
5. **`KnownProcessIds`** moved to `DevOpsMigrationPlatform.Abstractions.ProjectLifecycle` so the three connector projects depend only on Abstractions for the shared process-template identifiers.
6. **`WorkItemRevisionFolderParser` + `WorkItemRevisionFolderParseResult`** moved to `DevOpsMigrationPlatform.Abstractions.Agent.WorkItems` as the canonical package folder-naming contract. Of the two consented options (move the naming contract vs. consume the VS-H1 reader), the move was chosen because `ReferencedPathsFromWorkItemsStrategy` uses the parser to classify folder-shaped enumeration entries — a case the revision reader (which enumerates only `revision.json` artefacts) does not cover — so the move preserves behaviour byte-for-byte while the reader-based rewrite would have altered enumeration and legacy-fallback semantics.

The injectable consumers take the new ports as optional trailing constructor parameters defaulting to the single stateless implementation, matching the established optional-dependency pattern in this codebase; DI supplies the registered singleton in composed hosts while existing direct construction sites (including tests) remain source-compatible.

## Contract Tests

Written RED before the production change (13/13 failing), GREEN after:

- `AbstractionsPortArchitectureTests` (tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Architecture/) — pins port existence and location in Abstractions(.Agent), implementation conformance, the bans on concrete `UnifiedWorkerEventWriter` injection in workers and on static-helper calls in the fourteen consumers, the removal of the old type locations, and the behavioural contracts of `KnownProcessIds.TryResolve` and `WorkItemRevisionFolderParser.TryParse`.
- `AbstractionsPortContractTests` (same folder) — pins DI resolution of every port through `AddCoreAgentServices` (port and concrete resolve to the same singleton; reader/writer share one implementation) and the inventory-file round-trip contract (merge preserves other modules' counts; missing file yields an empty record).

## Alternatives Considered

- **Interface-only worker seam without keeping the concrete registration** — rejected: the hosted-service drain loop, `IFlushable` fan-out, and infrastructure-internal telemetry consumers need the wider concrete surface; forcing them through a fattened port would over-widen the contract.
- **Moving `TfsJobServices` itself into Abstractions.Agent** — rejected (blocked by design): it exposes the TFS SDK `WorkItemStore` and `TeamFoundationServerEndpointOptions`; the `ITfsJobServices` boundary contract achieves the dependency inversion without any SDK leak.
- **VS-M3 via the VS-H1 reader** — rejected for this change; see Decision item 6.
