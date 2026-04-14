# Tasks: Discovery Dependency Analysis

**Input**: Design documents from `/specs/012-discovery-dependencies/`  
**Tech stack**: C# 12 / .NET 10 · Spectre.Console.Cli · MSTest + Moq  
**User stories**: US1 Cross-Project Links (P1) · US2 Cross-Organisation Links (P2) · US3 WIQL Filter (P3)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Add the scenario config file and launch entry so the command can be run manually against a real ADO org from day one. No implementation yet.

- [ ] T001 Create `scenarios/discovery-dependency-ado-single-project.json` — `DiscoveryOptions`-format config pointing to `$ENV:AZDEVOPS_SYSTEM_TEST_ORG` / `migrationTest5` with PAT from `$ENV:AZDEVOPS_SYSTEM_TEST_PAT` (mirror `scenarios/inventory-ado-single-project.json` structure)
- [ ] T002 [P] Add `🔍 Migration CLI: Dependencies (Single Project)` launch entry to `.vscode/launch.json` — args: `discovery dependencies --config scenarios/discovery-dependency-ado-single-project.json`, env: `AZDEVOPS_SYSTEM_TEST_ORG`/`AZDEVOPS_SYSTEM_TEST_PAT`, preLaunchTask: `build-migration-cli`
- [ ] T003 [P] Add `🔍 Migration CLI: Dependencies (WIQL Filter)` launch entry to `.vscode/launch.json` — args: `discovery dependencies --config scenarios/discovery-dependency-ado-single-project.json --wiql "SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'User Story'"`, same env as T002

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: All domain types and interfaces in `Abstractions` that every user story depends on. Must be complete before any story implementation begins.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [ ] T004 Create `src/DevOpsMigrationPlatform.Abstractions/Models/LinkScope.cs` — `public enum LinkScope { CrossProject, CrossOrganisation }` with XML doc comments; NO `SameProject` value
- [ ] T005 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/TargetStatus.cs` — `public enum TargetStatus { Reachable, Deleted, AccessDenied, Unknown }` with XML doc comments
- [ ] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/DependencyRecord.cs` — `public record DependencyRecord` with nine `init`-only properties: `SourceWorkItemId` (int), `SourceWorkItemType` (string), `SourceProject` (string), `LinkType` (string), `LinkScope` (LinkScope), `TargetWorkItemId` (int), `TargetProject` (string), `TargetOrganisation` (string), `TargetStatus` (TargetStatus)
- [ ] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/DependencySummary.cs` — `public record DependencySummary` with `WorkItemsAnalysed`, `ExternalLinksFound`, `CrossProjectCount`, `CrossOrgCount` (int), `ReportFilePath` (string), all `init`-only
- [ ] T008 Create `src/DevOpsMigrationPlatform.Abstractions/Models/DependencyProgressEvent.cs` — `public abstract record DependencyProgressEvent`; nested `public sealed record DependencyFoundEvent(DependencyRecord Record) : DependencyProgressEvent`; `public sealed record DependencyHeartbeatEvent(string OrganisationUrl, string ProjectName, int WorkItemsAnalysed, int ExternalLinksFound, int CrossProjectCount, int CrossOrgCount, bool IsComplete, string? Error = null) : DependencyProgressEvent`
- [ ] T009 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Services/IDependencyDiscoveryService.cs` — `public interface IDependencyDiscoveryService` with `IAsyncEnumerable<DependencyProgressEvent> DiscoverDependenciesAsync(string? wiqlFilter, CancellationToken cancellationToken = default)`
- [ ] T010 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Services/IWorkItemLinkAnalysisService.cs` — `public interface IWorkItemLinkAnalysisService` with `IAsyncEnumerable<DependencyProgressEvent> AnalyseLinksAsync(string organisationUrl, string project, string pat, string? wiqlFilter, CancellationToken cancellationToken = default)`
- [ ] T011 Modify `src/DevOpsMigrationPlatform.Abstractions/Options/DiscoveryOptions.cs` — add `public int MaxConcurrency { get; set; } = 4;` property with XML doc comment; verify `dotnet build` still passes for both `net481` and `net10.0` targets
- [ ] T012 Create `features/cli/discovery/dependency-command-wiring.feature` — CLI-tier Gherkin for acceptance scenarios: (1) command runs and writes CSV to CWD; (2) zero external dependencies prints "No external dependencies found." and writes header-only CSV; (3) cross-org count appears in summary with `⚠` warning; (4) `--output` path respected; follow `acceptance-test-format.md` Format + Rules precisely (NOTE: `cli/discovery/` not `cli/inventory/` — separate sub-directory to avoid naming collision with the inventory feature file)
- [ ] T013 [P] Create `features/inventory/work-items/dependency-analysis.feature` — capability-tier Gherkin: (1) cross-project link recorded with all nine CSV fields; (2) same-project link silently discarded and never appears in report; (3) cross-organisation link recorded with `LinkScope=CrossOrganisation` and `TargetOrganisation` set; (4) inaccessible target recorded with appropriate `TargetStatus`

**Checkpoint**: Abstractions compile; feature files exist; no implementation yet

---

## Phase 3: User Story 1 — Identify Cross-Project Work Item Links (Priority: P1) 🎯 MVP

**Goal**: `DependencyCommand` produces a CSV listing every cross-project link for all configured projects in an ADO organisation. Same-project links are silently filtered.

**Independent Test**: Run `devopsmigration discovery dependencies --config scenarios/discovery-dependency-ado-single-project.json` against an org with at least one cross-project link. CSV at `discovery-dependencies.csv` exists, has header row, and at least one row with `LinkScope=CrossProject` containing all nine required fields. Terminal prints summary table.

### Implementation for User Story 1

- [ ] T014 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs` — implement `IWorkItemLinkAnalysisService`; constructor takes `IOptions<DiscoveryOptions>`, `IAzureDevOpsClientFactory`, `ILogger<AzureDevOpsDependencyAnalysisService>`; `AnalyseLinksAsync` (a) runs WIQL query (default `SELECT [System.Id] FROM WorkItems`) and **de-duplicates the returned work item IDs using `HashSet<int>`** before any batch-GET, (b) batch-GETs work items with `WorkItemExpand.Relations` in pages of 200, (c) for each relation parses target URL host to detect cross-org (host ≠ source org host) vs same-host, (d) for cross-org relations: emit `DependencyFoundEvent` with `LinkScope.CrossOrganisation`, `TargetOrganisation` = parsed host, `TargetProject` = "", `TargetStatus` = result of unauthenticated GET to target URL (Reachable/Deleted/AccessDenied/Unknown — never throws), (e) for same-host relations: batch-GET `System.TeamProject` of target IDs and classify as `CrossProject` or `SameProject`; discard `SameProject` silently, (f) for `CrossProject`: set `TargetStatus` from HTTP response codes (200→Reachable, 404→Deleted, 401/403→AccessDenied, other→Unknown), (g) emits `DependencyFoundEvent` per external link and `DependencyHeartbeatEvent` after each batch of 200 source items; uses `SemaphoreSlim(_options.Value.MaxConcurrency)` for parallel batches
- [ ] T014a [US1] Add de-duplication unit test to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — given a WIQL result returning work item ID 42 twice, assert that the service calls `GetWorkItemsBatchAsync` with ID 42 exactly once (de-duplication via `HashSet<int>` prevents duplicate processing)
- [ ] T015 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure/Services/DependencyDiscoveryService.cs` — implement `IDependencyDiscoveryService`; constructor takes `IOptions<DiscoveryOptions>`, `IEnumerable<IWorkItemLinkAnalysisService>` (resolved by keyed DI via `IServiceProvider` factory — see T016), `ILogger<DependencyDiscoveryService>`; `DiscoverDependenciesAsync` iterates enabled `DiscoveryOptions.Organisations`, resolves the correct `IWorkItemLinkAnalysisService` implementation by `entry.Type` key string: `"AzureDevOpsServices"` → ADO implementation, `"Simulated"` → Simulated implementation (US1 MVP: if no keyed service registered for `"Simulated"`, throw `NotSupportedException` "Simulated source not yet implemented — add in Phase 4"), `"TeamFoundationServer"` → TFS adapter (US1 MVP: if not registered, throw `NotSupportedException` "TFS source requires TfsDependencyProcessAdapter — registered in CLI host only"); yields all events; NEVER references any concrete implementation type from `CLI.Migration` or `Infrastructure.AzureDevOps` directly
- [ ] T016 Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DependencyServiceCollectionExtensions.cs` — `public static IServiceCollection AddAzureDevOpsDependencyAnalysis(this IServiceCollection services, IConfiguration configuration)`: binds `DiscoveryOptions`, registers `IAzureDevOpsClientFactory` (if not already), registers `AzureDevOpsDependencyAnalysisService` as a **keyed singleton** with key `"AzureDevOpsServices"` implementing `IWorkItemLinkAnalysisService`, registers `IDependencyDiscoveryService → DependencyDiscoveryService` (keyed service resolution via `IServiceProvider.GetKeyedService<IWorkItemLinkAnalysisService>(key)`)
- [ ] T017 [US1] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — `public sealed class DependencyCommand : CommandBase<DependencyCommand.Settings>`; `Settings` has `[CommandOption("--output")] string? OutputPath` and `[CommandOption("--wiql")] string? WiqlFilter`; `ExecuteInternalAsync` calls `CreateHost(..., (s,c) => s.AddAzureDevOpsDependencyAnalysis(c))`, resolves `IDependencyDiscoveryService`, opens `StreamWriter` to output path (warn if file exists), writes CSV header, iterates `DiscoverDependenciesAsync` pattern-matching on `DependencyFoundEvent` (write row) vs `DependencyHeartbeatEvent` (update Spectre.Console live table), after loop flushes CSV and writes `DependencySummary` table to console; handles "No external dependencies found." case (FR-008)
- [ ] T018 [US1] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs` — add `branch.AddCommand<DependencyCommand>("dependencies")` inside `config.AddBranch("discovery", ...)` with `.WithDescription(...)` and `.WithExample("discovery", "dependencies", "--config", "scenarios/discovery-dependency-ado-single-project.json")`
- [ ] T019 [US1] Create `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/DependencyCommandTests.cs` — unit test: `DependencyCommand_CanBeConstructed_WithParameterlessConstructor`; system test: `[TestCategory("SystemTest")] [Timeout(300_000)] DependencyCommand_SystemTest_AdoSingleProject_ScenarioFile_ExecutesSuccessfully` — skips if env vars absent, runs CLI via `CliRunner`, asserts exit code 0, "dependencies" success message, CSV file exists with header, ≥2 lines total (or "No external dependencies found." when project has no cross-project links)
- [ ] T020 [P] [US1] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — unit tests using `Mock<IAzureDevOpsClientFactory>`: (1) same-project relations are discarded (zero `DependencyFoundEvent` emitted); (2) cross-project relation emits one `DependencyFoundEvent` with correct fields; (3) `TargetStatus.Deleted` when secondary batch-GET returns 404; (4) `TargetStatus.AccessDenied` when 403; (5) `DependencyHeartbeatEvent` emitted after each batch of 200

**Checkpoint**: `dotnet build` passes; `DependencyCommand_CanBeConstructed` passes; manual launch profile runs without exception; CSV with header is written

---

## Phase 4: User Story 2 — Identify Cross-Organisation Work Item Links (Priority: P2)

**Goal**: Cross-organisation links appear in the CSV with `LinkScope=CrossOrganisation`, and the terminal summary shows a distinct `⚠` warning count for them. Simulated source fully implemented (enables CI testing without live ADO).

**Independent Test**: CSV rows with `LinkScope=CrossOrganisation` have `TargetOrganisation` set to the remote host and `TargetProject` may be empty. Terminal summary shows cross-org count with `⚠`.

### Implementation for User Story 2

- [ ] T021 [US2] Add cross-org unit tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — (1) cross-org link (target host ≠ source host) emits `DependencyFoundEvent` with `LinkScope.CrossOrganisation`, non-empty `TargetOrganisation`, empty `TargetProject`; (2) unreachable cross-org target yields `TargetStatus.Unknown` without throwing; (3) cross-org link with deleted target yields `TargetStatus.Deleted`. Note: the cross-org *emission logic* is part of T014 — T021 adds test coverage and verifies it end-to-end.
- [ ] T022 [US2] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — update console summary rendering: cross-org count row must include `⚠ ACTION REQUIRED — links will break` text when `CrossOrgCount > 0`; separate row from CrossProject count (FR-007, SC-006)
- [ ] T023 [US2] Create `src/DevOpsMigrationPlatform.Infrastructure/Services/SimulatedDependencyAnalysisService.cs` — implement **`IWorkItemLinkAnalysisService`** (not just a concrete class); constructor takes `IOptions<DiscoveryOptions>`, `ILogger<SimulatedDependencyAnalysisService>`; `AnalyseLinksAsync` uses `new Random(seed)` (seed from org entry or 42 by default), generates `workItemCount` (default 100) synthetic work items with 3 links each, 70% CrossProject / 30% CrossOrganisation, emits deterministic `DependencyFoundEvent` + `DependencyHeartbeatEvent` records; never emits `SameProject`
- [ ] T024 [US2] Register `SimulatedDependencyAnalysisService` in `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/DependencyServiceCollectionExtensions.cs` — add `services.AddKeyedSingleton<IWorkItemLinkAnalysisService, SimulatedDependencyAnalysisService>("Simulated")` alongside the ADO registration; `DependencyDiscoveryService` already resolves by key — no constructor change needed
- [ ] T025 [P] [US2] Create `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/SimulatedDependencyAnalysisServiceTests.cs` — unit tests: (1) given seed 42, produces identical record sequence on repeated calls (determinism); (2) produces no `SameProject` links; (3) CrossProject + CrossOrganisation proportions approximately 70/30; (4) zero `workItemCount` yields only one `DependencyHeartbeatEvent(IsComplete=true)` with zero counts
- [ ] T026 [P] [US2] Add unit tests to `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Dependencies/AzureDevOpsDependencyAnalysisServiceTests.cs` — (1) cross-org link emits `DependencyFoundEvent` with `LinkScope.CrossOrganisation` and non-empty `TargetOrganisation`; (2) cross-org link where target URL is unreachable yields `TargetStatus.Unknown` without throwing

**Checkpoint**: `dotnet build` passes; all new tests pass; Simulated org entry in scenario config produces synthetic report; summary `⚠` warning visible for any scenario with cross-org links

---

## Phase 5: User Story 3 — Scoped Discovery with WIQL Filter (Priority: P3)

**Goal**: `--wiql` parameter scopes analysis to matching work items only. Invalid WIQL exits with code 1 and a human-readable error before any network call to the link analysis APIs.

**Independent Test**: Provide `--wiql "SELECT [System.Id] FROM WorkItems WHERE [System.WorkItemType] = 'User Story'"` and verify only User Story work items appear in the `SourceWorkItemType` column of the CSV.

### Implementation for User Story 3

- [ ] T027 [US3] Modify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsDependencyAnalysisService.cs` — wire `wiqlFilter` parameter through WIQL execution: when non-null, use it as the query body in `QueryByWiqlAsync`; when null, use `SELECT [System.Id] FROM WorkItems`; catch `VssServiceResponseException` with status 400 (or similar bad-request), wrap as `InvalidOperationException` containing the server error `Message`, propagate to caller (FR-011: command exits 1 with human-readable WIQL error)
- [ ] T028 [US3] Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — in `ExecuteInternalAsync`, perform WIQL pre-validation as the **very first operation**, before opening the `StreamWriter` or making any API calls: call `AnalyseLinksAsync` with a dry-run WIQL check (or have `AzureDevOpsDependencyAnalysisService.ValidateWiqlAsync` issue the query and surface any server 400), and if the server returns a WIQL error, print the message and `return 1` immediately; the CSV file must NOT be created and no API calls beyond the initial WIQL validation call must have been made (FR-011: "exits with error code 1 and a human-readable message identifying the WIQL syntax error **before making any network calls**" — the validation call itself is the only permitted network call in the error path)
- [ ] T029 [US3] Add `DependencyCommand_WithValidWiqlFilter_ExecutesSuccessfully` unit test to `DependencyCommandTests.cs` — mock `IDependencyDiscoveryService`, verify that when `--wiql` string is passed it is forwarded unchanged to `DiscoverDependenciesAsync`
- [ ] T030 [US3] Add `DependencyCommand_WithInvalidWiqlFilter_ExitsWithCode1` unit test — mock service to throw `InvalidOperationException("WIQL syntax error: ...")`, verify exit code 1 and error message in output
- [ ] T031 [P] [US3] Add WIQL-filter unit tests to `AzureDevOpsDependencyAnalysisServiceTests.cs` — (1) custom `wiqlFilter` string is passed to `QueryByWiqlAsync` verbatim; (2) server 400 response surfaces as `InvalidOperationException` with meaningful message

**Checkpoint**: `dotnet build` passes; all new tests pass; `--wiql` flag filters work items; invalid WIQL exits with code 1

---

## Phase 6: TFS Subprocess Delegation (Prerequisite for FR-013)

**Goal**: `TeamFoundationServer` org entries delegate to `tfsmigration.exe dependencies` via `TfsDependencyProcessAdapter` using the same NDJSON subprocess bridge pattern as `TfsExporterProcessAdapter`.

**Independent Test**: With a TFS Windows-auth config, `discovery dependencies` spawns the subprocess and reads NDJSON output records without error. (Automated as a manual/Windows-only test.)

### Implementation for Phase 6

- [ ] T032 Create `src/DevOpsMigrationPlatform.CLI.Migration/TfsDependencyProcessAdapter.cs` — follows `TfsExporterProcessAdapter` pattern; constructor takes `IExternalToolRunner`, `ILogger<TfsDependencyProcessAdapter>`; `AnalyseLinksAsync(organisationUrl, project, pat, wiqlFilter, ct)` builds stdin JSON `{"collectionUrl":..., "project":..., "pat":..., "wiqlFilter":...}`, spawns `tfsmigration.exe dependencies` via `IExternalToolRunner`, reads NDJSON stdout lines, deserialises each as either `dependency-found` (→`DependencyFoundEvent`) or `heartbeat` (→`DependencyHeartbeatEvent`), relays via `yield return`, on non-zero exit code throws `InvalidOperationException` with stderr content
- [ ] T033 Modify `src/DevOpsMigrationPlatform.Infrastructure/Services/DependencyDiscoveryService.cs` — replace `NotSupportedException` for `TeamFoundationServer` with keyed service resolution: call `_serviceProvider.GetKeyedService<IWorkItemLinkAnalysisService>("TeamFoundationServer")`; if the keyed service is null (i.e., not registered — e.g., in a non-CLI host), throw `InvalidOperationException("TFS source requires TfsDependencyProcessAdapter, which is only registered in the CLI.Migration host.")`. DO NOT inject `TfsDependencyProcessAdapter` directly — `Infrastructure` must never reference any type from `CLI.Migration`.
- [ ] T034 Modify `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/DependencyCommand.cs` — in `CreateHost`, register `TfsDependencyProcessAdapter` as a **keyed singleton** with key `"TeamFoundationServer"` implementing `IWorkItemLinkAnalysisService`, and register `IExternalToolRunner → ExternalToolRunner` (mirror `MigrationExportCommand` registration pattern). This is the only place `TfsDependencyProcessAdapter` is registered — it never appears in `Infrastructure` or `Infrastructure.AzureDevOps`.
- [ ] T035 Add `dependencies` subcommand to `src/DevOpsMigrationPlatform.CLI.TfsMigration` — reads stdin JSON as `TfsDependencyRequest`, queries `WorkItemStore` for all (or WIQL-filtered) work items, inspects `WorkItem.WorkItemLinks`, classifies as `CrossProject` (same collection, different project) or `CrossOrganisation` (different collection URI), emits NDJSON heartbeat + dependency-found records to stdout; exit 0 on success
- [ ] T036 [P] Add `TfsDependencyProcessAdapter_EmitsFoundEvents` unit test — mock `IExternalToolRunner` to return NDJSON lines; verify `DependencyFoundEvent` records produced correctly; verify non-zero exit code throws

**Checkpoint**: `dotnet build` (both solutions) passes; TFS subprocess wiring compiles; `TfsInventory` launch profile still works

---

## Phase 7: Documentation Sync (MANDATORY — cannot be skipped)

**Purpose**: All three discrepancies from `discrepancies.md` are resolved and canonical docs updated.

- [ ] T037 Update `.agents/context/cli-commands.md` — add `| discovery dependencies | DependencyCommandSettings | Analyse work items for cross-project and cross-organisation links. Results written to discovery-dependencies.csv. |` to the Discovery Commands table; add invocation examples: `devopsmigration discovery dependencies --config migration.json` and `devopsmigration discovery dependencies --config migration.json --output ./reports/deps.csv`; add `branch.AddCommand<DependencyCommand>("dependencies");` to the registration code block
- [ ] T038 [P] Update `docs/cli.md` — add `### discovery dependencies` sub-section under `## Discovery Commands` (or add `## Discovery Commands` heading if absent): describe purpose, `--config`, `--output`, `--wiql` options, and console output format; reference `quickstart.md` for full examples
- [ ] T039 [P] Update `docs/source-types.md` — add a **Dependency Analysis** paragraph after each source type's Inventory section: (a) `AzureDevOpsServices` → REST batch-GET with `WorkItemExpand.Relations`; secondary batch-GET for project resolution; concurrency via `MaxConcurrency`; (b) `TeamFoundationServer` → subprocess delegation to `tfsmigration.exe dependencies` via same stdin JSON / NDJSON stdout protocol; (c) `Simulated` → synthetic seeded records with configurable count
- [ ] T040 Mark all three items in `specs/012-discovery-dependencies/discrepancies.md` as `Resolved`
- [ ] T041 [P] Review `analysis/pending-actions.md` — remove or mark as resolved any items addressed by this spec
- [ ] T042 Run `dotnet clean && dotnet build --no-incremental` — MUST pass with zero errors and zero warnings
- [ ] T043 Run `dotnet test` — ALL tests MUST pass (including the new unit tests; system tests skipped if env vars absent)
- [ ] T044 Run `🔍 Migration CLI: Dependencies (Single Project)` launch profile via `.vscode/launch.json` — verify observable output: exit code 0, CSV written to `discovery-dependencies.csv`, summary table printed to terminal

---

## Dependencies & Execution Order

### Phase Dependencies

- **Phase 1 (Setup)**: No prerequisites — start immediately
- **Phase 2 (Foundational)**: No prerequisites — can start in parallel with Phase 1
- **Phase 3 (US1)**: Requires Phase 2 complete — BLOCKS if T004–T013 not done
- **Phase 4 (US2)**: Requires Phase 3 complete (needs Phase 3 infra and command)
- **Phase 5 (US3)**: Requires Phase 3 complete; independent of Phase 4
- **Phase 6 (TFS)**: Requires Phase 3 complete; independent of Phases 4 and 5
- **Phase 7 (Docs)**: Requires Phases 3–6 complete (or explicit deferral of 4–6)

### User Story Dependencies

- **US1 (P1)**: Can start after Phase 2 — no dependency on US2/US3
- **US2 (P2)**: Enhances the Phase 3 command; shares `DependencyDiscoveryService`
- **US3 (P3)**: Adds `--wiql` parameter wiring; independent of US2 Simulated work

### Parallel Opportunities within Phases

**Phase 2**: T004–T013 are largely parallelisable; T008 depends on T004+T005+T006 (uses `DependencyRecord` and enums)  
**Phase 3**: T014 and T015 can proceed in parallel; T016–T018 follow after T014+T015  
**Phase 4**: T021+T022 can run in parallel with T023+T024  
**Phase 5**: All T027–T031 can run after Phase 3  
**Phase 6**: T032+T033+T035 are independent of each other; T034 follows T032+T033  

---

## MVP Scope Suggestion

To deliver a working command as quickly as possible:
1. **Phase 1** + **Phase 2** (T001–T013): infra + feature files (~6 tasks parallelisable)
2. **Phase 3** (T014–T020): full US1 including system test (~7 tasks, core value)

At the end of Phase 3 the command produces a working CSV for ADO organisations. Phases 4–7 incrementally add US2 warnings, WIQL filtering, TFS support, and doc sync.

---

## Format Validation

All tasks follow the mandatory checklist format `- [ ] [TaskID] [P?] [Story?] Description with file path`:

- Sequential IDs T001–T044 in execution order ✓
- `[P]` markers on parallelisable tasks (different files, no pending dependencies) ✓
- `[US1]`/`[US2]`/`[US3]` labels on all user-story-phase tasks ✓
- Setup and Foundational phase tasks have no story label ✓
- Every task includes an exact file path ✓
