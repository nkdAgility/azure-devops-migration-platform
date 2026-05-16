# Tasks: Simulated Data Source for End-to-End Migration Testing

**Feature**: Simulated Data Source  
**Branch**: `copilot/simulate-migration-data`  
**Spec**: [specs/008-simulated-data-source/spec.md](../../008-simulated-data-source/spec.md)  
**Plan**: [specs/copilot/simulate-migration-data/plan.md](./plan.md)  
**Date**: 2026-04-09

**Tech Stack**: C# 12 / .NET 10 · MSTest v3 · Reqnroll · `System.Random` (seeded) · `Microsoft.Extensions.DependencyInjection` / `Microsoft.Extensions.Options`

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete predecessor tasks)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Exact file paths are included in every task description

---

## Phase 1: Setup (Project Scaffolding)

**Purpose**: Create the two new .NET projects and wire them into the solution so all subsequent phases can compile.

**⚠️ CRITICAL**: No Phase 2+ work can begin until this phase is complete — the projects must exist and build cleanly.

- [x] T001 Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/DevOpsMigrationPlatform.Infrastructure.Simulated.csproj` targeting `net10.0`, referencing `DevOpsMigrationPlatform.Abstractions`, with folder structure `Options/`, `Generation/`, `Services/` — Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/DevOpsMigrationPlatform.Infrastructure.Simulated.csproj` exists and builds — Status: complete
- [x] T002 Add `DevOpsMigrationPlatform.Infrastructure.Simulated` project entry to `DevOpsMigrationPlatform.slnx` — Evidence: `DevOpsMigrationPlatform.slnx` contains `src/DevOpsMigrationPlatform.Infrastructure.Simulated/DevOpsMigrationPlatform.Infrastructure.Simulated.csproj` — Status: complete
- [x] T003 [P] Create `tests/DevOpsMigrationPlatform.SystemTests/DevOpsMigrationPlatform.SystemTests.csproj` targeting `net10.0`, with MSTest v3 and Reqnroll package references, and `[assembly: Parallelize(Scope = ExecutionScope.MethodLevel)]` — Superseded by spec `specs/017-simulated-infrastructure/tasks.md` (T003) which standardized on `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.csproj`; Evidence: that project exists and test suite passes — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md#T003
- [x] T004 [P] Add `DevOpsMigrationPlatform.SystemTests` project entry to `DevOpsMigrationPlatform.slnx` — Superseded by spec `specs/017-simulated-infrastructure/tasks.md` (T004) using `Infrastructure.Simulated.Tests`; Evidence: `DevOpsMigrationPlatform.slnx` contains `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.csproj` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md#T004

**Checkpoint**: Run `pwsh build.ps1` — both new projects must compile with zero errors before proceeding.

---

## Phase 2: Foundational (Shared Abstractions & Core Generation)

**Purpose**: Core abstractions, configuration options, config-schema registration, and the deterministic work item generator. Every user story depends on at least one item in this phase.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [x] T005 Define `IWorkItemImportSink` interface in `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportSink.cs` — signature: `WriteRevisionAsync(WorkItemRevision, IArtefactStore, string revisionFolderPath, CancellationToken)` and `CompleteAsync(CancellationToken)` per the contract in `contracts/IWorkItemImportSink.md` — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-008 and FR-012 using `IWorkItemImportTarget`/`IWorkItemImportTargetFactory`; Evidence: `src/DevOpsMigrationPlatform.Abstractions.Agent/Import/IWorkItemImportTarget.cs` and `IWorkItemImportTargetFactory.cs` are the active seam, `IWorkItemImportSink` does not exist — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-008
- [x] T006 [P] Create `SimulatedSourceOptions` (`sealed`, `init`-only) in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedSourceOptions.cs` — fields: `Seed`, `WorkItemCount` (required, `[Range(1,int.MaxValue)]`), `ProjectCount`, `WorkItemTypeDistribution`, `AvgRevisionsPerItem`, `IncludeAttachments`, `IncludeLinks`, `AttachmentSizeBytes`; add `IValidateOptions<SimulatedSourceOptions>` validator enforcing type-distribution sum-to-100 rule — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-001 and FR-035 introducing polymorphic `SimulatedEndpointOptions` + `SimulatedGeneratorConfig`; Evidence: `src/DevOpsMigrationPlatform.Abstractions/Options/SimulatedEndpointOptions.cs` and `SimulatedGeneratorConfig.cs` exist, `SimulatedSourceOptions` does not — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-001
- [x] T007 [P] Create `SimulatedTargetOptions` (`sealed`, `init`-only) in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedTargetOptions.cs` — fields: `ValidateOnWrite` (default `true`), `FailOnFirstError` (default `true`); `public static string SectionName => "target"` — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-001/FR-008 using endpoint-polymorphic config rather than dedicated target options class; Evidence: no `SimulatedTargetOptions.cs`, import target factory consumes `MigrationEndpointOptions` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-008
- [x] T008 Extend the source-type and target-type enumeration validators (wherever `source.type` and `target.type` are parsed/validated in `src/DevOpsMigrationPlatform.CLI.Migration/` or `src/DevOpsMigrationPlatform.Infrastructure/`) to accept `"Simulated"` as a valid value without throwing a config-validation error — Evidence: `src/DevOpsMigrationPlatform.Infrastructure/Config/MigrationPlatformOptionsValidator.cs` includes `ValidSourceTypes = [..., "Simulated"]` and `ValidTargetTypes = [..., "Simulated"]` — Status: complete
- [x] T009 [P] Create `SimulatedIdentitySet` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Generation/SimulatedIdentitySet.cs` — a fixed array of 10 synthetic `WorkItemIdentity` records with `DisplayName` prefixed `[SIMULATED]`, `UniqueName` ending `@simulated.invalid`, and a deterministic `Descriptor` — seed-independent — Superseded by spec `specs/017-simulated-infrastructure/spec.md` connector architecture using `SimulatedIdentitySource`; Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedIdentitySource.cs` exists and is registered in `SimulatedServiceCollectionExtensions` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md
- [x] T010 Create `SimulatedRevisionStream` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Generation/SimulatedRevisionStream.cs` — implements `IAsyncEnumerable<WorkItemRevision>`; initialises `System.Random(seed)` once; yields one `WorkItemRevision` at a time per the generation algorithm in `data-model.md` §4; never materialises a `List<WorkItemRevision>`; all field values prefixed `[SIMULATED]`; depends on T006 (`SimulatedSourceOptions`) and T009 (`SimulatedIdentitySet`) — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-016 implementation consolidation into `SimulatedWorkItemRevisionSource`; Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedWorkItemRevisionSource.cs` streams revisions and no `SimulatedRevisionStream.cs` exists — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-016
- [x] T011 Create skeleton `SimulatedServiceCollectionExtensions` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` with empty `AddSimulatedWorkItemExport(this IServiceCollection, ...)` and `AddSimulatedWorkItemImport(this IServiceCollection, ...)` stubs (filled in Phases 3 and 5 respectively) — Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` exists with concrete registrations — Status: complete

**Checkpoint**: `pwsh build.ps1` must pass with zero errors. `SimulatedRevisionStream` unit tests (if written) must confirm streaming — no in-memory list.

---

## Phase 3: User Story 1 — Simulate Work Item Discovery (Priority: P1) 🎯 MVP

**Goal**: `devopsmigration discovery inventory --config simulated.json` produces `discovery-summary.csv` with counts derived from config — no server, no PAT, no network.

**Independent Test**: Run `devopsmigration discovery inventory --config scenarios/migrate-simulated-25k.json` and verify that `discovery-summary.csv` reports 25,000 work items for one project; run twice with the same seed and confirm identical output.

### Gherkin Feature File for User Story 1

- [x] T012 [US1] Create `features/inventory/work-items/simulate-work-item-discovery.feature` — translate the three US1 acceptance scenarios from `specs/008-simulated-data-source/spec.md` into conformant Gherkin; include `@simulated` tag on all scenarios; follow tier/naming rules from `.agents/20-guardrails/workflow/acceptance-test-format.md` — Superseded by spec `specs/017-simulated-infrastructure/tasks.md` system-test approach and scenario-based coverage; Evidence: discovery coverage is implemented in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs` method `DiscoveryInventorySimulated_ExitsZeroAndWritesInventoryArtefacts` using `scenarios/inventory-simulated.json` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md

### Implementation for User Story 1

- [x] T013 [P] [US1] Create `SimulatedWorkItemDiscoveryService` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemDiscoveryService.cs` — implements `IWorkItemDiscoveryService`; returns one `ProjectDiscoverySummary` per configured project with `WorkItemCount = WorkItemCount / ProjectCount`; emits `IProgressSink` events; no WIQL or network calls; depends on T006 (`SimulatedSourceOptions`) — Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Discovery/SimulatedWorkItemDiscoveryService.cs` exists and implements `IWorkItemDiscoveryService` with no network calls — Status: complete
- [x] T014 [P] [US1] Create `SimulatedWorkItemRevisionSourceFactory` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemRevisionSourceFactory.cs` — implements `IWorkItemRevisionSourceFactory`; resolves the effective seed (auto-generate via `Random.Shared.Next()` if `Seed` is null, log at `Information`, write to `manifest.json` under `source.simulatedSeed` and `source.simulatedWorkItemCount`); instantiates `SimulatedWorkItemRevisionSource`; depends on T006, T010 — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-016 generator-based endpoint options without seed/manifest mutation requirement; Evidence: factory exists at `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedWorkItemRevisionSourceFactory.cs` but uses `SimulatedGeneratorConfig` from package config and does not implement seed+manifest behavior — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-016
- [x] T015 [US1] Implement the `AddSimulatedWorkItemExport` method in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` — registers `SimulatedSourceOptions` (bound from `"source"` section), `SimulatedWorkItemDiscoveryService` as `IWorkItemDiscoveryService`, and `SimulatedWorkItemRevisionSourceFactory` as `IWorkItemRevisionSourceFactory`; depends on T011, T013, T014 — Evidence: `AddSimulatedWorkItemExport` implemented with type registration, revision source factory registration, and discovery service registration in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` — Status: complete
- [x] T016 [US1] Wire source-type-aware DI registration in `src/DevOpsMigrationPlatform.MigrationAgent/` — when `job.Source?.Type == "Simulated"` call `services.AddSimulatedWorkItemExport(job.Source)`, otherwise call the existing ADO/TFS registration; depends on T015 — Superseded by spec `specs/017-simulated-infrastructure/spec.md` keyed composite connector architecture; Evidence: `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs` registers connector services and composite factories resolve by runtime endpoint type — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#Connector-Target-Model
- [x] T017 [US1] Create `scenarios/simulate-discovery-inventory.json` — a minimal ready-to-run config with `source.type: "Simulated"`, `source.workItemCount: 25000`, `mode: "Export"`, and no authentication fields; add a corresponding `.vscode/launch.json` entry `"🔍 Discovery: Simulated 25k"` pointing to this config — Superseded by spec `specs/017-simulated-infrastructure/spec.md` scenario set; Evidence: `scenarios/inventory-simulated.json` exists and is exercised by `SimulatedMigrationCommandTests`, while launch profiles use `queue-export-workitems-simulated-source.json` and `roundtrip-simulated.json` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md

**Checkpoint**: Running `devopsmigration discovery inventory --config scenarios/simulate-discovery-inventory.json` must produce `discovery-summary.csv` with a 25,000-item count. Run twice with the same seed; confirm CSV outputs are identical.

---

## Phase 4: User Story 2 — Full Simulated Export to a Package (Priority: P1)

**Goal**: `devopsmigration export --config simulated-export.json` with 25,000 work items fills the package `WorkItems/` folder in chronological revision order and writes a valid cursor to `Checkpoints/`.

**Independent Test**: Run `devopsmigration export --config scenarios/migrate-simulated-25k.json`; inspect package: verify `WorkItems/` revision folders exist, `Checkpoints/workitems.cursor.json` is populated, and `manifest.json` contains `source.simulatedSeed`. Interrupt mid-run, re-run, and confirm export resumes from the last checkpoint.

### Gherkin Feature File for User Story 2

- [x] T018 [US2] Create `features/export/work-items/revisions/simulate-work-item-export.feature` — translate the four US2 acceptance scenarios from `specs/008-simulated-data-source/spec.md` into conformant Gherkin; include `@simulated` tag on all scenarios; `@checkpoint` tag on the resume scenario; follow tier/naming rules from `.agents/20-guardrails/workflow/acceptance-test-format.md` — Superseded by newer feature naming and system-test coverage; Evidence: `features/export/work-items/simulated-export.feature` exists and CLI system tests validate simulated export/retry paths — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md

### Implementation for User Story 2

- [x] T019 [P] [US2] Create `SimulatedWorkItemRevisionSource` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemRevisionSource.cs` — implements `IWorkItemRevisionSource`; holds a `SimulatedRevisionStream` instance; exposes `GetRevisionsAsync(CancellationToken)` returning the stream's `IAsyncEnumerable<WorkItemRevision>`; emits `IProgressSink` events at per-revision granularity (matching FR-009); depends on T010 (`SimulatedRevisionStream`) — Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedWorkItemRevisionSource.cs` exists and streams via `IAsyncEnumerable<WorkItemRevision>` — Status: complete
- [x] T020 [P] [US2] Create `SimulatedAttachmentBinarySource` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedAttachmentBinarySource.cs` — implements `IAttachmentBinarySource`; generates deterministic pseudo-random byte arrays of `AttachmentSizeBytes` using a seed-derived `System.Random`; activated only when `IncludeAttachments = true`; depends on T006 (`SimulatedSourceOptions`) — Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Attachments/SimulatedAttachmentBinarySource.cs` exists — Status: complete
- [x] T021 [US2] Extend `SimulatedWorkItemRevisionSourceFactory` (T014) to conditionally register `SimulatedAttachmentBinarySource` as `IAttachmentBinarySource` when `IncludeAttachments = true` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemRevisionSourceFactory.cs`; depends on T014, T020 — Superseded by spec `specs/017-simulated-infrastructure/spec.md` keyed DI design where registrations are centralized in `SimulatedServiceCollectionExtensions`; Evidence: attachment source registration is handled via service registration, not conditional factory mutation — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#Connector-Target-Model
- [x] T022 [US2] Create `scenarios/migrate-simulated-25k.json` — full ready-to-run scenario with `source.type: "Simulated"`, `source.workItemCount: 25000`, `source.avgRevisionsPerItem: 3`, `source.includeLinks: true`, `source.includeAttachments: false`, `target.type: "Simulated"`, `target.validateOnWrite: true`, `target.failOnFirstError: true`, `mode: "Both"`, `artefacts.path: "${workspaceFolder}/Logs/SimulatedRun-25k"` — Superseded by newer scenario set defined in `specs/017-simulated-infrastructure/spec.md`; Evidence: `scenarios/queue-export-workitems-simulated-source.json`, `scenarios/queue-import-workitems-simulated-target.json`, and `scenarios/roundtrip-simulated.json` exist — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-027
- [x] T023 [US2] Add `"🧪 Migrate: Simulated 25k"` debug profile to `.vscode/launch.json` pointing to `scenarios/migrate-simulated-25k.json`; use the local Aspire topology consistent with existing launch profiles — Superseded by launch profiles for the newer scenarios; Evidence: `.vscode/launch.json` includes profiles for `queue-export-workitems-simulated-source.json`, `queue-import-workitems-simulated-target.json`, and `roundtrip-simulated.json` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md#T055

**Checkpoint**: `devopsmigration export --config scenarios/migrate-simulated-25k.json` must complete under 10 minutes. Package must contain `WorkItems/` revision folders, `Checkpoints/workitems.cursor.json`, and `manifest.json` with `source.simulatedSeed`. Checkpoint-resume test: interrupt at ~50%, re-run, confirm it completes in roughly half the time.

---

## Phase 5: User Story 3 — Full Simulated End-to-End Migration (Priority: P2)

**Goal**: `devopsmigration migrate --config simulated-both.json` with `source.type: Simulated` and `target.type: Simulated` completes export → validation → import with zero errors, no external connections, and live TUI progress.

**Independent Test**: Run `devopsmigration migrate --config scenarios/migrate-simulated-25k.json`; verify migration completes; run `devopsmigration validate`; confirm zero validation errors in `Logs/`; confirm `Logs/simulated-import-summary.jsonl` reports expected item counts.

### Gherkin Feature File for User Story 3

- [x] T024 [US3] Create `features/import/work-items/revisions/simulate-work-item-import.feature` — translate the four US3 acceptance scenarios from `specs/008-simulated-data-source/spec.md` into conformant Gherkin; include `@simulated` and `@end-to-end` tags; `@tui` tag on the TUI scenario; `@validation` tag on the round-trip fidelity scenario; follow tier/naming rules from `.agents/20-guardrails/workflow/acceptance-test-format.md` — Superseded by updated naming and scenario assets; Evidence: `features/import/work-items/simulated-import.feature` exists and roundtrip is covered by `scenarios/roundtrip-simulated.json` plus system tests — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md

### Implementation for User Story 3

- [x] T025 [P] [US3] Create `SimulatedImportValidationException` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedImportValidationException.cs` — derives from `InvalidOperationException`; carries a `IReadOnlyList<string> ValidationErrors` property; used by `SimulatedWorkItemImportSink` on schema validation failure — Superseded by spec `specs/017-simulated-infrastructure/spec.md` import-target model that does not use `IWorkItemImportSink`; Evidence: no `SimulatedImportValidationException` class exists and import path uses `SimulatedWorkItemImportTarget` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-018
- [x] T026 [P] [US3] Create `SimulatedWorkItemImportSink` in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemImportSink.cs` — implements `IWorkItemImportSink` (T005); on `WriteRevisionAsync` validates required fields (`workItemId`, `revisionIndex`, `changedDate`, `fields`) and `sha256` format when `ValidateOnWrite = true`; tracks revision count; when `FailOnFirstError = true` throws `SimulatedImportValidationException` immediately on failure; on `CompleteAsync` writes `Logs/simulated-import-summary.jsonl` via `IProgressSink`; emits per-revision `IProgressSink` events (FR-009); never writes to any external system; depends on T005, T007, T025 — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-018 using `SimulatedWorkItemImportTarget`; Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedWorkItemImportTarget.cs` exists and `SimulatedWorkItemImportSink.cs` does not — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-018
- [x] T027 [P] [US3] Create `AzureDevOpsWorkItemImportSink` stub in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsWorkItemImportSink.cs` — implements `IWorkItemImportSink`; `WriteRevisionAsync` and `CompleteAsync` initially throw `NotImplementedException` with a message indicating ADO import is not yet implemented — this satisfies DI wiring without breaking the build; depends on T005 — Superseded by newer connector rules and spec `specs/017-simulated-infrastructure/spec.md` FR-012/SC-005 forbidding placeholder stubs; Evidence: active implementation is `AzureDevOpsWorkItemImportTargetFactory` + `AzureDevOpsWorkItemImportTarget` and no sink stub exists — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-012
- [x] T028 [US3] Implement `WorkItemsModule.ImportAsync` in `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` — injects `IWorkItemImportSink`; streams revision folders via `IArtefactStore.EnumerateAsync("WorkItems/", ct)`; uses `CheckpointingService` with `Checkpoints/workitems.cursor.json` to resume; deserialises `revision.json` per folder; calls `_importSink.WriteRevisionAsync` for each; calls `_importSink.CompleteAsync` after the loop; emits `IProgressSink` events per revision (replaces the existing `NotImplementedException`); depends on T005, T026 — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-011 connector-agnostic `IWorkItemImportTargetFactory` flow and by `specs/035-workitem-import-support/spec.md` import-capability scope; Evidence: `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs` has concrete `ImportAsync` and does not throw `NotImplementedException` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-011
- [x] T029 [US3] Implement the `AddSimulatedWorkItemImport` method in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` — registers `SimulatedTargetOptions` (bound from `"target"` section) and `SimulatedWorkItemImportSink` as `IWorkItemImportSink` (scoped); depends on T011, T026 — Superseded by spec `specs/017-simulated-infrastructure/spec.md` FR-018 import-target factory registration model; Evidence: `AddSimulatedWorkItemImport` exists and registers `SimulatedWorkItemImportTargetFactory` keyed `"Simulated"` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-018
- [x] T030 [US3] Extend the source-type-aware DI wiring in `src/DevOpsMigrationPlatform.MigrationAgent/` (T016) to also wire the target: when `job.Target?.Type == "Simulated"` call `services.AddSimulatedWorkItemImport(job.Target)`, otherwise call `services.AddAzureDevOpsWorkItemImport()`; depends on T016, T029 — Superseded by keyed connector registration/composite dispatch architecture; Evidence: `MigrationAgentServiceExtensions` registers connector services once and composite factories resolve import target by endpoint type at runtime — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#Connector-Target-Model

**Checkpoint**: `devopsmigration migrate --config scenarios/migrate-simulated-25k.json` must complete with zero errors. `Logs/simulated-import-summary.jsonl` must report 25,000 items. `devopsmigration validate` must report zero errors (SC-005).

---

## Phase 6: User Story 4 — Automated System Tests (Priority: P2)

**Goal**: At least one `[TestCategory("SystemTest")]` test runs `devopsmigration migrate` end-to-end programmatically with 100 simulated items, passes in CI with zero external connectivity, and completes in under 5 minutes.

**Independent Test**: `dotnet test tests/DevOpsMigrationPlatform.SystemTests/ --filter "TestCategory=SystemTest"` passes in CI with no network access; elapsed time under 5 minutes.

### Gherkin Feature File for User Story 4

- [x] T031 [US4] Create `features/platform/simulated-migration/simulated-end-to-end-migration.feature` — translate the two US4 acceptance scenarios from `specs/008-simulated-data-source/spec.md` into conformant Gherkin; include `@simulated`, `@system-test`, and `@ci` tags; the performance-gate scenario should carry `@performance`; follow tier/naming rules from `.agents/20-guardrails/workflow/acceptance-test-format.md` — Superseded by command-level system tests and scenario-driven assets introduced in later specs; Evidence: end-to-end simulated coverage exists in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs` (`QueueRoundtripSimulated_ExitsZeroAndProducesPackageWithRevisions`) — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md

### Implementation for User Story 4

- [x] T032 [US4] Create `tests/DevOpsMigrationPlatform.SystemTests/SystemTests/SimulatedMigrationSystemTests.cs` — contains the `[TestClass]` with two `[TestMethod]` tests both carrying `[TestCategory("SystemTest")]` — Superseded by relocation of simulated system tests into existing CLI test project and dedicated simulated infrastructure test project; Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs` exists with multiple `[TestCategory("SystemTest")]` tests and `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests` project exists — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md#T003
- [x] T033 [US4] Implement the 100-item system test in `tests/DevOpsMigrationPlatform.SystemTests/SystemTests/SimulatedMigrationSystemTests.cs` — programmatically builds a `SimulatedSourceOptions` with `WorkItemCount = 100` and `Seed = 42`, invokes `devopsmigration migrate` in-process (or via `Process.Start`) with `mode: Both`, asserts: (a) `WorkItems/` folder exists in the package, (b) `Checkpoints/workitems.cursor.json` exists, (c) `Logs/progress.jsonl` contains at least one event, (d) `Logs/simulated-import-summary.jsonl` reports 100 items, (e) `devopsmigration validate` exits with code 0; depends on T028, T030 — Superseded by scenario-based queue E2E tests in `SimulatedMigrationCommandTests`; Evidence: tests cover export/import/roundtrip/inventory via simulated scenarios and assert package outputs and logs — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md
- [ ] T034 [US4] Implement the 25k performance-gate test in `tests/DevOpsMigrationPlatform.SystemTests/SystemTests/SimulatedMigrationSystemTests.cs` — same structure as T033 but with `WorkItemCount = 25000`; asserts elapsed time is under the configured threshold (default 10 minutes); test is marked `[Timeout(600000)]` and decorated with a comment indicating it is excluded from the fast CI filter; depends on T033 — Evidence: no `DevOpsMigrationPlatform.SystemTests` project exists and no current automated test asserts a 25k simulated performance gate threshold — Status: incomplete

**Checkpoint**: `dotnet test tests/DevOpsMigrationPlatform.SystemTests/ --filter "TestCategory=SystemTest&Name!=Performance"` must pass in CI in under 5 minutes. SC-006 satisfied.

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Documentation rectification for the discrepancies identified in `discrepancies.md`, plus quickstart validation.

- [x] T035 [P] Update `docs/capabilities-guide.md` to add a `Simulated` section documenting valid `source.type: "Simulated"` usage, configuration fields, and the `[SIMULATED]`-prefix convention — resolves the discrepancy flagged in `specs/008-simulated-data-source/discrepancies.md` — Evidence: `docs/capabilities-guide.md` contains simulated connector guidance and scenario references — Status: complete
- [x] T036 [P] Update `docs/configuration-reference.md` to add `"Simulated"` to the `source.type` and `target.type` enum documentation with links to `SimulatedSourceOptions` and `SimulatedTargetOptions` — resolves config-schema discrepancy — Evidence: `docs/configuration-reference.md` documents simulated source/target configuration and scenario usage — Status: complete
- [x] T037 [P] Update `docs/architecture.md` to add `DevOpsMigrationPlatform.Infrastructure.Simulated` to the Components table with a note: "testing-only; must not be referenced by production projects" — Evidence: `docs/architecture.md` includes `Infrastructure.Simulated` in architecture component documentation — Status: complete
- [x] T038 Update `docs/module-development-guide.md` to: (a) document `IWorkItemImportSink` and its role in the import path under "IDataTypeModule Contract", (b) update the `WorkItemsModule` row in the Module Responsibilities table to reflect that `ImportAsync` is fully implemented — Superseded by spec `specs/017-simulated-infrastructure/spec.md` import abstraction (`IWorkItemImportTarget`) and completed import documentation updates; Evidence: `docs/module-development-guide.md` documents current import-target/factory seams and WorkItems import support rather than `IWorkItemImportSink` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md#FR-008
- [ ] T039 Validate the `quickstart.md` walkthrough end-to-end: run each numbered step in `specs/copilot/simulate-migration-data/quickstart.md` and confirm the expected output files and TUI behaviour match; update quickstart if any command or path changed during implementation — Evidence: quickstart still references stale paths (`specs/copilot/...`, `scenarios/migrate-simulated-25k.json`, `tests/DevOpsMigrationPlatform.SystemTests`) and no recorded end-to-end walkthrough evidence exists — Status: incomplete

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately
- **Phase 2 (Foundational)**: Requires Phase 1 completion; BLOCKS Phases 3–7
- **Phase 3 (US1)**: Requires Phase 2; can start in parallel with Phases 4–6 after Phase 2 is done
- **Phase 4 (US2)**: Requires Phase 2; US2 export depends on `SimulatedRevisionStream` (T010) from Phase 2
- **Phase 5 (US3)**: Requires Phases 2, 4 (export must be working to validate end-to-end); `WorkItemsModule.ImportAsync` (T028) depends on `IWorkItemImportSink` (T005, Phase 2)
- **Phase 6 (US4)**: Requires Phases 3, 4, 5 all passing
- **Phase 7 (Polish)**: Requires all implementation phases complete

### User Story Dependencies

```
Phase 1 (Setup)
  └── Phase 2 (Foundational: T005–T011)
        ├── Phase 3 / US1 (T012–T017) — independent; no dependency on US2/US3/US4
        ├── Phase 4 / US2 (T018–T023) — independent; no dependency on US1/US3/US4
        ├── Phase 5 / US3 (T024–T030) — depends on Phase 4 (WorkItemsModule.ImportAsync needs export first)
        └── Phase 6 / US4 (T031–T034) — depends on US1 + US2 + US3 complete
              └── Phase 7 (Polish: T035–T039)
```

### Within-Story Task Dependencies

| Task | Depends On |
|------|-----------|
| T010 `SimulatedRevisionStream` | T006 `SimulatedSourceOptions`, T009 `SimulatedIdentitySet` |
| T013 `SimulatedWorkItemDiscoveryService` | T006 |
| T014 `SimulatedWorkItemRevisionSourceFactory` | T006, T010 |
| T015 `AddSimulatedWorkItemExport` stub | T011, T013, T014 |
| T016 DI wiring (source) | T015 |
| T017 discovery scenario config | T016 |
| T019 `SimulatedWorkItemRevisionSource` | T010 |
| T020 `SimulatedAttachmentBinarySource` | T006 |
| T021 factory attachment wiring | T014, T020 |
| T022 25k scenario file | T016, T030 (can be written any time; must be used after wiring complete) |
| T023 VS Code launch profile | T022 |
| T026 `SimulatedWorkItemImportSink` | T005, T007, T025 |
| T027 `AzureDevOpsWorkItemImportSink` stub | T005 |
| T028 `WorkItemsModule.ImportAsync` | T005, T026 |
| T029 `AddSimulatedWorkItemImport` | T011, T026 |
| T030 DI wiring (target) | T016, T029 |
| T033 100-item system test | T028, T030 |
| T034 25k performance test | T033 |

---

## Parallel Execution Examples

### Phase 1 — fully parallel after project skeleton exists

```
T001 Create Infrastructure.Simulated.csproj
T003 Create SystemTests.csproj            [parallel with T001]
T002 Add Infrastructure.Simulated to slnx [after T001]
T004 Add SystemTests to slnx              [after T003]
```

### Phase 2 — parallel after T005 and T010 dependencies

```
T005 IWorkItemImportSink                   [first]
T006 SimulatedSourceOptions                [parallel with T005]
T007 SimulatedTargetOptions                [parallel with T005, T006]
T008 Config schema validation              [parallel with T006, T007]
T009 SimulatedIdentitySet                  [parallel with T005–T008]
T010 SimulatedRevisionStream               [after T006, T009]
T011 Extension stubs                       [after T006, T007]
```

### Phase 3 / US1 — parallel models then sequential wiring

```
T012 Gherkin feature file (US1)            [parallel with T013, T014]
T013 SimulatedWorkItemDiscoveryService     [parallel with T014]
T014 SimulatedWorkItemRevisionSourceFactory [parallel with T013]
T015 AddSimulatedWorkItemExport            [after T013, T014]
T016 DI wiring (source)                    [after T015]
T017 Discovery scenario config             [after T016]
```

### Phase 4 / US2 — parallel implementations then sequential wiring

```
T018 Gherkin feature file (US2)            [parallel with T019, T020]
T019 SimulatedWorkItemRevisionSource       [parallel with T020]
T020 SimulatedAttachmentBinarySource       [parallel with T019]
T021 Factory attachment wiring             [after T019, T020]
T022 Scenario config file                  [parallel with T021]
T023 VS Code launch profile                [after T022]
```

### Phase 5 / US3 — parallel exception + sink + stub, then sequential

```
T024 Gherkin feature file (US3)            [parallel with T025, T026, T027]
T025 SimulatedImportValidationException    [parallel with T027]
T026 SimulatedWorkItemImportSink           [after T025; parallel with T027]
T027 AzureDevOpsWorkItemImportSink stub    [parallel with T025, T026]
T028 WorkItemsModule.ImportAsync           [after T026, T027]
T029 AddSimulatedWorkItemImport            [after T026]
T030 DI wiring (target)                    [after T029]
```

---

## Implementation Strategy

### MVP First (US1 + US2 Only)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational
3. Complete Phase 3: US1 — discovery works without a server
4. Complete Phase 4: US2 — export produces a valid package
5. **STOP and VALIDATE**: Run `devopsmigration export` with 25k items; confirm package structure; run quickstart steps 1 and 5
6. Demo if ready — both P1 stories deliver value independently

### Incremental Delivery

1. Phase 1 + Phase 2 → Foundation is solid; projects build cleanly
2. Phase 3 (US1) → Discovery works; CI no longer needs a live server for discovery tests
3. Phase 4 (US2) → Full export pipeline testable in isolation; checkpoint-resume verified at scale
4. Phase 5 (US3) → `WorkItemsModule.ImportAsync` implemented; complete round-trip works; TUI shows all phases
5. Phase 6 (US4) → System test project in place; CI has a permanent, infrastructure-free E2E gate
6. Phase 7 (Polish) → Documentation consistent with implementation; quickstart validated

### Parallel Team Strategy

With multiple contributors after Phase 2 is complete:

- **Contributor A**: Phase 3 (US1 — discovery)
- **Contributor B**: Phase 4 (US2 — export, `SimulatedRevisionStream` already available from Phase 2)
- **Contributor C**: Phase 5 starts in parallel on `IWorkItemImportSink` and import sink — integrates with Phase 4 output when export is stable

---

## Notes

- `[P]` tasks = different files, no incomplete predecessors — safe to parallelise
- `[US1]–[US4]` labels map directly to user stories in `specs/008-simulated-data-source/spec.md`
- All simulated `WorkItemRevision` field values MUST be prefixed `[SIMULATED]` (FR-003, data-model.md §8)
- `SimulatedRevisionStream` MUST never buffer all revisions — use `yield return` / `IAsyncEnumerable` (FR-004, guardrail II)
- Seed auto-generation: when `Seed` is null, generate via `Random.Shared.Next()`, log at `Information`, write to `manifest.json.source.simulatedSeed` (FR-011)
- `WorkItemsModule.ImportAsync` was previously `NotImplementedException` — T028 is the first real implementation
- System tests in Phase 6 must carry `[TestCategory("SystemTest")]` so CI can filter them into a dedicated stage
- Commit after each completed phase checkpoint; do not commit broken builds

