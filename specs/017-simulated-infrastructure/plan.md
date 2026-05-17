# Implementation Plan: Simulated Infrastructure Connector

**Branch**: `017-simulated-infrastructure` | **Date**: 2026-04-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `specs/017-simulated-infrastructure/spec.md`

## Summary

Add `DevOpsMigrationPlatform.Infrastructure.Simulated` — a fully self-contained synthetic connector that requires no Azure DevOps or TFS credentials. Enable credential-free export, import, and roundtrip testing.

This work has three interlocking parts:
1. **Polymorphic endpoint config model** — `MigrationEndpointOptions` becomes abstract; each connector defines its own derived options type; `EndpointOptionsTypeRegistry` + `PolymorphicEndpointOptionsConverter` in shared `Infrastructure` handle JSON dispatch.
2. **Boundary cleanup** — factory interfaces accept the base type; `WorkItemsModule` becomes connector-agnostic; two ADO leaks (Simulated routing and type-check) are removed; `CatalogService` moves to shared `Infrastructure`; `SimulatedWorkItemImportTarget` moves to `Infrastructure.Simulated`.
3. **New `Infrastructure.Simulated` assembly** — config-driven generator implements all required source and target interfaces; keyed DI wires it in; scenario configs + launch profiles enable offline testing.

## Technical Context

**Language/Version**: C# 12 / .NET 10.0 (new `Infrastructure.Simulated` assembly); `net481` not targeted by Simulated assembly  
**Primary Dependencies**: `Microsoft.Extensions.DependencyInjection`, `System.Text.Json`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Logging.Abstractions` — all already present in shared `Infrastructure`  
**Storage**: On-disk migration package via `IArtefactStore` / `FileSystemArtefactStore` (unchanged). `IStateStore` for checkpoints (unchanged).  
**Testing**: MSTest v3 + Reqnroll for acceptance tests; `[TestCategory("SystemTest")]` for scenario-level tests; `[TestCategory("Unit")]` for generator and config deserialization tests  
**Target Platform**: Windows / Linux server (Docker-compatible); `net10.0` only for all Simulated code  
**Project Type**: Class library (connector assembly) + DI extension wiring  
**Performance Goals**: A 500 work item × 5 revision Simulated export must complete in < 30 seconds on a developer laptop with no external I/O  
**Constraints**: Zero network calls in Simulated mode; no in-memory buffering of all revisions; deterministic output (same seed → same package layout)  
**Scale/Scope**: Supports arbitrary work item counts bounded only by disk space; designed for single-machine dev + CI test scenarios

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Context loaded:** guardrails files (`architecture-boundaries.md`, `coding-standards.md`, `testing-rules.md`, `module-rules.md`), context files (`migration-package-concept.md`, `job-lifecycle.md`, `package-manager.md`), and `docs/architecture.md`, `docs/module-development-guide.md`, `analysis/Simulated.md` all read.

- [x] **Package-First (I):** `SimulatedWorkItemRevisionSource` writes to `IArtefactStore` via the existing `WorkItemExportOrchestrator` path. `SimulatedWorkItemImportTarget` accepts calls from the existing `WorkItemImportOrchestrator`. No direct source-to-target.
- [x] **Streaming (II):** `SimulatedWorkItemRevisionSource.GetRevisionsAsync()` uses `yield return` — one record at a time, never buffered. `EnumerateAsync` is not called for generated data (generator creates records on demand).
- [x] **WorkItems Layout (III):** Generator produces `WorkItemRevision` records; the existing `WorkItemExportOrchestrator` writes them to `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/`. No custom folder logic in Simulated code.
- [x] **Checkpointing (IV):** `WorkItemsModule` already uses `ICheckpointingService` backed by `IStateStore`. Simulated export/import uses the same path with no changes to checkpointing.
- [x] **Module Isolation (V):** `WorkItemsModule` already uses only `IArtefactStore`, `IStateStore`, `IIdentityMappingService`. The factory interface change (accept base type) keeps it connector-agnostic.
- [x] **Separation of Planes (VI):** No changes to `ControlPlane`, `ControlPlaneHost`, TUI, or TFS subprocess. Simulated is a connector assembly only.
- [x] **Determinism (VII):** `MigrationEndpointOptions` → abstract base is a breaking config change. A version upgrader will be included (see research.md). Factory interface signature change is an internal contract; no external upgrader needed.
- [x] **ATDD-First (VIII):** All 5 user stories have ≥ 1 Given/When/Then scenario in `spec.md`. ATDD inner loop applies per scenario.
- [x] **SOLID & DI (IX):** All Simulated services use constructor injection. `SimulatedEndpointOptions` uses `init`-only or `set` properties. `AddSimulatedWorkItemExport()` / `AddSimulatedWorkItemImport()` / `AddSimulatedDependencyAnalysis()` are the registration extension points.

## Project Structure

### Documentation (this feature)

```text
specs/017-simulated-infrastructure/
├── plan.md              ← this file
├── research.md          ← Phase 0 output
├── data-model.md        ← Phase 1 output
├── quickstart.md        ← Phase 1 output
├── contracts/           ← Phase 1 output
└── tasks.md             ← Phase 2 output (/speckit.tasks)
```

### Source Code Changes

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   └── Options/
│       └── MigrationEndpointOptions.cs          ← sealed → abstract; remove all connector fields
│   └── Models/
│       └── OrganisationEntry.cs                 ← sealed → abstract; remove connector fields
│       └── OrganisationEndpoint.cs              ← MOVE to Infrastructure.AzureDevOps
│   └── Services/
│       └── IWorkItemRevisionSourceFactory.cs    ← signature change: accept MigrationEndpointOptions
│       └── IWorkItemImportTargetFactory.cs      ← signature change: accept MigrationEndpointOptions
│
├── DevOpsMigrationPlatform.Infrastructure/
│   └── Serialization/
│       └── EndpointOptionsTypeRegistry.cs       ← NEW: maps discriminator → System.Type
│       └── PolymorphicEndpointOptionsConverter.cs ← NEW: JsonConverter<MigrationEndpointOptions>
│       └── PolymorphicOrganisationEntryConverter.cs ← NEW: JsonConverter<OrganisationEntry>
│   └── Extensions/
│       └── EndpointOptionsRegistrationExtensions.cs ← NEW: AddEndpointOptionsType() / AddOrganisationEntryType()
│   └── Services/
│       └── CatalogService.cs                    ← MOVE from Infrastructure.AzureDevOps
│   └── Modules/
│       └── WorkItemsModule.cs                   ← remove connector-specific field reads
│   └── Import/
│       └── SimulatedWorkItemImportTarget.cs     ← MOVE to Infrastructure.Simulated
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   └── Options/
│       └── AzureDevOpsEndpointOptions.cs        ← NEW: derived from MigrationEndpointOptions
│       └── AzureDevOpsOrganisationEntry.cs      ← NEW: derived from OrganisationEntry
│   └── Models/
│       └── OrganisationEndpoint.cs              ← MOVED here; internal
│   └── Import/
│       └── AzureDevOpsWorkItemImportTargetFactory.cs ← remove "Simulated" routing branch; accept base type
│       └── AzureDevOpsResolutionStrategyFactory.cs   ← remove SimulatedWorkItemImportTarget type-check
│   └── Export/
│       └── AzureDevOpsWorkItemRevisionSourceFactory.cs ← accept base type; cast internally
│   └── Services/
│       └── CatalogService.cs                    ← REMOVED (moved to Infrastructure)
│
├── DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/
│   └── Options/
│       └── TeamFoundationServerEndpointOptions.cs ← NEW: derived from MigrationEndpointOptions
│       └── TeamFoundationServerOrganisationEntry.cs ← NEW: derived from OrganisationEntry (best-effort)
│
└── DevOpsMigrationPlatform.Infrastructure.Simulated/      ← NEW ASSEMBLY
    ├── DevOpsMigrationPlatform.Infrastructure.Simulated.csproj
    ├── Options/
    │   ├── SimulatedEndpointOptions.cs
    │   ├── SimulatedOrganisationEntry.cs
    │   ├── SimulatedGeneratorConfig.cs
    │   ├── SimulatedProjectConfig.cs
    │   └── SimulatedWorkItemTypeConfig.cs
    ├── Export/
    │   ├── SimulatedWorkItemRevisionSource.cs
    │   └── SimulatedWorkItemRevisionSourceFactory.cs
    ├── Import/
    │   ├── SimulatedWorkItemImportTarget.cs           ← MOVED from Infrastructure
    │   ├── SimulatedWorkItemImportTargetFactory.cs
    │   └── SimulatedResolutionStrategyFactory.cs
    ├── Services/
    │   ├── SimulatedProjectDiscoveryService.cs
    │   ├── SimulatedWorkItemDiscoveryService.cs
    │   ├── SimulatedWorkItemLinkAnalysisService.cs
    │   ├── SimulatedAttachmentBinarySource.cs
    │   ├── SimulatedEmbeddedImageDownloader.cs
    │   ├── SimulatedWorkItemCommentSource.cs
    │   └── SimulatedWorkItemCommentSourceFactory.cs
    └── SimulatedServiceCollectionExtensions.cs
```

### Test Projects

```text
tests/
├── DevOpsMigrationPlatform.Abstractions.Tests/   ← existing; new tests for base option deserialization
├── DevOpsMigrationPlatform.Infrastructure.Tests/ ← existing; new tests for EndpointOptionsTypeRegistry + converters
├── DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/ ← NEW
│   ├── Export/
│   │   └── SimulatedWorkItemRevisionSourceTests.cs
│   ├── Import/
│   │   └── SimulatedWorkItemImportTargetTests.cs
│   └── Services/
│       └── SimulatedProjectDiscoveryServiceTests.cs
└── DevOpsMigrationPlatform.SystemTests/          ← existing; new system test for simulated export scenario
    └── Features/
        └── SimulatedExportSystemTest.feature
```

---

## Phase 0: Research

> All NEEDS CLARIFICATION items resolved from codebase analysis and `analysis/Simulated.md`. No external research required.

### Decision 1 — Polymorphic JSON dispatch strategy

**Decision**: `EndpointOptionsTypeRegistry` (singleton) + `PolymorphicEndpointOptionsConverter` (custom `JsonConverter<MigrationEndpointOptions>`) in shared `Infrastructure`. Each connector's `AddXxx()` calls `services.AddEndpointOptionsType(key, type)` which appends to the registry.

**Rationale**: The only attribute-based alternative (`[JsonPolymorphic]` + `[JsonDerivedType]` on the base) requires the base assembly to reference every connector assembly — circular dependency, will not compile. The registry approach has zero circular dependencies and zero changes to `Abstractions` per connector.

**Alternatives considered**:
- Option B (flat base with sub-sections per connector): `MigrationEndpointOptions` would know about every connector. Adding GitHub would require editing `Abstractions`. Rejected.
- Option C (fully keyed DI sections): Forces every consumer of `job.Source` to resolve config via DI container calls rather than reading a model property. High friction with no architectural gain. Rejected.

---

### Decision 2 — `net481` targeting for `Infrastructure.Simulated`

**Decision**: `Infrastructure.Simulated` targets `net10.0` only. It is never called by the TFS subprocess.

**Rationale**: The TFS subprocess (`CLI.TfsMigration`) is the only `net481` consumer of `Infrastructure` and `Abstractions`. It does not perform import and never needs `SimulatedWorkItemImportTarget`. The factory interfaces are in `Abstractions` (multi-targeted), but the Simulated implementations can safely be `net10.0`-only — the same pattern as `Infrastructure.AzureDevOps`.

**Alternatives considered**: Multi-targeting `Infrastructure.Simulated` to `net481;net10.0` — unnecessary complexity, no concrete consumer on `net481`. Rejected.

---

### Decision 3 — `MigrationEndpointOptions` → abstract: config schema upgrader scope

**Decision**: Config schema version stays at current version. No upgrader is needed for the `MigrationEndpointOptions` abstract change.

**Rationale**: The JSON shape of existing scenario configs is unchanged — they still use `"Type": "AzureDevOpsServices"`, `"Url": "..."`, etc. The change is purely in the C# type that is deserialised into, not in the JSON structure. Existing configs will deserialize cleanly via the new `PolymorphicEndpointOptionsConverter` as long as `AzureDevOpsEndpointOptions` carries the same properties (`Url`, `Project`, `ApiVersion`, `Authentication`). No upgrader required.

---

### Decision 4 — `OrganisationEndpoint` move scope

**Decision**: `OrganisationEndpoint` is moved from `Abstractions` to `Infrastructure.AzureDevOps` and made `internal`. All call sites are in ADO-specific code only (`AzureDevOpsWorkItemRevisionSourceFactory`, `AzureDevOpsClientFactory`).

**Rationale**: Confirmed by codebase search — `OrganisationEndpoint` is constructed only in ADO code and passed to ADO internal services. No shared `Infrastructure` class or `Abstractions` interface references it after the factory interface signature change.

---

### Decision 5 — Generator determinism strategy

**Decision**: Use a seeded `System.Random(seed)` where `seed` is derived from `workItemId` for per-item field values, and from `revisionIndex` for per-revision fields. Timestamps use `DateTimeOffset.UtcNow` pinned to a fixed base date (2020-01-01T00:00:00Z) plus deterministic offsets derived from ID and revision.

**Rationale**: Deterministic output (same config → same package) is required by SC-002 and Constitution rule VII. Using seeded random per-item ensures independence between items.

---

## Phase 1: Design & Contracts

### Data Model

See [data-model.md](data-model.md) for entity definitions.

### Interface Contracts

See [contracts/](contracts/) for interface change summaries.

### Quickstart

See [quickstart.md](quickstart.md) for developer getting-started guide.

---

## Constitution Check (Post-Design Re-evaluation)

All items confirmed passing. No violations introduced by Phase 1 design decisions.

| Rule | Post-design status |
|---|---|
| Package-First (I) | ✅ Generator creates `WorkItemRevision` records fed into the existing `WorkItemExportOrchestrator` chain |
| Streaming (II) | ✅ `yield return` confirmed as only iteration pattern in `SimulatedWorkItemRevisionSource` |
| WorkItems Layout (III) | ✅ Layout is owned by `WorkItemExportOrchestrator` — Simulated does not touch it |
| Checkpointing (IV) | ✅ No new checkpoint logic; shared `ICheckpointingService` used unchanged |
| Module Isolation (V) | ✅ `WorkItemsModule` changes only remove connector-specific field reads — no new dependencies |
| Separation of Planes (VI) | ✅ No changes outside `Infrastructure*` assemblies |
| Determinism (VII) | ✅ Seeded random + fixed base date; no upgrader needed for this config change |
| ATDD-First (VIII) | ✅ Feature files will be written before implementation code per session |
| SOLID & DI (IX) | ✅ All new types use constructor injection; `AddSimulated*` extension methods follow existing pattern |

---

## Current status (Reconciled 2026-05-16)

- Implementation largely complete; status metadata has been reconciled against repository truth.
- Reconciliation scope is documentation only (Class A).

## Remaining incomplete work (IDs)

- `T076`
- `T081`

## Completed because superseded (IDs + source)

- `T008`, `T009`, `T020a`, `T021`, `T022`, `T026`, `T027` superseded by `specs/021.2-separation-of-concerns/spec.md` and current endpoint-context factory flow.
- `T063` superseded by actual fixture usage in `scenarios/queue-import-workitems-simulated-target.json`.
- `T069` superseded by current mode contract (`Migrate`) in `.agents/30-context/domains/cli-commands.md`.
- `T071` superseded by `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs`.

## Contradictions and reconciliation

- Earlier plan assumed factory signatures accepted endpoint options; runtime now resolves endpoints via injected endpoint info and `CreateAsync(CancellationToken)`.
- Earlier task expected `Mode: "Both"`; current contract uses `Mode: "Migrate"`.
- Earlier task expected `workitems-simulated-small.zip`; current scenario uses `workitems-2items-flat.zip`.

## Verification evidence

- `dotnet build DevOpsMigrationPlatform.slnx --no-incremental` succeeded.
- `dotnet test tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.csproj` succeeded (46/46).
