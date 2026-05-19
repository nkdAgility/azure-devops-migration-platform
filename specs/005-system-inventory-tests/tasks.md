# Tasks: System Test Framework for Inventory Command

**Input**: Design documents from `/specs/005-system-inventory-tests/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Feature**: System Test Framework for Inventory Command enabling developers to run `dotnet test --filter "TestCategory=SystemTest"` for live Azure DevOps validation

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and foundational system test infrastructure

- [ ] T001 Analyze current test project structure in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/InventoryCommandTests.cs — Status: incomplete
  - Evidence: `tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/` does not exist; no persisted analysis artifact for this exact path.
- [x] T002 [P] Research existing MSTest patterns and conventions in the test project — Status: complete
- [x] T003 [P] Validate existing TokenResolver.Resolve() integration points in src/DevOpsMigrationPlatform.Abstractions/Utilities/TokenResolver.cs — Status: complete

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core system test infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [x] T004 Create system test configuration helper classes based on data-model.md entities — Status: complete
- [x] T005 [P] Implement environment variable validation patterns for AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT — Status: complete
- [x] T006 [P] Create temporary artifact management utilities for test output cleanup — Status: complete
- [x] T007 Implement base system test infrastructure following established CLI test patterns — Status: complete
- [x] T008 Create ValidationResult utility for configuration and connectivity validation — Status: complete
- [x] T009 [P] Set up error message templates per contracts/test-interface.md requirements — Status: complete

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - Developer Running System Tests Locally (Priority: P1) 🎯 MVP

**Goal**: Enable developers to run system tests locally against live Azure DevOps organizations via environment variable configuration

**Independent Test**: Can be fully tested by setting environment variables and running `dotnet test --filter "TestCategory=SystemTest"`, delivering immediate feedback on inventory command functionality

### Gherkin Feature File for User Story 1 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 1 acceptance scenarios and committed before any step definitions or production code are written.**

- [x] T010 [US1] Create `features/cli/inventory/system-test-local-execution.feature` — translate `spec.md` User Story 1 acceptance scenarios into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) — Status: complete

### Implementation for User Story 1

- [x] T011 [P] [US1] Create SystemTestConfiguration class in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestConfiguration.cs — Status: complete
- [x] T012 [P] [US1] Create SystemTestContext class in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/TestUtilities/SystemTestContext.cs — Status: complete
- [ ] T013 [US1] Add system test method `InventoryCommand_SystemTest_ValidCredentials_ExecutesSuccessfully` to tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/InventoryCommandTests.cs — Status: incomplete
  - Evidence: `InventoryCommandTests.cs` does not exist; no live inventory-specific system test method with this name exists.
- [ ] T014 [US1] Add system test method `InventoryCommand_SystemTest_MissingEnvironmentVars_MarkInconclusive` to tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/InventoryCommandTests.cs — Status: incomplete
  - Evidence: current live tests use `Assert.Fail` guard clauses, not an inventory command inconclusive method at the requested location.
- [ ] T015 [US1] Add system test method `InventoryCommand_SystemTest_InvalidCredentials_ProvideClearErrorMessage` to tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/InventoryCommandTests.cs — Status: incomplete
  - Evidence: no method with this name/path; no dedicated invalid-credential inventory system test found.
- [x] T016 [US1] Implement environment variable validation using existing TokenResolver.Resolve() pattern — Status: complete
- [ ] T017 [US1] Add comprehensive error handling for authentication failures and network connectivity issues — Status: incomplete
  - Evidence: helper error templates exist, but command tests do not implement dedicated authentication/network failure handling coverage for inventory.
- [x] T018 [US1] Implement temporary output directory creation and cleanup in test methods — Status: complete/superseded; completed because superseded by `CliRunner.RunTestAsync(..., cleanOutputFolder: true)` usage in command system tests
  - Evidence: command tests consistently use `cleanOutputFolder: true`, and `SystemTestContext` also implements temp artifact cleanup.

**Checkpoint**: At this point, User Story 1 should be fully functional - developers can run system tests locally with appropriate environment variable setup

---

## Phase 4: User Story 2 - CI/CD Pipeline Automated Testing (Priority: P2)

**Goal**: Enable system tests to run automatically in GitHub Actions using repository secrets for secure credential management

**Independent Test**: Can be fully tested by configuring GitHub repository secrets and running the test suite in Actions, delivering automated validation of system integration

### Gherkin Feature File for User Story 2 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 2 acceptance scenarios before any step definitions or production code.**

- [x] T019 [US2] Create `features/cli/inventory/system-test-ci-execution.feature` — translate `spec.md` User Story 2 acceptance scenarios into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) — Status: complete

### Implementation for User Story 2

- [x] T020 [P] [US2] Create GitHub Actions workflow example in .github/workflows/system-tests.yml for system test execution — Status: complete/superseded; completed because superseded by integrated CI workflow in `.github/workflows/main.yml`
  - Evidence: `main.yml` runs `SystemTest_Live` with `AZDEVOPS_SYSTEM_TEST_*` environment wiring.
- [x] T021 [P] [US2] Add system test method `InventoryCommand_SystemTest_CIEnvironment_ExecutesSecurely` to tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/InventoryCommandTests.cs — Status: complete/superseded; completed because superseded by queue-command and migration live system tests in `tests/.../Commands/*CommandTests.cs`
  - Evidence: multiple `[TestCategory("SystemTest")]`/`SystemTest_Live` command tests exist; `InventoryCommandTests.cs` path no longer exists.
- [ ] T022 [US2] Add system test method `InventoryCommand_SystemTest_MissingSecrets_ContinuesPipeline` to tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/Discovery/InventoryCommandTests.cs — Status: incomplete
  - Evidence: no dedicated missing-secrets inventory system test exists at requested path; current live tests fail-fast on missing env vars.
- [ ] T023 [US2] Implement credential security validation ensuring no token values are exposed in test output or logs — Status: incomplete
  - Evidence: no explicit automated assertion in command tests validating PAT/token redaction in output.
- [ ] T024 [US2] Add test execution timeout and retry logic for network resilience in CI environment — Status: incomplete
  - Evidence: timeouts exist, but no retry logic for live system test execution is implemented in test harness/workflow.
- [x] T025 [US2] Create documentation section for repository secrets configuration in docs/contributor-guide.md — Status: complete/superseded; completed because superseded by `docs/live-system-testing-guide.md` CI section
  - Evidence: `live-system-testing-guide.md` documents GitHub Actions secrets wiring and secure usage.
- [ ] T026 [US2] Implement conditional test execution logic based on environment context (local vs CI) — Status: incomplete
  - Evidence: no CI-conditional live-test branching logic is present in tests or workflow beyond fixed live test stage execution.

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently - local development and CI pipeline execution both supported

---

## Phase 5: User Story 3 - New Contributor Onboarding (Priority: P3)

**Goal**: Provide comprehensive documentation and guidance enabling new contributors to quickly set up and run system tests

**Independent Test**: Can be fully tested by following documentation alone to set up a working system test environment, delivering successful test execution

### Gherkin Feature File for User Story 3 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 3 acceptance scenarios before any step definitions or production code.**

- [ ] T027 [US3] Create `features/platform/documentation/contributor-onboarding-system-tests.feature` — translate `spec.md` User Story 3 acceptance scenarios into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) — Status: incomplete
  - Evidence: no `features/platform/documentation/contributor-onboarding-system-tests.feature` file exists.

### Implementation for User Story 3

- [x] T028 [P] [US3] Create comprehensive system test documentation in docs/contributor-guide.md following contracts/documentation-contract.md requirements — Status: complete/superseded; completed because superseded by split documentation model (`docs/contributor-guide.md` + `docs/testing-guide.md` + `docs/live-system-testing-guide.md`)
  - Evidence: contributor guide now delegates testing and live-test setup to dedicated docs.
- [x] T029 [P] [US3] Add troubleshooting guide section with common error scenarios and solutions — Status: complete/superseded; completed because superseded by `docs/live-system-testing-guide.md` troubleshooting section
  - Evidence: live testing guide includes troubleshooting flow and failure checks.
- [x] T030 [P] [US3] Create step-by-step local development setup instructions for system tests — Status: complete/superseded; completed because superseded by `docs/live-system-testing-guide.md` local setup commands
  - Evidence: PowerShell, Command Prompt, and Bash setup + run commands are documented.
- [x] T031 [P] [US3] Add GitHub Actions integration examples and repository secrets configuration guide — Status: complete/superseded; completed because superseded by `docs/live-system-testing-guide.md` CI/workflow section
  - Evidence: guide contains GitHub Actions example with `AZDEVOPS_SYSTEM_TEST_*` secrets wiring.
- [x] T032 [P] [US3] Create security best practices section for credential management — Status: complete/superseded; completed because superseded by `docs/live-system-testing-guide.md` token guidance
  - Evidence: guide documents secret handling, minimal scope, and rotation guidance.
- [x] T033 [US3] Add verification commands and test setup validation procedures — Status: complete/superseded; completed because superseded by `docs/live-system-testing-guide.md` local run commands and validation guidance
  - Evidence: explicit `dotnet test --filter "TestCategory=SystemTest"` examples and setup checks are documented.
- [x] T034 [US3] Include cross-platform setup instructions (Windows, macOS, Linux) — Status: complete/superseded; completed because superseded by `docs/live-system-testing-guide.md` platform-specific command sections
  - Evidence: PowerShell, CMD, and Linux/macOS command blocks are present.
- [ ] T035 [US3] Create troubleshooting matrix with error symptoms, causes, and solutions — Status: incomplete
  - Evidence: no explicit troubleshooting matrix table exists in current contributor/live-testing docs.

**Checkpoint**: At this point, all three user stories should work independently with comprehensive documentation support

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Final integration, performance optimization, and documentation polish

- [ ] T036 [P] Validate system test execution time meets 30-second maximum requirement — Status: incomplete
  - Evidence: live command tests have 5–20 minute timeouts; no recorded verification of a 30-second maximum.
- [x] T037 [P] Add comprehensive logging for troubleshooting failed system tests — Status: complete/superseded; completed because superseded by `CliRunner` stdout/stderr capture + `.otel-diagnostics` troubleshooting guidance
  - Evidence: command tests print full CLI output and testing docs describe diagnostics collection paths.
- [ ] T038 [P] Implement rate limiting and retry mechanisms for Azure DevOps API calls — Status: incomplete
  - Evidence: no task-scoped implementation evidence in system test harness for retry/backoff behavior.
- [x] T039 Create integration examples showing system test usage in existing development workflows — Status: complete/superseded; completed because superseded by `docs/testing-guide.md` + `docs/live-system-testing-guide.md`
  - Evidence: repository test workflow and live-test command examples are documented.
- [ ] T040 [P] Add telemetry and metrics for system test usage patterns — Status: incomplete
  - Evidence: no dedicated system-test usage telemetry instrumentation is documented or asserted by tests.
- [ ] T041 [P] Create maintenance documentation for updating system tests as APIs change — Status: incomplete
  - Evidence: no dedicated maintenance section for live system test evolution was found.
- [ ] T042 Final validation of all functional requirements FR-001 through FR-012 — Status: incomplete
  - Evidence: several FR-linked tasks remain incomplete or superseded without consolidated FR validation evidence.

---

## Dependencies

### User Story Completion Order

1. **User Story 1 (P1)** → Must complete first (MVP foundation)
2. **User Story 2 (P2)** → Can start after US1 Phase 3 completion (extends local capability to CI)
3. **User Story 3 (P3)** → Can start after US1 completion, should complete after US2 (documents complete system)

### Task Dependencies Within Stories

**User Story 1 Critical Path**:
T010 (feature file) → T011, T012 (entities) → T013, T014, T015 (test methods) → T016, T017, T018 (implementation)

**User Story 2 Builds On**:
- Requires T011, T012 (SystemTestConfiguration/Context from US1)
- Can run T020, T021 in parallel with US1 implementation

**User Story 3 Integrates All**:
- Documents complete system after US1 and US2 implementation
- T028 can start early, T029-T035 require working system

---

## Parallel Execution Examples

### Phase 2 (Foundational)
- Parallel: T005 (environment validation) + T006 (artifact management) + T009 (error templates)
- Sequential: T004 → T007 → T008

### User Story 1 Implementation  
- Parallel: T011 + T012 (entity classes)
- Parallel: T013 + T014 + T015 (test methods)
- Sequential: T016 → T017 → T018 (implementation logic)

### User Story 2 + 3 Overlap
- Parallel: T020 (CI workflow) + T028 (documentation start)
- Parallel: T025 (secrets docs) + T029 (troubleshooting guide)

---

## Implementation Strategy

### MVP First (User Story 1)
- Focus on T010-T018 for immediate developer value
- Delivers functional system tests for local development
- Enables validation of inventory command against live systems

### Incremental Delivery
- Each completed user story adds independent value
- US1: Local development capability
- US2: Automated CI validation  
- US3: Complete contributor experience

### Quality Gates
- Constitutional compliance verified ✅
- All acceptance scenarios covered with Gherkin features
- Independent test criteria met for each user story
- 30-second execution time requirement validated
