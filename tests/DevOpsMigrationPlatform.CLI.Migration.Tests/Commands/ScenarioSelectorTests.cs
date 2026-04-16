using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Migration.Services;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class ScenarioSelectorTests
{
    private string _tempDir = null!;
    private string _originalEnv = null!;
    private string _originalCwd = null!;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"scenario-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _originalEnv = Environment.GetEnvironmentVariable("MigrationPlatform_Scenario_Folder") ?? "";
        _originalCwd = Directory.GetCurrentDirectory();
        UserPreferencesService.OverridePreferencesDirectory = Path.Combine(_tempDir, "prefs");
        Environment.SetEnvironmentVariable("MigrationPlatform_Scenario_Folder", null);
    }

    [TestCleanup]
    public void Cleanup()
    {
        Directory.SetCurrentDirectory(_originalCwd);
        UserPreferencesService.OverridePreferencesDirectory = null;
        Environment.SetEnvironmentVariable("MigrationPlatform_Scenario_Folder", 
            string.IsNullOrEmpty(_originalEnv) ? null : _originalEnv);
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [TestMethod]
    public void PromptForConfigFile_WithNoSources_ReturnsNull()
    {
        // Arrange — empty temp dir as cwd, no scenarios subfolder
        var emptyDir = Path.Combine(_tempDir, "empty");
        Directory.CreateDirectory(emptyDir);
        Directory.SetCurrentDirectory(emptyDir);

        var console = new Spectre.Console.Testing.TestConsole();

        // Act
        var result = ScenarioSelector.PromptForConfigFile(console);

        // Assert
        Assert.IsNull(result);
    }

    [TestMethod]
    public void PromptForConfigFile_WithSingleFileInEnvFolder_ReturnsAutoSelected()
    {
        // Arrange
        var envFolder = Path.Combine(_tempDir, "env-scenarios");
        Directory.CreateDirectory(envFolder);
        File.WriteAllText(Path.Combine(envFolder, "single.json"), "{}");
        Environment.SetEnvironmentVariable("MigrationPlatform_Scenario_Folder", envFolder);

        // Point cwd away so no other fallbacks trigger
        var emptyDir = Path.Combine(_tempDir, "empty-cwd");
        Directory.CreateDirectory(emptyDir);
        Directory.SetCurrentDirectory(emptyDir);

        var console = new Spectre.Console.Testing.TestConsole();

        // Act
        var result = ScenarioSelector.PromptForConfigFile(console);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.EndsWith("single.json"));
    }

    [TestMethod]
    public void PromptForConfigFile_WithSingleFileInPreferencesFolder_ReturnsAutoSelected()
    {
        // Arrange
        var prefFolder = Path.Combine(_tempDir, "pref-scenarios");
        Directory.CreateDirectory(prefFolder);
        File.WriteAllText(Path.Combine(prefFolder, "prefconfig.json"), "{}");
        UserPreferencesService.Set("scenario-folder", prefFolder);

        // Point cwd away so no other fallbacks trigger
        var emptyDir = Path.Combine(_tempDir, "empty-cwd2");
        Directory.CreateDirectory(emptyDir);
        Directory.SetCurrentDirectory(emptyDir);

        var console = new Spectre.Console.Testing.TestConsole();

        // Act
        var result = ScenarioSelector.PromptForConfigFile(console);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.EndsWith("prefconfig.json"));
    }

    [TestMethod]
    public void PromptForConfigFile_WithSingleFileInScenariosSubfolder_ReturnsAutoSelected()
    {
        // Arrange
        var cwdDir = Path.Combine(_tempDir, "project");
        var scenariosDir = Path.Combine(cwdDir, "scenarios");
        Directory.CreateDirectory(scenariosDir);
        File.WriteAllText(Path.Combine(scenariosDir, "scenario.json"), "{}");
        Directory.SetCurrentDirectory(cwdDir);

        var console = new Spectre.Console.Testing.TestConsole();

        // Act
        var result = ScenarioSelector.PromptForConfigFile(console);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.EndsWith("scenario.json"));
    }

    [TestMethod]
    public void PromptForConfigFile_WithNonexistentEnvFolder_FallsThrough()
    {
        // Arrange
        Environment.SetEnvironmentVariable("MigrationPlatform_Scenario_Folder", "/nonexistent/path");

        var emptyDir = Path.Combine(_tempDir, "empty-cwd3");
        Directory.CreateDirectory(emptyDir);
        Directory.SetCurrentDirectory(emptyDir);

        var console = new Spectre.Console.Testing.TestConsole();

        // Act
        var result = ScenarioSelector.PromptForConfigFile(console);

        // Assert
        Assert.IsNull(result); // Falls through all sources to null
    }

    [TestMethod]
    public void PromptForConfigFile_EnvVarTakesPriorityOverPreferences()
    {
        // Arrange
        var envFolder = Path.Combine(_tempDir, "env");
        Directory.CreateDirectory(envFolder);
        File.WriteAllText(Path.Combine(envFolder, "env.json"), "{}");
        Environment.SetEnvironmentVariable("MigrationPlatform_Scenario_Folder", envFolder);

        var prefFolder = Path.Combine(_tempDir, "pref");
        Directory.CreateDirectory(prefFolder);
        File.WriteAllText(Path.Combine(prefFolder, "pref.json"), "{}");
        UserPreferencesService.Set("scenario-folder", prefFolder);

        var emptyDir = Path.Combine(_tempDir, "empty-cwd4");
        Directory.CreateDirectory(emptyDir);
        Directory.SetCurrentDirectory(emptyDir);

        var console = new Spectre.Console.Testing.TestConsole();

        // Act
        var result = ScenarioSelector.PromptForConfigFile(console);

        // Assert
        Assert.IsNotNull(result);
        Assert.IsTrue(result.EndsWith("env.json"), $"Expected env.json but got: {result}");
    }
}
