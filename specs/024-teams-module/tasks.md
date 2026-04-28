# Tasks: IdentitiesModule, NodeStructureModule & TeamsModule

**Input**: Design documents from `/specs/024-teams-module/`
**Prerequisites**: plan.md (required), spec.md (required for user stories)

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story. All three connectors (Simulated, AzureDevOpsServices, TeamFoundationServer) are covered.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US0, US0b, US1)
- Include exact file paths in descriptions

---

## Phase 0: Research & Unknowns Resolution

**Purpose**: Resolve API surface unknowns before implementation. Document findings in `specs/024-teams-module/research.md`.

- [ ] T000a [P] Research TFS Identity API surface — confirm `IIdentityManagementService2.ReadIdentities()` works for both users and groups via TFS OM subprocess bridge
- [ ] T000b [P] Research TFS Teams API surface — confirm `TfsTeamService.QueryTeams()`, `GetTeamMembers()`, team settings access. Document version-specific limitations.
- [ ] T000c [P] Research ADO Teams REST API endpoints — confirm `_apis/projects/{project}/teams`, `_apis/work/teamsettings`, `_apis/work/teamsettings/iterations`, `_apis/work/teamsettings/iterations/{id}/capacities`, `_apis/projects/{project}/teams/{team}/members`
- [ ] T000d [P] Research ADO Identity REST API — confirm Graph API or Identity Picker API for enumerating all project identities
- [ ] T000e [P] Research default team detection — confirm `isDefaultTeam` flag (ADO) and `TfsTeamService` default team property (TFS)
- [ ] T000f [P] Research team area paths API — confirm `_apis/work/teamsettings/teamfieldvalues` endpoint
- [ ] T000g Compile research findings into `specs/024-teams-module/research.md`

**Checkpoint**: All API unknowns resolved. research.md produced.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Options classes, interface stubs, and DI wiring for all three modules

- [ ] T001 [P] [US0] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/IdentitiesModuleOptions.cs` — sealed class with `init`-only properties: `Enabled` (bool), `DefaultIdentity` (string), `SectionName = "MigrationPlatform:Modules:Identities"`
- [ ] T002 [P] [US0b] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/NodeStructureModuleOptions.cs` — sealed class with `init`-only properties: `Enabled` (bool), `ReplicateSourceTree` (bool), `AutoCreateNodes` (bool), `SectionName = "MigrationPlatform:Modules:Nodes"`
- [ ] T003 [P] [US1] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Modules/TeamsModuleOptions.cs` — sealed class with `init`-only properties: `Enabled` (bool), `Extensions` (dictionary for enabling/disabling sub-extensions), `SectionName = "MigrationPlatform:Modules:Teams"`
- [ ] T004 [P] [US0] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IIdentitySource.cs` — connector abstraction: `IAsyncEnumerable<IdentityDescriptor> EnumerateIdentitiesAsync(string projectName, CancellationToken ct)`
- [ ] T005 [P] [US1] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ITeamSource.cs` — connector abstraction: `IAsyncEnumerable<TeamDefinition> EnumerateTeamsAsync(string projectName, CancellationToken ct)`, `GetTeamSettingsAsync`, `GetTeamIterationsAsync`, `GetTeamMembersAsync`, `GetTeamCapacityAsync`, `GetTeamAreaPathsAsync`
- [ ] T006 [P] [US1] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/ITeamTarget.cs` — connector abstraction: `CreateOrUpdateTeamAsync`, `SetTeamSettingsAsync`, `AssignIterationAsync`, `AddMemberAsync`, `SetCapacityAsync`, `SetAreaPathsAsync`

**Checkpoint**: All abstractions in place — module and connector work can begin.

---

## Phase 2: Foundational (Prerequisite Refactoring)

**Purpose**: Address architecture-review findings (R1–R6) before new module code is written. Each refactoring is a self-contained commit. All existing tests must pass after each step.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T007 [R1] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IReferencedPathTracker.cs` — extract interface from `ReferencedPathTracker.cs` with methods `RecordAreaPathAsync`, `RecordIterationPathAsync`, `FlushAsync`. Update all consumers in `src/DevOpsMigrationPlatform.Infrastructure.Agent/` to depend on interface. Resolves MM-C3, VS-C1, VS-H3, MM-H1.
- [ ] T008 [R2] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/IClassificationTreeCapture.cs` — extract interface from `ClassificationTreeCapture.cs` with method `CaptureAsync`. Update DI and consumers. Resolves MM-C3, MM-H1.
- [ ] T009 [R3] Create `src/DevOpsMigrationPlatform.Abstractions.Agent/Tools/INodeEnsurer.cs` — extract interface from `NodeEnsurer.cs` with methods `ReplicateSourceTreeAsync`, `EnsureReferencedPathsAsync`. Update DI and consumers. Resolves MM-C2, MM-M2, CA-M1.
- [ ] T010 [R4] Rename `INodeStructureTool` → `INodeTranslationTool` and `NodeStructureTool` → `NodeTranslationTool` across ~32 files. Update DI registration key. Configuration section `MigrationPlatform:Tools:NodeStructure` remains unchanged. Resolves SA clarity.
- [ ] T011 [R5] Add `ProjectMapping` overload to `IRevisionFolderProcessorFactory` in `src/DevOpsMigrationPlatform.Abstractions.Agent/` and implement in `RevisionFolderProcessorFactory.cs`. Remove type-checking anti-pattern in `WorkItemsModule.cs` (line 219–227). Resolves MM-C1, MM-M1.
- [ ] T012 [R6] Fix `ReferencedPathTracker` DI lifetime from Transient to Singleton in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Tools/NodeStructure/NodeStructureToolServiceCollectionExtensions.cs`. Resolves VS-H3.

**Checkpoint**: All architecture violations resolved. Existing tests pass. New module work can begin.

---

## Phase 3: User Story 0 — IdentitiesModule (Priority: P0) 🎯 MVP

**Goal**: Export identity descriptors from source, build cross-cutting `IIdentityMappingService` for all downstream modules.

**Independent Test**: Export identities → verify `Identities/` folder → load mapping.json → confirm `Resolve()` returns correct target identity.

### Gherkin Feature Files for US 0 (mandatory)

- [ ] T013 [US0] Create `features/export/identities/export-identity-descriptors.feature` — translate spec.md US 0 acceptance scenarios S1, S5 into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)
- [ ] T014 [P] [US0] Create `features/import/identities/identity-mapping-resolution.feature` — translate spec.md US 0 acceptance scenarios S2, S3, S4, S6 into conformant Gherkin

### Implementation for US 0

- [ ] T015 [US0] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/IdentitiesModule.cs` — implement `IModule` with `Name = "Identities"`. `ExportAsync` (stream descriptors via `IIdentitySource` → write `Identities/descriptors.jsonl`), `ImportAsync` (build mapping from `mapping.json` + `descriptors.jsonl` + target directory lookup via Prepare phase), `ValidateAsync`
- [ ] T016 [US0] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityMappingService.cs` — full `IIdentityMappingService` implementation: load `mapping.json` overrides, automatic UPN/display name matching, default identity fallback, write `unresolved.json`
- [ ] T017 [P] [US0] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Identity/IdentityServiceCollectionExtensions.cs` — `AddIdentitiesModule()` registering module + options + mapping service
- [ ] T018 [P] [US0] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedIdentitySource.cs` — deterministic identity generation: produce N users + M groups from seed
- [ ] T019 [P] [US0] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/AzureDevOpsIdentitySource.cs` — Graph/Identity REST API: enumerate project identities (users + groups)
- [ ] T020 [P] [US0] Create TFS identity export handler in `src/DevOpsMigrationPlatform.CLI.TfsExport/TfsIdentityExportHandler.cs` — TFS OM `IIdentityManagementService2.ReadIdentities()` for users and groups
- [ ] T021 [US0] Create TFS identity bridge command in `src/DevOpsMigrationPlatform.TfsMigrationAgent/` — subprocess bridge command to invoke `TfsIdentityExportHandler` and relay results
- [ ] T022 [US0] Update `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` — register `SimulatedIdentitySource` as `IIdentitySource`
- [ ] T023 [P] [US0] Update `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ExportServiceCollectionExtensions.cs` — register `AzureDevOpsIdentitySource` as `IIdentitySource`
- [ ] T024 [US0] Add cursor-based checkpointing to `IdentitiesModule.ExportAsync` — write `identities.cursor.json` to `.migration/Checkpoints/` after each batch; resume from cursor on restart

### Tests for US 0

- [ ] T025 [P] [US0] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/IdentitiesModuleTests.cs` — unit tests: export writes descriptors.jsonl, import builds mapping, cursor resumption, fail-fast when not exported
- [ ] T026 [P] [US0] Create `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/SimulatedIdentitySourceTests.cs` — deterministic generation, seed consistency

**Checkpoint**: IdentitiesModule fully functional. `IIdentityMappingService.Resolve()` available for downstream modules.

---

## Phase 3b: Prepare Phase — Target Discovery & Validation (Priority: P0)

**Goal**: Implement the Prepare lifecycle phase that discovers target identities and validates compatibility before import.

**Independent Test**: Export identities → run prepare → verify `mapping.json` auto-resolved candidates and `unresolved.json` entries.

### Gherkin Feature Files for Prepare (mandatory)

- [ ] T024b [US0] Create `features/platform/prepare-phase.feature` — scenarios: (1) prepare discovers target identities and writes mapping.json, (2) prepare writes unresolved.json for unmatchable identities, (3) import auto-runs prepare when mapping.json missing, (4) import exits with error when unresolved identities exist after auto-prepare
- [ ] T024c [P] [US0] Create `features/export/identities/validate-identity-package.feature` — scenarios for ValidateAsync: missing descriptors.jsonl, malformed JSONL, missing required fields (FR-I07)

### Implementation for Prepare

- [ ] T024d [US0] Add `PrepareAsync` method to `IdentitiesModule` — query target identity directory via connector, match against `descriptors.jsonl` entries (UPN then display name), write `Identities/mapping.json` (auto-resolved candidates) and `Identities/unresolved.json` (unmatched identities)
- [ ] T024e [US0] Update `IdentitiesModule.ImportAsync` — check for `mapping.json` existence via `IArtefactStore.ExistsAsync()`. If missing, invoke `PrepareAsync` automatically. If `unresolved.json` is non-empty after auto-prepare, write structured error to `IProgressSink` and throw `MigrationException`
- [ ] T024f [US0] Add CLI `prepare` command — sends a prepare job to the Migration Agent via `ControlPlaneClient`. Update `.vscode/launch.json` with prepare debug profile.

### Tests for Prepare

- [ ] T024g [P] [US0] Add prepare tests to `IdentitiesModuleTests.cs` — test: prepare writes mapping.json, prepare writes unresolved.json, import auto-prepares, import fails on unresolved identities

**Checkpoint**: Prepare phase operational. Import auto-detects and runs prepare when needed.

---

## Phase 4: User Story 0b — NodeStructureModule (Priority: P0)

**Goal**: Extract existing export/import code into a proper `IModule`. No new node-processing logic — just lifecycle wrapper.

**Independent Test**: Export nodes → verify `Nodes/source-tree.json` → import → confirm trees exist on target.

### Gherkin Feature Files for US 0b (mandatory)

- [ ] T027 [US0b] Create `features/export/nodes/export-classification-tree.feature` — translate spec.md US 0b acceptance scenarios S1, S2, S7 into conformant Gherkin
- [ ] T028 [P] [US0b] Create `features/import/nodes/import-classification-tree.feature` — translate spec.md US 0b acceptance scenarios S3, S4, S5, S6 into conformant Gherkin

### Implementation for US 0b

- [ ] T029 [US0b] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/NodeStructureModule.cs` — implement `IModule` with `Name = "Nodes"`. No `DependsOn` property — module order is operator-controlled. `ExportAsync` delegates to `IClassificationTreeCapture.CaptureAsync()`. `ImportAsync` delegates to `INodeEnsurer.ReplicateSourceTreeAsync()` and/or `INodeEnsurer.EnsureReferencedPathsAsync()` based on options. `ValidateAsync` delegates to `INodeStructureValidator.ValidateAsync()`.
- [ ] T030 [US0b] Update `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/ModuleServiceCollectionExtensions.cs` — add `AddNodeStructureModule()` registering module + `NodeStructureModuleOptions`
- [ ] T031 [US0b] Add cursor-based checkpointing to `NodeStructureModule.ImportAsync` — write `nodes.cursor.json` to `.migration/Checkpoints/` after each node created; resume from cursor on restart
- [ ] T032 [US0b] Verify localised root name normalisation in `ClassificationTreeCapture` — confirm German `Bereich`/`Iteration` → English `Area`/`Iteration` in `source-tree.json`

### Tests for US 0b

- [ ] T033 [P] [US0b] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/NodeStructureModuleTests.cs` — unit tests: export delegates to capture, import delegates to ensurer, cursor resumption, option-driven behaviour (ReplicateSourceTree vs AutoCreateNodes)

**Checkpoint**: NodeStructureModule wraps existing code with IModule lifecycle. source-tree.json export and import both operational.

---

## Phase 5: User Story 0c — WorkItems NodeStructure Extension (Priority: P0)

**Goal**: WorkItemsModule extension that records area/iteration paths from every revision to `ReferencedPathTracker` during export.

**Independent Test**: Export work items with extension enabled → verify `referenced-paths.json` has unique paths from all revisions.

### Gherkin Feature Files for US 0c (mandatory)

- [ ] T034 [US0c] Create `features/export/nodes/workitems-referenced-paths.feature` — translate spec.md US 0c acceptance scenarios S1, S2, S3, S4 into conformant Gherkin

### Implementation for US 0c

- [ ] T035 [US0c] Add NodeStructure extension hook to `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/WorkItemsModule.cs` — during export, for each revision, call `IReferencedPathTracker.RecordAreaPathAsync(revision.AreaPath)` and `IReferencedPathTracker.RecordIterationPathAsync(revision.IterationPath)`
- [ ] T036 [US0c] Add configuration toggle for NodeStructure extension in WorkItemsModule — when disabled, skip path recording
- [ ] T036b [US0c] Add import-side path translation to WorkItemsModule NodeStructure extension — during `WorkItemsModule.ImportAsync`, for each revision, call `INodeTranslationTool.TranslatePath()` to translate `System.AreaPath` and `System.IterationPath` field values before writing to the target (FR-W04)

### Tests for US 0c

- [ ] T037 [P] [US0c] Add tests to `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/NodeStructureModuleTests.cs` (or dedicated test class) — verify: 100 work items across 5 area/3 iteration paths → 8 unique paths in referenced-paths.json; historical paths across revisions; disabled extension produces no output; import-side path translation via NodeTranslationTool

**Checkpoint**: WorkItems NodeStructure extension operational. `referenced-paths.json` populated during WorkItems export.

---

## Phase 6: User Story 1 — TeamsModule Core: Export/Import Team Definitions (Priority: P1)

**Goal**: Export all teams from source with settings, import into target with idempotent update.

**Independent Test**: Export teams → verify `Teams/` folder → import → confirm teams exist with correct settings.

### Gherkin Feature Files for US 1 (mandatory)

- [ ] T038 [US1] Create `features/export/teams/export-team-definitions.feature` — translate spec.md US 1 acceptance scenarios S1, S4 into conformant Gherkin
- [ ] T039 [P] [US1] Create `features/import/teams/import-team-definitions.feature` — translate spec.md US 1 acceptance scenarios S2, S3 into conformant Gherkin
- [ ] T039b [P] [US1] Create `features/export/teams/export-team-scope-filter.feature` — scenarios: (1) export all teams when scope="all", (2) export only matching teams when scope="teams" with filter pattern, (3) empty filter returns all (FR-011)
- [ ] T039c [P] [US1] Create `features/import/teams/import-default-team-detection.feature` — scenarios: (1) source default team maps to target default team regardless of name, (2) non-default teams match by name (FR-016)
- [ ] T039d [P] [US0b] Create `features/export/nodes/validate-node-package.feature` — scenarios for NodeStructureModule ValidateAsync: missing source-tree.json, malformed JSON, missing required schema fields (FR-N07)
- [ ] T039e [P] [US1] Create `features/export/teams/validate-team-package.feature` — scenarios for TeamsModule ValidateAsync: missing team.json files, malformed JSON, missing required fields (FR-013)

### Implementation for US 1

- [ ] T040 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Modules/TeamsModule.cs` — implement `IModule` with `Name = "Teams"`. No `DependsOn` property — module order is operator-controlled. `ExportAsync` enumerates teams via `ITeamSource`, writes `Teams/{team-slug}/team.json`. `ImportAsync` reads team files, creates/updates via `ITeamTarget`.
- [ ] T041 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamExportOrchestrator.cs` — orchestrate per-team export: settings, iterations, members, capacity, area paths
- [ ] T042 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamImportOrchestrator.cs` — orchestrate per-team import in fixed order: settings → NodeStructure → iterations → members → capacity
- [ ] T043 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamSlugGenerator.cs` — filesystem-safe slug from team display name (lowercase, replace spaces with hyphens, strip invalid chars)
- [ ] T044 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Agent/Teams/TeamsServiceCollectionExtensions.cs` — `AddTeamsModule()` registering module + options + orchestrators + slug generator
- [ ] T045 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedTeamSource.cs` — deterministic team generation from seed
- [ ] T046 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedTeamTarget.cs` — in-memory team target for testing
- [ ] T047 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/AzureDevOpsTeamSource.cs` — Teams REST API: `_apis/projects/{project}/teams`, `_apis/work/teamsettings`
- [ ] T048 [P] [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/AzureDevOpsTeamTarget.cs` — Teams REST API (write): create/update teams, set settings
- [ ] T049 [US1] Create TFS team export handler in `src/DevOpsMigrationPlatform.CLI.TfsExport/TfsTeamExportHandler.cs` — TFS OM `TfsTeamService.QueryTeams()`
- [ ] T050 [US1] Create TFS team bridge command in `src/DevOpsMigrationPlatform.TfsMigrationAgent/` — subprocess bridge for team operations
- [ ] T051 [US1] Update `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs` — register team source/target
- [ ] T052 [P] [US1] Update `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/ExportServiceCollectionExtensions.cs` — register team source/target
- [ ] T053 [US1] Add cursor-based checkpointing to `TeamsModule.ExportAsync` — write `teams.cursor.json` after each team exported
- [ ] T053b [US1] Implement team scope/filter in `TeamsModule.ExportAsync` — support `"teams"` scope (with optional `filter` parameter for team name pattern matching) and `"all"` scope (all teams in the project) per FR-011. When filter is set, only matching teams are exported.
- [ ] T053c [US1] Implement default team detection and mapping in `TeamsModule` — detect source project's default team by `isDefaultTeam` flag (not by name matching) and map it to the target project's default team during import, regardless of name differences (FR-016)

### Tests for US 1

- [ ] T054 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/TeamsModuleTests.cs` — unit tests: export writes team.json per team, import creates teams, idempotent update, cursor resumption, scope/filter behaviour, default team detection
- [ ] T054b-1 [P] [US0] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/IdentitiesModuleValidateTests.cs` — test ValidateAsync: missing descriptors.jsonl, malformed JSONL, missing required fields (FR-I07)
- [ ] T054b-2 [P] [US0b] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/NodeStructureModuleValidateTests.cs` — test ValidateAsync: missing source-tree.json, malformed JSON, missing required schema fields (FR-N07)
- [ ] T054b-3 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/TeamsModuleValidateTests.cs` — test ValidateAsync: missing team.json files, malformed JSON, missing required fields (FR-013)
- [ ] T054c-1 [US0] Wire platform retry policy (exponential back-off) into `AzureDevOpsIdentitySource` — ensure 429/5xx/408 responses are retried (FR-018)
- [ ] T054c-2 [US1] Wire platform retry policy (exponential back-off) into `AzureDevOpsTeamSource` and `AzureDevOpsTeamTarget` — ensure 429/5xx/408 responses are retried (FR-018)
- [ ] T054c-3 [US0–US1] Wire platform retry policy into TFS subprocess bridge commands — ensure retryable exit codes trigger re-invocation (FR-018)
- [ ] T054c-4 [P] [US0–US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.AzureDevOps.Tests/RetryPolicyTests.cs` — test: given a 429 response, when retry fires, then the request succeeds on second attempt; given 3 consecutive 5xx responses, then retry exhausts and throws (FR-018)
- [ ] T055 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/SimulatedTeamSourceTests.cs` — deterministic generation
- [ ] T056 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Simulated.Tests/SimulatedTeamTargetTests.cs` — in-memory storage

**Checkpoint**: TeamsModule core operational. Teams exported and imported with settings.

---

## Phase 7: User Story 2 + User Story 5 — Iterations & NodeStructure Extension (Priority: P2/P5)

**Goal**: Export/import team iteration assignments and team area/iteration path collection via NodeStructure extension.

**Independent Test**: Export team with iterations → verify package → import with path translation → verify assignments.

### Gherkin Feature Files for US 2 + US 5 (mandatory)

- [ ] T057 [US5] Create `features/export/teams/teams-node-structure-extension.feature` — translate spec.md US 5 acceptance scenarios S1, S5 into conformant Gherkin
- [ ] T058 [P] [US2] Create `features/export/teams/export-team-iterations.feature` — translate spec.md US 2 acceptance scenario S1 into conformant Gherkin
- [ ] T059 [P] [US2] Create `features/import/teams/import-team-iterations.feature` — translate spec.md US 2 acceptance scenarios S2, S3 into conformant Gherkin
- [ ] T060 [P] [US5] Create `features/import/teams/import-team-area-paths.feature` — translate spec.md US 5 acceptance scenarios S2, S3, S4 into conformant Gherkin

### Implementation for US 5 (Export side — must come first)

- [ ] T061 [US5] Add Teams NodeStructure extension to `TeamsModule.ExportAsync` — for each team, call `IReferencedPathTracker.RecordAreaPathAsync()` and `RecordIterationPathAsync()` for all team-assigned paths
- [ ] T062 [US5] Add configuration toggle for NodeStructure extension in TeamsModuleOptions — `Extensions:NodeStructure:Enabled` (default: true)

### Implementation for US 2

- [ ] T063 [US2] Extend `TeamExportOrchestrator` — export team iteration assignments (including default iteration) via `ITeamSource.GetTeamIterationsAsync()`, write to `team.json`
- [ ] T064 [US2] Extend `TeamImportOrchestrator` — import iteration assignments using `INodeTranslationTool.TranslatePath()` to resolve source → target paths, call `ITeamTarget.AssignIterationAsync()`
- [ ] T065 [US2] Handle unresolvable iteration paths — log warning and skip when `TranslatePath()` returns null

### Implementation for US 5 (Import side)

- [ ] T066 [US5] Extend `TeamImportOrchestrator` — translate team area assignments via `INodeTranslationTool.TranslatePath()`, call `ITeamTarget.SetAreaPathsAsync()`
- [ ] T067 [US5] Handle disabled NodeStructure extension on import — use source paths as-is, log warning for paths that don't exist on target

### Tests for US 2 + US 5

- [ ] T068 [P] [US2] Add iteration tests to `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/TeamsModuleTests.cs` — iteration assignment export/import, path translation, unresolvable path warning
- [ ] T069 [P] [US5] Add NodeStructure extension tests — path recording during export, translation during import, disabled extension behaviour, union with WorkItems paths (union test lives here because both Teams and WorkItems extension code must exist)

**Checkpoint**: Teams iterations and area paths fully operational with NodeTranslationTool integration.

---

## Phase 8: User Story 3 — Team Members (Priority: P3)

**Goal**: Export/import team membership with identity mapping.

**Independent Test**: Export team with 5 members → verify package → import with identity mapping → verify all members added.

### Gherkin Feature Files for US 3 (mandatory)

- [ ] T070 [US3] Create `features/export/teams/export-team-members.feature` — translate spec.md US 3 acceptance scenario S1 into conformant Gherkin
- [ ] T071 [P] [US3] Create `features/import/teams/import-team-members.feature` — translate spec.md US 3 acceptance scenarios S2, S3 into conformant Gherkin

### Implementation for US 3

- [ ] T072 [US3] Extend `TeamExportOrchestrator` — export team members with admin flags via `ITeamSource.GetTeamMembersAsync()`, write to `team.json`
- [ ] T073 [US3] Extend `TeamImportOrchestrator` — import members using `IIdentityMappingService.Resolve()` to map source → target identities, call `ITeamTarget.AddMemberAsync()`
- [ ] T074 [US3] Handle unresolvable member identity — log warning and skip when `Resolve()` returns default/unresolved

### Tests for US 3

- [ ] T075 [P] [US3] Add member tests to `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/TeamsModuleTests.cs` — member export/import, identity mapping, unresolvable identity warning, admin flag preservation

**Checkpoint**: Team members exported and imported with identity mapping.

---

## Phase 9: User Story 4 — Team Capacity (Priority: P4)

**Goal**: Export/import per-sprint capacity data for each team member.

**Independent Test**: Export capacity for 2 sprints → verify package → import → verify capacity matches.

### Gherkin Feature Files for US 4 (mandatory)

- [ ] T076 [US4] Create `features/export/teams/export-team-capacity.feature` — translate spec.md US 4 acceptance scenario S1 into conformant Gherkin
- [ ] T077 [P] [US4] Create `features/import/teams/import-team-capacity.feature` — translate spec.md US 4 acceptance scenarios S2, S3 into conformant Gherkin

### Implementation for US 4

- [ ] T078 [US4] Extend `TeamExportOrchestrator` — export per-member capacity via `ITeamSource.GetTeamCapacityAsync()` for each assigned iteration, write to `team.json`
- [ ] T079 [US4] Extend `TeamImportOrchestrator` — import capacity with iteration mapping (`INodeTranslationTool`) and member mapping (`IIdentityMappingService`), call `ITeamTarget.SetCapacityAsync()`
- [ ] T080 [US4] Handle capacity for unassigned iteration — log warning and skip
- [ ] T081 [US4] TFS capacity exemption — TFS OM pre-2017.2 does not expose capacity API; log informational message and skip gracefully

### Tests for US 4

- [ ] T082 [P] [US4] Add capacity tests to `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Modules/TeamsModuleTests.cs` — capacity export/import, iteration/member mapping, unassigned iteration warning, TFS exemption

**Checkpoint**: Team capacity operational. TFS gracefully degraded.

---

## Phase 10: Cross-Cutting Connector Verification (Batch H)

**Purpose**: Verify all acceptance scenarios pass for all three connectors.

- [ ] T083 [US0–US5] Verify all acceptance scenarios pass with Simulated connector — run full test suite with Simulated DI registration
- [ ] T084 [US0–US5] Verify all acceptance scenarios pass with AzureDevOps connector — run full test suite with ADO DI registration (requires live ADO org or recorded responses)
- [ ] T085 [US0–US5] Verify all acceptance scenarios pass with TFS connector — run full test suite with TFS DI registration (capacity scenarios expected to gracefully skip on pre-2017.2)

**Checkpoint**: All three connectors verified. No stubs or placeholders remain.

---

## Phase 11: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: Ensure all canonical docs reflect what was implemented in this spec.

- [ ] T086 Update `docs/modules.md` — add IdentitiesModule detailed section (name, config, package folder, cursor, behaviour). Note: no DependsOn property.
- [ ] T087 [P] Update `docs/modules.md` — add NodeStructureModule detailed section (extraction from tool, config, package folder, cursor, behaviour). Note: no DependsOn property.
- [ ] T088 [P] Update `docs/modules.md` — add TeamsModule detailed section (name, config, package folder, cursor, extensions, behaviour). Note: no DependsOn property. Extensions order is hardcoded.
- [ ] T089 Update `docs/configuration.md` — add `MigrationPlatform:Modules:Identities`, `MigrationPlatform:Modules:Nodes`, `MigrationPlatform:Modules:Teams` config schema sections
- [ ] T090 Update `.agents/context/package-format.md` — document `Teams/` folder internal structure (`Teams/{team-slug}/team.json`) and add `Identities`, `Nodes` to `includedTypes` example in manifest
- [ ] T091 [P] Update `.agents/context/checkpointing.md` — add NodeStructureModule cursor (`nodes.cursor.json`), IdentitiesModule cursor (`identities.cursor.json`), TeamsModule cursor (`teams.cursor.json`)
- [ ] T092 Mark all items in `specs/024-teams-module/discrepancies.md` as `Resolved` or `N/A`
- [ ] T093 Review `analysis/pending-actions.md` and remove any items resolved by this spec
- [ ] T094 Run `dotnet clean && dotnet build --no-incremental` — MUST pass
- [ ] T095 Run `dotnet test` — ALL tests MUST pass
- [ ] T096 Run at least one scenario config (e.g. `scenarios/queue-export-ado-workitems-single-project.json`) via a `.vscode/launch.json` debug profile and verify observable output

---

## Phase 12: Polish & Cross-Cutting Concerns

**Purpose**: Final quality pass across all user stories.

- [ ] T097 Verify OpenTelemetry instrumentation: all metrics, traces, and structured log events from spec.md Observability section are emitted. Cross-reference with spec.md § Observability (metrics: `identities.export.count`, `nodes.export.count`, `teams.export.count`, etc.)
- [ ] T098 [P] Verify configuration validation: invalid options (e.g., negative timeouts, empty default identity) produce clear error messages at startup
- [ ] T099 [P] Verify all `INodeTranslationTool` rename references are consistent — no leftover `INodeStructureTool` references in code, config, or docs
- [ ] T100 Update `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Tools/NodeStructure/` — rename `NodeStructureToolTests.cs` → `NodeTranslationToolTests.cs` if not already done in Phase 2

---

## Dependencies & Execution Order

### Phase Dependencies

- **Research (Phase 0)**: No dependencies — can start immediately. Produces research.md.
- **Setup (Phase 1)**: No dependencies — can start in parallel with Phase 0
- **Foundational (Phase 2)**: Can start in parallel with Phase 1 — BLOCKS all user story phases
- **US 0 — IdentitiesModule (Phase 3)**: Depends on Phase 1 + Phase 2
- **Prepare Phase (Phase 3b)**: Depends on Phase 3 (needs IdentitiesModule)
- **US 0b — NodeStructureModule (Phase 4)**: Depends on Phase 2 (uses R1–R3 interfaces)
- **US 0c — WorkItems Extension (Phase 5)**: Depends on Phase 4 (B1)
- **US 1 — TeamsModule Core (Phase 6)**: Depends on Phase 1 (ITeamSource/ITeamTarget)
- **US 2 + US 5 — Iterations/NodeStructure (Phase 7)**: Depends on Phase 4 + Phase 6
- **US 3 — Members (Phase 8)**: Depends on Phase 3 (IIdentityMappingService) + Phase 6
- **US 4 — Capacity (Phase 9)**: Depends on Phase 7 (iterations) + Phase 8 (members)
- **Connectors (Phase 10)**: Depends on all user story phases
- **Documentation (Phase 11)**: Depends on all user story phases
- **Polish (Phase 12)**: Depends on all above

### User Story Dependencies

- **US 0 (Identities)**: Blocks US 3 (Members), US 4 (Capacity) — anything requiring identity resolution
- **US 0b (NodeStructure)**: Blocks US 0c (WorkItems Extension), US 2 (Iterations), US 5 (Teams NodeStructure Extension)
- **US 0c (WorkItems Extension)**: Independent after US 0b; union test lives in Phase 7 (T069) where Teams extension code also exists
- **US 1 (Teams Core)**: Blocks US 2, US 3, US 4, US 5
- **US 2 + US 5 (Iterations/NodeStructure)**: Blocks US 4 (Capacity needs iterations)
- **US 3 (Members)**: Blocks US 4 (Capacity needs members)
- **US 4 (Capacity)**: Terminal — no dependents

### Parallel Opportunities

- Phase 1 tasks T001–T006 are all independent (different files) → full parallel
- Phase 2 refactorings R1–R3 (T007–T009) can run in parallel; R4 (T010) is independent; R5–R6 are independent
- US 0 (Phase 3) and US 0b (Phase 4) can run in parallel after Phase 2
- US 0c (Phase 5) and US 1 (Phase 6) can run in parallel
- Within US 1 (Phase 6), Simulated and ADO connector implementations (T045–T048) are parallel
- US 2/US 5 (Phase 7) and US 3 (Phase 8) can run in parallel after their dependencies are met
- All feature file creation tasks marked [P] within a phase are parallel

---

## Task Summary

| Phase | Story | Task Count | Parallel Tasks |
|-------|-------|-----------|---------------|
| 0: Research | All | 7 | 6 |
| 1: Setup | All | 6 | 6 |
| 2: Foundational | Refactoring | 6 | 5 |
| 3: US 0 Identities | US0 | 14 | 8 |
| 3b: Prepare | US0 | 6 | 2 |
| 4: US 0b NodeStructure | US0b | 7 | 2 |
| 5: US 0c WorkItems Ext | US0c | 5 | 1 |
| 6: US 1 Teams Core | US1 | 32 | 17 |
| 7: US 2+5 Iterations | US2, US5 | 13 | 5 |
| 8: US 3 Members | US3 | 6 | 2 |
| 9: US 4 Capacity | US4 | 7 | 2 |
| 10: Connectors | All | 3 | 0 |
| 11: Documentation | — | 11 | 3 |
| 12: Polish | — | 4 | 2 |
| **Total** | | **127** | **61** |
