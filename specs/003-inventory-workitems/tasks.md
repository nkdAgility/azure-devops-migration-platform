# Tasks: Work Items Inventory Command

**Input**: Design documents from `specs/003-inventory-workitems/`
**Prerequisites**: plan.md ✅, spec.md ✅, research.md ✅, data-model.md ✅, contracts/ ✅, quickstart.md ✅

---

## Phase 1: Setup

**Purpose**: Remove pre-existing violations that block compilation of the rewritten command.

- [X] T001 Delete `src/DevOpsMigrationPlatform.CLI.Migration/Commands/AzureDevOpsSettings.cs` (bare `--organisation`/`--token` CLI args — coding standards violation)
- [X] T002 Remove all `AzureDevOpsSettings` usages from `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs` and verify solution builds
- [X] T003 Remove `AzureDevOpsSettings` base class reference from `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — replace with an empty `CommandSettings` base temporarily so the project compiles

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: All shared abstractions that every user story depends on. No US phase can begin until this phase is complete.

**⚠️ CRITICAL**: These types are referenced by CLI, Infrastructure, and TFS projects. They must exist and compile before any US implementation begins.

- [X] T004 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Options/EndpointAuthenticationOptions.cs` — `Type` (string), `AccessToken` (string?) per data-model.md
- [X] T005 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Options/OrganisationEntry.cs` — `Type`, `Url`, `Projects` (List\<string\>), `ApiVersion`, `Authentication` (EndpointAuthenticationOptions), `Enabled` (bool, default true) per data-model.md
- [X] T006 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/InventorySummary.cs` — all 9 CSV fields: `Url`, `ProjectName`, `WorkItemsCount`, `RevisionsCount`, `ReposCount`, `PipelinesCount`, `IsComplete`, `Error`, `LastUpdatedUtc` per data-model.md
- [X] T007 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Models/InventoryProgressEvent.cs` — `ProjectName`, `Url`, `WorkItemsCount`, `RevisionsCount`, `IsComplete`, `WindowStart`, `WindowEnd`, `WindowSize`, `Error`, `Timestamp` per data-model.md
- [X] T008 Create `src/DevOpsMigrationPlatform.Abstractions/Options/InventoryOptions.cs` — `ConfigVersion`, `Source` (MigrationEndpointOptions?), `Organisations` (List\<OrganisationEntry\>?); startup validation: both set → error, neither set → error, Mode 1 project null without --all-projects → error, Mode 2 empty list → error
- [X] T009 [P] Add `Authentication` property (`EndpointAuthenticationOptions?`) to `src/DevOpsMigrationPlatform.Abstractions/Options/MigrationEndpointOptions.cs`
- [X] T010 [P] Create `src/DevOpsMigrationPlatform.Abstractions/Utilities/TokenResolver.cs` — static `Resolve(string? raw)`: returns null if null/empty; reads env var if starts with `$ENV:`; throws `InvalidOperationException` if env var unset; otherwise returns literal. Compiled for `net481;net10.0`
- [X] T011 Create `src/DevOpsMigrationPlatform.Abstractions/Services/IInventoryService.cs` — `IAsyncEnumerable<InventoryProgressEvent> CountWorkItemsAsync(string url, string project, string pat, CancellationToken ct)` per data-model.md
- [X] T012 [P] Write `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/TokenResolverTests.cs` — literal value returned unchanged; `$ENV:VAR` reads env var; unset env var throws; null/empty returns null

**Checkpoint**: All abstractions compile for net481 and net10.0. Tests pass. US implementation can now begin.

---

## Phase 3: User Story 1 — ADO Work Item Inventory (Priority: P1) 🎯 MVP

**Goal**: Operator runs `devopsmigration discovery inventory --config migration.json` against an Azure DevOps org and sees a live terminal table per project. Command exits 0 and writes `discovery-summary.csv`.

**Independent Test Criteria**: Point the CLI at a mocked `IInventoryService` returning known counts for 3 projects. Verify: one table row per project updates progressively; command exits 0; `discovery-summary.csv` exists with correct 9 columns and values; zero-item project row is present; invalid-PAT path exits non-zero.

- [X] T013 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` — date-window WIQL counting algorithm: 120-day initial window ending `DateTime.UtcNow`; if count ≥ 20,000 → halve window and retry same end date; if count == 0 → stop scanning; if count < 20,000 → yield IDs, advance window end to current window start, repeat; after success with window < 30 days → grow window by 1 day; uses `WorkItemTrackingHttpClient` WIQL endpoint
- [X] T014 [US1] Modify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/CatalogService.cs` — inject `WorkItemQueryWindowStrategy`; use it to bound page queries (ID-cursor paging retained within each window)
- [X] T015 [US1] Create `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/AzureDevOpsInventoryService.cs` implementing `IInventoryService` — uses `WorkItemQueryWindowStrategy` to enumerate windows; for each window fetches `System.Rev` in batches of 200 via `GetWorkItemsAsync`; yields `InventoryProgressEvent` per window with running totals; sets `IsComplete = true` on final event
- [X] T016 [US1] Rewrite `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — `Settings` with `[CommandOption] --config`, `[CommandOption] --output` (default CWD), `[CommandOption] --all-projects`; load `IOptions<InventoryOptions>`; validate on execute; Mode 1 path uses `IInventoryService`; render `LiveTable` via Spectre.Console with columns: Url, Project, Work Items, Revisions, Repos, Pipelines, Updated
- [X] T017 [P] [US1] Register `InventoryOptions` (bound to config root), `IInventoryService` → `AzureDevOpsInventoryService`, `WorkItemQueryWindowStrategy` in `src/DevOpsMigrationPlatform.CLI.Migration/Program.cs`
- [X] T018 [US1] Implement CSV write on command completion in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — write `discovery-summary.csv` to `--output` directory; all 9 columns (`Url,ProjectName,WorkItemsCount,RevisionsCount,ReposCount,PipelinesCount,IsComplete,Error,LastUpdatedUtc`); all projects written including failed ones
- [X] T019 [P] [US1] Write `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — single project with known counts; zero-item project returns 0/0 `IsComplete = true`; invalid PAT surfaces as error event with non-null `Error`

---

## Phase 4: User Story 3 — Progressive Query Narrowing (Priority: P2)

**Goal**: Any project regardless of total size is counted fully and correctly; no query ever returns a 20k-limit error; the window algorithm is verified on all edge cases.

**Independent Test Criteria**: Mock `IWorkItemTrackingHttpClient` returning exactly 20,000 items for first call, then fewer. Verify: window halves and retries; running total is correct; scanning stops on zero; window grows after narrow success; server error triggers halve-and-retry (min 3 retries).

- [X] T020 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — query returns exactly 20,000 items: window halves, retry issued, partial count NOT recorded from first attempt
- [X] T021 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — query returns < 20,000 items: window advances backward by current window size; total accumulates correctly across multiple windows
- [X] T022 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — query returns 0 items: scanning stops for that project; `IsComplete = true` emitted immediately
- [X] T023 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — window narrowed below 30 days after successful query: window grows by 1 day on next window
- [X] T024 [P] [US3] Write tests in `tests/DevOpsMigrationPlatform.Infrastructure.Tests/Inventory/AzureDevOpsInventoryServiceTests.cs` — WIQL server error: window halves and retries; after 3 retries without success emits error event with `IsComplete = true`
- [X] T025 [US3] Verify `src/DevOpsMigrationPlatform.Infrastructure.AzureDevOps/Services/WorkItemQueryWindowStrategy.cs` satisfies all T020–T024 tests; fix any gaps (server-error retry count, grow-after-narrow logic)

---

## Phase 5: User Story 4 — Multi-Org Tooling Roster (Priority: P3)

**Goal**: `organisations` array in config fans out across all enabled entries; disabled entries silently skipped; per-entry `projects` filter restricts scope.

**Independent Test Criteria**: Mock with 3 org entries (2 enabled, 1 disabled). Verify: table has rows only for enabled orgs' projects; disabled org produces no output; empty `projects` enumerates all; non-empty `projects` restricts; validation error on `organisations` + `source` both present.

- [X] T026 [US4] Implement Mode 2 fan-out in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — iterate `InventoryOptions.Organisations`; skip entries where `Enabled == false`; call `IInventoryService` per (entry, project) pair; collect results into same live table
- [X] T027 [US4] Implement per-entry `projects` filter in Mode 2 fan-out in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — if `Projects` is empty enumerate all projects via ADO list-projects API; if non-empty restrict to named projects; unresolvable project name logged as error row
- [X] T028 [P] [US4] Write tests — 2 enabled + 1 disabled org entries: table and CSV contain rows for both enabled orgs; disabled org not mentioned in any output
- [X] T029 [P] [US4] Write tests — `projects: []` on an entry enumerates all projects; `projects: ["Alpha", "Beta"]` produces exactly two rows per entry
- [X] T030 [P] [US4] Write tests — config with both `organisations` and `source` set: command exits non-zero with mutual-exclusion error message before any API call

---

## Phase 6: User Story 2 — TFS Subprocess Inventory (Priority: P2)

**Goal**: `source.type = TeamFoundationServer` transparently delegates to the TFS subprocess via `ExternalToolRunner`; NDJSON progress events drive the same live table; no TFS OM assembly loaded in the .NET 10 process.

**Independent Test Criteria**: Spawn subprocess with `ExternalToolRunner` mock; verify correct `inventory` subcommand args, credentials via stdin JSON, NDJSON events converted to `InventoryProgressEvent`; non-zero exit code surfaces as command failure.

- [X] T031 [US2] Create `src/DevOpsMigrationPlatform.CLI.TfsMigration/Commands/InventoryCommand.cs` — Spectre.Console `AsyncCommand`; accepts `--collection <url>`, `--project <name>` (optional), `--all-projects` (flag); reads credential JSON from stdin; constructs and delegates to `TfsInventoryAgent`
- [X] T032 [US2] Create `src/DevOpsMigrationPlatform.CLI.TfsMigration/TfsInventoryAgent.cs` — parallel of `TfsExportAgent`; accepts collection URL, optional project, `WindowOptions`; uses `WorkItemStoreExtensions.QueryCountAllByDateChunk` for date-windowed counting per project; emits `InventoryProgressEvent` as NDJSON via `StdoutProgressSink`; supports Windows auth and PAT
- [X] T033 [US2] Register `inventory` subcommand in `src/DevOpsMigrationPlatform.CLI.TfsMigration/Program.cs` — add `config.AddCommand<InventoryCommand>("inventory")`
- [X] T034 [US2] Create `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/TfsInventoryProcessAdapter.cs` — calls `ExternalToolRunner` with `inventory` subcommand + `--collection` and optionally `--project` / `--all-projects`; writes credential JSON to subprocess stdin; reads stdout lines and deserialises each to `InventoryProgressEvent`; returns `IAsyncEnumerable<InventoryProgressEvent>`
- [X] T035 [US2] Connect TFS path in `src/DevOpsMigrationPlatform.CLI.Migration/Commands/Discovery/InventoryCommand.cs` — when `source.type == "TeamFoundationServer"` delegate to `TfsInventoryProcessAdapter` instead of `IInventoryService`; re-route NDJSON events into the same Spectre.Console live table
- [X] T036 [P] [US2] Write tests — `TfsInventoryProcessAdapter` spawns subprocess with correct args; stdin contains credential JSON; stdout NDJSON lines deserialise to `InventoryProgressEvent` with correct fields
- [X] T037 [P] [US2] Write tests — subprocess exit code 1 causes `InventoryCommand` to return non-zero and print error; exit code 0 with error events in NDJSON surfaces per-project error rows in CSV

---

## Phase 7: Polish & Documentation Patches

**Purpose**: Patch architecture doc gaps filed in `discrepancies.md`. No functional code changes.

- [ ] T038 [P] Patch `docs/cli.md` — add `| discovery inventory | Count work items and revisions per project. Read-only pre-flight. |` to Commands table; add usage examples (`--config`, `--config --all-projects`, `--config --output`); add note that `discovery *` commands do not submit a `MigrationJob` to the control plane
- [ ] T039 [P] Patch `docs/configuration.md` — add `authentication` block (`type`, `accessToken`) to `source` and `target` schema examples; add `organisations` array key to Full Schema and Top-Level Fields table; document Mode 1 / Mode 2 mutual exclusion rule; document three-layer token resolution order
- [ ] T040 [P] Patch `docs/source-types.md` — fix `.NET 9` → `.NET 10` in AzureDevOpsServices requirements; add Inventory subsection to AzureDevOpsServices (date-window counting, PAT auth); add Inventory subsection to TeamFoundationServer (subprocess delegation, Windows auth, `WorkItemStoreExtensions`)
- [X] T041 [P] Patch `docs/tfs-exporter.md` — add Inventory Mode section: `inventory` subcommand accepted by `tfsmigration.exe`; date-windowed counting via `WorkItemStoreExtensions.QueryCountAllByDateChunk`; emits `InventoryProgressEvent` NDJSON on stdout; credentials via stdin JSON; exit codes 0–3

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
