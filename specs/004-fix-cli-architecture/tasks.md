---
description: "Task list for CLI architecture refactoring and command testing implementation"
---

# Tasks: Fix CLI Architecture and Add Command Testing

**Input**: Design documents from `/specs/004-fix-cli-architecture/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Testing infrastructure is explicitly requested via CommandAppTester integration for all CLI commands.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Single project**: `src/`, `tests/` at repository root per plan.md structure
- Paths shown below follow existing DevOpsMigrationPlatform.CLI.Migration project structure

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project initialization and test infrastructure setup

- [X] T001 Create test project DevOpsMigrationPlatform.CLI.Migration.Tests.csproj with Spectre.Console.Cli.Testing, MSTest, and Moq dependencies — Status: complete
- [X] T002 [P] Add PackageReference for Spectre.Console.Cli.Testing to test project — Status: complete
- [X] T003 [P] Create TestUtilities/InMemoryTestConfiguration.cs for configuration test doubles — Status: complete
- [X] T004 [P] Create TestUtilities/MockServiceProvider.cs for DI container test doubles — Status: complete

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T005 Create MigrationPlatformHost.cs static factory class in src/DevOpsMigrationPlatform.CLI.Migration/ — Status: complete
- [X] T006 Implement CommandBase<TSettings> abstract class in src/DevOpsMigrationPlatform.CLI.Migration/Commands/CommandBase.cs — Status: complete
- [X] T007 Extract configuration extraction logic from Program.cs to MigrationPlatformHost.CreateDefaultBuilder() method — Status: complete
- [X] T008 Migrate service registration logic from Program.cs to MigrationPlatformHost configuration — Status: complete
- [X] T009 Setup OpenTelemetry and logging configuration in MigrationPlatformHost per existing Program.cs patterns — Status: complete
- [ ] T010 Create Spectre.Console CommandApp integration in MigrationPlatformHost with proper DI configuration — Status: incomplete
  - Evidence: `MigrationPlatformHost` does not configure `CommandApp`; command registration remains in `Program.cs` (`src\DevOpsMigrationPlatform.CLI.Migration\Program.cs:52-154`).

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - CLI Commands Execute Without Errors (Priority: P1) 🎯 MVP

**Goal**: All CLI commands execute successfully without runtime errors when provided valid inputs

**Independent Test**: Run each command with valid parameters and verify exit code 0, run with invalid parameters and verify non-zero exit codes

### Gherkin Feature File for User Story 1 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 1 acceptance scenarios and committed before any step definitions or production code are written.**

- [X] T011 [US1] Create `features/cli/execute/commands-execute-successfully.feature` — translate `spec.md` User Story 1 acceptance scenarios into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) — Status: complete

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [X] T012 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/TfsExportCommandTests.cs with CommandAppTester validation tests — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md T001-T004
  - Evidence: `TfsExportCommand` and `TfsExportCommandTests.cs` do not exist; later job-unification spec replaced per-command submission with `queue` by `JobKind` (`specs\025.1-fold-to-job\spec.md:44-46`, `src\DevOpsMigrationPlatform.CLI.Migration\Commands\QueueCommand.cs:37-44`).
- [ ] T013 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/LogsCommandTests.cs with CommandAppTester validation tests — Status: incomplete
  - Evidence: `tests\DevOpsMigrationPlatform.CLI.Migration.Tests\Commands\LogsCommandTests.cs` is absent and no `CommandAppTester` usages are present in the test project.
- [X] T014 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/InventoryCommandTests.cs with CommandAppTester validation tests — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md T001-T004
  - Evidence: dedicated `InventoryCommand` test file is absent; inventory is now submitted through `queue` mode (`specs\025.1-fold-to-job\spec.md:68-70`, `src\DevOpsMigrationPlatform.CLI.Migration\Commands\QueueCommand.cs:136`).
- [X] T015 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/CommandBaseTests.cs for unit testing base class functionality — Status: complete

### Implementation for User Story 1

- [X] T016 [P] [US1] Refactor TfsExportCommand in src/DevOpsMigrationPlatform.CLI.Migration/Commands/TfsExportCommand.cs to inherit from CommandBase<TfsExportCommandSettings> — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md T001-T004
  - Evidence: `TfsExportCommand` no longer exists; queue submission is the canonical path after job unification (`specs\025.1-fold-to-job\spec.md:44-46`).
- [X] T017 [P] [US1] Refactor LogsCommand in src/DevOpsMigrationPlatform.CLI.Migration/Commands/LogsCommand.cs to inherit from CommandBase<LogsCommand.Settings> — Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns/tasks.md Step 1.6
  - Evidence: `LogsCommand` now inherits `ControlPlaneCommandBase<LogsCommand.Settings>` which encapsulates control-plane command behavior over `CommandBase` (`src\DevOpsMigrationPlatform.CLI.Migration\Commands\LogsCommand.cs:19`, `src\DevOpsMigrationPlatform.CLI.Migration\Commands\ControlPlaneCommandBase.cs:19`).
- [X] T018 [P] [US1] Refactor InventoryCommand in src/DevOpsMigrationPlatform.CLI.Migration/Commands/InventoryCommand.cs to inherit from CommandBase<InventoryCommand.Settings> — Status: complete/superseded; completed because superseded by specs/025.1-fold-to-job/tasks.md T001-T004
  - Evidence: dedicated `InventoryCommand` no longer exists; inventory execution is represented by `queue` with `JobKind.Inventory`.
- [X] T019 [US1] Implement proper error handling and telemetry in CommandBase<T>.ExecuteAsync() with exception logging — Status: complete
- [X] T020 [US1] Add IHostApplicationLifetime.StopApplication() calls to command completion in CommandBase<T> — Status: complete/superseded; completed because superseded by specs/021.2-separation-of-concerns/tasks.md Step 1.6
  - Evidence: current command lifecycle uses per-command host stop/disposal in `finally` instead of `IHostApplicationLifetime.StopApplication()` (`src\DevOpsMigrationPlatform.CLI.Migration\Commands\CommandBase.cs:171-178`).
- [ ] T021 [US1] Update Program.cs to use minimal bootstrapping pattern following azure-devops-migration-tools reference — Status: incomplete
  - Evidence: `Program.cs` still contains full command registration and console startup logic (`src\DevOpsMigrationPlatform.CLI.Migration\Program.cs:13-155`).

**Checkpoint**: At this point, User Story 1 should be fully functional - all commands execute without errors

---

## Phase 4: User Story 2 - Configuration Flows Correctly Through the System (Priority: P1)

**Goal**: --config parameter correctly passes configuration data from command line through to internal services

**Independent Test**: Provide specific config values and verify they reach appropriate services via logging or test assertions

### Gherkin Feature File for User Story 2 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 2 acceptance scenarios before any step definitions or production code.**

- [X] T022 [US2] Create `features/cli/execute/configuration-flow.feature` — translate `spec.md` User Story 2 acceptance scenarios into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) — Status: complete

### Tests for User Story 2

- [X] T023 [P] [US2] Create integration test tests/DevOpsMigrationPlatform.CLI.Migration.Tests/MigrationPlatformHostTests.cs for configuration binding validation — Status: complete
- [ ] T024 [P] [US2] Add configuration flow tests to existing command test files verifying config values reach target services — Status: incomplete
  - Evidence: no command-level tests assert source URL/auth values flowing from config into downstream services in existing `Commands\*.cs` tests.
- [X] T025 [P] [US2] Create tests for default config file resolution (migration.json) when --config not specified — Status: complete

### Implementation for User Story 2

- [X] T026 [US2] Implement --config parameter extraction logic in MigrationPlatformHost before DI container creation — Status: complete
- [ ] T027 [US2] Setup IOptions<T> pattern integration in MigrationPlatformHost service registration for all existing configuration classes — Status: incomplete
  - Evidence: `MigrationPlatformHost` binds `EnvironmentOptions`, but command flow still performs direct JSON config reads in `QueueCommand` (`src\DevOpsMigrationPlatform.CLI.Migration\Commands\QueueCommand.cs:83-87`), so end-to-end options integration for existing configuration classes remains incomplete.
- [ ] T028 [US2] Verify configuration binding between command-line args, environment variables, and config files follows proper precedence — Status: incomplete
  - Evidence: no explicit automated precedence test found; only extraction/default path behavior is verified in `MigrationPlatformHostTests`.
- [ ] T029 [US2] Update existing services to receive configuration via dependency injection rather than direct file access where applicable — Status: incomplete
  - Evidence: command flow still reads raw config JSON directly from file in `QueueCommand` (`src\DevOpsMigrationPlatform.CLI.Migration\Commands\QueueCommand.cs:83-87`).
- [ ] T030 [US2] Add configuration validation and error handling for malformed JSON and missing required sections — Status: incomplete
  - Evidence: schema validation exists, but no focused malformed JSON/missing required section test coverage is present in CLI test files.

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - CLI Architecture Follows Proper Host Builder Pattern (Priority: P2)

**Goal**: CLI architecture follows proper separation of concerns with commands managing their hosting lifecycle

**Independent Test**: Examine refactored code structure, verify DI container setup, run unit tests demonstrating proper separation

### Gherkin Feature File for User Story 3 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 3 acceptance scenarios before any step definitions or production code.**

- [X] T031 [US3] Create `features/cli/execute/host-builder-architecture.feature` — translate `spec.md` User Story 3 acceptance scenarios into conformant Gherkin (see `.agents/20-guardrails/workflow/acceptance-test-format.md`) — Status: complete

### Tests for User Story 3

- [ ] T032 [P] [US3] Create architectural validation tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/ArchitectureTests.cs verifying Program.cs line count < 50 — Status: incomplete
  - Evidence: `tests\DevOpsMigrationPlatform.CLI.Migration.Tests\ArchitectureTests.cs` does not exist.
- [X] T033 [P] [US3] Add unit tests verifying new commands can be added without modifying Program.cs or host setup — Status: complete
- [X] T034 [P] [US3] Create integration tests validating complete DI container service registration and resolution — Status: complete

### Implementation for User Story 3

- [ ] T035 [US3] Finalize Program.cs refactoring to contain only minimal bootstrapping logic (target < 50 lines) — Status: incomplete
  - Evidence: `Program.cs` currently has 156 lines with command-registration responsibilities.
- [X] T036 [US3] Ensure all service registration, configuration, and infrastructure setup is centralized in MigrationPlatformHost — Status: complete
- [X] T037 [US3] Validate that CommandBase<T> provides proper lifecycle management and service access patterns — Status: complete
- [X] T038 [US3] Add documentation comments to MigrationPlatformHost and CommandBase<T> explaining architecture patterns — Status: complete
- [ ] T039 [US3] Verify separation of concerns between bootstrapping (Program.cs), infrastructure (host builder), and commands — Status: incomplete
  - Evidence: DI and telemetry are centralized, but Program still owns substantial command composition and startup behavior.

**Checkpoint**: All user stories should now be independently functional with proper architecture

---

## Phase 6: Polish & Cross-Cutting Concerns  

**Purpose**: Improvements that affect multiple user stories and final validation

- [ ] T040 [P] Update existing command help text to ensure comprehensive information display — Status: incomplete
  - Evidence: no explicit validation artifact or test coverage proving help completeness across all commands.
- [ ] T041 [P] Add comprehensive error message validation for all invalid command usage scenarios — Status: incomplete
  - Evidence: targeted invalid-usage assertion suite is not present across the full command set.
- [ ] T042 [P] Performance validation ensuring command startup time remains under 2 seconds — Status: incomplete
  - Evidence: no benchmark/test artifact found for startup-time threshold.
- [ ] T043 [P] Memory usage baseline validation for DI container initialization — Status: incomplete
  - Evidence: no memory baseline measurement/report artifact found.
- [ ] T044 [P] Run quickstart.md validation against refactored architecture — Status: incomplete
  - Evidence: no recorded validation run proving quickstart remains accurate; quickstart still references removed command examples.
- [X] T045 [P] Update any documentation reflecting the new CLI architecture patterns — Status: complete
- [ ] T046 Code cleanup and refactoring for maintainability across all changed files — Status: incomplete
  - Evidence: no explicit completion evidence or cleanup checklist outcome recorded.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies - can start immediately  
- **Foundational (Phase 2)**: Depends on Setup completion - BLOCKS all user stories
- **User Stories (Phase 3-5)**: All depend on Foundational phase completion
  - User Story 1 & 2 (both P1) can proceed in parallel after Foundation
  - User Story 3 (P2) can start after Foundation but may benefit from US1/US2 completion
- **Polish (Final Phase)**: Depends on all user stories being complete

### User Story Dependencies

- **User Story 1 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories
- **User Story 2 (P1)**: Can start after Foundational (Phase 2) - No dependencies on other stories, but integration with US1 CommandBase refactoring
- **User Story 3 (P2)**: Can start after Foundational (Phase 2) - Architectural validation of refactoring from US1/US2

### Within Each User Story

- Gherkin feature files MUST be written first (ATDD Phase 1)
- Tests MUST be written and FAIL before implementation  
- CommandBase<T> foundation before command refactoring
- Individual command refactoring can proceed in parallel
- Architecture validation after implementation completion

### Parallel Opportunities

- All Setup tasks marked [P] can run in parallel
- All Foundational tasks can proceed after T005-T006 complete (MigrationPlatformHost + CommandBase<T> foundation)
- User Stories 1 & 2 can start in parallel after Foundational phase
- Command refactoring within User Story 1 (T016, T017, T018) can run in parallel
- Test file creation within each story can run in parallel where marked [P]

---

## Parallel Example: User Story 1

```bash
# Launch all tests for User Story 1 together:
Task: "Create TfsExportCommandTests.cs with CommandAppTester validation tests"  
Task: "Create LogsCommandTests.cs with CommandAppTester validation tests"
Task: "Create InventoryCommandTests.cs with CommandAppTester validation tests"
Task: "Create CommandBaseTests.cs for unit testing base class functionality"

# Launch all command refactoring for User Story 1 together:
Task: "Refactor TfsExportCommand to inherit from CommandBase<TfsExportCommandSettings>"
Task: "Refactor LogsCommand to inherit from CommandBase<LogsCommand.Settings>"  
Task: "Refactor InventoryCommand to inherit from CommandBase<InventoryCommand.Settings>"
```

---

## Implementation Strategy

### MVP First (User Stories 1 & 2 Only - Both P1)

1. Complete Phase 1: Setup
2. Complete Phase 2: Foundational (CRITICAL - blocks all stories) 
3. Complete Phase 3: User Story 1 (CLI commands execute without errors)
4. Complete Phase 4: User Story 2 (Configuration flows correctly)
5. **STOP and VALIDATE**: Test both P1 stories independently  
6. Deploy/demo if ready - this covers the critical runtime and configuration issues

### Incremental Delivery  

1. Complete Setup + Foundational → Foundation ready
2. Add User Story 1 → Test independently → CLI commands work without errors
3. Add User Story 2 → Test independently → Configuration system working (MVP!)
4. Add User Story 3 → Test independently → Architecture fully compliant  
5. Each story adds value without breaking previous stories

### Parallel Team Strategy

- **Foundation team**: Focus on Phase 2 (MigrationPlatformHost + CommandBase<T>)
- **Testing team**: Start User Story test creation immediately after Foundation  
- **Implementation team**: Refactor commands in parallel once CommandBase<T> available
- **Architecture team**: Focus on User Story 3 validation and compliance

---

## Total Task Count: 46 tasks

**Task count per user story**:
- **Setup**: 4 tasks
- **Foundational**: 6 tasks (critical blocking phase)
- **User Story 1** (P1): 11 tasks (CLI commands execute without errors) 
- **User Story 2** (P1): 9 tasks (Configuration flows correctly)
- **User Story 3** (P2): 9 tasks (Architecture follows host builder pattern)
- **Polish**: 7 tasks (cross-cutting improvements)

**Parallel opportunities identified**: 28 tasks marked with [P] can execute in parallel when dependencies met

**Independent test criteria**:
- **US1**: All commands execute with exit code 0 for valid inputs, non-zero for invalid inputs
- **US2**: Configuration values from --config parameter reach target services as verified by integration tests  
- **US3**: Program.cs < 50 lines, new commands addable without modifying core infrastructure

**Suggested MVP scope**: User Stories 1 & 2 (both P1) - resolves critical runtime errors and configuration issues

