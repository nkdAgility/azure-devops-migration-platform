# Tasks: 008-simulated-data-source (reconciled)

## Phase 1: Simulated source/target contract coverage

- [X] T001 [US1] Support `"Simulated"` as a valid `source.type` in config schema and docs. — Status: complete
  - Evidence: `docs/configuration-reference.md` and `docs/capabilities-guide.md` both include `Simulated` source support.
- [X] T002 [US3] Support `"Simulated"` as a valid `target.type` in config schema and docs. — Status: complete
  - Evidence: `docs/configuration-reference.md` and `docs/capabilities-guide.md` both include `Simulated` target support.
- [ ] T003 [US1] Deterministic generation explicitly keyed by `source.seed` + `source.workItemCount`. — Status: incomplete
  - Evidence: Current runtime model is `Generator.Projects[*].WorkItemTypes[*]`; no implemented `source.seed` + `source.workItemCount` contract in `SimulatedEndpointOptions` or checked-in simulated scenarios.
- [X] T004 [US2] Configurable `source.workItemCount` (min 1) with verified 25k completion. — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md TSK-008 and specs/021.1-simulated-infrastructure/tasks.md TSK-008
  - Evidence: Generator-based project/type counts replaced this spec's flat `workItemCount` contract.
- [ ] T005 [US2] Generated data conforms to package schema and passes validation for simulated runs. — Status: incomplete
  - Evidence: Simulated system tests verify revision output and successful runs, but this spec folder has no 25k-scale schema-conformance evidence artifact.
- [X] T006 [US1] Discovery inventory returns counts consistent with simulated configuration. — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md TSK-008 and docs/capabilities-guide.md Simulated inventory section
  - Evidence: Inventory behavior is now defined against generator project/type counts, not flat `workItemCount`.
- [X] T007 [US2] Simulated export writes through package abstractions/checkpoint flow. — Status: complete
  - Evidence: Export scenario and simulated system tests validate package output with cursor files and migration state.

## Phase 2: Import and telemetry behavior

- [X] T008 [US3] Simulated target accepts imports with no external-system writes. — Status: complete
  - Evidence: `scenarios/queue-import-workitems-simulated-target.json` + `SimulatedMigrationCommandTests.QueueImportSimulated_ExitsZeroAndAcceptsWorkItems`.
- [ ] T009 [US2] Simulated source/target emit progress at same granularity as real connectors. — Status: incomplete
  - Evidence: Progress/log files exist, but no side-by-side parity verification artifact against AzureDevOps/TFS connector granularity is recorded in this spec scope.
- [ ] T010 [US3] Full Simulated `Mode: Both` run with operator-configurable time limit. — Status: incomplete
  - Evidence: Current canonical mode is `Migrate` (roundtrip scenario exists), but no evidence of configurable time-limit enforcement and no 25k benchmark scenario/profile in this spec scope.
- [ ] T011 [US1] Auto-select seed when omitted, log it, persist it in manifest for reproducibility. — Status: incomplete
  - Evidence: This spec's seed contract is not represented in current simulated endpoint options/scenarios; no manifest-seed evidence is captured in this spec folder.

## Phase 3: Config dimensions, scenarios, and test gate

- [X] T012 [US4] Optional simulation dimensions (`projectCount`, distribution, avg revisions, includeAttachments/links). — Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/tasks.md TSK-008 and specs/021.1-simulated-infrastructure/tasks.md TSK-008
  - Evidence: Implemented dimensions are generator project/type driven (`Generator.Projects[*]...`) rather than this spec's original flat fields.
- [ ] T013 [US2] Provide default 25k ready scenario under `/scenarios` + launch profile. — Status: incomplete
  - Evidence: Simulated scenarios and launch profiles exist, but current checked-in examples are small datasets (for example 2–5 work items), not a default 25,000-item profile.
- [X] T014 [US4] At least one `[TestCategory("SystemTest")]` end-to-end simulated migrate test in CI-safe mode. — Status: complete
  - Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/SimulatedMigrationCommandTests.cs` contains multiple `[TestCategory("SystemTest")]` simulated end-to-end tests including roundtrip migrate.
