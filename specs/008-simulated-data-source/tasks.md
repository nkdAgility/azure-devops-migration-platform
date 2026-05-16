# Tasks: 008-simulated-data-source (reconciled)

## Phase 1: Simulated source/target contract coverage

- [x] T001 [US1] Support `"Simulated"` as a valid `source.type` in config schema and docs. Status: complete
- [x] T002 [US3] Support `"Simulated"` as a valid `target.type` in config schema and docs. Status: complete
- [ ] T003 [US1] Deterministic generation explicitly keyed by `source.seed` + `source.workItemCount`. Status: incomplete
  Evidence: Current model is `Generator.Projects[*].WorkItemTypes[*]`; no implemented `source.seed` contract in scenarios or options.
- [x] T004 [US2] Configurable `source.workItemCount` (min 1) with verified 25k completion. Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md + specs/021.1-simulated-infrastructure/spec.md  
  Evidence: Generator-based project/type counts replaced flat `workItemCount` contract.
- [ ] T005 [US2] Generated data conforms to package schema and passes validation for simulated runs. Status: incomplete
  Evidence: Tests confirm revision files and run success, but no explicit reconciliation evidence for full schema-conformance gate at 25k scale.
- [x] T006 [US1] Discovery inventory returns counts consistent with simulated configuration. Status: complete/superseded; completed because superseded by docs/capabilities-guide.md (Simulated inventory behavior) and generator-driven implementation  
  Evidence: Inventory behavior now defined against generator projects/work-item types, not flat `workItemCount`.
- [x] T007 [US2] Simulated export writes through package abstractions/checkpoint flow. Status: complete

## Phase 2: Import and telemetry behavior

- [x] T008 [US3] Simulated target accepts imports with no external-system writes. Status: complete
- [ ] T009 [US2] Simulated source/target emit progress at same granularity as real connectors. Status: incomplete
  Evidence: Progress exists, but no concrete side-by-side parity verification artifact found in this spec scope.
- [ ] T010 [US3] Full Simulated `Mode: Both` run with operator-configurable time limit. Status: incomplete
  Evidence: Roundtrip scenario exists; no evidence of configurable time-limit enforcement + 25k benchmark in this spec scope.
- [ ] T011 [US1] Auto-select seed when omitted, log it, persist it in manifest for reproducibility. Status: incomplete
  Evidence: No active seed contract in current scenario/options model; no manifest seed evidence found.

## Phase 3: Config dimensions, scenarios, and test gate

- [x] T012 [US4] Optional simulation dimensions (`projectCount`, distribution, avg revisions, includeAttachments/links). Status: complete/superseded; completed because superseded by specs/017-simulated-infrastructure/spec.md + specs/021.1-simulated-infrastructure/spec.md  
  Evidence: Implemented dimensions are generator-project/type driven (`Generator.Projects[*]...`) rather than original flat fields.
- [ ] T013 [US2] Provide default 25k ready scenario under `/scenarios` + launch profile. Status: incomplete
  Evidence: Simulated scenarios/launch profiles exist, but current examples are small datasets (not 25,000 default).
- [x] T014 [US4] At least one `[TestCategory("SystemTest")]` end-to-end simulated migrate test in CI-safe mode. Status: complete
