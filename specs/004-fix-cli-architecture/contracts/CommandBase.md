# CommandBase<T> Contract

**Purpose**: Abstract base class providing common command infrastructure and lifecycle management

## Interface Contract

```csharp
namespace DevOpsMigrationPlatform.CLI.Commands;

/// <summary>
/// Abstract base class for all CLI commands providing common infrastructure.
/// Follows azure-devops-migration-tools CommandBase pattern with proper lifecycle management.
/// </summary>
/// <typeparam name="TSettings">Command settings class inheriting from CommandSettings</typeparam>
public abstract class CommandBase<TSettings> : AsyncCommand<TSettings> 
    where TSettings : CommandSettings
{
    /// <summary>
    /// Access to the DI container for service resolution.
    /// Commands should use Services.GetRequiredService<T>() for dependency access.
    /// </summary>
    protected IServiceProvider Services { get; }
    
    /// <summary>
    /// Application lifetime management for proper shutdown signaling.
    /// Commands MUST call Lifetime.StopApplication() when work completes.
    /// </summary>
    protected IHostApplicationLifetime Lifetime { get; }
    
    /// <summary>
    /// Structured logger for command-specific logging.
    /// </summary>
    protected ILogger<CommandBase<TSettings>> Logger { get; }
    
    /// <summary>
    /// Activity source for OpenTelemetry tracing.
    /// </summary>
    protected ActivitySource ActivitySource { get; }

    /// <summary>
    /// Constructor with required dependencies for all commands.
    /// </summary>
    protected CommandBase(
        IServiceProvider services,
        IHostApplicationLifetime lifetime,
        ILogger<CommandBase<TSettings>> logger,
        ActivitySource activitySource);

    /// <summary>
    /// Sealed implementation of Spectre.Console AsyncCommand.ExecuteAsync().
    /// Provides common error handling, telemetry, and lifecycle management.
    /// Commands implement ExecuteInternalAsync() for their specific logic.
    /// </summary>
    public sealed override Task<int> ExecuteAsync(CommandContext context, TSettings settings);
    
    /// <summary>
    /// Abstract method that commands implement for their specific business logic.
    /// Called by ExecuteAsync() with proper error handling and telemetry wrapping.
    /// </summary>
    /// <param name="context">Spectre.Console command context</param>
    /// <param name="settings">Parsed command settings</param>
    /// <returns>Exit code: 0 for success, non-zero for failure</returns>
    protected abstract Task<int> ExecuteInternalAsync(CommandContext context, TSettings settings);
}
```

## Responsibilities

**Common Infrastructure**:
- OpenTelemetry activity creation and management
- Structured logging with command name context
- Exception handling with proper error logging and activity status
- Application lifetime control via `Lifetime.StopApplication()`

**Service Access Pattern**:
- Provide dependency injection access via protected Services property
- Encourage proper service resolution patterns: `Services.GetRequiredService<T>()`
- Maintain service lifetime management (services disposed with container)

**Error Handling**:
- Catch and log all unhandled exceptions from command implementations
- Set appropriate OpenTelemetry activity error status
- Return consistent error exit codes (1 for command failures)

**Lifecycle Management**:
- Ensure application shuts down properly after command completion
- Signal host application lifetime when command work is done
- Provide consistent startup/shutdown logging

## Implementation Contract

**Constructor Requirements**:
- All commands MUST accept the four required dependencies: IServiceProvider, IHostApplicationLifetime, ILogger<T>, ActivitySource
- Commands MAY accept additional specific dependencies but should prefer service location via Services property
- Constructor MUST call base constructor with required parameters

**ExecuteInternalAsync Requirements**:
- Commands MUST implement ExecuteInternalAsync() not ExecuteAsync()
- Return 0 for successful execution, non-zero for failures
- Use Logger for structured logging, Services for dependency access
- Do not call Lifetime.StopApplication() directly (handled by base class)

**Service Access Requirements**:
- Use `Services.GetRequiredService<T>()` for required dependencies
- Use `Services.GetService<T>()` for optional dependencies  
- Do not store service references in fields (get fresh instances when needed)
- Access configuration via IOptions<T> pattern: `Services.GetRequiredService<IOptions<ConfigClass>>().Value`

## Error Handling Contract

**Exception Management**:
- ExecuteAsync() catches all exceptions from ExecuteInternalAsync()
- Exceptions are logged with full context including command name and settings
- OpenTelemetry activity status set to Error with exception message
- Consistent return code (1) for all unhandled exceptions

**Logging Standards**:
- Information level for command start/completion
- Warning level for recoverable issues
- Error level for command failures and exceptions
- Debug level for detailed execution flow

**Activity Tracing**:
- Each command execution creates named activity: `TfsExportCommand`, `LogsCommand`, etc.  
- Activity includes command settings as tags for traceability
- Success/failure status properly recorded
- Activity properly disposed to prevent resource leaks

## Testing Contract

**Testability Requirements**:
- CommandBase<T> itself must be unit testable with mocked dependencies
- Concrete command implementations must be testable via CommandAppTester
- Service resolution must be mockable for isolated testing
- Lifecycle management must work in test environments

**Test Isolation**:
- Each test execution gets independent service provider and lifetime management
- No shared state between command executions
- Configuration provided via test doubles, not real files
- External dependencies provided via mocks, not real implementations