# Tasks: Team Board Configuration Export/Import

**Input**: Design documents from `/specs/039-team-board-settings/`

**Branch**: `039-team-board-settings`
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md) | **Data model**: [data-model.md](data-model.md)
**Contracts**: [IModuleExtension](contracts/IModuleExtension.md) · [ITeamExtension](contracts/ITeamExtension.md) · [ITeamBoardAdapter](contracts/ITeamBoardAdapter.md)
**Research**: [research.md](research.md) | **Quickstart**: [quickstart.md](quickstart.md)

**Test approach**: Test-first — failing tests are written **before** implementation.
Each story phase starts with failing tests in `*Tests.cs`, then production code. No new `.feature` files — existing ones are legacy and must not be modified.

**Test categories** (per `.agents/20-guardrails/workflow/testing-rules.md` — every test carries its parent family **and** specific tag):
- `[TestCategory("CodeTest")]` + `[TestCategory("UnitTests")]` — isolated class, all deps mocked
- `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` — behavioural scenarios via the internal Test DSL (`tests/DevOpsMigrationPlatform.Testing`)
- `[TestCategory("CodeTest")]` + `[TestCategory("IntegrationTests")]` — real infrastructure components wired in-process (e.g. adapter + mocked `WorkHttpClient`), no network
- `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Simulated")]` — end-to-end via the Simulated connector
- `SystemTest_Smoke` is operator-designated only — never assigned by a task

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Parallelisable (different files, no incomplete dependencies)
- **[Story]**: User story label (US1–US6)
- All paths are repository-relative

---

## Phase 1: Setup (Records, Interfaces, Options)

**Purpose**: Create all new abstraction types, interfaces, and options that the rest of the work depends on.
No orchestrator or connector code yet — just the shape of the domain.

- [ ] T001 Add board config metric constants to `src/DevOpsMigrationPlatform.Abstractions/Telemetry/WellKnownAgentMetricNames.cs` (add `// --- Teams Board Config Export/Import ---` section with 9 constants per Observability section in plan.md)
- [ ] T002 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/BoardColumnStateMapping.cs` (sealed record: `WorkItemType`, `State`)
- [ ] T003 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/BoardColumn.cs` (sealed record: `Name`, `ColumnType`, `ItemLimit`, `IsSplit`, `Description?`, `StateMappings`)
- [ ] T004 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/BoardSwimLane.cs` (sealed record: `Id?` (source-only metadata), `Name` (portable key)) — mirrors ADO `BoardRow` which exposes only `Id` + `Name`; no `Color`/`Description` (neither exists in the API)
- [ ] T005 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/CardRule.cs` (sealed record: `Name`, `Color?`, `IsEnabled`, `Filter`) and `CardRuleSettings.cs` (sealed record: `Rules`)
- [ ] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/BacklogMetadata.cs` (sealed record: `Name`, `WitCategory`, `LevelType`, `Rank`) and `BacklogLevelType.cs` enum (`Portfolio`, `Requirement`, `Task`) — mirrors ADO `BacklogLevelConfiguration.Type` (the backlog level type required by FR-004) + `Rank` (ordering)
- [ ] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/TaskboardColumn.cs` (sealed record: `Name`, `ColumnType`, `Order`, `StateMappings`)
- [ ] T008 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/BoardConfig.cs` (sealed record: `BoardName`, `Columns`, `SwimLanes`) — depends on T003, T004
- [ ] T009 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/TeamBoardConfig.cs` (sealed class `TeamBoardConfig` with `TeamName`, `ExportedAt`, `Boards`, `CardRules?`, `Backlogs`, `TaskboardColumns`) — depends on T005–T008
- [ ] T010 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/ITeamBoardAdapter.cs` — single adapter contract covering both export (Get*) and import (Update*) methods; see contracts/ITeamBoardAdapter.md
- [ ] T012 Extend `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/TeamsModuleOptions.cs` — add `BoardConfigImportMode` enum and `BoardConfigExtensionsOptions` sealed class (Columns, SwimLanes, CardRules, Backlogs, TaskboardColumns bools + ImportMode); add `BoardConfig` property to `TeamsModuleExtensionsOptions`
- [ ] T076 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/IModuleExtension.cs` — cross-cutting marker interface: `Module`, `Name`, `Order`, `SupportsExport`, `SupportsImport` (see data-model.md Extension Architecture section)
- [ ] T077 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/ITeamExtension.cs` — Teams per-entity extension contract: `IsEnabled(TeamsModuleExtensionsOptions)`, `ExportAsync(TeamExtensionContext, ct)`, `ImportAsync(TeamExtensionContext, ct)`; default interface method implementations for unsupported phases — depends on T076
- [ ] T078 Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/TeamExtensionContext.cs` — sealed record: `Organisation`, `Project`, `SourceProject`, `TeamDefinition`, `Slug`, `IPackageAccess`, `TeamsModuleExtensionsOptions`, `IProgressSink?`

**Checkpoint**: All new abstraction types compile. No orchestrator or connector code yet. Every new `.cs` file (production and test) MUST begin with the SPDX header block (`// SPDX-License-Identifier: AGPL-3.0-only` / `// Copyright (c) Naked Agility Limited`) — SA1633 fails the build otherwise.

---

## Phase 2: Foundational (ConnectorCapability Mechanism)

**Purpose**: Introduce the `ConnectorCapability` runtime flag mechanism. This MUST be complete before
any board config orchestrator code is written, because orchestrators depend on `IConnectorCapabilityProvider`.
TFS must register an explicit `ConnectorCapability.None` declaration — no null-guards in orchestrators.

**⚠️ CRITICAL**: Guardrail Rule 29 + runtime-compatibility Rule 7 forbid null-guard patterns.
TFS MUST register `IConnectorCapabilityProvider` explicitly. No `if (_provider is null)` guards.

- [ ] T013 Write `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/ConnectorCapabilityTests.cs` — `[TestCategory("CodeTest")]` + `[TestCategory("UnitTests")]` methods: (a) connector with capability returns `Has(flag) == true`; (b) connector without capability returns `Has(flag) == false`; (c) composite — a provider registered with `BoardConfig` returns true for `BoardColumns`, `BoardRows`, and `CardRules`; (d) TFS connector (`None`) returns false for every flag including the granular `BoardColumns`/`BoardRows`/`CardRules`/`Backlogs`/`TaskboardColumns`
- [ ] T014 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/ConnectorCapability.cs` — `[Flags] enum ConnectorCapability` with granular flags `BoardColumns=1<<0`, `BoardRows=1<<1`, `CardRules=1<<2`, `Backlogs=1<<3`, `TaskboardColumns=1<<4`, plus composite `BoardConfig = BoardColumns | BoardRows | CardRules` and `None=0` (see data-model.md ConnectorCapability). FR-015 names these granular flags individually; `BoardConfig` is the composite the export/import path checks for the Kanban board as a whole
- [ ] T015 [P] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/IConnectorCapabilityProvider.cs` (`bool Has(ConnectorCapability capability)`)
- [ ] T016 Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/ConnectorCapability/StaticConnectorCapabilityProvider.cs` (implements `IConnectorCapabilityProvider`, stores declared flags, `Has` returns bitwise test) — depends on T014, T015
- [ ] T017 Create `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Teams/TfsConnectorCapabilityProvider.cs` (implements `IConnectorCapabilityProvider`, always returns `ConnectorCapability.None`, documented as intentional explicit declaration) — depends on T014, T015
- [ ] T017b Create `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Teams/TfsNullBoardAdapter.cs` (implements `ITeamBoardAdapter`; all methods throw `NotSupportedException`; registered for TFS so DI can construct `BoardConfigTeamExtension` — the capability check fires first so these methods are never reached) — depends on T010
- [ ] T018 [P] Register `TfsConnectorCapabilityProvider` as `IConnectorCapabilityProvider` singleton and `TfsNullBoardAdapter` as `ITeamBoardAdapter` scoped in TFS connector DI setup (`AddTfsConnector` or equivalent extension) — depends on T017, T017b
- [ ] T019 [P] Register `StaticConnectorCapabilityProvider(BoardConfig | TaskboardColumns | Backlogs)` in AzureDevOpsServices connector DI setup (`BoardConfig` composite covers BoardColumns+BoardRows+CardRules)
- [ ] T020 [P] Register `StaticConnectorCapabilityProvider(BoardConfig | TaskboardColumns | Backlogs)` in Simulated connector DI setup (`BoardConfig` composite covers BoardColumns+BoardRows+CardRules)
- [ ] T021 Verify all `[TestMethod]` tests in `ConnectorCapabilityTests.cs` pass (unit test — no real connectors needed)
- [ ] T079 Write `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/TeamExtensionDispatchTests.cs` — `[TestCategory("CodeTest")]` + `[TestCategory("UnitTests")]` methods: (a) enabled extension with SupportsExport=true has ExportAsync called per team; (b) disabled extension is not called; (c) SupportsImport=false extension has ImportAsync not called; (d) extensions invoked in Order sequence
- [ ] T080 Extend `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsOrchestrator.cs` — `ExportAsync` and `ImportAsync` accept `IReadOnlyList<ITeamExtension>`; per-team loop builds `TeamExtensionContext` and calls each extension in order; handles per-extension errors without aborting the team loop — depends on T077, T078
- [ ] T081 Extend `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs` — inject `IEnumerable<ITeamExtension>` via constructor; in `ExportAsync` filter to `SupportsExport && IsEnabled`, sort by `Order`, pass list to orchestrator; same for `ImportAsync` — depends on T077, T080
- [ ] T082 Verify all `[TestMethod]` tests in `TeamExtensionDispatchTests.cs` pass

**Checkpoint**: ConnectorCapability tests green. Extension dispatch tests green. TeamsModule passes filtered, ordered extensions to TeamsOrchestrator. Ready for board config extension work.

---

## Phase 3: User Story 1 — Export Board Columns (Priority: P1) 🎯 MVP

**Goal**: Capture Kanban board column definitions (name, WIP limit, state mappings, split, type, description) per team into `Teams/{slug}/board-config.json`.

**Independent Test**: Run export with Simulated connector. Verify `Teams/alpha/board-config.json` exists with `boards[].columns` containing all seeded columns and their properties.

### Tests (write first — must fail before implementation)

- [ ] T022 Write `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/BoardConfigTeamExtensionTests.cs` — `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` methods covering all 5 US1 acceptance scenarios (multi-board teams, missing WIP limit, Simulated connector, TFS capability absent → Skipped); written against the internal Test DSL — add board-config scenario builders/runners/assertions to `tests/DevOpsMigrationPlatform.Testing/` if not already present; US6 import test methods added in Phase 8 to the same class

### Implementation for User Story 1

- [ ] T023 [P] [US1] Create skeleton `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/BoardConfigTeamExtension.cs` implementing `ITeamExtension` — `Name="BoardConfig"`, `SupportsExport=true`, `SupportsImport=true`, `Order=100`; `IsEnabled` checks `options.BoardConfig`; `ExportAsync` checks the composite `ConnectorCapability.BoardConfig` (= BoardColumns+BoardRows+CardRules) first for the Kanban board and returns early/Skipped if absent; backlogs and taskboard sections gate independently on `Has(ConnectorCapability.Backlogs)` and `Has(ConnectorCapability.TaskboardColumns)`; inject `ITeamBoardAdapter`, `IConnectorCapabilityProvider`, `ILogger`
- [ ] T024 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Teams/SimulatedBoardAdapter.cs` — implements `ITeamBoardAdapter`; `GetBoardsAsync` returns 2 deterministic boards each with 3 columns (one with no WIP limit, one split); swimlanes/card rules/backlogs/taskboard return empty stubs; `Update*` methods capture calls in-memory for test assertion
- [ ] T025 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Teams/AzureDevOpsBoardAdapter.cs` — implements `ITeamBoardAdapter`; `GetBoardsAsync` calls `WorkHttpClient.GetBoardsAsync` + `GetBoardColumnsAsync` with `TeamContext`; `Update*` methods call the corresponding PUT endpoints; swimlanes/card rules/backlogs/taskboard stubs to be filled in later stories — depends on T023
- [ ] T026 [US1] Complete `BoardConfigTeamExtension.ExportAsync`: iterate boards from `GetBoardsAsync`, build `TeamBoardConfig`, serialize to JSON, persist to `Teams/{slug}/board-config.json` via `ctx.Package.PersistContentAsync` — depends on T023, T024
- [ ] T027 [US1] Register `BoardConfigTeamExtension` as `ITeamExtension` (transient) in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamsServiceCollectionExtensions.cs` `AddTeamsModule` — depends on T026; no changes to `TeamsModule.cs` required (extension discovery is automatic via DI)
- [ ] T029 [US1] Add O-1 `ActivitySource` span (`teams.boardconfig.export`), O-2 metrics (`platform.teams.export.boardconfig.count/duration_ms/errors/in_flight`), and O-3 structured `ILogger` events (started/completed/skipped/error) to `BoardConfigTeamExtension.ExportAsync` per plan.md Observability section
- [ ] T030 [US1] Add O-4 `IProgressSink` progress events via `ctx.ProgressSink` in `BoardConfigTeamExtension.ExportAsync`: emit `{ Module="Teams", Stage="BoardConfigExporting", teamSlug }` at start and `{ Stage="BoardConfigExported", teamSlug, boardCount, durationMs }` on completion; emit `{ Stage="BoardConfigSkipped", reason }` when capability absent
- [ ] T031 [US1] Verify all US1 `[TestMethod]` tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/BoardConfigTeamExtensionTests.cs` pass
- [ ] T074 [US1] Add a `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` method to `BoardConfigTeamExtensionTests.cs`: when `BoardConfig.Columns` is disabled in options, export runs but `board-config.json` contains no `columns` data for any board (covers FR-014/SC-005 — independent extension enable/disable)
- [ ] T074b [US1] Add a `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` method to `BoardConfigTeamExtensionTests.cs`: when `BoardConfig.SwimLanes` is disabled, export runs but `board-config.json` contains no `swimLanes` data; same pattern for `CardRules` (T074c), `Backlogs` (T074d), `TaskboardColumns` (T074e) — covers SC-005 for all 5 types
- [ ] T075 [US1] Add a `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` method to `BoardConfigTeamExtensionTests.cs`: when source team's board columns are at process defaults (3 standard columns, no WIP limits), export writes `board-config.json` capturing the default column layout (covers EC-4)

**Checkpoint**: US1 feature file green. Export with Simulated connector writes `board-config.json` with column data. TFS path returns Skipped.

---

## Phase 4: User Story 2 — Export Swimlanes Per Team (Priority: P2)

**Goal**: Add swimlane (row) data to the per-team `board-config.json` artefact.

**Independent Test**: Simulated source has 2 named swimlanes per board. Run export. Verify `board-config.json → boards[].swimLanes` contains both lane names.

### Tests (write first — must fail before implementation)

- [ ] T032 [US2] Add `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` methods to `BoardConfigTeamExtensionTests.cs` covering all 4 US2 acceptance scenarios (custom lanes, default-only lane, Simulated validity, TFS Skipped)

### Implementation for User Story 2

- [ ] T033 [P] [US2] Extend `SimulatedBoardAdapter.GetBoardsAsync` to return 2 named swimlanes per board in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Teams/SimulatedBoardAdapter.cs`
- [ ] T034 [US2] Extend `AzureDevOpsBoardAdapter.GetBoardsAsync` to call `WorkHttpClient.GetBoardRowsAsync` and populate `SwimLanes` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Teams/AzureDevOpsBoardAdapter.cs`
- [ ] T035 [US2] No extension changes needed — `BoardConfigTeamExtension.ExportAsync` already serializes `BoardConfig.SwimLanes`; confirm serialization test passes — depends on T033
  > O-4: swimlane data is included in the same `BoardConfigExported` progress event added in T030; no separate O-4 task required.
- [ ] T036 [US2] Verify all US2 `[TestMethod]` tests in `BoardConfigTeamExtensionTests.cs` pass

**Checkpoint**: US2 feature file green. Swimlane data present in `board-config.json`.

---

## Phase 5: User Story 3 — Export Card Rule Settings Per Team (Priority: P3)

**Goal**: Add card rule settings to the per-team `board-config.json`.

**Independent Test**: Simulated source has 1 card rule. Run export. Verify `board-config.json → cardRules.rules` has 1 entry.

### Tests (write first — must fail before implementation)

- [ ] T037 [US3] Add `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` methods to `BoardConfigTeamExtensionTests.cs` covering all 4 US3 acceptance scenarios (rules present, board with no rules → null/empty, Simulated validity, TFS Skipped)

### Implementation for User Story 3

- [ ] T038 [P] [US3] Implement `SimulatedBoardAdapter.GetCardRuleSettingsAsync` — returns 1 canned card rule for the first board; returns `null` for others
- [ ] T039 [US3] Implement `AzureDevOpsBoardAdapter.GetCardRuleSettingsAsync` — calls `WorkHttpClient.GetBoardCardRuleSettingsAsync` with `TeamContext` and board name
- [ ] T040 [US3] Extend `BoardConfigTeamExtension.ExportAsync` to call `GetCardRuleSettingsAsync` per board and store result in `TeamBoardConfig.CardRules`
  > O-4: card rules data included in existing `BoardConfigExported` progress event (T030); no separate O-4 task required.
- [ ] T041 [US3] Verify all US3 `[TestMethod]` tests in `BoardConfigTeamExtensionTests.cs` pass

**Checkpoint**: US3 feature file green. Card rules present (or explicitly null) in `board-config.json`.

---

## Phase 6: User Story 4 — Export Backlog Metadata Per Team (Priority: P3)

**Goal**: Add backlog level display names and WIT categories (from the Backlogs endpoint) to `board-config.json`. Must NOT duplicate backlog visibility flags already in `team.json`.

**Independent Test**: Simulated source has 2 backlog levels. Run export. Verify `board-config.json → backlogs` has 2 entries with `name` and `witCategory`. Verify no `backlogVisibilities` field in this file.

### Tests (write first — must fail before implementation)

- [ ] T042 [US4] Add `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` methods to `BoardConfigTeamExtensionTests.cs` covering all 4 US4 acceptance scenarios (standard backlogs, default config, Simulated validity, import-only-metadata not flags)

### Implementation for User Story 4

- [ ] T043 [P] [US4] Implement `SimulatedBoardAdapter.GetBacklogsAsync` — returns 2 canned `BacklogMetadata` entries (e.g. `Epics` + `Stories` with WIT categories)
- [ ] T044 [US4] Implement `AzureDevOpsBoardAdapter.GetBacklogsAsync` — calls `WorkHttpClient.GetBacklogsAsync` with `TeamContext`; maps result to `BacklogMetadata` (name + WIT category only; no visibility flags)
- [ ] T045 [US4] Extend `BoardConfigTeamExtension.ExportAsync` to gate on `Has(ConnectorCapability.Backlogs)`, then call `GetBacklogsAsync` and store result in `TeamBoardConfig.Backlogs`; emit Skipped warning if the capability is absent
  > O-4: backlog count included in `BoardConfigExported` progress event (T030); no separate O-4 task required.
- [ ] T046 [US4] Verify all US4 `[TestMethod]` tests in `BoardConfigTeamExtensionTests.cs` pass; confirm no `backlogVisibilities` field appears in the serialized JSON (C3 — covers FR-010 import negative)

**Checkpoint**: US4 feature file green. Backlog metadata in `board-config.json` with no duplication of visibility flags.

---

## Phase 7: User Story 5 — Export Sprint Taskboard Columns Per Team (Priority: P4)

**Goal**: Add sprint taskboard column definitions to `board-config.json`.

**Independent Test**: Simulated source has 3 custom taskboard columns. Run export. Verify `board-config.json → taskboardColumns` has 3 entries with correct name and order.

### Tests (write first — must fail before implementation)

- [ ] T047 [US5] Add `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` methods to `BoardConfigTeamExtensionTests.cs` covering all 3 US5 acceptance scenarios (custom columns, Simulated validity, TFS Skipped)

### Implementation for User Story 5

- [ ] T048 [P] [US5] Implement `SimulatedBoardAdapter.GetTaskboardColumnsAsync` — returns 3 canned `TaskboardColumn` entries
- [ ] T049 [US5] Implement `AzureDevOpsBoardAdapter.GetTaskboardColumnsAsync` — calls `WorkHttpClient.GetTaskboardColumnsAsync` with `TeamContext`
- [ ] T050 [US5] Extend `BoardConfigTeamExtension.ExportAsync` to gate on `Has(ConnectorCapability.TaskboardColumns)`, then call `GetTaskboardColumnsAsync` and store result in `TeamBoardConfig.TaskboardColumns`; emit Skipped warning if the capability is absent
  > O-4: taskboard column count included in `BoardConfigExported` progress event (T030); no separate O-4 task required.
- [ ] T051 [US5] Verify all US5 `[TestMethod]` tests in `BoardConfigTeamExtensionTests.cs` pass

**Checkpoint**: US5 feature file green. Full `board-config.json` now contains all 5 data types (columns, swimlanes, card rules, backlogs, taskboard).

---

## Phase 8: User Story 6 — Import Board Configuration to Target (Priority: P1)

**Goal**: Read `board-config.json` from the package and apply it to the target team's boards using the configured `importMode` (Replace/Merge/Skip).

**Independent Test**: Export a Simulated team, then run import against `SimulatedBoardAdapter`. Verify `UpdateBoardColumnsAsync` called once per board with matching column data. Verify idempotency (running twice produces same state).

### Tests (write first — must fail before implementation)

- [ ] T052 [US6] Add US6 import `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` methods to `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/BoardConfigTeamExtensionTests.cs` (same class as export, T022) — all 9 US6 acceptance scenarios (columns/swimlanes/card rules/backlogs/taskboard; Replace idempotency; Replace removes extras; Merge preserves extras; Skip no-op; Skip-with-empty applies; board not in target → warning; invalid state mapping → warning; Simulated end-to-end)

### Implementation for User Story 6

- [ ] T053 [P] [US6] Extend `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Teams/SimulatedBoardAdapter.cs` with `Update*` method implementations (created in T024 with stubs) — captures all write calls in-memory for assertion in tests
- [ ] T054 [US6] Extend `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Teams/AzureDevOpsBoardAdapter.cs` with `Update*` method implementations (created in T025 with stubs) — calls `WorkHttpClient` PUT endpoints with `TeamContext`
- [ ] T055 [US6] Implement `BoardConfigTeamExtension.ImportAsync` skeleton in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/Extensions/BoardConfigTeamExtension.cs` — already injects `ITeamBoardAdapter` (non-nullable; capability check handles TFS path); read `board-config.json` from `ctx.Package`; check `Has(ConnectorCapability.BoardConfig)` first; dispatch per `importMode`
- [ ] T055b [US6] Extend `src/DevOpsMigrationPlatform.Abstractions.Agent/Teams/ITeamBoardAdapter.cs` with Merge-mode read methods if not already present: `GetBoardColumnsAsync`, `GetBoardSwimLanesAsync`, `GetCurrentTaskboardColumnsAsync` — these are already in the contract; confirm implementations exist in T053/T054
- [ ] T056 [US6] Implement **Replace** mode in `BoardConfigTeamExtension.ImportAsync` for all 5 types — calls `Update*Async` unconditionally with package data
- [ ] T057 [US6] Implement **Merge** mode in `BoardConfigTeamExtension.ImportAsync` — for each type: call `Get*Async`, compute delta, call `Update*Async` with merged list; preserve target-only entries — depends on T055b
- [ ] T058 [US6] Implement **Skip** mode in `BoardConfigTeamExtension.ImportAsync` — if target has existing config: emit info log and skip; if absent: apply as Replace
- [ ] T059 [US6] Implement missing-board warning (FR-012): if board name from package does not exist on target, log structured warning and continue
- [ ] T060 [US6] Implement invalid-state-mapping warning (FR-013): during column import, omit state mappings referencing absent target states and emit per-column warning
- [ ] T061 [US6] Register `ITeamBoardAdapter` implementations in DI — `AzureDevOpsBoardAdapter` in AzureDevOps connector setup; `SimulatedBoardAdapter` in Simulated connector setup (no `TeamsModule.cs` changes needed — `BoardConfigTeamExtension` receives `ITeamBoardAdapter` via constructor injection)
- [ ] T062 [US6] Add O-1 `ActivitySource` span (`teams.boardconfig.import`), O-2 metrics (`platform.teams.import.boardconfig.count/duration_ms/errors/in_flight/skipped`), and O-3 structured `ILogger` events to `BoardConfigTeamExtension.ImportAsync` per plan.md Observability section
- [ ] T063 [US6] Add O-4 `IProgressSink` progress events via `ctx.ProgressSink` in `BoardConfigTeamExtension.ImportAsync`: emit `{ Stage="BoardConfigImporting", teamSlug, importMode }` at start and `{ Stage="BoardConfigImported", ... }` on completion; emit `{ Stage="BoardConfigImportSkipped", reason }` when capability absent or Skip mode
- [ ] T064 [US6] Verify all 9 US6 `[TestMethod]` tests in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Teams/BoardConfigTeamExtensionTests.cs` pass
- [ ] T070 [US6] Add a `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` method to `BoardConfigTeamExtensionTests.cs`: given a package has board config for team 'Alpha' but team 'Alpha' does not exist in the target, when import runs, board config import for that team is skipped and a structured warning is emitted naming the team (covers FR-017)
- [ ] T071 [US6] Implement absent-team guard in `BoardConfigTeamExtension.ImportAsync`: before applying any board config, verify target team exists; if absent → `LogWarning` and return early
- [ ] T072 [US6] Add a `[TestCategory("CodeTest")]` + `[TestCategory("DomainTests")]` method to `BoardConfigTeamExtensionTests.cs`: given a target board update call returns a permission denied error, when board config import runs, a structured warning is emitted and import continues without aborting (covers EC-3)
- [ ] T073 [US6] Implement permission-denied error handling in `BoardConfigTeamExtension.ImportAsync`: catch `UnauthorizedAccessException` / HTTP 403 from `ITeamBoardAdapter.Update*Async`; emit `LogWarning` and continue to next board

**Checkpoint**: US6 feature file green. End-to-end round-trip (Simulated export → import → target state verified) works. Idempotency scenario passes.

---

## Phase 9: Polish & Cross-Cutting Concerns

**Purpose**: Connector acceptance tests, Simulated round-trip, and documentation.

- [ ] T065 [P] Write `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/Teams/SimulatedBoardAdapterExportTests.cs` — `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Simulated")]` — full round-trip via the Simulated connector: seeded source → export → `board-config.json` → verify JSON shape for all 5 data types
- [ ] T066 [P] Write `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/Teams/SimulatedBoardAdapterImportTests.cs` — `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Simulated")]` — import modes: Replace/Merge/Skip acceptance scenarios against `SimulatedBoardAdapter`
- [ ] T067 [P] Write `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/Teams/AzureDevOpsBoardAdapterTests.cs` (new test project `DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests` following the Simulated.Tests pattern) — `[TestCategory("CodeTest")]` + `[TestCategory("IntegrationTests")]` — contract tests for `AzureDevOpsBoardAdapter` wiring the real adapter to a mocked `WorkHttpClient` in-process (no network)
- [ ] T083 Add `[TestCategory("SystemTest")]` + `[TestCategory("SystemTest_Simulated")]` method to `SimulatedBoardAdapterExportTests.cs` or a new `TeamBoardConfigPerformanceTests.cs`: run export across 10 simulated teams with 2 boards each; assert total elapsed time stays under 5 minutes (SC-001 — scale assertion; the 5-minute figure is the success-criterion target, not the category speed budget)
- [ ] T068 Confirm `schema/migration.schema.json` is regenerated (via `AddSchemaEntry<TeamsModuleOptions>`) to include new `BoardConfig` options section
- [ ] T069 Run quickstart.md validation scenarios (Scenarios 1–5) and confirm all pass

---

## Phase 10: Documentation Sync (MANDATORY — Spec-Completion Gate)

**Purpose**: Constitution v2.0.0 Governance (Spec-Completion Gate + v1.3.4 doc-sync phase)
requires every canonical doc named in a doc-task to be updated, and `discrepancies.md`
fully resolved, before the spec branch may merge. This phase is not optional.

- [ ] T084 Create/update `specs/039-team-board-settings/discrepancies.md` — record any deviations between spec, plan, and implementation (including the ConnectorCapability composite/granular decision and the SC-001 budget note); every entry MUST be marked `Resolved` or `N/A` before merge
- [ ] T085 Update `.agents/30-context/domains/connector-model.md` — document the `ConnectorCapability` mechanism (composite `BoardConfig` = `BoardColumns | BoardRows | CardRules`, plus `Backlogs`, `TaskboardColumns`) and the explicit-registration / no-null-guard rule
- [ ] T086 Update `.agents/30-context/architecture/execution-model.md` (and `execution-contract.md` if affected) — add `BoardConfigTeamExtension` as the worked example of the Extension layer (one `ITeamExtension`, both export+import, capability-gated adapter seam)
- [ ] T087 Update `docs/capabilities-guide.md` (and any connector-coverage doc) — record board configuration export/import as a Teams capability, including the TFS `Skipped` behaviour
- [ ] T088 Review `analysis/pending-actions.md` — remove any item now implemented by this spec
- [ ] T089 Run the Mandatory Compliance Review Loop: re-read each doc touched above against the implementation, fix any drift, repeat until zero violations

**Checkpoint**: All doc-tasks `[x]`, `discrepancies.md` fully `Resolved`/`N/A`, `pending-actions.md` pruned. Spec branch is now mergeable.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No dependencies — start immediately. Parallel tasks T002–T007 run in parallel.
- **Phase 2 (Foundational)**: Depends on Phase 1 (T012 required for T016 DI registration). **BLOCKS all story phases.**
- **Phase 3 (US1)**: Depends on Phase 2 complete. T022 (feature file) must be written before T023–T026.
- **Phase 4 (US2)**: Depends on Phase 3 complete (orchestrator skeleton and SimulatedBoardAdapter exist).
- **Phase 5 (US3)**: Depends on Phase 3 complete. Can start in parallel with Phase 4.
- **Phase 6 (US4)**: Depends on Phase 3 complete. Can start in parallel with Phases 4–5.
- **Phase 7 (US5)**: Depends on Phase 3 complete. Can start in parallel with Phases 4–6.
- **Phase 8 (US6)**: Depends on all export phases (3–7) complete — reads artefacts they write.
- **Phase 9 (Polish)**: Depends on Phase 8 complete.
- **Phase 10 (Documentation Sync)**: Depends on Phase 9 complete. MANDATORY Spec-Completion Gate — branch MUST NOT merge until all doc-tasks are `[x]` and `discrepancies.md` is fully resolved.

### User Story Dependencies

| Story | Priority | Depends on | Parallel with |
|---|---|---|---|
| US1 (Export Columns) | P1 | Phase 2 | — |
| US2 (Export Swimlanes) | P2 | US1 complete | US3, US4, US5 |
| US3 (Export Card Rules) | P3 | US1 complete | US2, US4, US5 |
| US4 (Export Backlogs) | P3 | US1 complete | US2, US3, US5 |
| US5 (Export Taskboard) | P4 | US1 complete | US2, US3, US4 |
| US6 (Import All) | P1 | US1–US5 complete | — |

### Within Each Phase

- Failing `[TestMethod]` tests MUST be written first — they must fail before any production code is added
- Abstractions before implementations
- Orchestrator before module wiring
- Module wiring before DI registration
- Tests pass before moving to next phase

### Parallel Opportunities

- T002–T007 (all records) run simultaneously
- T014+T015 (capability enum + interface) run simultaneously
- T018+T019+T020 (DI registrations per connector) run simultaneously after T016+T017
- T023+T024 (orchestrator skeleton + SimulatedBoardAdapter) run simultaneously
- T032+T033 (Simulated + Azure swimlane extensions) run simultaneously
- T037+T038, T042+T043, T047+T048 — same pattern for each subsequent story
- T052+T053 (SimulatedBoardAdapter + AzureDevOpsBoardAdapter skeletons) run simultaneously
- T064+T065+T066 (all connector test files) run simultaneously

---

## Parallel Example: Phase 3 (US1)

```
# Group 1 — write failing tests first:
T022: Write BoardConfigTeamExtensionTests.cs with failing [TestMethod] methods for US1

# Group 2 — after T022, run simultaneously:
T023: Create BoardConfigTeamExtension.cs skeleton
T024: Create SimulatedBoardAdapter.cs (GetBoardsAsync columns)

# Sequential — depends on T023 + T024:
T025: Complete AzureDevOpsBoardAdapter.GetBoardsAsync
T026: Complete BoardConfigTeamExtension.ExportAsync serialization + persist

# Sequential — depends on T026:
T027: Register BoardConfigTeamExtension in TeamsServiceCollectionExtensions
T029: Add observability (spans, metrics, logging)
T030: Add O-4 progress events
T031: Verify all 5 US1 test methods green
```

---

## Implementation Strategy

### MVP First (US1 + US6 columns only)

1. Complete Phase 1 (Setup) — all records and interfaces
2. Complete Phase 2 (ConnectorCapability) — CRITICAL blocker
3. Complete Phase 3 (US1 — Export Columns)
4. Complete Phase 8 up to T055 (US6 — Import Columns, Replace mode only)
5. **STOP and VALIDATE**: Export + import column round-trip with Simulated connector
6. Demo to stakeholders — board columns migrate end-to-end

### Incremental Delivery (add each export story, then full import)

1. MVP above → US1 columns + US6 columns import
2. Add US2 (swimlanes) → extend `board-config.json`, extend import
3. Add US3 (card rules) → extend `board-config.json`, extend import
4. Add US4 (backlogs) → extend `board-config.json`, extend import
5. Add US5 (taskboard) → extend `board-config.json`, extend import
6. Complete US6 Merge + Skip modes
7. Phase 9 polish

### Single-Developer Sequential

1. Phase 1 → Phase 2 → Phase 3 → Phase 4 → Phase 5 → Phase 6 → Phase 7 → Phase 8 → Phase 9

---

## Notes

- `[P]` tasks = different files, no incomplete dependencies — safe to run in parallel
- Tests MUST be written before production code and MUST initially fail (test-first). No new `.feature` files — existing `.feature` files are legacy and must not be modified.
- Every `[TestMethod]`/`[TestClass]` MUST carry its parent family tag (`CodeTest` or `SystemTest`) AND its specific category tag (see Test categories above) — Touch = Tag hard gate.
- TFS connector explicitly registers `TfsConnectorCapabilityProvider` → `ConnectorCapability.None` and `TfsNullBoardAdapter` → `ITeamBoardAdapter`; no null-guard in extension code (guardrail Rule 29)
- `board-config.json` is separate from `team.json` — backlog visibility flags stay in `team.json`
- `importMode` applies uniformly to all 5 board config types (no per-type mode flag)
- Commit after each checkpoint to preserve incremental progress
- Quickstart.md validation (T068) is the final gate before marking the feature complete
