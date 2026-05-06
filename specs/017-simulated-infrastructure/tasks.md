# Tasks: Simulated Infrastructure Connector

**Input**: Design documents from `specs/017-simulated-infrastructure/`
**Prerequisites**: plan.md ✅ | spec.md ✅ | research.md ✅ | data-model.md ✅ | contracts/interface-changes.md ✅

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: Which user story this task belongs to
- Exact file paths are included in each description

---

## Phase 1: Setup

**Purpose**: Create the new `Infrastructure.Simulated` project, add it to the solution, and wire it into the build.

- [X] T001 Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/DevOpsMigrationPlatform.Infrastructure.Simulated.csproj` targeting `net10.0` with references to `Abstractions` and `Infrastructure`
- [X] T002 Add `Infrastructure.Simulated` project to `DevOpsMigrationPlatform.slnx`
- [X] T003 Create `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests.csproj` (MSTest v3, references Infrastructure.Simulated and test helpers)
- [X] T004 Add `Infrastructure.Simulated.Tests` project to `DevOpsMigrationPlatform.slnx`
- [X] T005 Verify `dotnet build` passes with the new empty projects before any feature code is added

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core changes to `Abstractions` and shared `Infrastructure` that every user story depends on. No user story implementation can start until this phase is complete.

**⚠️ CRITICAL**: All tasks in this phase must be complete and the build must pass before Phase 3+ begins.

### Abstractions changes

- [X] T006 Change `src/DevOpsMigrationPlatform.Abstractions/Options/MigrationEndpointOptions.cs` from `sealed class` to `abstract class` containing only `public string Type { get; set; }` — remove all other properties
- [X] T007 Change `src/DevOpsMigrationPlatform.Abstractions/Models/OrganisationEntry.cs` from concrete to `abstract class` containing only `Type`, `Projects`, and `Enabled` — remove connector-specific fields
- [X] T008 Change `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemRevisionSourceFactory.cs` — new signature: `Task<IWorkItemRevisionSource> CreateAsync(MigrationEndpointOptions endpoint, CancellationToken ct)`
- [X] T009 Change `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemImportTargetFactory.cs` — new signature: `Task<IWorkItemImportTarget> CreateAsync(MigrationEndpointOptions endpoint, CancellationToken ct)`

### Polymorphic registry in shared `Infrastructure`

- [X] T010 Create `src/DevOpsMigrationPlatform.Infrastructure/Serialization/EndpointOptionsTypeRegistry.cs` — singleton; `Register(string key, Type type)` throws on duplicate; `TryGetType(string key, out Type? type)` returns false for unknown keys
- [X] T011 [P] Create `src/DevOpsMigrationPlatform.Infrastructure/Serialization/PolymorphicEndpointOptionsConverter.cs` — `JsonConverter<MigrationEndpointOptions>` that reads `type` field first, looks up in `EndpointOptionsTypeRegistry`, then deserialises the full JSON into the concrete type
- [X] T012 [P] Create `src/DevOpsMigrationPlatform.Infrastructure/Serialization/PolymorphicOrganisationEntryConverter.cs` — same pattern as T011 but for `OrganisationEntry`
- [X] T013 Create `src/DevOpsMigrationPlatform.Infrastructure/Extensions/EndpointOptionsRegistrationExtensions.cs` — `AddEndpointOptionsType(this IServiceCollection, string key, Type type)` and `AddOrganisationEntryType` helpers that register into `EndpointOptionsTypeRegistry`
- [X] T014 Wire `EndpointOptionsTypeRegistry` as a singleton and register both converters into the shared `JsonSerializerOptions` configuration in `src/DevOpsMigrationPlatform.Infrastructure/InfrastructureServiceCollectionExtensions.cs` (or equivalent startup entry point)

### `AzureDevOpsEndpointOptions` derived type

- [X] T015 Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Options/AzureDevOpsEndpointOptions.cs` — `sealed class` inheriting `MigrationEndpointOptions`; carries `Url`, `ResolvedUrl`, `Project`, `ApiVersion`, `Authentication`
- [X] T016 [P] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Options/AzureDevOpsOrganisationEntry.cs` — `sealed class` inheriting `OrganisationEntry`; carries `Url`, `ResolvedUrl`, `ApiVersion`, `Authentication`
- [X] T017 Move `src/DevOpsMigrationPlatform.Abstractions/Models/OrganisationEndpoint.cs` to `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Models/OrganisationEndpoint.cs` and change to `internal`
- [X] T018 Register `AzureDevOpsEndpointOptions` and `AzureDevOpsOrganisationEntry` via `AddEndpointOptionsType` / `AddOrganisationEntryType` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/AzureDevOpsServiceCollectionExtensions.cs`

### `TeamFoundationServerEndpointOptions` derived type (best-effort)

- [X] T019 [P] Create `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Options/TeamFoundationServerEndpointOptions.cs` — `sealed class` inheriting `MigrationEndpointOptions`; carries `Url`, `ResolvedUrl`, `Project`, `ApiVersion`, `Authentication` (net481 best-effort)
- [X] T020 [P] Create `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Options/TeamFoundationServerOrganisationEntry.cs` — `sealed class` inheriting `OrganisationEntry` (net481 best-effort)
- [X] T020a Update `src/DevOpsMigrationPlatform.Infrastructure.TfsObjectModel/Export/TfsWorkItemRevisionSourceFactory.cs` — change `CreateAsync` signature to `(MigrationEndpointOptions endpoint, CancellationToken ct)`; cast to `TeamFoundationServerEndpointOptions` internally with `ArgumentException` on type mismatch (net481 best-effort; compile under `net481` build)
- [X] T020b Register `TeamFoundationServerEndpointOptions` and `TeamFoundationServerOrganisationEntry` via `AddEndpointOptionsType` / `AddOrganisationEntryType` in the TFS assembly's DI extension (`AddTfsWorkItemExport()` or equivalent); gate registration on `net10.0` build only if the registry is not available on `net481`

### ADO factory updates to accept base type

- [X] T021 Update `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Export/AzureDevOpsWorkItemRevisionSourceFactory.cs` — change `CreateAsync` signature to `(MigrationEndpointOptions endpoint, CancellationToken ct)`; cast to `AzureDevOpsEndpointOptions` internally with `ArgumentException` on type mismatch
- [X] T022 Update `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsWorkItemImportTargetFactory.cs` — change signature to `(MigrationEndpointOptions endpoint, CancellationToken ct)`; remove `"Simulated"` routing branch entirely; cast to `AzureDevOpsEndpointOptions`; register as keyed service `"AzureDevOpsServices"`
- [X] T023 Update `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Import/AzureDevOpsResolutionStrategyFactory.cs` — remove `if (target is SimulatedWorkItemImportTarget)` branch; register as keyed service `"AzureDevOpsServices"`

### `CatalogService` relocation

- [X] T024 Move `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/CatalogService.cs` to `src/DevOpsMigrationPlatform.Infrastructure/Services/CatalogService.cs` — no code changes needed; only namespace update
- [X] T025 Remove `CatalogService` from ADO DI registration; add it to `Infrastructure` DI registration

### `WorkItemsModule` connector-agnostic cleanup

- [X] T026 Update `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` `ExportAsync` — remove all reads of `job.Source.Url`, `job.Source.Project`, `job.Source.Authentication`; remove construction of `OrganisationEndpoint`; pass `job.Source!` directly to `_sourceFactory.CreateAsync`
- [X] T027 Update `src/DevOpsMigrationPlatform.Infrastructure/Modules/WorkItemsModule.cs` `ImportAsync` — remove connector-specific field reads from `job.Target`; pass `job.Target!` directly to `_targetFactory.CreateAsync`

**Build gate**: `dotnet clean && dotnet build --no-incremental` MUST pass after T027 before proceeding to Phase 3.

---

## Phase 3: User Story 5 — ADO boundary cleanup (Priority: P2) [enables US1+US2]

**Goal**: ADO assembly has zero knowledge of Simulated; keyed DI routes correctly before the Simulated assembly is created.

**Independent Test**: Run `scenarios/queue-import-workitems-simulated-fixture.json` — must still pass after the ADO leaks are removed. The keyed `"Simulated"` service is not yet registered so jobs with `Target.Type: "Simulated"` will fail clearly with a missing-service error, not silently.

- [X] T028 [US5] Create `features/import/work-items/simulated-boundary-cleanup.feature` — Gherkin scenarios from spec.md User Story 5 acceptance scenarios (see `.agents/guardrails/acceptance-test-format.md`)
- [X] T029 [P] [US5] Write unit test `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/Import/AzureDevOpsWorkItemImportTargetFactoryTests.cs` — assert that `CreateAsync` with a `SimulatedEndpointOptions` throws `ArgumentException` (not `NotImplementedException`)
- [X] T030 [P] [US5] Write unit test `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/Import/AzureDevOpsResolutionStrategyFactoryTests.cs` — assert that `CreateAsync` does not accept a `SimulatedWorkItemImportTarget` instance (pass a mock ADO target; verify `NullResolutionStrategy` is NOT returned)

**Checkpoint**: ADO assembly is now clean. Existing ADO system tests must still pass.

---

## Phase 4: User Story 4 — Polymorphic endpoint config deserialization (Priority: P2)

**Goal**: `MigrationEndpointOptions` deserializes to the correct derived type for ADO, TFS, and Simulated configs. Unknown types fail with a clear error.

**Independent Test**: Unit tests in `DevOpsMigrationPlatform.Infrastructure.Tests` deserialise three JSON strings and assert the concrete C# type.

- [X] T032 [US4] Create `features/platform/config/polymorphic-endpoint-config.feature` — Gherkin scenarios from spec.md User Story 4 acceptance scenarios
- [X] T033 [P] [US4] Write unit test `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Serialization/EndpointOptionsTypeRegistryTests.cs` — `Register` duplicate key throws; `TryGetType` unknown key returns false
- [X] T034 [P] [US4] Write unit test `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Serialization/PolymorphicEndpointOptionsConverterTests.cs` — deserialise JSON with `"Type": "AzureDevOpsServices"` → `AzureDevOpsEndpointOptions`; `"Type": "Simulated"` → `SimulatedEndpointOptions`; unknown type → `JsonException` with discriminator value in message
- [X] T035 [US4] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedEndpointOptions.cs` — `sealed class` inheriting `MigrationEndpointOptions`; `Generator: SimulatedGeneratorConfig`
- [X] T036 [P] [US4] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedOrganisationEntry.cs` — `sealed class` inheriting `OrganisationEntry`; same `Generator` field
- [X] T037 [P] [US4] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedGeneratorConfig.cs` with `List<SimulatedProjectConfig> Projects`
- [X] T038 [P] [US4] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedProjectConfig.cs` with `Name`, `WorkItemTypes`, `LinkTopology`, `AttachmentSizeKb`, `HasComments`, `HasEmbeddedImages`
- [X] T039 [P] [US4] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Options/SimulatedWorkItemTypeConfig.cs` with `Type`, `Count`, `RevisionsPerItem`
- [X] T040 [US4] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` with real (non-throwing) empty method bodies for `AddSimulatedWorkItemExport()`, `AddSimulatedWorkItemImport()`, and `AddSimulatedDependencyAnalysis()`; call `services.AddEndpointOptionsType("Simulated", typeof(SimulatedEndpointOptions))` and `services.AddOrganisationEntryType("Simulated", typeof(SimulatedOrganisationEntry))` in `AddSimulatedWorkItemExport()` — **do NOT use `throw new NotImplementedException()`**
- [X] T041 [US4] Verify tests T033/T034 pass with the registry wired in the test harness using `AddSimulatedWorkItemExport()`

**Checkpoint**: Config deserialization routes correctly. Unit tests pass.

---

## Phase 5: User Story 1 — Simulated export (Priority: P1) 🎯 MVP

**Goal**: Operator can run `queue-export-workitems-simulated-source.json` and get a valid migration package written to disk with no network calls.

**Independent Test**: Launch profile `queue-export-workitems-simulated-source` runs end-to-end; `output/<run>/WorkItems/` contains revision folders in lexicographic order.

### Feature file

- [X] T042 [US1] Create `features/export/work-items/simulated-export.feature` — Gherkin scenarios from spec.md User Story 1 acceptance scenarios

### Generator services

- [X] T043 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedWorkItemRevisionSource.cs` — `IWorkItemRevisionSource` implementation; `GetRevisionsAsync()` uses `yield return` to stream `WorkItemRevision` records from `SimulatedEndpointOptions.Generator`; deterministic field values seeded by `(workItemId, revisionIndex)`; validates `RevisionsPerItem >= 1` and throws `InvalidOperationException` at construction if violated
- [X] T044 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Export/SimulatedWorkItemRevisionSourceFactory.cs` — `IWorkItemRevisionSourceFactory`; casts `MigrationEndpointOptions` to `SimulatedEndpointOptions`; throws `ArgumentException` on type mismatch
- [X] T045 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedProjectDiscoveryService.cs` — `IProjectDiscoveryService`; returns `Generator.Projects[].Name` as `IAsyncEnumerable<string>`
- [X] T046 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemDiscoveryService.cs` — `IWorkItemDiscoveryService`; returns deterministic work item IDs and revision counts from `Generator.Projects[].WorkItemTypes[].Count × RevisionsPerItem`
- [X] T047 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedAttachmentBinarySource.cs` — `IAttachmentBinarySource`; returns `AttachmentSizeKb × 1024` deterministic bytes (seeded by attachment ID + filename); returns empty stream if `AttachmentSizeKb == 0`
- [X] T048 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedEmbeddedImageDownloader.cs` — `IEmbeddedImageDownloader`; returns a minimal 1×1 placeholder PNG byte array for any URL; never makes a network call
- [X] T049 [P] [US1] Verify `IWorkItemCommentSource` and `IWorkItemCommentSourceFactory` exist in `Abstractions`; if either is missing, create the interface(s) in `src/DevOpsMigrationPlatform.Abstractions/Services/` before proceeding. Then create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemCommentSource.cs` — `IWorkItemCommentSource`; streams N synthetic comments where N is derived from the generator config; streams zero comments when `HasComments: false`
- [X] T050 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemCommentSourceFactory.cs` — `IWorkItemCommentSourceFactory`; casts to `SimulatedEndpointOptions`
- [X] T051 [US1] Register all export services in `AddSimulatedWorkItemExport()` in `SimulatedServiceCollectionExtensions.cs`

### Unit tests

- [X] T052 [P] [US1] Write `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/Export/SimulatedWorkItemRevisionSourceTests.cs` — assert streaming (no buffering), deterministic output, `RevisionsPerItem=0` throws, empty `Projects` yields zero records
- [X] T053 [P] [US1] Write `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/Services/SimulatedProjectDiscoveryServiceTests.cs` — assert project names match config

### Scenario config + launch profile

- [X] T054 [US1] Create `scenarios/queue-export-workitems-simulated-source.json` with 2 projects, 3 work item types, 5 revisions each, `LinkTopology: "Tree"`, `HasComments: true`, `AttachmentSizeKb: 10`
- [X] T055 [US1] Add `.vscode/launch.json` debug profile `queue-export-workitems-simulated-source` pointing at `scenarios/queue-export-workitems-simulated-source.json`

### System test

- [X] T056 [US1] Write `tests/DevOpsMigrationPlatform.SystemTests/Features/Export/SimulatedExportSystemTest.feature` — `[TestCategory("SystemTest")]` asserting the scenario config runs end-to-end and produces a non-empty `WorkItems/` folder with valid `revision.json` files

**Checkpoint**: User Story 1 fully functional and independently testable.

---

## Phase 6: User Story 2 — Simulated import (Priority: P1)

**Goal**: Developer can run an import job with `Target.Type: "Simulated"` and get observable progress without writing to any external system.

**Independent Test**: `scenarios/queue-import-workitems-simulated-fixture.json` continues to pass; new `scenarios/queue-import-workitems-simulated-target.json` also passes.

### Feature file

- [X] T057 [US2] Create `features/import/work-items/simulated-import.feature` — Gherkin scenarios from spec.md User Story 2 acceptance scenarios

### Import services

- [X] T058 [US2] Move `src/DevOpsMigrationPlatform.Infrastructure/Import/SimulatedWorkItemImportTarget.cs` to `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedWorkItemImportTarget.cs`; update namespace; ensure it assigns sequential IDs and emits progress events
- [X] T059 [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedWorkItemImportTargetFactory.cs` — `IWorkItemImportTargetFactory`; casts to `SimulatedEndpointOptions`; registered as keyed service `"Simulated"`
- [X] T060 [P] [US2] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Import/SimulatedResolutionStrategyFactory.cs` — `IWorkItemResolutionStrategyFactory`; always returns `NullResolutionStrategy`; registered as keyed service `"Simulated"`
- [X] T061 [US2] Register import services in `AddSimulatedWorkItemImport()` in `SimulatedServiceCollectionExtensions.cs`

### Unit tests

- [X] T062 [P] [US2] Write `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/Import/SimulatedWorkItemImportTargetTests.cs` — assert sequential ID assignment, progress events emitted per item, no external I/O

### Scenario config + launch profile

- [X] T063 [US2] Create `scenarios/queue-import-workitems-simulated-target.json` using `scenarios/testdata/workitems-simulated-small.zip` as input (a small pre-built fixture zip; create this fixture as part of this task by running the simulated export profile once and zipping the output into `scenarios/testdata/`); `Target.Type: "Simulated"`
- [X] T064 [US2] Add `.vscode/launch.json` debug profile `queue-import-workitems-simulated-target`

### System test

- [X] T065 [US2] Add simulated import scenario to `tests/DevOpsMigrationPlatform.SystemTests/Features/Import/SimulatedImportSystemTest.feature` — `[TestCategory("SystemTest")]` asserting the existing fixture scenario and the new target scenario both complete with zero errors

**Checkpoint**: User Story 2 fully functional and independently testable.

---

## Phase 7: User Story 3 — Roundtrip (Priority: P2)

**Goal**: A single `Both`-mode job runs export then import with Simulated on both ends. The package passes validation.

**Independent Test**: `scenarios/roundtrip-simulated.json` via new launch profile completes without error.

- [X] T066 [US3] Create `features/export/work-items/simulated-roundtrip.feature` — Gherkin scenarios from spec.md User Story 3 acceptance scenarios
- [X] T067 [P] [US3] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/Services/SimulatedWorkItemLinkAnalysisService.cs` — `IWorkItemLinkAnalysisService` keyed `"Simulated"`; resolves the existing `NotSupportedException` in `DependencyDiscoveryService`; returns empty link analysis for Simulated jobs
- [X] T068 [P] [US3] Register link analysis in `AddSimulatedDependencyAnalysis()` in `SimulatedServiceCollectionExtensions.cs`
- [X] T069 [US3] Create `scenarios/roundtrip-simulated.json` — `Mode: "Both"`, Simulated source (small: 5 items × 2 revisions), Simulated target
- [X] T070 [US3] Add `.vscode/launch.json` debug profile `roundtrip-simulated`
- [ ] T071 [US3] Add roundtrip acceptance scenario to `tests/DevOpsMigrationPlatform.SystemTests/Features/Export/SimulatedExportSystemTest.feature` asserting `Both` mode completes and package passes validation

**Checkpoint**: Full roundtrip works without credentials.

---

## Phase 8: Documentation Sync (Mandatory)

**Purpose**: Resolve all discrepancies logged in `discrepancies.md` by updating canonical docs.

- [X] T072 Update `docs/architecture.md` — remove `OrganisationEndpoint` from Abstractions type table (Discrepancy 2); move `CatalogService` from ADO to shared `Infrastructure` component list (Discrepancy 4); add `DevOpsMigrationPlatform.Infrastructure.Simulated` to assembly table (Discrepancy 5); remove `SimulatedWorkItemImportTarget` from shared `Infrastructure` list (Discrepancy 6)
- [X] T073 Update `docs/module-development-guide.md` — update `IWorkItemRevisionSourceFactory` and `IWorkItemImportTargetFactory` interface signatures to `CreateAsync(MigrationEndpointOptions endpoint, CancellationToken ct)` (Discrepancy 1)
- [X] T074 [P] Add "Polymorphic Endpoint Config" section to `docs/configuration-reference.md` — explain `type` discriminator, connector-specific fields in derived types, unknown type error, `EndpointOptionsTypeRegistry` (Discrepancy 3)
- [X] T075 Mark all 6 discrepancies in `specs/017-simulated-infrastructure/discrepancies.md` as `Resolved`
- [ ] T076 Review `analysis/pending-actions.md` — mark any actions completed by this feature as done; add new actions if outstanding work is identified

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T077 [P] Add `InternalsVisibleTo` attribute to `Infrastructure.Simulated.csproj` for `Infrastructure.Simulated.Tests`
- [X] T078 [P] Verify `DevOpsMigrationPlatform.Infrastructure.TfsObjectModel` still compiles cleanly after `MigrationEndpointOptions` and `OrganisationEntry` become abstract — TFS subprocess consumes `net481` builds
- [X] T079 [P] Run `dotnet clean && dotnet build --no-incremental` — confirm zero errors and zero new warnings
- [X] T080 Run `dotnet test` — confirm all tests pass including new `[TestCategory("SystemTest")]` tests
- [ ] T081 Run scenario `scenarios/queue-export-workitems-simulated-source.json` via launch profile — confirm observable output and non-empty package

---

## Dependencies

```
Phase 1 (Setup)
  → Phase 2 (Foundational) — T006–T027 must all complete before Phase 3+
    → Phase 3 (US5 boundary) — can start as soon as T022/T023 are done
    → Phase 4 (US4 polymorphic config) — needs T010–T014; T035 needs T035 option types
    → Phase 5 (US1 export) — needs Phase 4 complete (T035–T040)
    → Phase 6 (US2 import) — needs Phase 5 complete (SimulatedWorkItemImportTarget move, T058)
      → Phase 7 (US3 roundtrip) — needs Phase 5 + Phase 6 complete
        → Phase 8 (Docs) — needs all user story phases complete
          → Phase 9 (Polish) — final gate
```

### Parallel opportunities within phases

**Phase 2**: T011 ∥ T012 (both converters); T015 ∥ T016 (ADO options types); T019 ∥ T020 (TFS options types)
**Phase 4**: T033 ∥ T034 (registry tests); T036 ∥ T037 ∥ T038 ∥ T039 (Simulated config model types)
**Phase 5**: T045 ∥ T046 ∥ T047 ∥ T048 ∥ T049 ∥ T050 (all export services after T043/T044)
**Phase 6**: T059 ∥ T060 (import factory + resolution strategy)
**Phase 7**: T067 ∥ T068 (link analysis service + registration)
**Phase 8**: T072 ∥ T073 ∥ T074 (independent doc files)

---

## Independent Test Criteria per User Story

| User Story | Independent Test | Config / Command |
|---|---|---|
| US1 — Simulated export | Package written to disk; no network calls | `scenarios/queue-export-workitems-simulated-source.json` |
| US2 — Simulated import | Items reported as created; progress emitted; no ADO writes | `scenarios/queue-import-workitems-simulated-fixture.json` + `simulated-target.json` |
| US3 — Roundtrip | `Both` mode completes; package passes validation | `scenarios/roundtrip-simulated.json` |
| US4 — Polymorphic config | Unit tests pass; ADO/TFS/Simulated JSON deserialises to correct types | `dotnet test --filter PolymorphicEndpointOptionsConverter` |
| US5 — ADO boundary | No Simulated references in ADO assembly; existing fixture test passes | `scenarios/queue-import-workitems-simulated-fixture.json` |

---

## Implementation Strategy

**MVP scope**: Complete Phase 2 + Phase 3 + Phase 5 (US1 export) first. This unlocks offline testing for any developer with zero credentials.

**Phase 2 is the highest-risk phase** — it touches `Abstractions`, shared `Infrastructure`, and all existing connector factories. Treat it as a single atomic commit; do not leave the build broken between tasks.

**Never leave `throw new NotImplementedException()`** in any reachable code path. All Simulated implementations must be complete and functional before being committed.

**Validation**: After T027, after T041, and after T056 — run `dotnet clean && dotnet build --no-incremental` and `dotnet test` before proceeding.
