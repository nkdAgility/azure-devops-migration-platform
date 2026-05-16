# Tasks: Work Items Inventory Command

**Input**: Design documents from `specs/003-inventory-workitems/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

---

## Phase 1: Setup

**Purpose**: Remove pre-existing violations that block compilation of the rewritten command.

- [x] T001 Delete `src/DevOpsMigrationPlatform.CLI.Migration/Commands/AzureDevOpsSettings.cs` (bare `--organisation`/`--token` CLI args — coding standards violation) — Status: complete
- [x] T002 Remove all `AzureDevOpsSettings` usages from `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs` and verify solution builds — Status: complete
- [x] T003 Remove `AzureDevOpsSettings` base class reference from `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — replace with an empty `CommandSettings` base temporarily so the project compiles. Evidence: direct `discovery inventory` command/file no longer exists under queue/job architecture (`src/DevOpsMigrationPlatform.CLI.Migration/Program.cs`). Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: All shared abstractions that every user story depends on. No US phase can begin until this phase is complete.

**⚠️ CRITICAL**: These types are referenced by CLI, Infrastructure, and TFS projects. They must exist and compile before any US implementation begins.

- [x] T004 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Options/EndpointAuthenticationOptions.cs` — `Type` (string), `AccessToken` (string?) per data-model.md — Status: complete
- [x] T005 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Options/OrganisationEntry.cs` — `Type`, `Url`, `Projects` (List\<string\>), `ApiVersion`, `Authentication` (EndpointAuthenticationOptions), `Enabled` (bool, default true) per data-model.md — Status: complete
- [x] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/InventorySummary.cs` — all 9 CSV fields: `Url`, `ProjectName`, `WorkItemsCount`, `RevisionsCount`, `ReposCount`, `PipelinesCount`, `IsComplete`, `Error`, `LastUpdatedUtc` per data-model.md. Evidence: summary model exists in evolved location `src/DevOpsMigrationPlatform.Abstractions/ControlPlaneApi/InventorySummary.cs` with queue/job telemetry integration. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/InventoryProgressEvent.cs` — `ProjectName`, `Url`, `WorkItemsCount`, `RevisionsCount`, `IsComplete`, `WindowStart`, `WindowEnd`, `WindowSize`, `Error`, `Timestamp` per data-model.md. Evidence: progress model exists at `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/InventoryProgressEvent.cs`. Status: complete/superseded; completed because superseded by specs/028.2-job-execution-by-task
- [x] T008 Create `src/DevOpsMigrationPlatform.Abstractions/Options/InventoryOptions.cs` — `ConfigVersion`, `Source` (MigrationEndpointOptions?), `Organisations` (List\<OrganisationEntry\>?); startup validation: both set → error, neither set → error, Mode 1 project null without --all-projects → error, Mode 2 empty list → error. Evidence: options model consolidated into `MigrationPlatformOptions` + endpoint/organisation entries and queue/job config pipeline (`docs/configuration-reference.md`, `src/.../Options/*`). Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T009 [P] Add `Authentication` property (`EndpointAuthenticationOptions?`) to `src/DevOpsMigrationPlatform.Abstractions/Options/MigrationEndpointOptions.cs`. Evidence: auth moved to concrete endpoint options with `ToOrganisationEndpoint()` flow (`src/DevOpsMigrationPlatform.Abstractions/Options/AzureDevOpsEndpointOptions.cs`, `.../TeamFoundationServerEndpointOptions.cs`). Status: complete/superseded; completed because superseded by specs/025-agent-config-package
- [x] T010 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Utilities/TokenResolver.cs` — static `Resolve(string? raw)`: returns null if null/empty; reads env var if starts with `$ENV:`; throws `InvalidOperationException` if env var unset; otherwise returns literal. Compiled for `net481;net10.0`. Evidence: resolver implemented as `src/DevOpsMigrationPlatform.Abstractions/Options/ConfigTokenResolver.cs`. Status: complete/superseded; completed because superseded by specs/025-agent-config-package
- [x] T011 Create `src/DevOpsMigrationPlatform.Abstractions/Services/IInventoryService.cs` — `IAsyncEnumerable<InventoryProgressEvent> CountWorkItemsAsync(string url, string project, string pat, CancellationToken ct)` per data-model.md. Evidence: inventory interface moved to `src/DevOpsMigrationPlatform.Abstractions.Agent/Discovery/IInventoryService.cs`. Status: complete/superseded; completed because superseded by specs/028.2-job-execution-by-task
- [x] T012 [P] Write `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/TokenResolverTests.cs` — literal value returned unchanged; `$ENV:VAR` reads env var; unset env var throws; null/empty returns null. Evidence: tests exist at `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/TokenResolverTests.cs`. Status: complete/superseded; completed because superseded by specs/028.2-job-execution-by-task

**Checkpoint**: All abstractions compile for net481 and net10.0. Tests pass. US implementation can now begin.

---

## Phase 3: User Story 1 — ADO Work Item Inventory (Priority: P1) 🎯 MVP

**Goal**: Operator runs `devopsmigration discovery inventory --config migration.json` against an Azure DevOps org and sees a live terminal table per project. Command exits 0 and writes `discovery-summary.csv`.

**Independent Test Criteria**: Point the CLI at a mocked `IInventoryService` returning known counts for 3 projects. Verify: one table row per project updates progressively; command exits 0; `discovery-summary.csv` exists with correct 9 columns and values; zero-item project row is present; invalid-PAT path exits non-zero.

- [x] T013 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` — date-window WIQL counting algorithm: 120-day initial window ending `DateTime.UtcNow`; if count ≥ 20,000 → halve window and retry same end date; if count == 0 → stop scanning; if count < 20,000 → yield IDs, advance window end to current window start, repeat; after success with window < 30 days → grow window by 1 day; uses `WorkItemTrackingHttpClient` WIQL endpoint. Evidence: strategy exists at `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Export/WorkItemQueryWindowStrategy.cs`. Status: complete/superseded; completed because superseded by specs/028.2-job-execution-by-task
- [x] T014 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/CatalogService.cs` — inject `WorkItemQueryWindowStrategy`; use it to bound page queries (ID-cursor paging retained within each window). Evidence: discovery/catalog moved under agent runtime (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/CatalogService.cs`) with job execution flow. Status: complete/superseded; completed because superseded by specs/028.2-job-execution-by-task
- [x] T015 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsInventoryService.cs` implementing `IInventoryService` — uses `WorkItemQueryWindowStrategy` to enumerate windows; for each window fetches `System.Rev` in batches of 200 via `GetWorkItemsAsync`; yields `InventoryProgressEvent` per window with running totals; sets `IsComplete = true` on final event. Evidence: inventory service/factory wiring exists in `src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryService.cs` and `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Factories/InventoryServiceFactory.cs`. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T016 [US1] Rewrite `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — `Settings` with `[CommandOption] --config`, `[CommandOption] --output` (default CWD), `[CommandOption] --all-projects`; load `IOptions<InventoryOptions>`; validate on execute; Mode 1 path uses `IInventoryService`; render `LiveTable` via Spectre.Console with columns: Url, Project, Work Items, Revisions, Repos, Pipelines, Updated. Evidence: command surface moved to `queue` (`src/DevOpsMigrationPlatform.CLI.Migration/Program.cs`) and inventory is `JobKind.Inventory`. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T017 [P] [US1] Register `InventoryOptions` (bound to config root), `IInventoryService` → `AzureDevOpsInventoryService`, `WorkItemQueryWindowStrategy` in `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs`. Evidence: inventory registrations moved into service extensions (`src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/InventoryServiceCollectionExtensions.cs`, `src/DevOpsMigrationPlatform.MigrationAgent/MigrationAgentServiceExtensions.cs`). Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T018 [US1] Implement CSV write on command completion in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — write `discovery-summary.csv` to `--output` directory; all 9 columns (`Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,PipelinesCount,IsComplete,Error,LastUpdatedUtc`); all projects written including failed ones. Evidence: CSV writing implemented by orchestrator as `inventory.csv` (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryOrchestrator.cs`). Status: complete/superseded; completed because superseded by specs/033-runtime-state-categories
- [x] T019 [P] [US1] Write `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — single project with known counts; zero-item project returns 0/0 `IsComplete = true`; invalid PAT surfaces as error event with non-null `Error`. Evidence: test coverage exists in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/InventoryServiceTests.cs` and `.../InventoryOrchestratorTests.cs`. Status: complete/superseded; completed because superseded by specs/028.2-job-execution-by-task

---

## Phase 4: User Story 3 — Progressive Query Narrowing (Priority: P2)

**Goal**: Any project regardless of total size is counted fully and correctly; no query ever returns a 20k-limit error; the window algorithm is verified on all edge cases.

**Independent Test Criteria**: Mock `IWorkItemTrackingHttpClient` returning exactly 20,000 items for first call, then fewer. Verify: window halves and retries; running total is correct; scanning stops on zero; window grows after narrow success; server error triggers halve-and-retry (min 3 retries).

- [x] T020 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — query returns exactly 20,000 items: window halves, retry issued, partial count NOT recorded from first attempt. Evidence: coverage moved under `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory/*` with queue/agent flow. Status: complete/superseded; completed because superseded by specs/020-resumable-batching-cursor
- [x] T021 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — query returns < 20,000 items: window advances backward by current window size; total accumulates correctly across multiple windows. Evidence: inventory service tests exist in agent test suite (`...Infrastructure.Agent.Tests/Inventory/InventoryServiceTests.cs`). Status: complete/superseded; completed because superseded by specs/020-resumable-batching-cursor
- [x] T022 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — query returns 0 items: scanning stops for that project; `IsComplete = true` emitted immediately. Evidence: equivalent inventory progress semantics covered in `InventoryServiceTests` and `InventoryCompatibilitySemanticsTests`. Status: complete/superseded; completed because superseded by specs/033-runtime-state-categories
- [x] T023 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — window narrowed below 30 days after successful query: window grows by 1 day on next window. Evidence: strategy implementation moved and validated in current inventory pipeline. Status: complete/superseded; completed because superseded by specs/020-resumable-batching-cursor
- [x] T024 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — WIQL server error: window halves and retries; after 3 retries without success emits error event with `IsComplete = true`. Evidence: current agent inventory tests and resilience paths in runtime service suite. Status: complete/superseded; completed because superseded by specs/020-resumable-batching-cursor
- [x] T025 [US3] Verify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` satisfies all T020–T024 tests; fix any gaps (server-error retry count, grow-after-narrow logic). Evidence: strategy now located at `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Export/WorkItemQueryWindowStrategy.cs` in active runtime. Status: complete/superseded; completed because superseded by specs/020-resumable-batching-cursor

---

## Phase 5: User Story 4 — Multi-Org Tooling Roster (Priority: P3)

**Goal**: `organisations` array in config fans out across all enabled entries; disabled entries silently skipped; per-entry `projects` filter restricts scope.

**Independent Test Criteria**: Mock with 3 org entries (2 enabled, 1 disabled). Verify: table has rows only for enabled orgs' projects; disabled org produces no output; empty `projects` enumerates all; non-empty `projects` restricts; validation error on `organisations` + `source` both present.

- [x] T026 [US4] Implement Mode 2 fan-out in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — iterate `InventoryOptions.Organisations`; skip entries where `Enabled == false`; call `IInventoryService` per (entry, project) pair; collect results into same live table. Evidence: fan-out now occurs in agent inventory modules/orchestrator (`src/DevOpsMigrationPlatform.Infrastructure.Agent/Discovery/InventoryService.cs`, `InventoryOrchestrator.cs`). Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T027 [US4] Implement per-entry `projects` filter in Mode 2 fan-out in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — if `Projects` is empty enumerate all projects via ADO list-projects API; if non-empty restrict to named projects; unresolvable project name logged as error row. Evidence: organisation and project scoping now handled in runtime endpoint models and inventory service factories (`src/.../Options/*OrganisationEntry.cs`, `.../Factories/InventoryServiceFactory.cs`). Status: complete/superseded; completed because superseded by specs/025-agent-config-package
- [x] T028 [P] [US4] Write tests — 2 enabled + 1 disabled org entries: table and CSV contain rows for both enabled orgs; disabled org not mentioned in any output. Evidence: current coverage in `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Discovery/InventoryCompatibilitySemanticsTests.cs` and orchestrator tests. Status: complete/superseded; completed because superseded by specs/033-runtime-state-categories
- [x] T029 [P] [US4] Write tests — `projects: []` on an entry enumerates all projects; `projects: ["Alpha", "Beta"]` produces exactly two rows per entry. Evidence: endpoint/project resolution now validated in inventory service/orchestrator test suite under `tests/DevOpsMigrationPlatform.Infrastructure.Agent.Tests/Inventory`. Status: complete/superseded; completed because superseded by specs/025-agent-config-package
- [x] T030 [P] [US4] Write tests — config with both `organisations` and `source` set: command exits non-zero with mutual-exclusion error message before any API call. Evidence: config model and validators consolidated in current `MigrationPlatformOptions` pipeline; direct command path removed. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job

---

## Phase 6: User Story 2 — TFS Subprocess Inventory (Priority: P2)

**Goal**: `source.type = TeamFoundationServer` transparently delegates to the TFS subprocess via `ExternalToolRunner`; NDJSON progress events drive the same live table; no TFS OM assembly loaded in the .NET 10 process.

**Independent Test Criteria**: Spawn subprocess with `ExternalToolRunner` mock; verify correct `inventory` subcommand args, credentials via stdin JSON, NDJSON events converted to `InventoryProgressEvent`; non-zero exit code surfaces as command failure.

- [x] T031 [US2] Create `src/DevOpsMigrationPlatform.CLI.TfsMigration/Commands/InventoryCommand.cs` — Spectre.Console `AsyncCommand`; accepts `--collection <url>`, `--project <name>` (optional), `--all-projects` (flag); reads credential JSON from stdin; constructs and delegates to `TfsInventoryAgent`. Evidence: TFS execution is now first-class via `DevOpsMigrationPlatform.TfsMigrationAgent` leasing `JobKind.Inventory`. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T032 [US2] Create `src/DevOpsMigrationPlatform.CLI.TfsMigration/TfsInventoryAgent.cs` — parallel of `TfsExportAgent`; accepts collection URL, optional project, `WindowOptions`; uses `WorkItemStoreExtensions.QueryCountAllByDateChunk` for date-windowed counting per project; emits `InventoryProgressEvent` as NDJSON via `StdoutProgressSink`; supports Windows auth and PAT. Evidence: TFS inventory implemented in TFS migration agent worker and modules (`src/DevOpsMigrationPlatform.TfsMigrationAgent/TfsJobAgentWorker.cs`). Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T033 [US2] Register `inventory` subcommand in `src/DevOpsMigrationPlatform.CLI.TfsMigration/Program.cs` — add `config.AddCommand<InventoryCommand>("inventory")`. Evidence: subprocess command architecture removed; control-plane/agent dispatch routes inventory by `JobKind`. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T034 [US2] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/TfsInventoryProcessAdapter.cs` — calls `ExternalToolRunner` with `inventory` subcommand + `--collection` and optionally `--project` / `--all-projects`; writes credential JSON to subprocess stdin; reads stdout lines and deserialises each to `InventoryProgressEvent`; returns `IAsyncEnumerable<InventoryProgressEvent>`. Evidence: no `ExternalToolRunner` inventory path in current CLI; TFS handled by TFS agent lease protocol (`docs/capabilities-guide.md`). Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T035 [US2] Connect TFS path in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — when `source.type == "TeamFoundationServer"` delegate to `TfsInventoryProcessAdapter` instead of `IInventoryService`; re-route NDJSON events into the same Spectre.Console live table. Evidence: CLI now only submits queue jobs (`src/DevOpsMigrationPlatform.CLI.Migration/Program.cs`). Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T036 [P] [US2] Write tests — `TfsInventoryProcessAdapter` spawns subprocess with correct args; stdin contains credential JSON; stdout NDJSON lines deserialise to `InventoryProgressEvent` with correct fields. Evidence: TFS inventory behavior now covered by `tests/DevOpsMigrationPlatform.TfsMigrationAgent.Tests/TfsJobAgentWorkerTests.cs`. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T037 [P] [US2] Write tests — subprocess exit code 1 causes `InventoryCommand` to return non-zero and print error; exit code 0 with error events in NDJSON surfaces per-project error rows in CSV. Evidence: command-level subprocess model replaced; runtime behavior validated in agent tests and queue flow tests. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job

---

## Phase 7: Polish & Documentation Patches

**Purpose**: Patch architecture doc gaps filed in `discrepancies.md`. No functional code changes.

- [x] T038 [P] Patch `docs/cli-guide.md` — add `| discovery inventory | Count work items and revisions per project. Read-only pre-flight. |` to Commands table; add usage examples (`--config`, `--config --all-projects`, `--config --output`); add note that `discovery *` commands do not submit a `MigrationJob` to the control plane. Evidence: `docs/cli-guide.md` now documents queue submission model and `Mode: Inventory` via queue. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job
- [x] T039 [P] Patch `docs/configuration-reference.md` — add `authentication` block (`type`, `accessToken`) to `source` and `target` schema examples; add `organisations` array key to Full Schema and Top-Level Fields table; document Mode 1 / Mode 2 mutual exclusion rule; document three-layer token resolution order. Evidence: auth + organisations + mode/token guidance present in `docs/configuration-reference.md`. Status: complete
- [x] T040 [P] Patch `docs/capabilities-guide.md` — fix `.NET 9` → `.NET 10` in AzureDevOpsServices requirements; add Inventory subsection to AzureDevOpsServices (date-window counting, PAT auth); add Inventory subsection to TeamFoundationServer (subprocess delegation, Windows auth, `WorkItemStoreExtensions`). Evidence: `docs/capabilities-guide.md` contains .NET 10 and inventory subsections for ADO/TFS/Simulated. Status: complete
- [x] T041 [P] Patch `docs/tfs-exporter.md` — add Inventory Mode section: `inventory` subcommand accepted by `tfsmigration.exe`; date-windowed counting via `WorkItemStoreExtensions.QueryCountAllByDateChunk`; emits `InventoryProgressEvent` NDJSON on stdout; credentials via stdin JSON; exit codes 0–3. Evidence: `docs/tfs-exporter.md` no longer exists; TFS inventory is documented under `docs/capabilities-guide.md` + `docs/agent-hosting.md` with TfsMigrationAgent lease model. Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job

---

## Dependencies

```
Phase 1 (Setup)
  └── Phase 2 (Foundational — T004–T012)
        ├── Phase 3 (US-1 — T013–T019)    ← MVP
        │     └── Phase 4 (US-3 — T020–T025)  [fills edge-case gaps in T013]
        │           └── Phase 5 (US-4 — T026–T030)
        └── Phase 6 (US-2 — T031–T037)    [independent of US-3/US-4 except T013–T015]
Phase 7 (Polish — T038–T041)              [independent of all code phases]
```

**US-1 and US-3 note**: `WorkItemQueryWindowStrategy` is created in T013 (Phase 3) and tested by T020–T024 (Phase 4). US-3 tests may reveal gaps in T013 — T025 is the fix task for those gaps.

**US-2 and US-3 note**: TFS subprocess uses `WorkItemStoreExtensions.QueryCountAllByDateChunk` (existing POC code, net481). The ADO `WorkItemQueryWindowStrategy` (T013) does not need to be complete before TFS work begins.

---

## Parallel Execution Examples

### Within Phase 3 (US-1)
T013, T017, T019 can start in parallel once Phase 2 is complete.
T014 depends on T013. T015 depends on T014. T016 depends on T015. T018 depends on T016.

### Within Phase 6 (US-2)
T031, T032, T033 (all TFS subprocess) can run in parallel — different files, no cross-dependency.
T034 depends on T031. T035 depends on T034.

### Phase 7 (Polish)
T038–T041 are fully independent of each other and of all code phases. Run all four in parallel.

---

## Implementation Strategy

**MVP** = Phase 1 + Phase 2 + Phase 3 (T001–T019)

Delivers: `devopsmigration discovery inventory --config migration.json` works end-to-end for a single ADO org; live table; CSV output; validation errors on bad config; exits non-zero on auth failure.

**Increment 2** = Phase 4 (T020–T025) — proves large collections (>20k work items) are counted correctly.

**Increment 3** = Phase 5 (T026–T030) — multi-org roster mode.

**Increment 4** = Phase 6 (T031–T037) — TFS support.

**Increment 5** = Phase 7 (T038–T041) — doc patches (can be done at any point after MVP).
