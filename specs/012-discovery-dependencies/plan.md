# Implementation Plan: Discovery Dependency Analysis

**Branch**: `012-discovery-dependencies` | **Date**: 2026-04-14 | **Spec**: [spec.md](spec.md)  
**Input**: Feature specification from `/specs/012-discovery-dependencies/spec.md`

## Summary

Introduce a `devopsmigration discovery dependencies` command that analyses work items in configured Azure DevOps organisations / TFS collections and produces a CSV report of all external outbound links (`CrossProject` and `CrossOrganisation`). Same-project links are silently discarded. The command runs locally (no control plane job submission), reuses `DiscoveryOptions` for config, supports an optional WIQL filter, and streams results to avoid loading all links into memory.

**Technical approach**: A new `IDependencyDiscoveryService` (platform-agnostic orchestrator in `Infrastructure`) dispatches to per-source-type `IWorkItemLinkAnalysisService` implementations, resolved by keyed DI using the org `Type` string. Implementations: `AzureDevOpsDependencyAnalysisService` (ADO REST batch API), `SimulatedDependencyAnalysisService` (deterministic synthetic records), and `TfsDependencyProcessAdapter` (TFS subprocess delegation — registered only in the `CLI.Migration` DI host). All interfaces live in `Abstractions`. The `DependencyCommand` in `CLI.Migration` iterates the `IAsyncEnumerable`, writes CSV rows via `StreamWriter` incrementally, and renders a Spectre.Console progress table.

## Technical Context

**Language/Version**: C# 12 / .NET 10  
**Primary Dependencies**: Spectre.Console.Cli (CLI layer), Azure DevOps REST SDK, Microsoft.Extensions.DependencyInjection, Microsoft.Extensions.Options  
**Storage**: No artefact store used — discovery command. CSV written directly to local filesystem via `StreamWriter`.  
**Testing**: MSTest + Moq; `[TestCategory("SystemTest")]` + `CliRunner` for system tests  
**Target Platform**: Windows / Linux / macOS (.NET 10 host); TFS subprocess is Windows-only (.NET 4.8)  
**Project Type**: New CLI command within existing `devopsmigration` binary  
**Performance Goals**: Process ≥50,000 work items × 10 links without OOM (SC-003)  
**Constraints**: No in-memory accumulation of records; `SemaphoreSlim` for bounded concurrency; CSV row-by-row streaming; WIQL error must exit before any network calls  
**Scale/Scope**: Up to 50 k source work items × avg 10 links (500 k link classifications); single + multi-project; multi-org

## Constitution Check

*GATE: All guardrails context loaded and all principles reviewed.*

> Confirmed: all files under `/.agents/guardrails/`, `/.agents/context/`, `docs/cli.md`, `docs/source-types.md`, `docs/architecture.md`, `docs/modules.md`, and `agents.md` have been read before this plan was written.

- [N/A] **Package-First (I):** Does not apply — discovery commands do not read or write a migration package. No `IArtefactStore` is involved.
- [N/A] **Streaming (II):** Import revisions not applicable. Memory safety is maintained via `IAsyncEnumerable` + `StreamWriter` row-by-row. All in-memory accumulation of link records is forbidden.
- [N/A] **WorkItems Layout (III):** Does not apply — no package writing.
- [N/A] **Checkpointing (IV):** No checkpoint support for discovery commands (consistent with `discovery inventory`).
- [N/A] **Module Isolation (V):** Not a migration module. `IArtefactStore` / `IStateStore` are not used.
- [✓] **Separation of Planes (VI):** Command submits no `MigrationJob`, contains no migration execution logic. TFS delegation via `TfsDependencyProcessAdapter` (implements `IWorkItemLinkAnalysisService`) registered only in `CLI.Migration` host — `Infrastructure` never references `CLI.Migration`.
- [✓] **Determinism (VII):** Simulated source uses seeded `Random`. Additive change only — no breaking schema modification.
- [✓] **ATDD-First (VIII):** All three user stories have Given/When/Then acceptance scenarios. Feature files created in Phase 2 before any implementation.
- [✓] **SOLID & DI (IX):** All new services use constructor injection. All three `IWorkItemLinkAnalysisService` implementations registered as keyed services by org `Type` string. `DiscoveryOptions` extended with `MaxConcurrency` (additive). Interfaces in `DevOpsMigrationPlatform.Abstractions`. DI registration in `DependencyServiceCollectionExtensions`.

**Post-design re-check**: Constitution Check passes.

## Project Structure

### Documentation (this feature)

```text
specs/012-discovery-dependencies/
├── plan.md                           ✅ This file
├── research.md                       ✅ Phase 0 output
├── data-model.md                     ✅ Phase 1 output
├── quickstart.md                     ✅ Phase 1 output
├── contracts/
│   └── dependency-command.md         ✅ Phase 1 output
└── tasks.md                          ✅ Phase 2 output
```

### Source Code

```text
src/
├── DevOpsMigrationPlatform.Abstractions/
│   ├── Models/
│   │   ├── LinkScope.cs                                    NEW
│   │   ├── TargetStatus.cs                                 NEW
│   │   ├── DependencyRecord.cs                             NEW
│   │   ├── DependencySummary.cs                            NEW
│   │   └── DependencyProgressEvent.cs                      NEW
│   ├── Services/
│   │   ├── IDependencyDiscoveryService.cs                  NEW
│   │   └── IWorkItemLinkAnalysisService.cs                 NEW
│   └── Options/
│       └── DiscoveryOptions.cs                             MODIFIED (+MaxConcurrency)
│
├── DevOpsMigrationPlatform.Infrastructure/
│   └── Services/
│       ├── DependencyDiscoveryService.cs                   NEW
│       └── SimulatedDependencyAnalysisService.cs           NEW
│
├── DevOpsMigrationPlatform.Infrastructure.AzureDevOps/
│   ├── Services/
│   │   └── AzureDevOpsDependencyAnalysisService.cs         NEW
│   └── DependencyServiceCollectionExtensions.cs            NEW
│
└── DevOpsMigrationPlatform.CLI.Migration/
    ├── Commands/Discovery/
    │   └── DependencyCommand.cs                            NEW
    ├── TfsDependencyProcessAdapter.cs                      NEW
    └── Program.cs                                          MODIFIED

scenarios/
└── discovery-dependency-ado-single-project.json            NEW

.vscode/
└── launch.json                                             MODIFIED (+2 dependency launch entries)

features/
├── cli/discovery/
│   └── dependency-command-wiring.feature                   NEW
└── inventory/work-items/
    └── dependency-analysis.feature                         NEW

tests/
├── DevOpsMigrationPlatform.CLI.Migration.Tests/
│   └── Commands/Discovery/
│       └── DependencyCommandTests.cs                       NEW
├── DevOpsMigrationPlatform.Infrastructure.Tests/
│   └── Dependencies/
│       ├── AzureDevOpsDependencyAnalysisServiceTests.cs    NEW
│       └── SimulatedDependencyAnalysisServiceTests.cs      NEW
└── DevOpsMigrationPlatform.Abstractions.Tests/
    └── Models/
        └── DependencyRecordTests.cs                        NEW
```

**Doc tasks (resolved during `speckit.implement`)**:
- `.agents/context/cli-commands.md` — add `discovery dependencies` row + canonical invocation examples
- `docs/cli.md` — add `### discovery dependencies` under `## Discovery Commands`
- `docs/source-types.md` — add Dependency Analysis subsection per source type (ADO, TFS, Simulated)

## Complexity Tracking

No constitution violations. All design decisions are additive and follow established patterns.

| Concern | Decision | Rationale |
|---------|----------|-----------|
| ADO link URL has no project segment | Secondary batch-GET `System.TeamProject` | `WorkItemRelation.Url` is `…/workItems/{id}` — project resolution requires an explicit lookup |
| TFS adds a new subprocess adapter | `TfsDependencyProcessAdapter` implements `IWorkItemLinkAnalysisService`; registered as keyed service in CLI host only | Follows `TfsExporterProcessAdapter` pattern; `Infrastructure` never references `CLI.Migration` |
| Three `IWorkItemLinkAnalysisService` implementations | Registered as keyed services by org `Type` string | Clean DI dispatch without coupling `DependencyDiscoveryService` to concrete types |
| `MaxConcurrency` on `DiscoveryOptions` | Additive property, default `4` | Non-breaking; all existing callers receive a safe default |
