# Implementation Plan: Work Items Inventory Command

**Branch**: `003-inventory-workitems` | **Date**: 2026-04-04 | **Spec**: [spec.md](spec.md)

## Summary

Implement a `devopsmigration discovery inventory` CLI command that counts work items and revisions per project for one or more Azure DevOps organisations or TFS collections. Configuration is entirely file-driven (no bare credential CLI args). Two config modes are supported: `source`-based (single migration config reuse) and `organisations`-based (multi-org tooling roster). Work item counting uses a date-window strategy that halves the window when the 20k query limit is approached. The TFS path delegates to the existing `.NET 4.8` subprocess via `ExternalToolRunner`. Results stream as a live Spectre.Console table and are saved to `discovery-summary.csv`.

---

## Technical Context

**Language/Version**: C# 10+, .NET 10 (CLI.Migration, Infrastructure.AzureDevOps); .NET 4.8 (CLI.TfsMigration)
**Primary Dependencies**: Spectre.Console.Cli (CLI), Microsoft.TeamFoundation.* VSTSDK (TFS subprocess), Microsoft.VisualStudio.Services.Client (ADO REST), MSTest + Moq (tests)
**Storage**: None — read-only pre-flight operation; no package, no checkpoint, no database
**Testing**: MSTest + `MockBehavior.Strict` Moq; Reqnroll for acceptance scenarios
**Target Platform**: CLI tool (`devopsmigration.exe`) — Windows + Linux
**Project Type**: CLI command + supporting services
**Performance Goals**: First table update within 5 s of command start; a 100k-work-item project counted fully without query error
**Constraints**: No in-memory accumulation of all work item IDs; no control plane job submission; no package writes; TFS OM never loaded in the .NET 10 process
**Scale/Scope**: Up to ~50 orgs in roster mode; up to ~500k total work items per run

---

## Constitution Check

> ALL files in `/.agents/20-guardrails/`, relevant `/.agents/30-context/` files, and `/docs/` files have been read. See Architecture References table in `spec.md`.

- [x] **Package-First (I):** Inventory is a read-only pre-flight operation. It writes no package and calls no `IArtefactStore`. Not applicable — no violation possible.
- [x] **Streaming (II):** No revision folders or work item revisions are loaded into memory. Query windows are processed one at a time. The `IAsyncEnumerable<InventoryProgressEvent>` pattern ensures lazy streaming.
- [x] **WorkItems Layout (III):** Not applicable — inventory writes no package.
- [x] **Checkpointing (IV):** Not applicable — inventory writes no cursors or checkpoints.
- [x] **Module Isolation (V):** `IInventoryService` is defined in `Abstractions`. `AzureDevOpsInventoryService` in `Infrastructure.AzureDevOps` is not referenced directly by CLI command code; it is resolved via DI.
- [x] **Separation of Planes (VI):** Inventory does not submit a job to the control plane. The CLI command is a direct read-only operation. TFS delegation uses `ExternalToolRunner` — no coupling to TFS OM in the .NET 10 process. No UI coupling in inventory service logic.
- [x] **Determinism (VII):** Config schema version is `1.0` (same as migration config). No breaking schema change in this feature — `authentication` is additive. The `organisations` key is new and opt-in; no upgrader required for existing configs that lack it.
- [x] **ATDD-First (VIII):** All four user stories have Given/When/Then acceptance scenarios. Each will be implemented one scenario per ATDD session per commit.
- [x] **SOLID & DI (IX):** `IInventoryService` defined in Abstractions. `InventoryOptions` bound via `IOptions<InventoryOptions>` with `SectionName`. `AzureDevOpsInventoryService` injected into command via constructor. Registration via `AddInventoryServices()` extension.

---

## Project Structure

### Documentation (this feature)

```text
specs/003-inventory-workitems/
├── plan.md              ← this file
├── spec.md
├── research.md          ← Phase 0 complete
├── data-model.md        ← Phase 1 complete
├── quickstart.md        ← Phase 1 complete
├── discrepancies.md
├── contracts/
│   └── inventory-contracts.md  ← Phase 1 complete
└── checklists/
    └── requirements.md
```

### Source Code — affected projects

```text
src/
├── DevOpsMigrationPlatform.Abstractions/          ← net481;net10.0
│   ├── Options/
│   │   ├── MigrationEndpointOptions.cs            MODIFY — add Authentication property
│   │   ├── EndpointAuthenticationOptions.cs       NEW
│   │   ├── OrganisationEntry.cs                   NEW
│   │   └── InventoryOptions.cs                    NEW
│   ├── Models/
│   │   ├── InventorySummary.cs                    NEW
│   │   └── InventoryProgressEvent.cs              NEW
│   ├── Services/
│   │   └── IInventoryService.cs                   NEW
│   └── Utilities/
│       └── TokenResolver.cs                       NEW
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/   ← net10.0
│   └── Services/
│       ├── WorkItemQueryWindowStrategy.cs         NEW — shared date-window algorithm
│       ├── CatalogService.cs                      MODIFY — add WorkItemQueryWindowStrategy dependency
│       └── AzureDevOpsInventoryService.cs         NEW — uses WorkItemQueryWindowStrategy
│
├── DevOpsMigrationPlatform.CLI.Migration/         ← net10.0
│   ├── Commands/
│   │   ├── AzureDevOpsSettings.cs                 DELETE (bare credential flags — violation)
│   │   └── Discovery/
│   │       ├── InventoryCommand.cs                REWRITE — config-driven, two modes
│   │       └── TfsInventoryProcessAdapter.cs      NEW — spawns TFS subprocess inventory subcommand
│   └── Program.cs                                 MODIFY — register InventoryOptions, IInventoryService
│                                                           remove AzureDevOpsSettings references
│
├── DevOpsMigrationPlatform.CLI.TfsMigration/      ← net481
│   ├── Commands/
│   │   └── InventoryCommand.cs                    NEW — `inventory` subcommand entry point
│   ├── TfsInventoryAgent.cs                       NEW — parallel of TfsExportAgent, uses WorkItemStoreExtensions
│   └── Program.cs                                 MODIFY — register `inventory` subcommand

tests/
├── DevOpsMigrationPlatform.Infrastructure.Tests/
│   └── Inventory/
│       ├── AzureDevOpsInventoryServiceTests.cs    NEW — date-window strategy unit tests
│       └── TokenResolverTests.cs                  NEW — $ENV: resolution unit tests
└── DevOpsMigrationPlatform.ControlPlane.Tests/    (no changes)
```

**Structure Decision**: Single CLI command + infrastructure service. No new projects. All new production code lands in existing projects at their existing target frameworks.

---

## Architecture Alignment Notes

The following discrepancies between the spec and the current `docs/` files will be patched as part of this feature's implementation commits (per `discrepancies.md`):

1. `docs/cli-guide.md` — add `inventory work-items` to the Commands table and usage examples.
2. `docs/configuration-reference.md` — add `authentication` block to `source`/`target` schema; add `organisations` key; document two config modes and validation rules.
3. `docs/capabilities-guide.md` — add inventory subsection to both AzureDevOpsServices and TeamFoundationServer sections.
4. `docs/tfs-exporter.md` — add Inventory Mode section documenting the `inventory` subcommand and NDJSON protocol.

---

## Implementation Sequence (ATDD order)

Each row is one ATDD session (one scenario, one commit).

| # | User Story | Scenario | Key deliverable |
|---|---|---|---|
| 1 | US-1 | Config validation — `organisations` + `source` mutual exclusion | `InventoryOptions` model + validator; `InventoryCommand` settings refactor |
| 2 | US-1 | Single-project count via ADO (live table updates) | `IInventoryService`, `AzureDevOpsInventoryService` (basic), `InventoryCommand` Mode 1 |
| 3 | US-3 | Window halves when query hits 20k limit | `WorkItemQueryWindowStrategy` (shared); `AzureDevOpsInventoryService` uses it; `TokenResolver` |
| 4 | US-3 | Window advances backward until zero results | Completion detection in date-window loop |
| 5 | US-1 | CSV output on completion | `WriteCsv` in `InventoryCommand`; `InventorySummary` |
| 6 | US-4 | `organisations` mode — disabled entries skipped | `InventoryCommand` Mode 2 fan-out loop |
| 7 | US-4 | `organisations` mode — `projects` filter | Per-entry project restriction in Mode 2 |
| 8 | US-2 | TFS subprocess `inventory` subcommand spawned | `TfsInventoryProcessAdapter`, `TfsInventoryAgent`, new subprocess command |
| 9 | US-2 | TFS date-window narrowing via `WorkItemStoreExtensions` | `TfsInventoryAgent` using existing `QueryCountAllByDateChunk` |
| 10 | US-2 | TFS NDJSON events converted to live-table updates | End-to-end subprocess → table flow |

---

## Complexity Tracking

> No constitution gate violations requiring justification in this plan.

---

## Current status

Plan implementation is **historical and mostly superseded** by the queue/control-plane/agent runtime architecture now present in the repository.

## Remaining incomplete work (IDs)

None. After reconciliation, no task is marked `— Status: incomplete`.

## Completed because superseded (IDs + source)

Superseded task set: `T003, T006-T008, T010-T038, T041`.

Primary supersession sources:

- `specs/025.1-fold-to-job/tasks.md` (queue/control-plane dispatch)
- `specs/028.2-job-execution-by-task/tasks.md` (agent task-plan ownership)
- `specs/033-runtime-state-categories/tasks.md` (inventory runtime outputs/cadence)
- `specs/025-agent-config-package/tasks.md` (configuration/auth model consolidation)
- `specs/020-resumable-batching-cursor/tasks.md` (windowing/resume behavioral evolution)

## Contradictions and reconciliation

- Direct CLI discovery command assumptions were replaced by `queue` job submission.
- TFS subprocess adapter assumptions were replaced by `TfsMigrationAgent` worker execution.
- `discovery-summary.csv` output assumptions were replaced by runtime `inventory.csv`/`inventory.json` package artifacts.

## Verification evidence

- `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs` (queue-based command surface)
- `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs` (inventory artifact output + resume)
- `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/InventoryServiceCollectionExtensions.cs` and `.../Factories/InventoryServiceFactory.cs` (DI/runtime wiring)
- `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/*` and `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs` (current verification surface)


