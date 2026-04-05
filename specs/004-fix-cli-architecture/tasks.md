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

- [ ] T001 Create test project DevOpsMigrationPlatform.CLI.Migration.Tests.csproj with Spectre.Console.Cli.Testing, MSTest, and Moq dependencies
- [ ] T002 [P] Add PackageReference for Spectre.Console.Cli.Testing to test project
- [ ] T003 [P] Create TestUtilities/InMemoryTestConfiguration.cs for configuration test doubles
- [ ] T004 [P] Create TestUtilities/MockServiceProvider.cs for DI container test doubles

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core infrastructure that MUST be complete before ANY user story can be implemented

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [ ] T005 Create MigrationPlatformHost.cs static factory class in src/DevOpsMigrationPlatform.CLI.Migration/
- [ ] T006 Implement CommandBase<TSettings> abstract class in src/DevOpsMigrationPlatform.CLI.Migration/Commands/CommandBase.cs
- [ ] T007 Extract configuration extraction logic from Program.cs to MigrationPlatformHost.CreateDefaultBuilder() method  
- [ ] T008 Migrate service registration logic from Program.cs to MigrationPlatformHost configuration
- [ ] T009 Setup OpenTelemetry and logging configuration in MigrationPlatformHost per existing Program.cs patterns
- [ ] T010 Create Spectre.Console CommandApp integration in MigrationPlatformHost with proper DI configuration

**Checkpoint**: Foundation ready - user story implementation can now begin in parallel

---

## Phase 3: User Story 1 - CLI Commands Execute Without Errors (Priority: P1) 🎯 MVP

**Goal**: All CLI commands execute successfully without runtime errors when provided valid inputs

**Independent Test**: Run each command with valid parameters and verify exit code 0, run with invalid parameters and verify non-zero exit codes

### Gherkin Feature File for User Story 1 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. It must be written from the `spec.md` User Story 1 acceptance scenarios and committed before any step definitions or production code are written.**

- [ ] T011 [US1] Create `features/cli/execute/commands-execute-successfully.feature` — translate `spec.md` User Story 1 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Tests for User Story 1

> **NOTE: Write these tests FIRST, ensure they FAIL before implementation**

- [ ] T012 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/TfsExportCommandTests.cs with CommandAppTester validation tests
- [ ] T013 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/LogsCommandTests.cs with CommandAppTester validation tests
- [ ] T014 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/InventoryCommandTests.cs with CommandAppTester validation tests
- [ ] T015 [P] [US1] Create tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/CommandBaseTests.cs for unit testing base class functionality

### Implementation for User Story 1

- [ ] T016 [P] [US1] Refactor TfsExportCommand in src/DevOpsMigrationPlatform.CLI.Migration/Commands/TfsExportCommand.cs to inherit from CommandBase<TfsExportCommandSettings>
- [ ] T017 [P] [US1] Refactor LogsCommand in src/DevOpsMigrationPlatform.CLI.Migration/Commands/LogsCommand.cs to inherit from CommandBase<LogsCommand.Settings>
- [ ] T018 [P] [US1] Refactor InventoryCommand in src/DevOpsMigrationPlatform.CLI.Migration/Commands/InventoryCommand.cs to inherit from CommandBase<InventoryCommand.Settings>  
- [ ] T019 [US1] Implement proper error handling and telemetry in CommandBase<T>.ExecuteAsync() with exception logging
- [ ] T020 [US1] Add IHostApplicationLifetime.StopApplication() calls to command completion in CommandBase<T>
- [ ] T021 [US1] Update Program.cs to use minimal bootstrapping pattern following azure-devops-migration-tools reference

**Checkpoint**: At this point, User Story 1 should be fully functional - all commands execute without errors

---

## Phase 4: User Story 2 - Configuration Flows Correctly Through the System (Priority: P1)

**Goal**: --config parameter correctly passes configuration data from command line through to internal services

**Independent Test**: Provide specific config values and verify they reach appropriate services via logging or test assertions

### Gherkin Feature File for User Story 2 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 2 acceptance scenarios before any step definitions or production code.**

- [ ] T022 [US2] Create `features/cli/execute/configuration-flow.feature` — translate `spec.md` User Story 2 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Tests for User Story 2

- [ ] T023 [P] [US2] Create integration test tests/DevOpsMigrationPlatform.CLI.Migration.Tests/MigrationPlatformHostTests.cs for configuration binding validation
- [ ] T024 [P] [US2] Add configuration flow tests to existing command test files verifying config values reach target services  
- [ ] T025 [P] [US2] Create tests for default config file resolution (migration.json) when --config not specified

### Implementation for User Story 2

- [ ] T026 [US2] Implement --config parameter extraction logic in MigrationPlatformHost before DI container creation
- [ ] T027 [US2] Setup IOptions<T> pattern integration in MigrationPlatformHost service registration for all existing configuration classes
- [ ] T028 [US2] Verify configuration binding between command-line args, environment variables, and config files follows proper precedence
- [ ] T029 [US2] Update existing services to receive configuration via dependency injection rather than direct file access where applicable
- [ ] T030 [US2] Add configuration validation and error handling for malformed JSON and missing required sections

**Checkpoint**: At this point, User Stories 1 AND 2 should both work independently

---

## Phase 5: User Story 3 - CLI Architecture Follows Proper Host Builder Pattern (Priority: P2)

**Goal**: CLI architecture follows proper separation of concerns with commands managing their hosting lifecycle

**Independent Test**: Examine refactored code structure, verify DI container setup, run unit tests demonstrating proper separation

### Gherkin Feature File for User Story 3 (mandatory)

> **NOTE: This `.feature` file is the ATDD Phase 1 artifact. Write from `spec.md` User Story 3 acceptance scenarios before any step definitions or production code.**

- [ ] T031 [US3] Create `features/cli/execute/host-builder-architecture.feature` — translate `spec.md` User Story 3 acceptance scenarios into conformant Gherkin (see `.agents/guardrails/acceptance-test-format.md`)

### Tests for User Story 3

- [ ] T032 [P] [US3] Create architectural validation tests in tests/DevOpsMigrationPlatform.CLI.Migration.Tests/ArchitectureTests.cs verifying Program.cs line count < 50
- [ ] T033 [P] [US3] Add unit tests verifying new commands can be added without modifying Program.cs or host setup
- [ ] T034 [P] [US3] Create integration tests validating complete DI container service registration and resolution

### Implementation for User Story 3

- [ ] T035 [US3] Finalize Program.cs refactoring to contain only minimal bootstrapping logic (target < 50 lines)
- [ ] T036 [US3] Ensure all service registration, configuration, and infrastructure setup is centralized in MigrationPlatformHost
- [ ] T037 [US3] Validate that CommandBase<T> provides proper lifecycle management and service access patterns
- [ ] T038 [US3] Add documentation comments to MigrationPlatformHost and CommandBase<T> explaining architecture patterns
- [ ] T039 [US3] Verify separation of concerns between bootstrapping (Program.cs), infrastructure (host builder), and commands

**Checkpoint**: All user stories should now be independently functional with proper architecture

---

## Phase 6: Polish & Cross-Cutting Concerns  

**Purpose**: Improvements that affect multiple user stories and final validation

- [ ] T040 [P] Update existing command help text to ensure comprehensive information display
- [ ] T041 [P] Add comprehensive error message validation for all invalid command usage scenarios
- [ ] T042 [P] Performance validation ensuring command startup time remains under 2 seconds
- [ ] T043 [P] Memory usage baseline validation for DI container initialization
- [ ] T044 [P] Run quickstart.md validation against refactored architecture
- [ ] T045 [P] Update any documentation reflecting the new CLI architecture patterns
- [ ] T046 Code cleanup and refactoring for maintainability across all changed files

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