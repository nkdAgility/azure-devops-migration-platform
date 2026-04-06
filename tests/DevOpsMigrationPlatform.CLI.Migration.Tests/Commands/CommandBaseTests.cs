using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class CommandBaseTests
{
    [TestMethod]
    public void Constructor_WithValidDependencies_CanAccessServices()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService, TestServiceImplementation>();
        var serviceProvider = services.BuildServiceProvider();
        
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        var activitySource = new ActivitySource("test");

        // Act
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);

        // Assert
        var testService = command.GetRequiredService<ITestService>();
        Assert.IsNotNull(testService);
        Assert.IsInstanceOfType<TestServiceImplementation>(testService);
        
        var optionalService = command.GetService<ITestService>();
        Assert.IsNotNull(optionalService);
        
        // Cleanup
        activitySource.Dispose();
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullServiceProvider_ThrowsArgumentNullException()
    {
        // Arrange
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");

        // Act
        var command = new TestCommand(null!, mockLifetime.Object, mockLogger.Object, activitySource);
    }

    [TestMethod]
    [ExpectedException(typeof(ArgumentNullException))]
    public void Constructor_WithNullLifetime_ThrowsArgumentNullException()
    {
        // Arrange
        var serviceProvider = MockServiceProvider.Create(InMemoryTestConfiguration.CreateDefault());
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");

        // Act
        var command = new TestCommand(serviceProvider, null!, mockLogger.Object, activitySource);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithSuccessfulCommand_ReturnsZeroAndCallsStopApplication()
    {
        // Arrange
        var serviceProvider = MockServiceProvider.Create(InMemoryTestConfiguration.CreateDefault());
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");
        
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);
        command.SetReturnValue(0); // Success

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        var result = await command.ExecuteAsyncPublic(context, settings);

        // Assert
        Assert.AreEqual(0, result);
        mockLifetime.Verify(x => x.StopApplication(), Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithFailedCommand_ReturnsErrorCodeAndCallsStopApplication()
    {
        // Arrange
        var serviceProvider = MockServiceProvider.Create(InMemoryTestConfiguration.CreateDefault());
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");
        
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);
        command.SetReturnValue(1); // Error

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        var result = await command.ExecuteAsyncPublic(context, settings);

        // Assert
        Assert.AreEqual(1, result);
        mockLifetime.Verify(x => x.StopApplication(), Times.Once);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithExceptionInCommand_ReturnsErrorCodeAndLogsException()
    {
        // Arrange
        var serviceProvider = MockServiceProvider.Create(InMemoryTestConfiguration.CreateDefault());
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>(MockBehavior.Strict);
        using var activitySource = new ActivitySource("test");
        
        // Setup logger to expect error logging
        mockLogger.Setup(x => x.Log(
            LogLevel.Information,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            null,
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()));
            
        mockLogger.Setup(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()));
        
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);
        command.SetException(new InvalidOperationException("Test error"));

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        var result = await command.ExecuteAsyncPublic(context, settings);

        // Assert
        Assert.AreEqual(1, result); // Error exit code
        mockLifetime.Verify(x => x.StopApplication(), Times.Once);
        
        // Verify error was logged
        mockLogger.Verify(x => x.Log(
            LogLevel.Error,
            It.IsAny<EventId>(),
            It.IsAny<It.IsAnyType>(),
            It.IsAny<Exception>(),
            It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
    }

    [TestMethod]
    public void GetRequiredService_WithRegisteredService_ReturnsService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(new TestServiceImplementation());
        var serviceProvider = services.BuildServiceProvider();
        
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");
        
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);

        // Act
        var service = command.GetRequiredService<ITestService>();

        // Assert
        Assert.IsNotNull(service);
        Assert.IsInstanceOfType(service, typeof(TestServiceImplementation));
    }

    [TestMethod]
    [ExpectedException(typeof(InvalidOperationException))]
    public void GetRequiredService_WithUnregisteredService_ThrowsInvalidOperationException()
    {
        // Arrange
        var serviceProvider = MockServiceProvider.Create(InMemoryTestConfiguration.CreateDefault());
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");
        
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);

        // Act
        var service = command.GetRequiredService<IUnregisteredService>();
    }

    [TestMethod]
    public void GetService_WithRegisteredService_ReturnsService()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddSingleton<ITestService>(new TestServiceImplementation());
        var serviceProvider = services.BuildServiceProvider();
        
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");
        
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);

        // Act
        var service = command.GetService<ITestService>();

        // Assert
        Assert.IsNotNull(service);
        Assert.IsInstanceOfType(service, typeof(TestServiceImplementation));
    }

    [TestMethod]
    public void GetService_WithUnregisteredService_ReturnsNull()
    {
        // Arrange
        var serviceProvider = MockServiceProvider.Create(InMemoryTestConfiguration.CreateDefault());
        var mockLifetime = MockServiceProvider.CreateMockLifetime();
        var mockLogger = new Mock<ILogger<TestCommand>>();
        using var activitySource = new ActivitySource("test");
        
        var command = new TestCommand(serviceProvider, mockLifetime.Object, mockLogger.Object, activitySource);

        // Act
        var service = command.GetService<IUnregisteredService>();

        // Assert
        Assert.IsNull(service);
    }

    // Test implementations
    public class TestCommand : CommandBase<TestSettings>
    {
        private int _returnValue = 0;
        private Exception? _exceptionToThrow;

        public TestCommand(IServiceProvider serviceProvider, IHostApplicationLifetime lifetime, 
            ILogger<TestCommand> logger, ActivitySource activitySource)
            : base(serviceProvider, lifetime, logger, activitySource)
        {
        }

        public void SetReturnValue(int value) => _returnValue = value;
        public void SetException(Exception exception) => _exceptionToThrow = exception;

        protected override Task<int> ExecuteInternalAsync(CommandContext context, TestSettings settings, CancellationToken cancellationToken = default)
        {
            if (_exceptionToThrow != null)
                throw _exceptionToThrow;
            return Task.FromResult(_returnValue);
        }
        
        // Expose protected methods for testing
        public new TService GetRequiredService<TService>() where TService : notnull
            => base.GetRequiredService<TService>();
            
        public new TService? GetService<TService>() where TService : class
            => base.GetService<TService>();
            
        // Public wrapper for protected ExecuteAsync to enable testing
        public Task<int> ExecuteAsyncPublic(CommandContext context, TestSettings settings, CancellationToken cancellationToken = default)
            => base.ExecuteAsync(context, settings, cancellationToken);
    }

    public class TestSettings : Spectre.Console.Cli.CommandSettings
    {
        [CommandOption("--test")]
        [System.ComponentModel.Description("Test option")]
        public string? TestOption { get; set; }
    }

    // Test services
    public interface ITestService { }
    
    public class TestServiceImplementation : ITestService { }
    
    public interface IUnregisteredService { }
    
    // Fake implementation for testing
    private class FakeRemainingArguments : IRemainingArguments
    {
        public IReadOnlyList<string> Raw => Array.Empty<string>();
        public ILookup<string, string?> Parsed => Array.Empty<KeyValuePair<string, string?>>().ToLookup(x => x.Key, x => x.Value);
    }
}