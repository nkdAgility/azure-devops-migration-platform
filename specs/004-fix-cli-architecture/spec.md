# Feature Specification: Fix CLI Architecture and Add Command Testing

**Feature Branch**: `004-fix-cli-architecture`  
**Created**: April 5, 2026  
**Status**: Draft  
**Input**: User description: "Fix CLI architecture by refactoring Program.cs to follow proper host builder pattern, fix config passing issues, and add comprehensive command testing using Spectre.Console.Cli.Testing to ensure all commands work correctly"

## Architecture References

This specification is grounded in the following documentation:

- **docs/architecture.md**: Confirmed accurate - defines CLI as thin shell, commands submit jobs via ControlPlaneClient
- **docs/cli-guide.md**: Confirmed accurate - specifies CLI technology (Spectre.Console) and command structure  
- **.agents/20-guardrails/core/architecture-boundaries.md**: Confirmed accurate - Rule 16: "CLI must not contain migration logic"
- **.agents/20-guardrails/core/coding-standards.md**: Confirmed accurate - C# 10+, .NET 9/10 standards
- **.agents/20-guardrails/workflow/testing-rules.md**: Confirmed accurate - MSTest + Reqnroll framework requirements

**Discrepancy**: Current Program.cs implementation violates the "thin shell" principle by aggregating all DI setup and configuration logic. The reference implementation in azure-devops-migration-tools demonstrates the correct pattern where commands manage their own hosting lifecycle through a host builder.

## User Scenarios & Testing

### User Story 1 - CLI Commands Execute Without Errors (Priority: P1)

As a migration platform operator, I need all CLI commands to execute without runtime errors so that I can confidently use the platform for migrations.

**Why this priority**: Without working commands, the platform is unusable. This is the foundation that enables all other functionality.

**Independent Test**: Can be fully tested by running each command with valid and invalid parameters and verifying appropriate behavior (success/failure codes, error messages, help text).

**Acceptance Scenarios**:

1. **Given** a valid migration config file exists, **When** I run `devopsmigration discovery inventory --config migration.json`, **Then** the command executes successfully and returns exit code 0
2. **Given** an invalid config file path, **When** I run any command with `--config invalid-path.json`, **Then** the command displays a clear error message and returns non-zero exit code
3. **Given** no config file specified, **When** I run commands that require config, **Then** the system uses the default `migration.json` or shows appropriate error message
4. **Given** I pass `--help` to any command, **When** the command executes, **Then** it displays comprehensive help text without errors

### User Story 2 - Configuration Flows Correctly Through the System (Priority: P1)

As a platform operator, I need the `--config` parameter to correctly pass configuration data from the command line through to internal services so that my migration jobs use the correct settings.

**Why this priority**: Configuration is critical for all migrations. If config doesn't flow correctly, migrations will fail or use wrong settings.

**Independent Test**: Can be tested by providing specific config values and verifying they reach the appropriate services via logging or test assertions.

**Acceptance Scenarios**:

1. **Given** I specify `--config custom.json` with specific source URLs, **When** a command executes, **Then** the internal services receive the correct source URL configuration
2. **Given** I specify config with authentication settings, **When** the command attempts to connect, **Then** it uses the provided authentication parameters
3. **Given** config contains telemetry settings, **When** commands execute, **Then** the telemetry system is configured according to the config file settings

### User Story 3 - CLI Architecture Follows Proper Host Builder Pattern (Priority: P2)

As a platform developer, I need the CLI architecture to follow proper separation of concerns with commands managing their hosting lifecycle so that the code is maintainable and testable.

**Why this priority**: Clean architecture enables faster development, easier testing, and better long-term maintainability.

**Independent Test**: Can be tested by examining the refactored code structure, verifying DI container setup, and running unit tests that demonstrate proper separation.

**Acceptance Scenarios**:

1. **Given** the refactored CLI architecture, **When** I examine Program.cs, **Then** it contains only minimal bootstrapping logic (similar to azure-devops-migration-tools pattern)
2. **Given** a new command needs to be added, **When** a developer implements it, **Then** they can do so without modifying Program.cs or host setup logic
3. **Given** the refactored architecture, **When** commands execute, **Then** they have full access to DI container services while managing their own lifecycle

### Edge Cases

- What happens when config file is malformed JSON?
- How does system handle when config file exists but lacks required sections?
- What occurs when multiple config sources conflict (file vs environment variables vs command args)?
- How does the system respond when DI container fails to build due to configuration errors?

## Requirements

### Functional Requirements

**CLI Architecture (Command Hosting)**

1. **Program.cs Simplification**: Program.cs MUST contain only minimal bootstrapping code - creating the host builder and running the console app. All DI container setup, service registration, and infrastructure configuration MUST be moved to a dedicated host builder class.

2. **Command-Managed Hosting**: Commands MUST manage their own hosting lifecycle as demonstrated in the azure-devops-migration-tools reference implementation. Commands MUST inherit from `CommandBase<T>` which provides access to `IServiceProvider` and `IHostApplicationLifetime`, allowing commands to control application lifetime via `_appLifetime.StopApplication()`. Commands MUST NOT create their own host instances (rejecting the POC pattern).

3. **Configuration Extraction**: The system MUST extract `--config` and `-c` parameters before Spectre.Console processes arguments, using the same pattern as azure-devops-migration-tools `CommandSettingsBase.ForceGetConfigFile()`.

4. **Host Builder Pattern**: A dedicated host builder class (e.g., `MigrationPlatformHost`) MUST centralize all service registration, configuration binding, telemetry setup, and infrastructure concerns previously in Program.cs.

**Configuration Flow**

5. **Config Parameter Handling**: The `--config` parameter MUST be consumed by the host builder before DI container creation to ensure configuration is available during service registration.

6. **Default Config Resolution**: When no `--config` is specified, the system MUST default to `migration.json` in the current working directory, consistent with the existing ExtractConfigFileArg logic.

7. **Options Pattern Integration**: All configuration MUST flow through IOptions<T> pattern with proper validation, ensuring services receive configuration via dependency injection rather than direct file access.

**Command Testing Infrastructure**

8. **Spectre.Console.Cli.Testing Integration**: All CLI commands MUST be testable using CommandAppTester from Spectre.Console.Cli.Testing package.

9. **Automated Command Validation**: Test suite MUST include automated tests for every command that verify:
   - Commands execute without runtime errors with valid inputs
   - Commands return appropriate exit codes for success/failure scenarios  
   - Commands display proper error messages for invalid inputs
   - Help text displays correctly for all commands

10. **Integration Test Coverage**: Tests MUST cover the complete flow from command-line argument parsing through DI container resolution to service execution.

11. **Test Isolation Strategy**: Tests MUST use in-memory test doubles for all external dependencies (configuration, HTTP clients, file system access) to ensure clean isolation and fast execution.

### Non-Functional Requirements

**Architecture Quality**

11. **Separation of Concerns**: The refactored architecture MUST cleanly separate bootstrapping (Program.cs), infrastructure setup (host builder), and command logic (command classes).

12. **Testability**: All commands MUST be unit testable by mocking IServiceProvider and related dependencies.

13. **Maintainability**: Adding new commands MUST require changes only to command classes and host builder configuration, never to Program.cs.

**Compatibility**

14. **Backward Compatibility**: All existing command-line interfaces and parameters MUST continue to work exactly as before the refactoring.

15. **Configuration Compatibility**: All existing configuration file formats and environment variable patterns MUST remain fully compatible.

## Success Criteria

**Technical Measuring Success**

1. **All CLI commands execute successfully**: 100% of existing commands complete without runtime exceptions when provided valid inputs
2. **Zero architecture violations**: Program.cs contains fewer than 50 lines of code and only bootstrapping logic
3. **Complete test coverage**: All commands have automated tests with 100% pass rate in CI pipeline
4. **Configuration flow verification**: All config file settings correctly reach their target services as verified by integration tests
5. **Performance maintained**: Command startup time remains under 2 seconds for all operations
6. **Memory efficiency**: DI container initialization uses no more than current baseline memory consumption

**User Experience Success**

7. **Error message clarity**: Invalid command usage results in clear, actionable error messages
8. **Help text completeness**: All commands display comprehensive help when requested
9. **Developer productivity**: New command implementation requires zero changes to core infrastructure
10. **Debugging capability**: Failed commands provide sufficient logging information for troubleshooting

## Key Entities

**CLI Infrastructure Entities**
- `MigrationPlatformHost`: Central host builder managing DI and infrastructure (follows azure-devops-migration-tools MigrationToolHost pattern)
- `CommandBase<T>`: Base class providing IServiceProvider, IHostApplicationLifetime, and common command functionality  
- `TypeRegistrar`/`TypeResolver`: Spectre.Console DI bridge components
- `CommandAppTester`: Testing infrastructure for command validation

**Configuration Entities**
- `MigrationOptions`: Strongly-typed configuration model bound via IOptions pattern
- `TelemetryOptions`: Telemetry configuration settings
- `InventoryOptions`: Inventory-specific configuration
- Configuration file: JSON configuration providing runtime settings

## Assumptions

- Command line interface contract remains unchanged to maintain user compatibility
- Existing service registrations and DI patterns will be preserved in the new host builder
- Test execution environment supports both local filesystem and in-memory configuration sources
- Spectre.Console.Cli.Testing package provides sufficient test capabilities for command validation
- Reference azure-devops-migration-tools implementation patterns are applicable to current platform architecture requirements
- All existing CLI integration points (Aspire, ControlPlane, etc.) will continue to work through the refactored architecture

## Clarifications

### Session 2026-04-05

- Q: Test isolation strategy for CLI commands? → A: Use in-memory test doubles for all external dependencies (config, HTTP, file system)
- Q: CommandBase implementation pattern for host management? → A: CommandBase<T> with IHostApplicationLifetime + IServiceProvider for full host control (Pattern 2 from azure-devops-migration-tools)

## Current status

- Reconciled against repository truth on 2026-05-16.
- Core host-builder and CommandBase infrastructure exists.
- Significant task drift exists between this spec and current CLI architecture/command set.

## Remaining incomplete work (IDs)

`T010, T013, T021, T024, T028, T029, T030, T032, T035, T039, T040, T041, T042, T043, T044, T046`

## Completed because superseded (IDs + source)

- `T012, T014, T016, T018` superseded by `specs/025.1-fold-to-job` (single `Job` model + queue-driven submission replacing legacy per-command flows).
- `T017, T020` superseded by `specs/021.2-separation-of-concerns` and current `ControlPlaneCommandBase` layering.

## Contradictions and reconciliation

- This spec expects minimal Program.cs bootstrapping, but current Program.cs still owns command routing/composition.
- This spec expects dedicated `TfsExportCommand`/`InventoryCommand` test flow, but current implementation routes through `queue` modes.
- Documentation (`docs/cli-guide.md`) reflects host-builder patterns more strongly than runtime Program.cs currently does.

## Verification evidence

- `src\DevOpsMigrationPlatform.CLI.Migration\MigrationPlatformHost.cs`
- `src\DevOpsMigrationPlatform.CLI.Migration\Commands\CommandBase.cs`
- `src\DevOpsMigrationPlatform.CLI.Migration\Program.cs`
- `tests\DevOpsMigrationPlatform.CLI.Migration.Tests\MigrationPlatformHostTests.cs`
- `tests\DevOpsMigrationPlatform.CLI.Migration.Tests\Commands\CommandBaseTests.cs`
- `features\cli\execute\commands-execute-successfully.feature`
- `features\cli\execute\configuration-flow.feature`
- `features\cli\execute\host-builder-architecture.feature`
- `specs/025.1-fold-to-job/spec.md`
- `specs/021.2-separation-of-concerns/spec.md`
