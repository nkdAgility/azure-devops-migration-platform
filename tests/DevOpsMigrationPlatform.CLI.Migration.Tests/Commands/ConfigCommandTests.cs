using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Migration.Services;
using DevOpsMigrationPlatform.CLI.Migration.Commands;
using DevOpsMigrationPlatform.CLI.Migration.Settings;
using Spectre.Console.Cli;
using Spectre.Console.Testing;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class ConfigSetCommandTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"prefs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        UserPreferencesService.OverridePreferencesDirectory = _tempDir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        UserPreferencesService.OverridePreferencesDirectory = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithValidKey_SetsPreferenceAndReturnsZero()
    {
        // Arrange
        var command = new ConfigSetCommand();
        var settings = new ConfigSetCommandSettings { Key = "scenario-folder", Value = "/my/path" };
        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "set", null);

        // Act
        var result = await InvokeExecuteAsync(command, context, settings);

        // Assert
        Assert.AreEqual(0, result);
        Assert.AreEqual("/my/path", UserPreferencesService.Get("scenario-folder"));
    }

    [TestMethod]
    public async Task ExecuteAsync_WithUnknownKey_ReturnsOne()
    {
        // Arrange
        var command = new ConfigSetCommand();
        var settings = new ConfigSetCommandSettings { Key = "unknown-key", Value = "value" };
        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "set", null);

        // Act
        var result = await InvokeExecuteAsync(command, context, settings);

        // Assert
        Assert.AreEqual(1, result);
    }

    private static async Task<int> InvokeExecuteAsync(ConfigSetCommand command, CommandContext context, ConfigSetCommandSettings settings)
    {
        // Use reflection to call the protected ExecuteAsync method
        var method = typeof(AsyncCommand<ConfigSetCommandSettings>)
            .GetMethod("ExecuteAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(CommandContext), typeof(ConfigSetCommandSettings), typeof(CancellationToken) }, null);
        var task = (Task<int>)method!.Invoke(command, new object[] { context, settings, CancellationToken.None })!;
        return await task;
    }

    private class FakeRemainingArguments : IRemainingArguments
    {
        public IReadOnlyList<string> Raw => Array.Empty<string>();
        public ILookup<string, string?> Parsed => Array.Empty<KeyValuePair<string, string?>>().ToLookup(x => x.Key, x => x.Value);
    }
}

[TestClass]
public class ConfigGetCommandTests
{
    private string _tempDir = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"prefs-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        UserPreferencesService.OverridePreferencesDirectory = _tempDir;
    }

    [TestCleanup]
    public void Cleanup()
    {
        UserPreferencesService.OverridePreferencesDirectory = null;
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithSetKey_ReturnsZero()
    {
        // Arrange
        UserPreferencesService.Set("scenario-folder", "/test/path");
        var command = new ConfigGetCommand();
        var settings = new ConfigGetCommandSettings { Key = "scenario-folder" };
        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "get", null);

        // Act
        var result = await InvokeExecuteAsync(command, context, settings);

        // Assert
        Assert.AreEqual(0, result);
    }

    [TestMethod]
    public async Task ExecuteAsync_WithUnsetSupportedKey_ReturnsZero()
    {
        // Arrange
        var command = new ConfigGetCommand();
        var settings = new ConfigGetCommandSettings { Key = "scenario-folder" };
        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "get", null);

        // Act
        var result = await InvokeExecuteAsync(command, context, settings);

        // Assert
        Assert.AreEqual(0, result); // Unset but supported → 0 with warning
    }

    [TestMethod]
    public async Task ExecuteAsync_WithUnknownKey_ReturnsOne()
    {
        // Arrange
        var command = new ConfigGetCommand();
        var settings = new ConfigGetCommandSettings { Key = "nonexistent-key" };
        var context = new CommandContext(Array.Empty<string>(), new FakeRemainingArguments(), "get", null);

        // Act
        var result = await InvokeExecuteAsync(command, context, settings);

        // Assert
        Assert.AreEqual(1, result);
    }

    private static async Task<int> InvokeExecuteAsync(ConfigGetCommand command, CommandContext context, ConfigGetCommandSettings settings)
    {
        var method = typeof(AsyncCommand<ConfigGetCommandSettings>)
            .GetMethod("ExecuteAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null, new[] { typeof(CommandContext), typeof(ConfigGetCommandSettings), typeof(CancellationToken) }, null);
        var task = (Task<int>)method!.Invoke(command, new object[] { context, settings, CancellationToken.None })!;
        return await task;
    }

    private class FakeRemainingArguments : IRemainingArguments
    {
        public IReadOnlyList<string> Raw => Array.Empty<string>();
        public ILookup<string, string?> Parsed => Array.Empty<KeyValuePair<string, string?>>().ToLookup(x => x.Key, x => x.Value);
    }
}
