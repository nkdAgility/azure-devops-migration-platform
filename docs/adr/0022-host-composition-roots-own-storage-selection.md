# ADR 0022 — Host Composition Roots Own Storage Selection; Modules Depend Only on Storage Contracts

## Status

Accepted

Executes architecture-audit items **MM-C1**, **MM-H1**, and **CA-C2** (analysis/archcheck/report.md) as one coupled Class C change under explicit operator consent, with contract compatibility tests and a test-first trace as required by `.agents/20-guardrails/core/change-governance.md`.

## Context

Three coupled boundary violations were confirmed by the architecture audit:

1. **MM-H1** — `Infrastructure.TfsObjectModel` contained `MigrationPlatformHost`, the full DI host builder for the TFS export subprocess (Serilog sinks, OpenTelemetry/Azure Monitor exporters, and concrete storage selection). A module owned a host composition root, which is a host responsibility per the module model (`.agents/30-context/domains/module-model.md`): modules expose a single registration entry point (`AddTfsObjectModelModule`) and never compose telemetry, logging, or storage implementations.
2. **MM-C1** — that host builder was the sole reason `Infrastructure.TfsObjectModel.csproj` carried a `ProjectReference` to `Infrastructure.Storage.FileSystem`, coupling a connector module to a concrete storage implementation instead of the `Abstractions.Storage` contracts.
3. **CA-C2** — `JobAgentWorker` (net10 agent) and `TfsJobAgentWorker` (net481 agent) declared `using DevOpsMigrationPlatform.Infrastructure.Storage.FileSystem;`, binding the use-case ring to the concrete storage assembly. Inspection showed both usings were dead: the workers already consume storage exclusively through `Abstractions.Storage` contracts (`IPackageAccess`, `IPackageMigrationConfigLoader` via `ModulePipelineWorkerBase`).

## Decision

**Host composition roots — and only host composition roots — select concrete storage implementations. Modules and job workers depend exclusively on `Abstractions.Storage` contracts.**

Concretely:

1. `MigrationPlatformHost` (the TFS subprocess host builder) moved from `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/JobLifecycle/TfsExecution/` to `src/DevOpsMigrationPlatform.TfsMigrationAgent/Hosting/` (namespace `DevOpsMigrationPlatform.TfsMigrationAgent.Hosting`). The `AddPackageBoundaryServices()` call (storage-implementation selection) moved with it, so it now executes inside the TfsMigrationAgent host project. `Infrastructure.TfsObjectModel` exposes only its module registration entry point, `AddTfsObjectModelModule`.
2. The `ProjectReference` from `Infrastructure.TfsObjectModel.csproj` to `Infrastructure.Storage.FileSystem.csproj` was deleted. The module depends only on `Abstractions.Storage`.
3. The dead `Infrastructure.Storage.FileSystem` usings were removed from `JobAgentWorker.cs` and `TfsJobAgentWorker.cs`. The host composition roots (`MigrationAgentServiceExtensions`, `TfsMigrationAgentServiceExtensions`, and the moved `MigrationPlatformHost`) remain the only places that reference the FileSystem implementation (`AddPackageStorageServices`, `AddPackageMigrationConfigLoader`, `AddPackageBoundaryServices`) — which is exactly where concrete selection belongs.

No new abstraction was introduced: the existing `Abstractions.Storage` surface (`IPackageAccess`, `IPackageMigrationConfigLoader`, `ActivePackageState`, `IPackageStoreFactory`) was already sufficient.

## Contract Tests

The boundary is pinned by contract compatibility tests (written RED before the production change where observable):

- `StorageBoundaryArchitectureTests` (tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Architecture/) — asserts the csproj reference ban (MM-C1), the absence of a host composition root inside the module (MM-H1), and the ban on `Storage.FileSystem` usage in both job workers (CA-C2).
- `StorageCompositionContractTests` (tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/) — asserts `AddTfsMigrationAgentServices` still resolves `IPackageAccess`/`IPackageMigrationConfigLoader`, and that the relocated `MigrationPlatformHost.CreateDefaultBuilder` still composes a working package boundary.
- Existing coverage retained: `DiCompositionTests` (net10 agent full-container build with `ValidateOnBuild`) and `SystemTest_Smoke_AgentStartsWithoutStartupOrDiErrors`.

## Alternatives Considered

**A dedicated subprocess host project** for `MigrationPlatformHost`: rejected as over-structure — the builder has no production callers outside the TFS agent boundary (the agent model's `TfsJobServiceFactory` superseded the CLI-args subprocess model), so a folder inside the existing TfsMigrationAgent host is sufficient and avoids a new csproj.

**Deleting `MigrationPlatformHost` outright** (it currently has no production call sites): rejected for this change — the audit consent covers relocation, not removal; deletion would be a separate decision.

## Consequences

- `Infrastructure.TfsObjectModel` can no longer reach concrete storage types at compile time; any future storage need inside the module must go through `Abstractions.Storage`.
- The TfsMigrationAgent host project now owns two composition surfaces: the agent worker host (`Program.cs` + `AddTfsMigrationAgentServices`) and the legacy subprocess host (`Hosting/MigrationPlatformHost`).
- Swapping the storage implementation (e.g. `Infrastructure.Storage.AzureBlob`) is now a host-only edit across all composition roots.
- `Infrastructure.TfsObjectModel` still carries Serilog/OTel package references used by its telemetry adapters; pruning unused packages is deliberately out of scope for this ADR.
