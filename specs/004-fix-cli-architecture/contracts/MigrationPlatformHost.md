# MigrationPlatformHost Contract

**Purpose**: Central host builder factory for CLI application infrastructure setup

## Interface Contract

```csharp
namespace DevOpsMigrationPlatform.CLI;

/// <summary>
/// Central host builder factory managing DI container setup and infrastructure configuration.
/// Follows the azure-devops-migration-tools MigrationToolHost pattern.
/// </summary>
public static class MigrationPlatformHost
{
    /// <summary>
    /// Creates a configured IHostBuilder with all services registered and Spectre.Console setup.
    /// Extracts --config parameter before DI container creation to ensure configuration 
    /// is available during service registration.
    /// </summary>
    /// <param name="args">Command line arguments including --config parameter</param>
    /// <returns>Configured IHostBuilder ready for RunConsoleAsync()</returns>
    public static IHostBuilder CreateDefaultBuilder(string[] args);
}
```

## Responsibilities

**Configuration Management**:
- Extract --config and -c parameters before Spectre.Console processing
- Build layered IConfiguration: appsettings.json → config file → environment variables
- Ensure configuration is available during service registration phase

**Service Registration**:
- All existing service registrations from Program.cs
- Telemetry and OpenTelemetry configuration  
- ControlPlaneClient and related services
- Inventory services and options binding

**Spectre.Console Integration**:
- CommandApp configuration with all existing commands
- Command branch setup (discovery, etc.)
- TypeRegistrar bridge for DI integration

**Infrastructure Setup**:
- Logging configuration with appropriate levels
- ActivitySource registration for telemetry
- Error handling and application lifetime management

## Configuration Contract

**Input**: Command line arguments array
**Configuration Precedence** (highest to lowest):
1. Command line arguments (already extracted)
2. Environment variables  
3. User-specified config file (--config parameter)
4. appsettings.json (base defaults)

**Configuration Extraction**:
```csharp
private static (string configFile, string[] remainingArgs) ExtractConfigFileArg(string[] args)
{
    // Extract --config/-c before Spectre.Console sees them
    // Return config file path and remaining args for Spectre.Console
}
```

## Service Registration Contract

**Required Service Categories**:
- IConfiguration (singleton, built from layered sources)
- IOptions<T> bindings for all existing options classes
- Logging with console output and appropriate minimum levels
- OpenTelemetry with ActivitySource and conditional Azure Monitor export
- ControlPlaneClient with base URL configuration
- Inventory services (IInventoryService, TfsInventoryProcessAdapter, etc.)

**Service Lifetime Management**:
- Singleton for stateless services and configuration
- Scoped for request-like operations (as appropriate)
- Transient for stateful operations

## Error Handling Contract

**Configuration Errors**:
- Invalid config file paths should fail gracefully with clear error messages
- Missing required configuration sections should be handled by IOptions validation
- JSON parsing errors should provide actionable error information

**Service Registration Errors**:
- Duplicate service registrations should be avoided
- Circular dependencies should be detected during container build
- Missing required services should fail fast with clear dependency information

## Compatibility Requirements

**Backward Compatibility**:
- All existing command-line interfaces must work unchanged
- All existing configuration file formats must be supported
- All existing environment variable patterns must be preserved

**Forward Compatibility**:
- Adding new commands should require only host builder configuration updates
- Adding new services should follow standard IServiceCollection extension patterns
- Configuration schema changes should be additive where possible