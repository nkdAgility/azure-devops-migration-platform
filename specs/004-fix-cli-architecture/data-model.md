# Data Model: CLI Architecture Entities and Relationships

**Feature**: Fix CLI Architecture and Add Command Testing  
**Phase**: Phase 1 Design  
**Date**: April 5, 2026

## Core Entities

### MigrationPlatformHost

**Purpose**: Central host builder managing DI container and infrastructure setup
**Lifecycle**: Static factory class, creates IHostBuilder instances
**Relationships**: Creates and configures IServiceCollection, manages service registrations

**Properties**:
- Static factory method: `CreateDefaultBuilder(string[] args) -> IHostBuilder`
- Configuration extraction logic (ExtractConfigFileArg equivalent)
- Service registration orchestration
- Telemetry and logging setup
- Spectre.Console integration configuration

**Validation Rules**:
- Must extract --config parameter before DI container creation
- Must preserve existing service registration patterns during migration
- Must not contain command-specific logic (single responsibility)

**State Transitions**: 
1. Static factory called with command line args
2. Configuration extracted and layered (appsettings → config file → environment)
3. Service registration performed  
4. Spectre.Console CommandApp configured
5. Returns configured IHostBuilder

### CommandBase<TSettings>

**Purpose**: Abstract base class providing common command infrastructure
**Lifecycle**: Base class instantiated by Spectre.Console via DI container
**Relationships**: Used by all concrete command implementations, accesses IServiceProvider

**Properties**:
- `IServiceProvider Services`: Access to DI container services
- `IHostApplicationLifetime Lifetime`: Control over application lifecycle  
- `ILogger<CommandBase<TSettings>> Logger`: Structured logging capability
- `ActivitySource ActivitySource`: OpenTelemetry tracing
- Abstract `ExecuteInternalAsync()`: Command implementation contract

**Validation Rules**:
- Must call `Lifetime.StopApplication()` when command work completes
- Must provide consistent error handling and telemetry patterns
- Must not contain migration-specific logic (remains generic base)

**State Transitions**:
1. Instantiated by Spectre.Console with dependencies injected
2. `ExecuteAsync()` called by framework  
3. Common setup performed (telemetry, error handling)
4. `ExecuteInternalAsync()` called for command-specific work
5. Lifecycle managed via `Lifetime.StopApplication()`

### CLI Command Entities

**TfsExportCommand**, **LogsCommand**, **InventoryCommand**

**Purpose**: Execute specific CLI operations with proper lifecycle management
**Lifecycle**: Inherit from CommandBase<T>, instantiated per command execution
**Relationships**: Consume services via base class IServiceProvider access

**Properties**:
- Settings class containing command-line parameters
- Service dependencies accessed via `Services.GetRequiredService<T>()`
- Command-specific business logic in `ExecuteInternalAsync()` override

**Validation Rules**:
- Must inherit from CommandBase<TSettings> 
- Must not access configuration files directly (use IOptions<T>)
- Must delegate to appropriate services rather than containing business logic  

**State Transitions**:
1. Settings parsed by Spectre.Console framework
2. Command instantiated with DI-provided dependencies
3. Business logic executed via service delegation
4. Progress/results communicated via injected services
5. Application lifetime controlled via base class

### Test Infrastructure Entities

### CommandAppTester

**Purpose**: Isolated testing infrastructure for CLI command validation
**Lifecycle**: Created per test method for clean test isolation
**Relationships**: Wraps actual command implementations with test doubles

**Properties**:
- In-memory configuration test doubles  
- Mock service provider implementations
- Captured output and exit code results
- Isolated test environment (no external dependencies)

**Validation Rules**:
- Must use in-memory test doubles for all external dependencies
- Must validate exit codes, output content, and error scenarios
- Must test configuration flow from command line to internal services

**State Transitions**:
1. Test setup creates CommandAppTester with test configuration
2. Command executed via CommandAppTester.Run() 
3. Output captured and exit code recorded
4. Test assertions performed on results
5. Test cleanup disposes isolated test environment

## Entity Relationships

```
Program.cs (minimal)
    ↓ calls
MigrationPlatformHost.CreateDefaultBuilder()
    ↓ creates & configures
IServiceCollection (with all registered services)
    ↓ passed to
CommandApp (Spectre.Console)
    ↓ resolves & instantiates
CommandBase<TSettings> implementations
    ↓ access services via
IServiceProvider interface
    ↓ tested by
CommandAppTester (with test doubles)
```

## Configuration Data Flow

```
Command Line Args (--config, etc.)
    ↓ extracted by
MigrationPlatformHost.ExtractConfigFileArg()
    ↓ used to build
IConfiguration (layered: appsettings → config file → env vars)
    ↓ bound to
IOptions<T> services
    ↓ injected into
Command implementations via DI
```

## Test Data Flow

```
Test Configuration (in-memory)
    ↓ provided to
CommandAppTester  
    ↓ creates isolated
CommandApp with test doubles
    ↓ executes
Command under test
    ↓ produces
Exit code + Output + Error results
    ↓ validated by
Test assertions (MSTest)
```

## Validation & Constraints

**Cross-Entity Constraints**:
- All commands MUST inherit from CommandBase<TSettings>
- All configuration MUST flow through IOptions<T> pattern
- All external dependencies MUST be injected via constructor
- All tests MUST use in-memory test doubles for isolation
- Host builder MUST extract configuration before DI container creation