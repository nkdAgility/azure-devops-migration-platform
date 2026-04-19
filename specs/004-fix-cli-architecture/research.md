# Research Report: CLI Architecture Patterns and Implementation Strategies

**Feature**: Fix CLI Architecture and Add Command Testing  
**Phase**: Phase 0 Research  
**Date**: April 5, 2026

## Research Questions Resolved

### 1. Host Builder Pattern Migration from azure-devops-migration-tools

**Decision**: Use MigrationToolHost pattern with CreateDefaultBuilder() method

**Rationale**: The azure-devops-migration-tools implementation demonstrates proven separation of concerns:
- Program.cs contains only `var hostBuilder = MigrationToolHost.CreateDefaultBuilder(args); await hostBuilder.RunConsoleAsync();`
- Host builder centralizes all infrastructure: service registration, configuration binding, telemetry, logging
- Configuration extraction happens before DI container creation using `CommandSettingsBase.ForceGetConfigFile()`

**Implementation Pattern**:
```csharp
public static class MigrationPlatformHost
{
    public static IHostBuilder CreateDefaultBuilder(string[] args)
    {
        var (configFile, spectreArgs) = ExtractConfigFileArg(args);
        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(spectreArgs);
        // Configure services, telemetry, configuration binding
        return hostBuilder.UseSpectreConsole(/* command configuration */);
    }
}
```

**Alternatives Considered**:
- POC pattern (commands create own hosts): Rejected due to isolation and performance issues
- Current pattern (Program.cs does everything): Rejected due to SOLID violations

### 2. CommandBase<T> Implementation Pattern  

**Decision**: CommandBase<T> with IServiceProvider + IHostApplicationLifetime injection

**Rationale**: azure-devops-migration-tools CommandBase provides proper lifecycle management:
- Commands inherit from `CommandBase<TSettings> : AsyncCommand<TSettings>`
- Constructor receives: `IServiceProvider services, IHostApplicationLifetime appLifetime, ILogger<T> logger`
- Commands control lifecycle via `_appLifetime.StopApplication()` when work completes
- Base class provides common telemetry, error handling, and service access patterns

**Implementation Pattern**:
```csharp
public abstract class CommandBase<TSettings> : AsyncCommand<TSettings> 
    where TSettings : CommandSettings
{
    protected IServiceProvider Services { get; }
    protected IHostApplicationLifetime Lifetime { get; }
    
    public CommandBase(IServiceProvider services, IHostApplicationLifetime lifetime, /* other common deps */)
    {
        Services = services;
        Lifetime = lifetime;
    }
    
    protected virtual async Task<int> ExecuteInternalAsync(CommandContext context, TSettings settings)
    {
        // Command implementation
        Lifetime.StopApplication(); // Control lifecycle
        return 0;
    }
}
```

**Alternatives Considered**:
- Direct IServiceProvider in each command: Rejected due to code duplication
- Commands manage own DI containers: Rejected due to POC pattern problems

### 3. Spectre.Console.Cli.Testing Integration

**Decision**: CommandAppTester with in-memory test doubles for all external dependencies

**Rationale**: Spectre.Console.Cli.Testing provides:
- `CommandAppTester` for isolated command execution without side effects
- `TestConsole` for interactive prompt testing  
- Exit code, output, and settings validation capabilities
- Integration with MSTest framework required by testing standards

**Implementation Pattern**:
```csharp
[TestMethod]
public void InventoryCommand_WithValidConfig_ReturnsSuccessExitCode()
{
    // Arrange
    var testConfig = CreateInMemoryConfiguration(/* test values */);
    var app = new CommandAppTester();
    app.SetDefaultCommand<InventoryCommand>();
    
    // Act  
    var result = app.Run("--config", "test.json");
    
    // Assert
    Assert.AreEqual(0, result.ExitCode);
    Assert.Contains("✅", result.Output);
}
```

**Alternatives Considered**:
- Real file system with temp directories: Rejected due to test isolation concerns
- Docker containers: Rejected due to complexity and speed requirements

### 4. Configuration Flow Validation

**Decision**: Preserve existing ConfigurationBuilder layering with host builder management

**Rationale**: Current configuration precedence is correct (command args > env vars > config files):
```csharp
var configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json", optional: false)           // Base defaults
    .AddJsonFile(configFile, optional: true)                    // User config
    .AddEnvironmentVariables()                                  // Highest precedence
    .Build();
```

Host builder will maintain this pattern but centralize it rather than leaving in Program.cs.

**Implementation Strategy**:
- Move configuration building into MigrationPlatformHost
- Preserve ExtractConfigFileArg() pattern before DI container creation
- Continue using IOptions<T> binding for service consumption
- Test configuration flow with in-memory IConfiguration instances

**Alternatives Considered**: 
- Change configuration precedence order: Rejected due to .NET standard compliance
- Direct configuration file access in commands: Rejected due to testability issues

## Summary

All research questions resolved. Implementation will follow azure-devops-migration-tools proven patterns while maintaining compatibility with current platform architecture. Ready to proceed to Phase 1 design.