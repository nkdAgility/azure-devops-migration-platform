# Implementation Plan: Simulated Data Source for End-to-End Migration Testing

**Branch**: `copilot/simulate-migration-data` | **Date**: 2026-04-09 | **Spec**: [specs/008-simulated-data-source/spec.md](../../008-simulated-data-source/spec.md)  
**Input**: Feature specification from `/specs/008-simulated-data-source/spec.md`

## Summary

Introduce a `Simulated` source and target type that generates deterministic, schema-conformant work item data without any network access. The simulated source implements `IWorkItemRevisionSource` / `IWorkItemRevisionSourceFactory`, and a new `IWorkItemImportSink` abstraction decouples the import target so that both ADO and Simulated targets can be injected. A new `DevOpsMigrationPlatform.Infrastructure.Simulated` project houses all generated-data logic. All existing platform components (Job Engine, modules, TUI, checkpointing, progress streaming) work unmodified — only service registrations differ when `source.type` or `target.type` is `"Simulated"`. The feature also delivers `WorkItemsModule.ImportAsync` (currently `NotImplementedException`) as a prerequisite for simulated end-to-end testing.

## Technical Context

**Language/Version**: C# 12 / .NET 10  
**Primary Dependencies**: `System.Random` (seeded, deterministic generation), `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Options`, `Reqnroll.MSTest` (acceptance tests)  
**Storage**: Package on `file:///` via `IArtefactStore` / `IStateStore`; cursor files in `Checkpoints/`  
**Testing**: MSTest v3 with Reqnroll; `[TestCategory("SystemTest")]` for end-to-end; `[TestCategory("Unit")]` for all new services  
**Target Platform**: .NET 10 cross-platform (Windows / Linux / macOS; CI on Linux)  
**Project Type**: Infrastructure library + CLI extension + system test  
**Performance Goals**: 25,000 work items exported in < 10 minutes on a developer workstation; 100-item system test completes in < 5 minutes in CI  
**Constraints**: Zero external network calls during any simulated run; no buffering all revisions into memory; streaming `IAsyncEnumerable<WorkItemRevision>`  
**Scale/Scope**: Verified at 25,000 work items; default scenario is 25k; system test uses 100 items for speed

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading confirmed:** All files in `/.agents/guardrails/` (architecture-boundaries.md, workitems-rules.md, migration-rules.md, coding-standards.md, module-rules.md) and all files in `/.agents/context/` (migration-package-concept.md, workitems-format-summary.md, checkpointing-summary.md, artefact-store.md, job-lifecycle.md, identity-and-mapping.md, import-streaming.md, cli-commands.md) and relevant `/docs/` files (architecture.md, source-types.md, configuration.md, modules.md, tui.md) were read before completing this gate.

- [x] **Package-First (I):** `SimulatedWorkItemRevisionSource` yields `WorkItemRevision` objects to `WorkItemExportOrchestrator`, which writes exclusively via `IArtefactStore`. The simulated target (`SimulatedWorkItemImportSink`) reads from the package via `IArtefactStore` — no direct source-to-target path.
- [x] **Streaming (II):** `SimulatedRevisionStream` is an `IAsyncEnumerable<WorkItemRevision>` that yields one revision at a time; `WorkItemsModule.ImportAsync` (newly implemented) reads one revision folder at a time via `IArtefactStore.EnumerateAsync`. No in-memory list of all revisions.
- [x] **WorkItems Layout (III):** The simulated source produces `WorkItemRevision` records processed by the existing `WorkItemExportOrchestrator`, which writes `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/revision.json`. No path logic in the simulated source; layout derived by the orchestrator as always.
- [x] **Checkpointing (IV):** `WorkItemsModule.ExportAsync` already uses `CheckpointingService` with `Checkpoints/workitems.cursor.json`. `ImportAsync` will use the same service. The simulated source introduces no new cursor files.
- [x] **Module Isolation (V):** All persistence in `SimulatedWorkItemImportSink` goes through `IArtefactStore`. `SimulatedWorkItemRevisionSourceFactory` is in `Infrastructure.Simulated` — no reference to `FileSystemArtefactStore` or `AzureBlobArtefactStore`. Identity flows through `IIdentityMappingService`.
- [x] **Separation of Planes (VI):** No changes to control plane, TUI, or CLI migration logic. The simulated source/target live entirely in `Infrastructure.Simulated` and are wired by the agent's DI setup. The Job Engine remains UI-free and `IProgressSink`-only.
- [x] **Determinism (VII):** `SimulatedWorkItemRevisionSource` uses `System.Random(seed)` for all generated values, guaranteeing byte-identical `revision.json` content for the same `seed` + `workItemCount`. The seed is written into `manifest.json` so any run can be reproduced. `configHash` includes the simulated config parameters.
- [x] **ATDD-First (VIII):** All four user stories in the spec have Given/When/Then acceptance scenarios. Each scenario is implemented via the ATDD inner loop (Specification → Test Gen → Implementation → Review).
- [x] **SOLID & DI (IX):** `SimulatedSourceOptions` and `SimulatedTargetOptions` are `sealed` with `init`-only properties and `public static string SectionName`. All simulated services receive dependencies via constructor injection. `SimulatedServiceCollectionExtensions` provides `AddSimulatedWorkItemExport` and `AddSimulatedWorkItemImport` registration methods.

## Project Structure

### Documentation (this feature)

```text
specs/copilot/simulate-migration-data/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
└── contracts/
    ├── simulated-source-config.md    # SimulatedSourceOptions contract
    └── simulated-target-config.md    # SimulatedTargetOptions contract
```

### Source Code Layout

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   └── Services/
│       └── IWorkItemImportSink.cs              # NEW — decoupled import target interface
│
├── DevOpsMigrationPlatform.Infrastructure.Simulated/   # NEW PROJECT
│   ├── DevOpsMigrationPlatform.Infrastructure.Simulated.csproj
│   ├── Options/
│   │   ├── SimulatedSourceOptions.cs           # seed, workItemCount, projectCount, etc.
│   │   └── SimulatedTargetOptions.cs           # validateOnWrite, failOnFirstError
│   ├── Generation/
│   │   ├── SimulatedRevisionStream.cs          # IAsyncEnumerable<WorkItemRevision>
│   │   └── SimulatedIdentitySet.cs             # Fixed set of synthetic user identities
│   ├── Services/
│   │   ├── SimulatedWorkItemRevisionSource.cs         # implements IWorkItemRevisionSource
│   │   ├── SimulatedWorkItemRevisionSourceFactory.cs  # implements IWorkItemRevisionSourceFactory
│   │   ├── SimulatedWorkItemDiscoveryService.cs       # implements IWorkItemDiscoveryService
│   │   └── SimulatedWorkItemImportSink.cs             # implements IWorkItemImportSink
│   └── SimulatedServiceCollectionExtensions.cs
│
├── DevOpsMigrationPlatform.Infrastructure/
│   ├── Modules/
│   │   └── WorkItemsModule.cs                  # MODIFY — implement ImportAsync with IWorkItemImportSink
│   └── (no other changes)
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   └── Services/
│       └── AzureDevOpsWorkItemImportSink.cs    # NEW — implements IWorkItemImportSink for ADO target
│
└── DevOpsMigrationPlatform.MigrationAgent/
    └── (source-type-aware DI wiring — registers Simulated or ADO factories by job.Source.Type)

tests/
└── DevOpsMigrationPlatform.CLI.Migration.Tests/
    └── SystemTests/
        └── SimulatedMigrationSystemTests.cs   # [TestCategory("SystemTest")]

scenarios/
└── migrate-simulated-25k.json                 # NEW — ready-to-run simulated 25k scenario

.vscode/
└── launch.json                                # ADD — "🧪 Migrate: Simulated 25k" debug profile
```

**Structure Decision**: A new dedicated project `Infrastructure.Simulated` isolates all generated-data logic. It depends only on `Abstractions` (for `IWorkItemRevisionSource`, `IWorkItemRevisionSourceFactory`, `IWorkItemImportSink`, `IWorkItemDiscoveryService`). This prevents any simulated-data code from leaking into production infrastructure. A new `IWorkItemImportSink` abstraction in `Abstractions` provides the injection point for both the ADO import target (new `AzureDevOpsWorkItemImportSink`) and the simulated target (`SimulatedWorkItemImportSink`). `WorkItemsModule` is modified to inject and use `IWorkItemImportSink`, completing the currently deferred import path.

## Complexity Tracking

No constitution violations. All design decisions are consistent with the existing architecture.

| Decision | Why Needed | Simpler Alternative Rejected Because |
|----------|------------|--------------------------------------|
| New `Infrastructure.Simulated` project | Isolates test-only generation code from production infrastructure | Adding to `Infrastructure.AzureDevOps` would couple a test-only concept to a production connector project, violating module isolation |
| New `IWorkItemImportSink` abstraction | Decouples `WorkItemsModule.ImportAsync` from the ADO-specific target writer | Without abstraction, the module cannot be unit-tested and the simulated target cannot be injected |
| `WorkItemsModule.ImportAsync` implemented here | Required prerequisite for simulated end-to-end migration (Story 3) | Deferring further leaves the import pipeline untestable and blocks all E2E system tests |
