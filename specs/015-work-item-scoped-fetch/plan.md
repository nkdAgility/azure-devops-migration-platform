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

> **Mandatory context loading:** Confirmed — ALL files in `/.agents/guardrails/` (architecture-boundaries.md, coding-standards.md, testing-rules.md, workitems-rules.md, migration-rules.md, module-rules.md, control-plane-rules.md, test-first-workflow.md, acceptance-test-format.md), ALL files in `/.agents/context/` (migration-package-concept.md), and relevant `/docs/` files (architecture.md, modules.md, work-item-iteration-pattern.md, source-types.md) have been read.

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

**Structure Decision**: This feature adds types across the existing multi-project solution architecture. No new projects are created. The Abstractions project gets the interface and model types; the Infrastructure.AzureDevOps project gets the ADO implementation; the multi-targeted Infrastructure project gets the TFS implementation.

## Complexity Tracking

No constitution violations. All design decisions align with existing patterns and guardrails.
