# Quick Start: CLI Architecture Implementation Guide

**Feature**: Fix CLI Architecture and Add Command Testing  
**Date**: April 5, 2026

## Development Environment Setup

### Prerequisites

- .NET 10 SDK installed
- Visual Studio 2022 or VS Code with C# extension
- PowerShell 7+ for build scripts
- Git for version control

### Required NuGet Packages

Add to `DevOpsMigrationPlatform.CLI.Migration.csproj`:
```xml
<!-- Already present -->
<PackageReference Include="Spectre.Console.Cli" Version="0.49.1" />
<PackageReference Include="Microsoft.Extensions.Hosting" Version="10.*" />

<!-- Already present for testing -->
<PackageReference Include="MSTest.TestFramework" Version="3.*" />
<PackageReference Include="MSTest.TestAdapter" Version="3.*" />

<!-- NEW: Add for CLI testing -->
<PackageReference Include="Spectre.Console.Cli.Testing" />
```

## Implementation Order

### Phase 1: Create MigrationPlatformHost

1. **Extract configuration logic** from existing Program.cs
2. **Create MigrationPlatformHost.cs** with CreateDefaultBuilder() method
3. **Move all service registrations** to host builder
4. **Preserve configuration layering**: appsettings → config file → environment variables

```csharp
// New file: src/DevOpsMigrationPlatform.CLI.Migration/MigrationPlatformHost.cs
public static class MigrationPlatformHost
{
    public static IHostBuilder CreateDefaultBuilder(string[] args)
    {
        // Extract --config before Spectre.Console sees it
        var (configFile, spectreArgs) = ExtractConfigFileArg(args);
        
        // Build layered configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile(configFile, optional: true)
            .AddEnvironmentVariables()
            .Build();
            
        // Create host builder with all service registrations
        var hostBuilder = Microsoft.Extensions.Hosting.Host.CreateDefaultBuilder(spectreArgs);
        
        // Configure services (move from Program.cs)
        hostBuilder.ConfigureServices((context, services) => {
            // All existing service registrations
        });
        
        // Configure Spectre.Console
        hostBuilder.UseSpectreConsole(config => {
            // All existing command configurations
        });
        
        return hostBuilder;
    }
}
```

### Phase 2: Create CommandBase<T>

1. **Create CommandBase.cs** in Commands folder
2. **Implement base class** with IServiceProvider and IHostApplicationLifetime
3. **Add common infrastructure**: logging, telemetry, error handling

```csharp
// New file: src/DevOpsMigrationPlatform.CLI.Migration/Commands/CommandBase.cs
public abstract class CommandBase<TSettings> : AsyncCommand<TSettings> 
    where TSettings : CommandSettings
{
    protected IServiceProvider Services { get; }
    protected IHostApplicationLifetime Lifetime { get; }
    protected ILogger<CommandBase<TSettings>> Logger { get; }
    protected ActivitySource ActivitySource { get; }
    
    public CommandBase(
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        ILogger<CommandBase<TSettings>> logger,
        ActivitySource activitySource)
    {
        Services = services;
        Lifetime = lifetime;
        Logger = logger;
        ActivitySource = activitySource;
    }
    
    public sealed override async Task<int> ExecuteAsync(CommandContext context, TSettings settings)
    {
        using var activity = ActivitySource.StartActivity(GetType().Name);
        try
        {
            Logger.LogInformation("Starting {CommandName}", GetType().Name);
            return await ExecuteInternalAsync(context, settings);
        }
        catch (Exception ex)
        {
            Logger.LogError(ex, "Command {CommandName} failed", GetType().Name);
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return 1;
        }
        finally
        {
            Lifetime.StopApplication();
        }
    }
    
    protected abstract Task<int> ExecuteInternalAsync(CommandContext context, TSettings settings);
}
```

### Phase 3: Refactor Existing Commands

1. **Update each command** to inherit from CommandBase<T>
2. **Add constructor injection** for dependencies
3. **Move logic** to ExecuteInternalAsync() override

```csharp
// Update: TfsExportCommand.cs, LogsCommand.cs, InventoryCommand.cs
public sealed class TfsExportCommand : CommandBase<TfsExportCommand.Settings>
{
    public TfsExportCommand(
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        ILogger<TfsExportCommand> logger,
        ActivitySource activitySource)
        : base(services, lifetime, logger, activitySource)
    {
    }
    
    protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings)
    {
        // Existing command logic
        // Access services via Services.GetRequiredService<T>()
        return 0;
    }
}
```

### Phase 4: Simplify Program.cs

1. **Replace all logic** with minimal host builder call
2. **Remove service registrations** (moved to MigrationPlatformHost)
3. **Remove configuration setup** (moved to MigrationPlatformHost)

```csharp
// Updated: src/DevOpsMigrationPlatform.CLI.Migration/Program.cs
internal class Program
{
    static async Task<int> Main(string[] args)
    {
        var hostBuilder = MigrationPlatformHost.CreateDefaultBuilder(args);
        return await hostBuilder.RunConsoleAsync();
    }
}
```

### Phase 5: Add Testing Infrastructure 

1. **Create test project** DevOpsMigrationPlatform.CLI.Migration.Tests
2. **Add CommandAppTester tests** for each command
3. **Create test utilities** for in-memory configuration and mocking

```csharp
// New: tests/DevOpsMigrationPlatform.CLI.Migration.Tests/Commands/TfsExportCommandTests.cs
[TestClass]
public class TfsExportCommandTests
{
    [TestMethod]
    public void TfsExportCommand_WithValidParams_ReturnsSuccessCode()
    {
        // Arrange
        var app = new CommandAppTester();
        app.SetDefaultCommand<TfsExportCommand>();
        
        // Act
        var result = app.Run("--collection", "http://test", "--project", "Test", "--output", "./test");
        
        // Assert
        Assert.AreEqual(0, result.ExitCode);
        Assert.IsTrue(result.Output.Contains("✅"));
    }
    
    [TestMethod] 
    public void TfsExportCommand_WithMissingParams_ReturnsErrorCode()
    {
        // Similar pattern for error scenarios
    }
}
```

## Testing Strategy

### Unit Tests
- **CommandBase<T>**: Test common functionality (logging, lifecycle, error handling)
- **MigrationPlatformHost**: Test service registration and configuration setup
- **Individual Commands**: Test via CommandAppTester with various parameter scenarios

### Integration Tests
- **End-to-end command execution**: Verify complete flow from command line to service calls
- **Configuration binding**: Ensure config values reach appropriate services  
- **Error scenarios**: Test invalid configs, missing dependencies, etc.

### Test Data Management
- **In-memory configuration**: Use ConfigurationBuilder with in-memory providers
- **Service mocking**: Mock external dependencies via IServiceProvider test doubles
- **Isolated test environment**: Each test gets clean CommandAppTester instance

## Common Patterns

### Service Access in Commands
```csharp
protected override async Task<int> ExecuteInternalAsync(CommandContext context, Settings settings)
{
    var service = Services.GetRequiredService<ISomeService>();
    var options = Services.GetRequiredService<IOptions<SomeOptions>>();
    
    // Use services for command logic
    return 0;
}
```

### Test Configuration Setup
```csharp
private static IConfiguration CreateTestConfiguration(params (string key, string value)[] values)
{
    var builder = new ConfigurationBuilder();
    foreach (var (key, value) in values)
    {
        builder.AddInMemoryCollection(new[] { new KeyValuePair<string, string>(key, value) });
    }
    return builder.Build();
}
```

## Validation Checklist

- [x] Program.cs contains < 50 lines (minimal bootstrapping only)  
- [x] All commands inherit from CommandBase<T>
- [x] All service access via constructor injection
- [x] Configuration flows through IOptions<T> pattern  
- [x] All commands have CommandAppTester test coverage
- [x] No external dependencies in test environment
- [x] Existing command-line interfaces remain unchanged