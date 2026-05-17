# Tasks: Discovery Dependency Analysis

**Input**: Design documents from `/specs/012-discovery-dependencies/`  
**Tech stack**: C# 12 / .NET 10 · Spectre.Console.Cli · MSTest + Moq  
**User stories**: US1 Cross-Project Links (P1) · US2 Cross-Organisation Links (P2) · US3 WIQL Filter (P3) · US4 Project Dependency Summary (P2)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the scenario config file and launch entry so the command can be run manually against a real ADO org from day one. No implementation yet.

- [X] T001 Create `scenarios/discovery-dependency-ado-single-project.json` — `DiscoveryOptions`-format config pointing to `$ENV:AZDEVOPS_SYSTEM_TEST_ORG` / `migrationTest5` with PAT from `$ENV:AZDEVOPS_SYSTEM_TEST_PAT` (mirror `scenarios/inventory-ado-single-project.json` structure) — Status: complete
- [X] T002 [P] Add `🔍 Migration CLI: Dependencies (Single Project)` launch entry to `.vscode/launch.json` — args: `discovery dependencies --config scenarios/discovery-dependency-ado-single-project.json`, env: `AZDEVOPS_SYSTEM_TEST_ORG`/`AZDEVOPS_SYSTEM_TEST_PAT`, preLaunchTask: `build-migration-cli` — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/032-icapture-interface
  - Evidence: Queue-based dependencies profile exists (`.vscode/launch.json`), while `Program.cs` exposes `queue` not `discovery dependencies`.
- [ ] T003 [P] Add `🔍 Migration CLI: Dependencies (WIQL Filter)` launch entry to `.vscode/launch.json` — args: `discovery dependencies --config scenarios/discovery-dependency-ado-single-project.json --wiql "SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'User Story'"`, same env as T002 — Status: incomplete
  - Evidence: No `🔍 Migration CLI: Dependencies (WIQL Filter)` launch profile exists in `.vscode/launch.json`; only `Queue Dependencies (Single Project)` is present.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: All domain types and interfaces in `Abstractions` that every user story depends on. Must be complete before any story implementation begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T004 Create `src/DevOpsMigrationPlatform.Abstractions/Models/LinkScope.cs` — `public enum LinkScope { CrossProject, CrossOrganisation }` with XML doc comments; NO `SameProject` value — Status: complete/superseded; completed because superseded by specs/016-organisation-endpoint + specs/030-module-analiser-refactor
  - Evidence: `LinkScope` now lives in `src/DevOpsMigrationPlatform.Abstractions.Agent/WorkItems/LinkScope.cs` under post-refactor architecture.
- [X] T005 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/TargetStatus.cs` — `public enum TargetStatus { Reachable, Deleted, AccessDenied, Unknown }` with XML doc comments — Status: complete/superseded; completed because superseded by specs/016-organisation-endpoint + specs/030-module-analiser-refactor
  - Evidence: `TargetStatus` exists in `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/TargetStatus.cs` after contract split.
- [X] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/DependencyRecord.cs` — `public record DependencyRecord` with nine `init`-only properties: `SourceWorkItemId` (int), `SourceWorkItemType` (string), `SourceProject` (string), `LinkType` (string), `LinkScope` (LinkScope), `TargetWorkItemId` (int), `TargetProject` (string), `TargetOrganisation` (string), `TargetStatus` (TargetStatus) — Status: complete/superseded; completed because superseded by specs/016-organisation-endpoint + specs/030-module-analiser-refactor
  - Evidence: `DependencyRecord` exists in `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/DependencyRecord.cs` with expanded fields.
- [X] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/DependencySummary.cs` — `public record DependencySummary` with `WorkItemsAnalysed`, `ExternalLinksFound`, `CrossProjectCount`, `CrossOrgCount` (int), `ReportFilePath` (string), all `init`-only — Status: complete/superseded; completed because superseded by specs/016-organisation-endpoint + specs/030-module-analiser-refactor
  - Evidence: `DependencySummary` exists in `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/DependencySummary.cs`.
- [X] T008 Create `src/DevOpsMigrationPlatform.Abstractions/Models/DependencyProgressEvent.cs` — `public abstract record DependencyProgressEvent`; nested `public sealed record DependencyFoundEvent(DependencyRecord Record) : DependencyProgressEvent`; `public sealed record DependencyHeartbeatEvent(string OrganisationUrl, string ProjectName, int WorkItemsAnalysed, int ExternalLinksFound, int CrossProjectCount, int CrossOrgCount, bool IsComplete, string? Error = null) : DependencyProgressEvent` — Status: complete/superseded; completed because superseded by specs/016-organisation-endpoint + specs/030-module-analiser-refactor
  - Evidence: `DependencyProgressEvent` exists in `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/DependencyProgressEvent.cs`.
- [X] T009 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Services/IDependencyDiscoveryService.cs` — `public interface IDependencyDiscoveryService` with `IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(string? wiqlFilter, CancellationToken cancellationToken = default)` — Status: complete/superseded; completed because superseded by specs/016-organisation-endpoint + specs/030-module-analiser-refactor
  - Evidence: `IDependencyDiscoveryService` moved to `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IDependencyDiscoveryService.cs`.
- [X] T010 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemLinkAnalysisService.cs` — `public interface IWorkItemLinkAnalysisService` with `IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(string organisationUrl, string project, string pat, string? wiqlFilter, CancellationToken cancellationToken = default)` — Status: complete/superseded; completed because superseded by specs/016-organisation-endpoint + specs/030-module-analiser-refactor
  - Evidence: `IWorkItemLinkAnalysisService` moved to `src/DevOpsMigrationPlatform.Abstractions.Agent/Import/IWorkItemLinkAnalysisService.cs`.
- [X] T011 Modify `src/DevOpsMigrationPlatform.Abstractions/Options/DiscoveryOptions.cs` — add `public int MaxConcurrency { get; set; } = 4;` property with XML doc comment stating: "Maximum concurrent batch requests to source (default 4). Binds from JSON config key `maxConcurrency` (snake_case per convention). Prevents rate-limit triggers during parallel link fetching." Verify `dotnet build` still passes for both `net481` and `net10.0` targets. — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor (T014a rename)
  - Evidence: Concurrency policy is now under `MigrationPlatformOptions.Policies.Throttle.MaxConcurrency` (used by dependency factories/services).
- [X] T012 Create `features/cli/discovery/dependency-command-wiring.feature` — CLI-tier Gherkin for acceptance scenarios: (1) command runs and writes CSV to CWD; (2) zero external dependencies prints "No external dependencies found." and writes header-only CSV; (3) cross-org count appears in summary with `⚠` warning; (4) `--output` path respected; follow `acceptance-test-format.md` Format + Rules precisely (NOTE: `cli/discovery/` not `cli/inventory/` — separate sub-directory to avoid naming collision with the inventory feature file) — Status: complete
- [X] T013 [P] Create `features/inventory/work-items/dependency-analysis.feature` — capability-tier Gherkin: (1) cross-project link recorded with all nine CSV fields; (2) same-project link silently discarded and never appears in report; (3) cross-organisation link recorded with `LinkScope=CrossOrganisation` and `TargetOrganisation` set; (4) inaccessible target recorded with appropriate `TargetStatus` — Status: complete

**Checkpoint**: Abstractions compile; feature files exist; no implementation yet

---

## Phase 3: User Story 1 — Identify Cross-Project Work Item Links (Priority: P1) 🎯 MVP

**Goal**: `DependencyCommand` produces a CSV listing every cross-project link for all configured projects in an ADO organisation. Same-project links are silently filtered.

**Independent Test**: Run `devopsmigration discovery dependencies --config scenarios/discovery-dependency-ado-single-project.json` against an org with at least one cross-project link. CSV at `discovery-dependencies.csv` exists, has header row, and at least one row with `LinkScope=CrossProject` containing all nine required fields. Terminal prints summary table.

### Implementation for User Story 1

- [X] T014 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs` — implement `IWorkItemLinkAnalysisService`; constructor takes `IOptions<DiscoveryOptions>`, `IAzureDevOpsClientFactory`, `ILogger<AzureDevOpsDependencyAnalysisService>`; `AnalyseLinksAsync` (a) runs WIQL query (default `SELECT [System.Id] FROM WorkItems`) and **de-duplicates the returned work item IDs using `HashSet<int>`** before any batch-GET, (b) batch-GETs work items with `WorkItemExpand.Relations` in pages of 200, (c) for each relation parses target URL host to detect cross-org (host ≠ source org host) vs same-host, (d) for cross-org relations: emit `DependencyFoundEvent` with `LinkScope.CrossOrganisation`, `TargetOrganisation` = parsed host, `TargetProject` = "", `TargetStatus` = result of unauthenticated GET to target URL (Reachable/Deleted/AccessDenied/Unknown — never throws), (e) for same-host relations: batch-GET `System.TeamProject` of target IDs and classify as `CrossProject` or `SameProject`; discard `SameProject` silently, (f) for `CrossProject`: set `TargetStatus` from HTTP response codes (200→Reachable, 404→Deleted, 401/403→AccessDenied, other→Unknown), (g) emits `DependencyFoundEvent` per external link and `DependencyHeartbeatEvent` after each batch of 200 source items; uses `SemaphoreSlim(_options.Value.MaxConcurrency)` for parallel batches — Status: complete/superseded; completed because superseded by specs/015-work-item-scoped-fetch + specs/030-module-analiser-refactor
  - Evidence: Service implemented at `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Discovery/AzureDevOpsDependencyAnalysisService.cs` using `IWorkItemFetchService` (015 refactor).
- [ ] T014a [US1] Add de-duplication unit test to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — given a WIQL result returning work item ID 42 twice, assert that the service calls `GetWorkItemsBatchAsync` with ID 42 exactly once (de-duplication via `HashSet<int>` prevents duplicate processing) — Status: incomplete
  - Evidence: `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` contains resume-scope tests only; no duplicate WIQL ID de-duplication assertion.
- [X] T015 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Services/DependencyDiscoveryService.cs` — implement `IDependencyDiscoveryService`; constructor takes `IOptions<DiscoveryOptions>`, `IEnumerable<IWorkItemLinkAnalysisService>` (resolved by keyed DI via `IServiceProvider` factory — see T016), `ILogger<DependencyDiscoveryService>`; `DiscoverDependenciesAsync` iterates enabled `DiscoveryOptions.Organisations`, resolves the correct `IWorkItemLinkAnalysisService` implementation by `entry.Type` key string: `"AzureDevOpsServices"` → ADO implementation, `"Simulated"` → Simulated implementation (US1 MVP: if no keyed service registered for `"Simulated"`, throw `NotSupportedException` "Simulated source not yet implemented — add in Phase 4"), `"TeamFoundationServer"` → TFS adapter (US1 MVP: if not registered, throw `NotSupportedException` "TFS source requires TfsDependencyProcessAdapter — registered in CLI host only"); yields all events; NEVER references any concrete implementation type from `CLI.Migration` or `Infrastructure.AzureDevOps` directly — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/032-icapture-interface
  - Evidence: Orchestration implemented at `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/DependencyDiscoveryService.cs` in agent runtime model.
- [X] T016 Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DependencyServiceCollectionExtensions.cs` — `public static IServiceCollection AddAzureDevOpsDependencyAnalysis(this IServiceCollection services, IConfiguration configuration)`: binds `DiscoveryOptions`, registers `IAzureDevOpsClientFactory` (if not already), registers `AzureDevOpsDependencyAnalysisService` as a **keyed singleton** with key `"AzureDevOpsServices"` implementing `IWorkItemLinkAnalysisService`, registers `IDependencyDiscoveryService → DependencyDiscoveryService` (keyed service resolution via `IServiceProvider.GetKeyedService<IWorkItemLinkAnalysisService>(key)`) — Status: complete
- [X] T017 [US1] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — `public sealed class DependencyCommand : CommandBase<DependencyCommand.Settings>`; `Settings` has `[CommandOption("--output")] string? OutputPath` and `[CommandOption("--wiql")] string? WiqlFilter`; `ExecuteInternalAsync` calls `CreateHost(..., (s,c) => s.AddAzureDevOpsDependencyAnalysis(c))`, resolves `IDependencyDiscoveryService`, opens `StreamWriter` to output path (warn if file exists), writes CSV header, iterates `DiscoverDependenciesAsync` pattern-matching on `DependencyFoundEvent` (write row) vs `DependencyHeartbeatEvent` (update Spectre.Console live table), after loop flushes CSV and writes `DependencySummary` table to console; handles "No external dependencies found." case (FR-008) — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/032-icapture-interface
  - Evidence: Local command path replaced by capture/analyse pipeline (`DependencyCapture`, `DependencyAnalyser`) under queue job execution.
- [X] T018 [US1] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs` — add `branch.AddCommand<DependencyCommand>("dependencies")` inside `config.AddBranch("discovery", ...)` with `.WithDescription(...)` and `.WithExample("discovery", "dependencies", "--config", "scenarios/discovery-dependency-ado-single-project.json")` — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/032-icapture-interface
  - Evidence: CLI command registration now centers on `queue` (`src/DevOpsMigrationPlatform.CLI.Migration/Program.cs:66-70`).
- [ ] T019 [US1] Create `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/DependencyCommandTests.cs` — unit test: `DependencyCommand_CanBeConstructed_WithParameterlessConstructor`; system test: `[TestCategory("SystemTest")] [Timeout(300_000)] DependencyCommand_SystemTest_AdoSingleProject_ScenarioFile_ExecutesSuccessfully` — skips if env vars absent, runs CLI via `CliRunner`, asserts exit code 0, "dependencies" success message, CSV file exists with header, ≥2 lines total (or "No external dependencies found." when project has no cross-project links) — Status: incomplete
  - Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/DependencyCommandTests.cs` does not exist and CLI no longer exposes `DependencyCommand`.
- [ ] T020 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — unit tests using `Mock<IAzureDevOpsClientFactory>`: (1) same-project relations are discarded (zero `DependencyFoundEvent` emitted); (2) cross-project relation emits one `DependencyFoundEvent` with correct fields; (3) `TargetStatus.Deleted` when secondary batch-GET returns 404; (4) `TargetStatus.AccessDenied` when 403; (5) `DependencyHeartbeatEvent` emitted after each batch of 200 — Status: incomplete
  - Evidence: Dependency tests exist but do not cover all listed scenarios (same-project discard, 404/403 mappings, heartbeat per 200) in one suite; see `...Infrastructure.Agent.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs`.

**Checkpoint**: `dotnet build` passes; `DependencyCommand_CanBeConstructed` passes; manual launch profile runs without exception; CSV with header is written

---

## Phase 4: User Story 2 — Identify Cross-Organisation Work Item Links (Priority: P2)

**Goal**: Cross-organisation links appear in the CSV with `LinkScope=CrossOrganisation`, and the terminal summary shows a distinct `⚠` warning count for them. Simulated source fully implemented (enables CI testing without live ADO).

**Independent Test**: CSV rows with `LinkScope=CrossOrganisation` have `TargetOrganisation` set to the remote host and `TargetProject` may be empty. Terminal summary shows cross-org count with `⚠`.

### Implementation for User Story 2

- [ ] T021 [US2] Add cross-org unit tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — (1) cross-org link (target host ≠ source host) emits `DependencyFoundEvent` with `LinkScope.CrossOrganisation`, non-empty `TargetOrganisation`, empty `TargetProject`; (2) unreachable cross-org target yields `TargetStatus.Unknown` without throwing; (3) cross-org link with deleted target yields `TargetStatus.Deleted`. Note: the cross-org *emission logic* is part of T014 — T021 adds test coverage and verifies it end-to-end. — Status: incomplete
  - Evidence: No dedicated cross-org/deleted-target test cases exist in the dependency analysis test suite.
- [X] T022 [US2] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — update console summary rendering: cross-org count row must include `⚠ ACTION REQUIRED — links will break` text when `CrossOrgCount > 0`; separate row from CrossProject count (FR-007, SC-006) — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/032-icapture-interface
  - Evidence: Warning/reporting moved to progress/analysis stages rather than `DependencyCommand` console summary.
- [X] T023 [US2] Create `src/DevOpsMigrationPlatform.Infrastructure/Services/SimulatedDependencyAnalysisService.cs` — implement **`IWorkItemLinkAnalysisService`** (not just a concrete class); constructor takes `IOptions<DiscoveryOptions>`, `ILogger<SimulatedDependencyAnalysisService>`; `AnalyseLinksAsync` uses `new Random(seed)` (seed from org entry or 42 by default), generates `workItemCount` (default 100) synthetic work items with 3 links each, 70% CrossProject / 30% CrossOrganisation, emits deterministic `DependencyFoundEvent` + `DependencyHeartbeatEvent` records; never emits `SameProject` — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure (T067)
  - Evidence: Simulated implementation intentionally returns empty sequence in `SimulatedWorkItemLinkAnalysisService` per later connector design.
- [X] T024 [US2] Register `SimulatedDependencyAnalysisService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DependencyServiceCollectionExtensions.cs` — add `services.AddKeyedSingleton<IWorkItemLinkAnalysisService, SimulatedDependencyAnalysisService>("Simulated")` alongside the ADO registration; `DependencyDiscoveryService` already resolves by key — no constructor change needed — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure
  - Evidence: Simulated keyed registration now in `src/DevOpsMigrationPlatform.Infrastructure.Simulated/SimulatedServiceCollectionExtensions.cs`.
- [ ] T025 [P] [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/SimulatedDependencyAnalysisServiceTests.cs` — unit tests: (1) given seed 42, produces identical record sequence on repeated calls (determinism); (2) produces no `SameProject` links; (3) CrossProject + CrossOrganisation proportions approximately 70/30; (4) zero `workItemCount` yields only one `DependencyHeartbeatEvent(IsComplete=true)` with zero counts — Status: incomplete
  - Evidence: `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/SimulatedDependencyAnalysisServiceTests.cs` does not exist.
- [ ] T026 [P] [US2] Add unit tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — (1) cross-org link emits `DependencyFoundEvent` with `LinkScope.CrossOrganisation` and non-empty `TargetOrganisation`; (2) cross-org link where target URL is unreachable yields `TargetStatus.Unknown` without throwing — Status: incomplete
  - Evidence: Cross-org emission/unknown-status assertions are not present as specified in `AzureDevOpsDependencyAnalysisServiceTests`.

**Checkpoint**: `dotnet build` passes; all new tests pass; Simulated org entry in scenario config produces synthetic report; summary `⚠` warning visible for any scenario with cross-org links

---

## Phase 5: User Story 3 — Scoped Discovery with WIQL Filter (Priority: P3)

**Goal**: `--wiql` parameter scopes analysis to matching work items only. Invalid WIQL exits with code 1 and a human-readable error before any network call to the link analysis APIs.

**Independent Test**: Provide `--wiql "SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'User Story'"` and verify only User Story work items appear in the `SourceWorkItemType` column of the CSV.

### Implementation for User Story 3

- [X] T027 [US3] Modify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs` — wire `wiqlFilter` parameter through WIQL execution: when non-null, use it as the query body in `QueryByWiqlAsync`; when null, use `SELECT [System.Id] FROM WorkItems`; catch `VssServiceResponseException` with status 400 (or similar bad-request), wrap as `InvalidOperationException` containing the server error `Message`, propagate to caller (FR-011: command exits 1 with human-readable WIQL error) — Status: complete/superseded; completed because superseded by specs/015-work-item-scoped-fetch
  - Evidence: WIQL/filter handling now implemented in `AzureDevOpsDependencyAnalysisService` with fetch-scope pipeline from spec 015.
- [X] T028 [US3] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — in `ExecuteInternalAsync`, perform WIQL pre-validation as the **very first operation**, before opening the `StreamWriter` or making any API calls: call `AnalyseLinksAsync` with a dry-run WIQL check (or have `AzureDevOpsDependencyAnalysisService.ValidateWiqlAsync` issue the query and surface any server 400), and if the server returns a WIQL error, print the message and `return 1` immediately; the CSV file must NOT be created and no API calls beyond the initial WIQL validation call must have been made (FR-011: "exits with error code 1 and a human-readable message identifying the WIQL syntax error **before making any network calls**" — the validation call itself is the only permitted network call in the error path) — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/032-icapture-interface
  - Evidence: WIQL failure behavior now occurs through queue-mode services; no standalone command pre-validation path exists.
- [ ] T029 [US3] Add `DependencyCommand_WithValidWiqlFilter_ExecutesSuccessfully` unit test to `DependencyCommandTests.cs` — mock `IDependencyDiscoveryService`, verify that when `--wiql` string is passed it is forwarded unchanged to `DiscoverDependenciesAsync` — Status: incomplete
  - Evidence: No CLI `DependencyCommand` test method named `DependencyCommand_WithValidWiqlFilter_ExecutesSuccessfully` exists.
- [ ] T030 [US3] Add `DependencyCommand_WithInvalidWiqlFilter_ExitsWithCode1` unit test — mock service to throw `InvalidOperationException("WIQL syntax error: ...")`, verify exit code 1 and error message in output — Status: incomplete
  - Evidence: No CLI `DependencyCommand_WithInvalidWiqlFilter_ExitsWithCode1` test exists.
- [ ] T031 [P] [US3] Add WIQL-filter unit tests to `AzureDevOpsDependencyAnalysisServiceTests.cs` — (1) custom `wiqlFilter` string is passed to `QueryByWiqlAsync` verbatim; (2) server 400 response surfaces as `InvalidOperationException` with meaningful message — Status: incomplete
  - Evidence: No WIQL 400-path unit tests are present in current dependency analysis tests.

**Checkpoint**: `dotnet build` passes; all new tests pass; `--wiql` flag filters work items; invalid WIQL exits with code 1

---

## Phase 6: User Story 4 — Project-Level Dependency Summary for Consolidation Planning (Priority: P2)

**Goal**: After streaming work-item links to CSV, compute a project-level dependency summary (second CSV row per directed project pair) and a Mermaid diagram showing the project dependency graph. All computation is done via a live accumulator during the streaming pass — zero additional API calls. Cross-org targets appear as leaf nodes in the diagram.

**Memory profile for millions of links**: Project-pair accumulator holds `Dictionary<ProjectPairKey, int>` bounded by P² (P = project count, typically 100s–1000s). Per-link memory is O(1). This scales from millions of links without heap pressure.

**Independent Test**: Run `devopsmigration discovery dependencies --config discovery-config-with-cross-org.json` against an org with cross-project and cross-org links. Both `discovery-project-dependencies.csv` and `discovery-project-dependencies.md` exist; diagram renders in GitHub/ADO wiki without errors; project pairs are aggregated correctly; cross-org nodes are visually distinct in the diagram (orange).

### Implementation for User Story 4

- [X] T032 [P] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/ProjectPairKey.cs` — `internal readonly record struct ProjectPairKey(string SourceProject, string TargetProject, string TargetOrganisation, LinkScope LinkScope)` — lightweight key for streaming accumulator dictionary; `TargetOrganisation` is empty for CrossProject pairs, hostname for CrossOrganisation; exact equality and hashcode for dictionary viability — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor
  - Evidence: Project pair key exists at `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/DependencyGraph/ProjectPairKey.cs`.
- [X] T033 [P] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/ProjectDependencyRecord.cs` — `internal record ProjectDependencyRecord` with properties `SourceProject`, `TargetProject`, `TargetOrganisation`, `LinkCount`, `LinkScope`, `GroupId` (int); written to `discovery-project-dependencies.csv` and used by MermaidDiagramBuilder — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor
  - Evidence: `ProjectDependencyRecord` exists in dependency graph domain (`Infrastructure.Agent`), not CLI command layer.
- [X] T034 Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/MermaidDiagramBuilder.cs` — `internal sealed class MermaidDiagramBuilder` emits Mermaid `flowchart LR` syntax; (a) takes `IEnumerable<ProjectDependencyRecord>` source, (b) builds node ID map with sanitisation rule `Regex.Replace(name, @"[^a-zA-Z0-9]", "_")` + prefix `P_`, (c) emits `P_SourceId -->|"N links"| P_TargetId` for each pair, (d) applies `:::external` class to cross-org target nodes, (e) footer: `classDef external fill:#f96,stroke:#c63,color:#000`, (f) validates Mermaid syntax and escapes node labels in double quotes per Mermaid v10 spec — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor
  - Evidence: `MermaidDiagramBuilder` exists at `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/DependencyGraph/MermaidDiagramBuilder.cs`.
- [ ] T035 [P] [US4] Create unit test `MermaidDiagramBuilderTests.cs` in `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/` — (1) sanitisation: `"My.Project"` → `P_My_Project`; (2) cross-org node receives `:::external` class; (3) edge labels are quoted; (4) output is valid Mermaid (no unclosed blocks, proper syntax); (5) renders correctly in GitHub Markdown preview — Status: incomplete
  - Evidence: `MermaidDiagramBuilderTests.cs` does not exist; only transitive diagram tests are present (`TransitiveMermaidBuilderTests.cs`).
- [X] T036 [US4] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — in `ExecuteInternalAsync`, after `CreateHost(...)`, declare `var projectAccumulator = new Dictionary<ProjectPairKey, int>();` alongside the CSV `StreamWriter`; for each `DependencyFoundEvent`, compute the `ProjectPairKey` and increment the counter: `projectAccumulator.TryGetValue(key, out var count); projectAccumulator[key] = count + 1;` (or use `Interlocked.Increment` if parallel); after the stream completes, convert the accumulator to `List<ProjectDependencyRecord>` with GroupIds assigned via Union-Find (see T037) — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor
  - Evidence: Streaming accumulator is implemented in `DependencyOrchestrator.GenerateAnalysisOutputsAsync`.
- [X] T037 [US4] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/UnionFindComponentLabeler.cs` — `internal static class UnionFindComponentLabeler` with a single public method `AssignComponentIds(IEnumerable<ProjectDependencyRecord> pairs) : Dictionary<string, int>` — builds a graph from project pair edges, runs Union-Find to identify connected components, assigns `GroupId` (1-based) to all nodes in each component; all projects reachable via directed or undirected edges get the same GroupId (including cross-org targets as leaf nodes) — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor
  - Evidence: `UnionFindComponentLabeler` exists at `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/DependencyGraph/UnionFindComponentLabeler.cs`.
- [X] T038 [US4] Modify `DependencyCommand.ExecuteInternalAsync` — after the stream, call `UnionFindComponentLabeler.AssignComponentIds(projectPairs)` to label connected components; then: (a) write `discovery-project-dependencies.csv` rows sorted by LinkCount descending, (b) build Mermaid diagram and write to `discovery-project-dependencies.md`, (c) print compact project dependency table to console (source\u2192target, link count, scope, sorted by count desc); cross-org targets prefixed with 🌐; outputs only written if at least one external dependency is found — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor
  - Evidence: Project CSV + Mermaid outputs are written by `DependencyOrchestrator` (`discovery-project-dependencies.csv/.md`).
- [X] T039 [US4] Modify `DependencyCommand.Settings` — add three new `CommandOption` fields: `--output-projects <PATH>` (default `discovery-project-dependencies.csv` in same dir as `--output`), `--output-diagram <PATH>` (default `discovery-project-dependencies.md` in same dir as `--output`); both are optional and override the default paths if provided — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/032-icapture-interface
  - Evidence: Output path control now package/job-driven in orchestration, not command options.
- [ ] T040 [P] [US4] Add project-level aggregation unit tests to `DependencyCommandTests.cs` — (1) given 100 links between ProjectA→ProjectB and 30 between ProjectA→ProjectC, assert `ProjectDependencyRecord` rows total 2 with correct link counts; (2) given cross-org link ProjectA→org2, assert row has `TargetOrganisation` and `LinkScope=CrossOrganisation`; (3) mock accumulator, assert Mermaid diagram contains both nodes and edges with correct labels; (4) verify GroupId assignment: ProjectA↔ProjectB↔ProjectC should all have GroupId=1 — Status: incomplete
  - Evidence: No `DependencyCommandTests.cs` project-aggregation tests exist in current test tree.
- [ ] T041 [P] [US4] Create integration test scenario `scenarios/discovery-dependency-with-cross-org.json` — points to two orgs with at least one cross-org link for manual verification that all three outputs (work-item CSV, project CSV, diagram) are written correctly — Status: incomplete
  - Evidence: `scenarios/discovery-dependency-with-cross-org.json` is missing.

**Checkpoint**: `dotnet build` passes; all new tests pass; `discovery-project-dependencies.csv` and `.md` exist; diagram renders in GitHub/ADO wiki; project-pair counts match hand-computed aggregation

---

## Phase 7: TFS Subprocess Delegation (Prerequisite for FR-013)

**Goal**: `TeamFoundationServer` org entries delegate to `tfsmigration.exe dependencies` via `TfsDependencyProcessAdapter` using the same NDJSON subprocess bridge pattern as `TfsExporterProcessAdapter`.

**Independent Test**: With a TFS Windows-auth config, `discovery dependencies` spawns the subprocess and reads NDJSON output records without error. (Automated as a manual/Windows-only test.)

### Implementation for Phase 7

- [ ] T042 Create `src/DevOpsMigrationPlatform.CLI.Migration/TfsDependencyProcessAdapter.cs` — follows `TfsExporterProcessAdapter` pattern; constructor takes `IExternalToolRunner`, `ILogger<TfsDependencyProcessAdapter>`; `AnalyseLinksAsync(organisationUrl, project, pat, wiqlFilter, ct)` builds stdin JSON `{"collectionUrl":..., "project":..., "pat":..., "wiqlFilter":...}`, spawns `tfsmigration.exe dependencies` via `IExternalToolRunner`, reads NDJSON stdout lines, deserialises each as either `dependency-found` (→`DependencyFoundEvent`) or `heartbeat` (→`DependencyHeartbeatEvent`), relays via `yield return`, on non-zero exit code throws `InvalidOperationException` with stderr content — Status: incomplete
  - Evidence: `src/DevOpsMigrationPlatform.CLI.Migration/TfsDependencyProcessAdapter.cs` is missing.
- [ ] T043 Modify `src/DevOpsMigrationPlatform.Infrastructure/Services/DependencyDiscoveryService.cs` — replace `NotSupportedException` for `TeamFoundationServer` with keyed service resolution: call `_serviceProvider.GetKeyedService<IWorkItemLinkAnalysisService>("TeamFoundationServer")`; if the keyed service is null (i.e., not registered — e.g., in a non-CLI host), throw `InvalidOperationException("TFS source requires TfsDependencyProcessAdapter, which is only registered in the CLI.Migration host.")`. DO NOT inject `TfsDependencyProcessAdapter` directly — `Infrastructure` must never reference any type from `CLI.Migration`. — Status: incomplete
  - Evidence: `DependencyDiscoveryService` still throws `NotSupportedException` for `TeamFoundationServer` keyed service absence (`src/.../DependencyDiscoveryService.cs:83-91`).
- [ ] T044 Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — in `CreateHost`, register `TfsDependencyProcessAdapter` as a **keyed singleton** with key `"TeamFoundationServer"` implementing `IWorkItemLinkAnalysisService`, and register `IExternalToolRunner → ExternalToolRunner` (mirror `MigrationExportCommand` registration pattern). This is the only place `TfsDependencyProcessAdapter` is registered — it never appears in `Infrastructure` or `Infrastructure.AzureDevOps`. — Status: incomplete
  - Evidence: No `DependencyCommand` host registration exists; CLI uses `queue` command only (`src/.../Program.cs`).
- [ ] T045 Add `dependencies` subcommand to `src/DevOpsMigrationPlatform.CLI.TfsMigration` — reads stdin JSON as `TfsDependencyRequest`, queries `WorkItemStore` for all (or WIQL-filtered) work items, inspects `WorkItem.WorkItemLinks`, classifies as `CrossProject` (same collection, different project) or `CrossOrganisation` (different collection URI), emits NDJSON heartbeat + dependency-found records to stdout; exit 0 on success — Status: incomplete
  - Evidence: `src/DevOpsMigrationPlatform.CLI.TfsMigration` project/path does not exist in current repository; no `dependencies` subcommand implementation found there.
- [ ] T046 [P] Add `TfsDependencyProcessAdapter_EmitsFoundEvents` unit test — mock `IExternalToolRunner` to return NDJSON lines; verify `DependencyFoundEvent` records produced correctly; verify non-zero exit code throws — Status: incomplete
  - Evidence: No `TfsDependencyProcessAdapter_EmitsFoundEvents` test exists.

**Checkpoint**: `dotnet build` (both solutions) passes; TFS subprocess wiring compiles; `TfsInventory` launch profile still works

---

## Phase 8: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: All three discrepancies from `discrepancies.md` are resolved and canonical docs updated.

- [X] T047 Update `.agents/30-context/domains/cli-commands.md` — add `| discovery dependencies | DependencyCommandSettings | Analyse work items for cross-project and cross-organisation links. Results written to discovery-dependencies.csv, project pairs to discovery-project-dependencies.csv, and diagram to discovery-project-dependencies.md. |` to the Discovery Commands table; add invocation examples: `devopsmigration discovery dependencies --config migration.json` and `devopsmigration discovery dependencies --config migration.json --output ./reports/deps.csv`; add `branch.AddCommand<DependencyCommand>("dependencies");` to the registration code block — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/021.2-separation-of-concerns
  - Evidence: CLI canonical command docs now describe queue-mode Dependencies (`.agents/30-context/domains/cli-commands.md`).
- [X] T048 [P] Update `docs/cli-guide.md` — add `### discovery dependencies` sub-section under `## Discovery Commands` (or add `## Discovery Commands` heading if absent): describe purpose, `--config`, `--output`, `--output-projects`, `--output-diagram`, `--wiql` options, and console output format (two tables); reference `quickstart.md` for full examples — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/021.2-separation-of-concerns
  - Evidence: `docs/cli-guide.md` documents queue-mode Dependencies rather than `discovery dependencies` subcommand.
- [X] T049 [P] Update `docs/capabilities-guide.md` — add a **Dependency Analysis** paragraph after each source type's Inventory section: (a) `AzureDevOpsServices` → REST batch-GET with `WorkItemExpand.Relations`; secondary batch-GET for project resolution; concurrency via `MaxConcurrency`; (b) `TeamFoundationServer` → subprocess delegation to `tfsmigration.exe dependencies` via same stdin JSON / NDJSON stdout protocol; (c) `Simulated` → synthetic seeded records with configurable count — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/021.2-separation-of-concerns
  - Evidence: `docs/capabilities-guide.md` contains Simulated dependency analysis details under source types.
- [ ] T050 Mark all three items in `specs/012-discovery-dependencies/discrepancies.md` as `Resolved` — Status: incomplete
  - Evidence: `specs/012-discovery-dependencies/discrepancies.md` remains `Status: Pending rectification`.
- [ ] T051 [P] Review `analysis/pending-actions.md` — remove or mark as resolved any items addressed by this spec — Status: incomplete
  - Evidence: No evidence in `analysis/pending-actions.md` that 012-specific items were resolved; file still tracks unrelated pending entries.
- [X] T052 Run `dotnet clean && dotnet build --no-incremental` — MUST pass with zero errors and zero warnings — Status: complete
- [ ] T053 Run `dotnet test` — ALL tests MUST pass (including the new unit tests; system tests skipped if env vars absent) — Status: incomplete
  - Evidence: `dotnet test DevOpsMigrationPlatform.slnx` did not complete within session timeout; full-suite pass evidence unavailable.
- [X] T054 Run `🔍 Migration CLI: Dependencies (Single Project)` launch profile via `.vscode/launch.json` — verify observable output: exit code 0, all three output files written, summary tables printed to terminal — Status: complete/superseded; completed because superseded by specs/030-module-analiser-refactor + specs/021.2-separation-of-concerns
  - Evidence: Launch profile exists as `Queue Dependencies (Single Project)` in `.vscode/launch.json`.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No prerequisites — start immediately
- **Phase 2 (Foundational)**: No prerequisites — can start in parallel with Phase 1
- **Phase 3 (US1)**: Requires Phase 2 complete — BLOCKS if T004–T013 not done
- **Phase 4 (US2)**: Requires Phase 3 complete (needs Phase 3 infra and command)
- **Phase 5 (US3)**: Requires Phase 3 complete; independent of Phase 4
- **Phase 6 (US4)**: Requires Phase 3 complete; benefits from Phases 4–5 optional (Simulated+WIQL testing)
- **Phase 7 (TFS)**: Requires Phase 3 complete; independent of Phases 4–6
- **Phase 8 (Docs)**: Requires Phases 3–7 complete (or explicit deferral of 4–7)

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no dependency on US2/US3
- **US2 (P2)**: Enhances the Phase 3 command; shares `DependencyDiscoveryService`
- **US3 (P3)**: Adds `--wiql` parameter wiring; independent of US2 Simulated work
- **US4 (P2)**: Project-level aggregation; independent of US2/US3; benefits from existing `DependencyCommand` and `DependencyDiscoveryService`

### Parallel Opportunities within Phases

**Phase 2**: T004–T013 are largely parallelisable; T008 depends on T004+T005+T006 (uses `DependencyRecord` and enums)  
**Phase 3**: T014 and T015 can proceed in parallel; T016–T018 follow after T014+T015  
**Phase 4**: T021+T022 can run in parallel with T023+T024  
**Phase 5**: All T027–T031 can run after Phase 3  
**Phase 6**: T032+T033+T035+T039 are independent; T034 follows T032+T033; T036–T040 follow T034  
**Phase 7**: T042+T043+T045 are independent; T044 follows T042+T043  
**Phase 8**: T047–T054 are sequential; T052–T054 are final gates  

---

## MVP Scope Suggestion

To deliver a working command as quickly as possible:
1. **Phase 1** + **Phase 2** (T001–T013): infra + feature files (~6 tasks parallelisable)
2. **Phase 3** (T014–T020): full US1 including system test (~7 tasks, core value)

At the end of Phase 3 the command produces a working CSV for ADO organisations. Phases 4–7 incrementally add US2 warnings, WIQL filtering, project-level summary with diagram, TFS support, and doc sync.

---

## Format Validation

All tasks follow the mandatory checklist format `- [ ] [TaskID] [P?] [Story?] Description with file path`:

- Sequential IDs T001–T054 in execution order ✓
- `[P]` markers on parallelisable tasks (different files, no pending dependencies) ✓
- `[US1]`/`[US2]`/`[US3]`/`[US4]` labels on all user-story-phase tasks ✓
- Setup and Foundational phase tasks have no story label ✓
- Every task includes an exact file path ✓

---

## Summary of Phases

| Phase | User Story | Tasks | Purpose |
|-------|-----------|-------|---------|
| 1 | — | T001–T003 (3 tasks) | Setup: scenario config, launch entries |
| 2 | — | T004–T013 (10 tasks) | Foundational: abstractions, enums, interfaces, feature files |
| 3 | US1 | T014–T020 (7 tasks) | CSV streaming, cross-project link detection, ADO REST |
| 4 | US2 | T021–T026 (6 tasks) | Cross-org detection, `⚠` warnings, Simulated source |
| 5 | US3 | T027–T031 (5 tasks) | WIQL filter support, validation |
| 6 | US4 | T032–T041 (10 tasks) | Project aggregator, MermaidDiagramBuilder, GroupId assignment |
| 7 | — | T042–T046 (5 tasks) | TFS subprocess delegation |
| 8 | — | T047–T054 (8 tasks) | Documentation sync + final gates |
| **TOTAL** | | **54 tasks** | |

