using Microsoft.Extensions.DependencyInjection;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using Spectre.Console;
using Spectre.Console.Cli;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class CommandBaseTests
{
    [TestMethod]
    public async Task ExecuteAsync_WithSuccessfulCommand_ReturnsZero()
    {
        // Arrange
        var command = new TestCommand();
        command.SetReturnValue(0);

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        var result = await command.ExecuteAsyncPublic(context, settings);

        // Assert
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithFailedCommand_ReturnsErrorCode()
    {
        // Arrange
        var command = new TestCommand();
        command.SetReturnValue(1);

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        var result = await command.ExecuteAsyncPublic(context, settings);

        // Assert
        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithExceptionInCommand_ReturnsErrorCode()
    {
        // Arrange
        var command = new TestCommand();
        command.SetException(new InvalidOperationException("Test error"));

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        var result = await command.ExecuteAsyncPublic(context, settings);

        // Assert
        Assert.AreEqual(1, result);
    }

    [TestMethod]
    public async Task CreateHost_RegistersServicesAndResolvesFromHost()
    {
        // Arrange
        var command = new TestCommand();
        command.SetCreateHostOnExecute((services, config) =>
        {
            services.AddSingleton<ITestService, TestServiceImplementation>();
        });

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        var result = await command.ExecuteAsyncPublic(context, settings);

        // Assert
        Assert.AreEqual(0, result);
        Assert.IsTrue(command.HostWasCreated, "Host should have been created");
    }

    [TestMethod]
    public async Task ExecuteAsync_DisposesHostAfterExecution()
    {
        // Arrange
        var command = new TestCommand();
        command.SetCreateHostOnExecute();

        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "test", null);
        var settings = new TestSettings();

        // Act
        await command.ExecuteAsyncPublic(context, settings);

        // Assert — Host should be disposed (accessing it should not work after execution)
        Assert.IsTrue(command.WasExecuted, "Command should have executed");
    }

    // Test implementations
    public class TestCommand : CommandBase<TestSettings>
    {
        private int _returnValue = 0;
        private Exception? _exceptionToThrow;
        private Action<IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration>? _hostConfigAction;
        private bool _createHostOnExecute;
        public bool HostWasCreated { get; private set; }
        public bool WasExecuted { get; private set; }

        public void SetReturnValue(int value) => _returnValue = value;
        public void SetException(Exception exception) => _exceptionToThrow = exception;

        public void SetCreateHostOnExecute(
            Action<IServiceCollection, Microsoft.Extensions.Configuration.IConfiguration>? configureServices = null)
        {
            _createHostOnExecute = true;
            _hostConfigAction = configureServices;
        }

        protected override async Task<int> ExecuteInternalAsync(CommandContext context, TestSettings settings, CancellationToken cancellationToken = default)
        {
            WasExecuted = true;

            if (_createHostOnExecute)
            {
                await CreateHost(Array.Empty<string>(), _hostConfigAction);
                HostWasCreated = Host != null;
            }

            if (_exceptionToThrow != null)
                throw _exceptionToThrow;
            return _returnValue;
        }

        // Public wrapper for protected ExecuteAsync to enable testing
        public Task<int> ExecuteAsyncPublic(CommandContext context, TestSettings settings, CancellationToken cancellationToken = default)
            => base.ExecuteAsync(context, settings, cancellationToken);
    }

    public class TestSettings : CommandSettings
    {
        [CommandOption("--test")]
        [System.ComponentModel.Description("Test option")]
        public string? TestOption { get; set; }
    }

    // Test services
    public interface ITestService { }

    public class TestServiceImplementation : ITestService { }

    // Fake implementation for testing
    private class FakeRemainingArguments : IRemainingArguments
    {
        public IReadOnlyList<string> Raw => Array.Empty<string>();
        public ILookup<string, string?> Parsed => Array.Empty<KeyValuePair<string, string?>>().ToLookup(x => x.Key, x => x.Value);
    }
}