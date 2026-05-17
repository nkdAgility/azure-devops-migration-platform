# Implementation Plan: Work Item Scoped Fetch Service

**Branch**: `015-work-item-scoped-fetch` | **Date**: 2026-04-18 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/015-work-item-scoped-fetch/spec.md`

## Summary

Introduce `IWorkItemFetchService` — a new abstraction in `DevOpsMigrationPlatform.Abstractions` that streams field-projected, in-process-filtered work items via `IAsyncEnumerable<FetchedWorkItem>`. It sits above `IWorkItemQueryWindowStrategy` (date-windowed WIQL) and below callers (Inventory, Dependency, Catalog). The ADO implementation batch-fetches using only declared fields; the TFS implementation uses the TFS Object Model. Both use `OrganisationEndpoint` (feature 016, complete) as the connection context. Callers are refactored to delegate field fetching through this service.

## Technical Context

**Language/Version**: C# 10+, targeting .NET 10 (net10.0); TFS implementation also targets net481 via multi-targeted project  
**Primary Dependencies**: `Microsoft.TeamFoundationServer.Client` (ADO REST), TFS Object Model (net481), `Microsoft.Extensions.DependencyInjection`  
**Storage**: N/A — this service is read-only (fetches from source APIs, does not write to package)  
**Testing**: Reqnroll.MSTest + Moq (MockBehavior.Strict); unit tests for filter evaluation and batch logic  
**Target Platform**: Windows / Linux (.NET 10); Windows-only for TFS Object Model (net481)  
**Project Type**: Library (shared service abstraction + infrastructure implementations)  
**Performance Goals**: Bounded memory during 20,000+ item scans — no full result set in memory  
**Constraints**: Azure DevOps REST API batch limit of 200 IDs per `GetWorkItemsAsync` call; WIQL result limit of 20,000 handled by window strategy  
**Scale/Scope**: 20,000+ work items per project; 2 callers refactored (discovery + dependency); 1 transitive update (catalog)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Confirmed — ALL files in `/.agents/20-guardrails/` (architecture-boundaries.md, coding-standards.md, testing-rules.md, workitems-rules.md, migration-rules.md, module-rules.md, control-plane-rules.md, test-first-workflow.md, acceptance-test-format.md), ALL files in `/.agents/30-context/` (migration-package-concept.md), and relevant `/docs/` files (architecture.md, modules.md, work-item-iteration-pattern.md, source-types.md) have been read.

- [x] **Package-First (I):** `IWorkItemFetchService` is a read-only fetch abstraction — it does not write to the package or perform source-to-target migration. Export/import paths are explicitly excluded (FR-012).
- [x] **Streaming (II):** `FetchAsync` returns `IAsyncEnumerable<FetchedWorkItem>` — one item at a time, no buffering of full result sets (FR-003).
- [x] **WorkItems Layout (III):** This feature does not touch the WorkItems folder structure. It operates at the API query level, not the package level.
- [x] **Checkpointing (IV):** This service does not perform checkpointing — it is a stateless fetch operation. Callers (discovery, dependency) manage their own cursors.
- [x] **Module Isolation (V):** Interface defined in `DevOpsMigrationPlatform.Abstractions`. Implementations in infrastructure projects. No concrete store references in caller code.
- [x] **Separation of Planes (VI):** No control plane, TUI, or CLI logic affected. The fetch service runs within the discovery/dependency analysis context only.
- [x] **Determinism (VII):** Given the same source data and scope, `FetchAsync` yields the same items in the same order (determined by `IWorkItemQueryWindowStrategy` ordering). No randomness introduced.
- [x] **ATDD-First (VIII):** The spec contains 7 acceptance scenarios across 3 user stories plus 5 edge cases. Each will be implemented via the ATDD inner loop.
- [x] **SOLID & DI (IX):** `IWorkItemFetchService` is an interface in Abstractions. `AzureDevOpsWorkItemFetchService` receives `IWorkItemQueryWindowStrategy` + `IAzureDevOpsClientFactory` via constructor injection. Registration via dedicated `Add*Services` extension method. No raw `IConfiguration` access.

## Project Structure

### Documentation (this feature)

```text
specs/015-work-item-scoped-fetch/
├── spec.md              # Feature specification
├── plan.md              # This file
├── research.md          # Phase 0 output — design decisions and rationale
├── data-model.md        # Phase 1 output — entity definitions and relationships
├── quickstart.md        # Phase 1 output — usage guide
├── contracts/           # Phase 1 output — interface contracts
│   └── IWorkItemFetchService.md
├── discrepancies.md     # Architecture doc gaps to resolve
├── checklists/
│   └── requirements.md  # Spec quality checklist
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Models/
│   │   ├── WorkItemFetchScope.cs          # NEW — query scope record
│   │   ├── FetchedWorkItem.cs             # NEW — result record
│   │   ├── WorkItemFieldFilterOptions.cs  # NEW — filter placeholder (feature 014)
│   │   ├── FilterOperator.cs              # NEW — filter operator enum (feature 014)
│   │   └── OrganisationEndpoint.cs        # EXISTING (feature 016)
│   └── Services/
│       └── IWorkItemFetchService.cs       # NEW — interface
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   ├── Services/
│   │   ├── AzureDevOpsWorkItemFetchService.cs           # NEW — ADO implementation
│   │   ├── AzureDevOpsWorkItemDiscoveryService.cs       # REFACTOR — delegate to IWorkItemFetchService
│   │   └── AzureDevOpsDependencyAnalysisService.cs      # REFACTOR — delegate to IWorkItemFetchService
│   ├── InventoryServiceCollectionExtensions.cs           # MODIFY — register IWorkItemFetchService
│   └── DependencyServiceCollectionExtensions.cs          # MODIFY — register IWorkItemFetchService
│
├── DevOpsMigrationPlatform.Infrastructure/
│   └── Services/
│       └── CatalogService.cs              # EXISTING — transitive update (no logic change)
│
└── DevOpsMigrationPlatform.CLI.TfsExport/              # net481-only (TFS OM types)
    └── Services/
        └── TfsWorkItemFetchService.cs     # NEW — TFS Object Model implementation

tests/
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/
│   └── Services/
│       ├── AzureDevOpsWorkItemFetchServiceTests.cs      # NEW — unit tests
│       └── WorkItemFieldFilterEvaluatorTests.cs         # NEW — filter logic tests
└── DevOpsMigrationPlatform.CLI.TfsExport.Tests/         # net481-only test project
    └── Services/
        └── TfsWorkItemFetchServiceTests.cs              # NEW — TFS implementation tests
```

**Structure Decision (reconciled)**: Later architecture work (`specs/021.2-separation-of-concerns`) moved these contracts and tests into split assemblies/projects (`Abstractions.Agent`, `Infrastructure.Agent.Tests`, `Infrastructure.TfsObjectModel`). The implementation is present, but several original paths in this plan are now historical.

## Complexity Tracking

Historical note: this plan predates later constitution tightening and separation-of-concerns refactors. Reconciliation findings are recorded below.

## Current status

- Reconciled on 2026-05-16 against current repository layout.
- Plan remains directionally valid but contains stale paths and one superseded behaviour statement from the spec chain.

## Remaining incomplete work (IDs)

- T013, T021, T028, T029 (see `tasks.md` evidence notes).

## Completed because superseded (IDs + source)

- `specs/021.2-separation-of-concerns` superseded original file/project paths for T001, T002, T003, T004, T005, T006, T009, T010, T012, T017, T019, T030.
- `specs/014-field-filter-scope` superseded the earlier “do not change export path” intent tied to FR-012.

## Contradictions and reconciliation

- Original source/test tree in this plan reflects pre-split assemblies and does not match current repository structure.
- TFS testing intent in this plan assumed dedicated tests; current repository has no dedicated `TfsWorkItemFetchService` test file, so completion is not claimed for that task.
- Full-suite green evidence is not available in this spec folder; reconcile as incomplete verification.

## Verification evidence

- Current implementation paths verified:
  - `src/DevOpsMigrationPlatform.Abstractions.Agent/Export/IWorkItemFetchService.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Export/AzureDevOpsWorkItemFetchService.cs`
  - `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Export/TfsWorkItemFetchService.cs`
- Current tests/docs verified:
  - `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Services/AzureDevOpsWorkItemFetchServiceTests.cs`
  - `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Services/WorkItemFieldFilterEvaluatorTests.cs`
  - `docs/work-item-iteration-guide.md`, `docs/module-development-guide.md`, `docs/architecture.md`

