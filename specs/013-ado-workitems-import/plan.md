# Implementation Plan: Azure DevOps Work Items Import

**Branch**: `013-ado-workitems-import` | **Date**: 2026-04-15 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/013-ado-workitems-import/spec.md`

## Summary

Implement the `WorkItemsModule.ImportAsync` method to replace the current `NotSupportedException` stub. The import reads from an exported artefact package via `IArtefactStore.EnumerateAsync("WorkItems/")` in lexicographic order and replays each revision folder through four staged operations (Create → Fields → Links → Attachments) into a target Azure DevOps project. All Azure DevOps SDK write calls are wrapped behind a new `IWorkItemImportTarget` abstraction. Cursor-based checkpointing enables full resumability. A SQLite-backed `idmap.db` provides indexed source-to-target ID mapping with pluggable `WorkItemResolutionStrategy` extensions for seeding the map from the target.

## Technical Context

**Language/Version**: C# 10+ / .NET 10.0  
**Primary Dependencies**: `Microsoft.TeamFoundationServer.Client` 20.x (Azure DevOps REST SDK), `Microsoft.Data.Sqlite` (new — for `idmap.db`), `System.Text.Json`, `Polly` (retry with back-off)  
**Storage**: `IArtefactStore` (reads package), `IStateStore` (cursor), SQLite (`Checkpoints/idmap.db` — package-local indexed store, not a control-plane database)  
**Testing**: Reqnroll.MSTest + Moq (MockBehavior.Strict)  
**Target Platform**: Windows / Linux (cross-platform .NET 10)  
**Project Type**: Module implementation within existing migration platform  
**Performance Goals**: 1,000 work items with revision history imported in under 30 minutes on standard connection; constant memory usage regardless of package size  
**Constraints**: Streaming — one revision folder in memory at a time; no in-memory sorting of `EnumerateAsync` results; attachment binaries streamed, never buffered  
**Scale/Scope**: Tested at 20,000+ revision folders; single target project per import job  

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Confirmed — ALL files in `/.agents/guardrails/` (system-architecture, coding-standards, testing-standards, workitems-rules, migration-rules, module-template, aspire-integration, atdd-workflow, acceptance-test-format), ALL files in `/.agents/context/` (package-format, workitems-format, import-streaming, checkpointing, identity-and-mapping, job-contract), and relevant `/docs/` files (architecture, modules, work-item-iteration-pattern, cli, configuration) have been read.

- [x] **Package-First (I):** Import reads from the on-disk package via `IArtefactStore`. No direct source-to-target migration. The Azure DevOps target SDK calls are behind `IWorkItemImportTarget` — the module itself only touches `IArtefactStore` for reads.
- [x] **Streaming (II):** Import processes one revision folder at a time via `IArtefactStore.EnumerateAsync("WorkItems/")`. No list/array accumulation. No in-memory sort. The `EnumerateAsync` implementation already returns lexicographic order.
- [x] **WorkItems Layout (III):** Folder structure `WorkItems/yyyy-MM-dd/<ticks>-<workItemId>-<revisionIndex>/` is preserved as-is. Import only reads — never writes to or modifies the WorkItems layout. Comment folders (`<ticks>-<workItemId>-c<commentId>/`) are interleaved chronologically and processed alongside revision folders.
- [x] **Checkpointing (IV):** Cursor file at `Checkpoints/workitems.cursor.json`. Five canonical stage values: `CreatedOrUpdated`, `AppliedFields`, `AppliedLinks`, `UploadedAttachments`, `Completed`. Resume begins from the next stage after `lastProcessed`. No watermark tables.
- [x] **Module Isolation (V):** All persistence through `IArtefactStore` (package reads) and `IStateStore` (cursor writes). ID map via `IIdMapStore` abstraction. Identity resolution via `IIdentityMappingService`. No concrete store references.
- [x] **Separation of Planes (VI):** All import logic lives in the Job Engine (module + infrastructure). CLI remains a stub-free passthrough that submits a `MigrationJob`. No migration logic in CLI, TUI, or control plane.
- [x] **Determinism (VII):** Re-running import is idempotent — cursor and `idmap.db` prevent duplicate creation. No schema breaking changes (import consumes existing `revision.json` format).
- [x] **ATDD-First (VIII):** The spec contains 6 user stories with 23 Given/When/Then acceptance scenarios. Each scenario will be implemented via the ATDD inner loop — one scenario per session per commit.
- [x] **SOLID & DI (IX):** All new services (`IWorkItemImportTarget`, `IIdMapStore`, `IWorkItemResolutionStrategy`, `WorkItemImportOrchestrator`) receive dependencies via constructor injection. Options classes are sealed with `init`-only properties and `SectionName` constants. Interfaces in `Abstractions`. Registration via `AddWorkItemImportServices()`.

## Project Structure

### Documentation (this feature)

```text
specs/013-ado-workitems-import/
├── plan.md              # This file
├── research.md          # Phase 0 output
├── data-model.md        # Phase 1 output
├── quickstart.md        # Phase 1 output
├── contracts/           # Phase 1 output
│   └── IWorkItemImportTarget.md
├── discrepancies.md     # Architecture discrepancies (from speckit.specify)
└── tasks.md             # Phase 2 output (speckit.tasks)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Services/
│   │   ├── IWorkItemImportTarget.cs          # NEW — target write abstraction
│   │   ├── IWorkItemImportTargetFactory.cs   # NEW — factory for target instances
│   │   ├── IIdMapStore.cs                    # NEW — idmap.db abstraction
│   │   └── IWorkItemResolutionStrategy.cs    # NEW — pluggable ID resolution
│   ├── Models/
│   │   ├── ImportedWorkItemResult.cs         # NEW — result from target write
│   │   └── IdMapEntry.cs                     # NEW — source→target mapping record
│   └── Options/
│       └── WorkItemImportOptions.cs          # NEW — import-specific options
│
├── DevOpsMigrationPlatform.Infrastructure/
│   ├── Import/
│   │   ├── WorkItemImportOrchestrator.cs     # NEW — streaming import loop
│   │   ├── RevisionFolderProcessor.cs        # NEW — 4-stage processing per folder
│   │   └── SqliteIdMapStore.cs               # NEW — SQLite IIdMapStore implementation
│   └── Modules/
│       └── WorkItemsModule.cs                # MODIFIED — ImportAsync implemented
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   ├── Import/
│   │   ├── AzureDevOpsWorkItemImportTarget.cs       # NEW — SDK wrapper
│   │   ├── AzureDevOpsWorkItemImportTargetFactory.cs # NEW — factory
│   │   ├── TargetFieldResolutionStrategy.cs         # NEW — WIQL custom field
│   │   ├── TargetHyperlinkResolutionStrategy.cs     # NEW — hyperlink scan
│   │   └── NullResolutionStrategy.cs                # NEW — no target query fallback
│   └── ImportServiceCollectionExtensions.cs          # NEW — AddAzureDevOpsImportServices()
│
├── DevOpsMigrationPlatform.CLI.Migration/
│   └── Commands/
│       └── QueueCommand.cs                   # MODIFIED — enable import mode

tests/
├── DevOpsMigrationPlatform.Infrastructure.Tests/
│   └── Import/
│       ├── WorkItemImportOrchestratorTests.cs       # NEW
│       ├── RevisionFolderProcessorTests.cs          # NEW
│       ├── SqliteIdMapStoreTests.cs                 # NEW
│       └── WorkItemImportOrchestratorSteps.cs       # NEW (Reqnroll)
│       └── WorkItemImportOrchestratorContext.cs      # NEW (Reqnroll)
│
features/
├── import/
│   └── work-items/
│       ├── revisions/
│       │   └── streaming-replay.feature             # NEW
│       ├── attachments/
│       │   └── import-attachments.feature           # NEW
│       └── links/
│           └── import-links.feature                 # NEW
├── platform/
│   └── checkpointing/
│       └── import-cursor-resume.feature             # NEW

scenarios/
└── import-ado-workitems-single-project.json         # NEW — test scenario config
```

**Structure Decision**: Follows the existing project layout. New import code is placed in the `Import/` subdirectory of both `Infrastructure` and `Infrastructure.AzureDevOps`, mirroring the existing `Export/` layout. The `IWorkItemImportTarget` abstraction mirrors `IWorkItemRevisionSource` in Abstractions. SQLite ID map implementation lives in `Infrastructure` (not in `Infrastructure.AzureDevOps`) because it is connector-agnostic.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| SQLite dependency (`Microsoft.Data.Sqlite`) added to `Infrastructure` | `idmap.db` requires indexed lookup for 20k+ mappings. The spec mandates SQLite for portable, single-file, indexed storage inside the package. | JSON-based `idmap.json` was considered but is too slow for O(1) lookup at scale. The checkpointing guardrail prohibits databases for the cursor, but SQLite is explicitly permitted for the ID map (package-local storage, not control-plane). |
| `IWorkItemImportTarget` is a new single-use abstraction | FR-018 mandates wrapping all Azure DevOps SDK write calls behind an abstraction. This is the import-side mirror of `IWorkItemRevisionSource`. | Direct SDK calls in module code would violate system-architecture rule 21 and coding-standards rule 12 (SDK calls behind abstractions). The abstraction enables mocking for tests and future non-ADO targets. |
