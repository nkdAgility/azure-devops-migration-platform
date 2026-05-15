# Implementation Plan: Fix CLI Architecture and Add Command Testing

**Branch**: `004-fix-cli-architecture` | **Date**: April 5, 2026 | **Spec**: [spec.md](./spec.md)
**Input**: Feature specification from `/specs/004-fix-cli-architecture/spec.md`

**Note**: This template is filled in by the `/speckit.plan` command. See `.specify/templates/plan-template.md` for the execution workflow.

## Summary

Refactor CLI Program.cs from a monolithic 150+ line god-object into a proper host builder pattern following azure-devops-migration-tools architecture. Move all DI container setup, service registration, and infrastructure concerns to a dedicated `MigrationPlatformHost`. Implement `CommandBase<T>` that provides commands with `IServiceProvider` and `IHostApplicationLifetime` for proper lifecycle management. Add comprehensive command testing infrastructure using Spectre.Console.Cli.Testing to prevent runtime failures.

## Technical Context

**Language/Version**: C# 10+, .NET 10 (per guardrails coding-standards.md)  
**Primary Dependencies**: Spectre.Console.Cli 0.49.1, Microsoft.Extensions.DependencyInjection 10.*, OpenTelemetry.Extensions.Hosting 1.*  
**Storage**: File-based configuration (appsettings.json, migration.json), PostgreSQL for ControlPlane data persistence  
**Testing**: MSTest + Reqnroll.MSTest (per guardrails testing-rules.md), Spectre.Console.Cli.Testing for CLI command validation  
**Target Platform**: Cross-platform .NET 10 console application, supports Windows/Linux/macOS
**Project Type**: Console CLI application with host builder pattern and DI container management  
**Performance Goals**: CLI command startup < 2 seconds, DI container initialization within current baseline memory consumption  
**Constraints**: Must maintain 100% backward compatibility with existing command-line interfaces, zero architecture violations (Program.cs < 50 lines)  
**Scale/Scope**: 4 existing commands (discovery inventory, tfsexport, logs, plus branch commands), testing infrastructure for current + future commands

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

> **Mandatory context loading:** Before completing this gate, confirmed that ALL files in
> `/.agents/20-guardrails/`, ALL files in `/.agents/30-context/`, and relevant `/docs/` files
> have been read: ✅ architecture-boundaries.md, coding-standards.md, testing-rules.md, cli.md, architecture.md

- [✅] **Package-First (I):** CLI delegates all package operations to ControlPlane/Agents. No direct package access in CLI refactor.
- [✅] **Streaming (II):** CLI doesn't process revisions - delegates to Job Engine. Refactor maintains this separation.
- [✅] **WorkItems Layout (III):** CLI doesn't touch WorkItems structure. Host builder pattern preserves existing delegation.
- [✅] **Checkpointing (IV):** CLI doesn't write checkpoints - delegates to Agents. Architecture refactor maintains this boundary.
- [⚠️] **Module Isolation (V):** PARTIALLY COMPLIANT - CLI will use proper IServiceProvider/DI abstractions after refactor. Current Program.cs violates constructor injection by doing manual service registration. **FIXED BY THIS FEATURE**.
- [✅] **Separation of Planes (VI):** CLI properly delegates to ControlPlane via ControlPlaneClient. Refactor preserves this. No migration logic in CLI.
- [✅] **Determinism (VII):** CLI architecture changes don't affect package determinism or upgrader requirements.
- [✅] **ATDD-First (VIII):** Feature spec contains proper Given/When/Then scenarios for all user stories. Will follow ATDD inner loop.  
- [❌] **SOLID & DI (IX):** **MAJOR VIOLATION** - Current Program.cs contains 100+ lines of manual service registration, violates Single Responsibility, lacks proper DI patterns. **THIS FEATURE SPECIFICALLY FIXES THIS VIOLATION**.

**Gate Status**: ⚠️ CONDITIONAL PASS - Current architecture violates SOLID & DI principles, but this feature exists specifically to fix those violations.

## Project Structure

### Documentation (this feature)

```text
specs/[###-feature]/
├── plan.md              # This file (/speckit.plan command output)
├── research.md          # Phase 0 output (/speckit.plan command)
├── data-model.md        # Phase 1 output (/speckit.plan command)
├── quickstart.md        # Phase 1 output (/speckit.plan command)
├── contracts/           # Phase 1 output (/speckit.plan command)
└── tasks.md             # Phase 2 output (/speckit.tasks command - NOT created by /speckit.plan)
```

### Source Code (repository root)

```text
src/DevOpsMigrationPlatform.CLI.Migration/
├── Program.cs                           # REFACTORED: Minimal bootstrapping only (15-20 lines)
├── MigrationPlatformHost.cs            # NEW: Central host builder managing DI and infrastructure  
├── Commands/
│   ├── CommandBase.cs                  # NEW: Base class with IServiceProvider + IHostApplicationLifetime
│   ├── TfsExportCommand.cs            # UPDATED: Inherit from CommandBase<T>
│   ├── LogsCommand.cs                 # UPDATED: Inherit from CommandBase<T>  
│   └── Discovery/
│       └── InventoryCommand.cs        # UPDATED: Inherit from CommandBase<T>
├── Infrastructure/
│   ├── TypeRegistrar.cs               # EXISTING: Spectre.Console DI bridge (no changes)
│   └── TypeResolver.cs                # EXISTING: Spectre.Console DI bridge (no changes)
└── appsettings.json                    # EXISTING: Base configuration (no changes)

tests/DevOpsMigrationPlatform.CLI.Migration.Tests/
├── DevOpsMigrationPlatform.CLI.Migration.Tests.csproj  # NEW: Test project
├── Commands/
│   ├── TfsExportCommandTests.cs        # NEW: CommandAppTester validation tests
│   ├── LogsCommandTests.cs             # NEW: CommandAppTester validation tests  
│   ├── InventoryCommandTests.cs        # NEW: CommandAppTester validation tests
│   └── CommandBaseTests.cs             # NEW: Unit tests for base command functionality
├── MigrationPlatformHostTests.cs       # NEW: Host builder configuration tests
└── TestUtilities/
    ├── InMemoryTestConfiguration.cs    # NEW: Test doubles for configuration
    └── MockServiceProvider.cs          # NEW: Test doubles for DI services
```
api/
└── [same as backend above]

ios/ or android/
└── [platform-specific structure: feature modules, UI flows, platform tests]
```

## Complexity Tracking

> **Fill ONLY if Constitution Check has violations that must be justified**

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Manual service registration in Program.cs | Current architecture violates SOLID/SRP principles | Direct service access would require major refactor of all commands and break testability |

## Phase 0: Outline & Research

### Research Tasks

1. **Host Builder Pattern Migration**: Extract service registration patterns from current Program.cs and map to MigrationPlatformHost.CreateDefaultBuilder(). Research azure-devops-migration-tools MigrationToolHost implementation for configuration extraction, service registration order, and telemetry setup patterns.

2. **CommandBase Implementation**: Research CommandBase<T> inheritance pattern from azure-devops-migration-tools. Analyze constructor injection pattern, IHostApplicationLifetime usage, and service provider access patterns for command lifecycle management.

3. **Spectre.Console.Cli.Testing Integration**: Research CommandAppTester patterns, in-memory configuration testing, and test isolation strategies for CLI command validation. Investigate test doubles for IServiceProvider and external dependencies.

4. **Configuration Flow Validation**: Research IOptions<T> binding patterns in host builder context, environment variable override behavior, and configuration validation lifecycle to ensure config reaches target services properly.

### Research Output: research.md

Decision tracking for:
- Host builder service registration order and lifecycle management
- CommandBase<T> constructor signature and dependency injection patterns  
- Test isolation strategy using in-memory test doubles
- Configuration binding and validation patterns for CLI context

## Phase 1: Design & Contracts

### Key Entities (data-model.md)

**CLI Infrastructure Entities**
- `MigrationPlatformHost`: Central host builder with CreateDefaultBuilder() method, service registration, configuration binding
- `CommandBase<T>`: Abstract base class providing IServiceProvider, IHostApplicationLifetime, common error handling and telemetry setup
- `CommandAppTester`: Test infrastructure for isolated command validation with mock services

**Configuration Entities**  
- Configuration flow: command-line args → environment variables → config files (existing IOptions<T> patterns preserved)
- Host builder manages configuration extraction before DI container creation
- Commands receive configuration via dependency injection, never direct file access

### Interface Contracts (contracts/)

**MigrationPlatformHost Contract**
```csharp
public static class MigrationPlatformHost
{
    public static IHostBuilder CreateDefaultBuilder(string[] args);
    // Manages: config extraction, service registration, telemetry setup, infrastructure concerns
}
```

**CommandBase<T> Contract**  
```csharp
public abstract class CommandBase<TSettings> : AsyncCommand<TSettings>
    where TSettings : CommandSettings
{
    protected IServiceProvider Services { get; }
    protected IHostApplicationLifetime Lifetime { get; }
    // Provides: service access, lifecycle management, common error handling
}
```

### Testing Strategy (contracts/)

**CLI Command Testing Contract**
- All commands testable via CommandAppTester with in-memory test doubles
- Configuration provided via in-memory IConfiguration instances  
- External dependencies mocked via IServiceProvider test doubles
- Exit codes, output, and error scenarios validated for every command

## Phase 1: Agent Context Update

✅ **COMPLETED**: Successfully updated `.github/copilot-instructions.md` with CLI architecture patterns:
- Added Spectre.Console.Cli 0.49.1, Microsoft.Extensions.DependencyInjection 10.*, OpenTelemetry.Extensions.Hosting 1.*  
- Added file-based configuration patterns (appsettings.json, migration.json)
- Added console CLI application with host builder pattern recognition

## Post-Design Constitution Check

*GATE: Re-evaluate after Phase 1 design completion.*

> **Context validated:** Design artifacts completed - data-model.md, contracts/, quickstart.md, agent context updated.
> Confirmed CLI architecture follows azure-devops-migration-tools reference pattern.

- [✅] **Package-First (I):** Design preserves CLI delegation to ControlPlane. No direct package access in refactored architecture.
- [✅] **Streaming (II):** Design maintains CLI/Job Engine separation. Host builder doesn't process revisions.  
- [✅] **WorkItems Layout (III):** Design preserves existing delegation patterns. CLI doesn't affect WorkItems structure.
- [✅] **Checkpointing (IV):** Design maintains CLI/Agent boundary. No checkpoint logic in host builder.
- [✅] **Module Isolation (V):** **FIXED** - MigrationPlatformHost + CommandBase<T> design enforces proper DI. All dependencies injected via constructor.
- [✅] **Separation of Planes (VI):** Design preserves ControlPlaneClient delegation. No migration logic in CLI architecture.
- [✅] **Determinism (VII):** CLI architecture changes are pure infrastructure. No impact on package determinism.
- [✅] **ATDD-First (VIII):** Design includes comprehensive CLI testing via CommandAppTester. All commands validated.
- [✅] **SOLID & DI (IX):** **FIXED** - Host builder design follows Single Responsibility. CommandBase<T> provides proper DI. Program.cs becomes minimal entry point.

**Gate Status**: ✅ **FULL COMPLIANCE** - All constitution violations addressed by design. Ready for implementation.

## Constitution Violations Addressed

| Violation | Why Needed | How Design Fixes |
|-----------|------------|------------------|
| SOLID & DI violation in Program.cs | 150+ line god-object with manual DI setup violates SRP | MigrationPlatformHost extracts service registration. CommandBase<T> provides proper DI. Program.cs becomes < 10 lines |
| Module Isolation partial compliance | Manual service registration bypasses DI container | Host builder pattern enforces constructor injection. All dependencies through IServiceProvider |

