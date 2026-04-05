# CLI Testing Strategy Contract

**Purpose**: Comprehensive testing approach for CLI command validation and architecture verification

## Testing Framework Contract

**Required Testing Stack**:
- MSTest as test runner (per testing-standards.md guardrails)
- Spectre.Console.Cli.Testing for CLI command testing
- In-memory test doubles for all external dependencies
- Moq for mocking interfaces with MockBehavior.Strict

**Test Project Structure**:
```text
tests/DevOpsMigrationPlatform.CLI.Migration.Tests/
├── Commands/
│   ├── TfsExportCommandTests.cs        # CommandAppTester validation tests  
│   ├── LogsCommandTests.cs             # CommandAppTester validation tests
│   ├── InventoryCommandTests.cs        # CommandAppTester validation tests
│   └── CommandBaseTests.cs             # Unit tests for base functionality
├── MigrationPlatformHostTests.cs       # Host builder configuration tests
└── TestUtilities/
    ├── InMemoryTestConfiguration.cs    # Configuration test doubles
    └── MockServiceProvider.cs          # DI container test doubles
```

## Command Testing Contract

### CommandAppTester Integration

**Required Test Categories**:
1. **Valid Parameter Tests**: Verify commands execute successfully with proper inputs
2. **Invalid Parameter Tests**: Verify appropriate error messages and non-zero exit codes  
3. **Help Text Tests**: Verify --help displays comprehensive information without errors
4. **Configuration Flow Tests**: Verify config values reach internal services

**CommandAppTester Pattern**:
```csharp
[TestMethod]
public void CommandName_WithValidInputs_ReturnsSuccessCode()
{
    // Arrange
    var app = new CommandAppTester();
    app.SetDefaultCommand<CommandUnderTest>();
    
    // Act
    var result = app.Run("param1", "value1", "--param2", "value2");
    
    // Assert
    Assert.AreEqual(0, result.ExitCode);
    Assert.Contains("expected output", result.Output);
}
```

### Test Isolation Strategy

**In-Memory Test Doubles**:
- Configuration via `ConfigurationBuilder` with in-memory collections
- Service mocking via test-specific IServiceProvider implementations
- No external file system, network, or database dependencies
- Clean test environment for each test method execution

**Configuration Test Pattern**:
```csharp
private static IConfiguration CreateTestConfiguration(params (string key, string value)[] values)
{
    var configData = values.ToDictionary(x => x.key, x => x.value);
    return new ConfigurationBuilder()
        .AddInMemoryCollection(configData)
        .Build();
}
```

**Service Provider Test Pattern**:
```csharp
private static IServiceProvider CreateTestServiceProvider(IConfiguration config)
{
    var services = new ServiceCollection();
    services.AddSingleton(config);
    services.AddSingleton<IOptions<TestOptions>>(sp => 
        Options.Create(config.Get<TestOptions>()));
    // Add mock services as needed
    return services.BuildServiceProvider();
}
```

## Unit Testing Contract

### CommandBase<T> Testing

**Required Test Coverage**:
- Constructor dependency injection
- ExecuteAsync() error handling and telemetry
- Lifecycle management (Lifetime.StopApplication() calls)
- Activity creation and disposal
- Exception logging and exit code handling

**CommandBase Test Pattern**:
```csharp
[TestMethod]
public void CommandBase_ExecuteAsync_HandlesExceptionsCorrectly()
{
    // Arrange
    var mockLifetime = new Mock<IHostApplicationLifetime>();
    var mockLogger = new Mock<ILogger<TestCommand>>();
    var mockActivitySource = new Mock<ActivitySource>();
    var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, mockActivitySource.Object);

    // Act
    var result = command.ExecuteAsync(context, settings);

    // Assert
    Assert.AreEqual(1, result.Result); // Error exit code
    mockLifetime.Verify(x => x.StopApplication(), Times.Once);
    // Verify logging calls
}
```

### Host Builder Testing

**Required Test Coverage**:
- Service registration verification
- Configuration binding validation
- Spectre.Console command registration
- Error scenarios (missing config, invalid JSON, etc.)

**Host Builder Test Pattern**:
```csharp
[TestMethod]
public void MigrationPlatformHost_CreateDefaultBuilder_RegistersAllRequiredServices()
{
    // Arrange
    var args = new[] { "--config", "test.json" };
    
    // Act  
    var hostBuilder = MigrationPlatformHost.CreateDefaultBuilder(args);
    using var host = hostBuilder.Build();
    
    // Assert
    Assert.IsNotNull(host.Services.GetService<IInventoryService>());
    Assert.IsNotNull(host.Services.GetService<IOptions<MigrationOptions>>());
    Assert.IsNotNull(host.Services.GetService<ActivitySource>());
    // Verify all expected services are registered
}
```

## Integration Testing Contract

### End-to-End Command Flow

**Integration Test Requirements**:
- Complete command execution from command line parsing to service calls
- Configuration flow validation from --config parameter to service consumption
- Error propagation from services to command exit codes
- Telemetry and logging integration verification

**Integration Test Pattern**:
```csharp
[TestClass]
public class CommandIntegrationTests
{
    [TestMethod]
    public async Task InventoryCommand_WithRealConfiguration_ExecutesSuccessfully()
    {
        // Arrange: Create test configuration file
        var testConfig = CreateTestConfigurationFile();
        var hostBuilder = MigrationPlatformHost.CreateDefaultBuilder(new[] { "--config", testConfig });
        
        // Act: Execute command through host
        using var host = hostBuilder.Build();
        var commandApp = host.Services.GetRequiredService<CommandApp>();
        var result = await commandApp.RunAsync(new[] { "discovery", "inventory", "--all-projects" });
        
        // Assert: Verify successful execution and proper service interactions
        Assert.AreEqual(0, result);
    }
}
```

## Test Data Management Contract

### Configuration Test Data

**Test Configuration Sources**:
- In-memory IConfiguration instances for unit tests
- Temporary JSON files for integration tests (cleaned up automatically)
- Environment variable simulation via ConfigurationBuilder
- Invalid configuration scenarios (malformed JSON, missing sections, etc.)

### Service Mock Management

**Mocking Strategy**:
- Mock external HTTP clients (ControlPlaneClient, etc.)
- Mock file system operations (when not testing via IArtefactStore)
- Mock authorization and authentication services
- Use real implementations for internal business logic (prefer testing real behavior)

**Mock Verification**:
- Verify service method calls with expected parameters
- Verify service call ordering where relevant
- Verify error handling paths in service interactions
- Use MockBehavior.Strict to catch unexpected service calls

## Continuous Integration Contract

### Test Execution Requirements

**CI Pipeline Integration**:
- All tests must pass in CI environment without external dependencies
- Test execution must be deterministic (no flaky tests)
- Tests must complete within reasonable time limits (< 2 minutes total)
- Test results must be reported in standard formats for CI consume

**Coverage Requirements**:
- All commands must have CLI validation tests via CommandAppTester
- All public methods in CommandBase<T> must have unit test coverage
- All service registrations in host builder must be verified
- All error scenarios must have test coverage

### Test Environment Isolation

**CI Environment Requirements**:
- No network dependencies (all external services mocked)
- No file system dependencies beyond temporary test files
- No environment-specific configuration (tests provide own config)
- No shared state between test executions (parallel test execution safe)