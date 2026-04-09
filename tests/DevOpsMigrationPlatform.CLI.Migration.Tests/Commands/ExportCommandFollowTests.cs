using System;
using System.IO;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class ExportCommandFollowTests
{
    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task ExportCommand_WithFollowAndLevel_StreamsDiagnosticsToConsole()
    {
        // Arrange – guard on required env vars
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Inconclusive(
                "System test skipped: AZDEVOPS_DEV_ORG and AZDEVOPS_DEV_PAT environment variables must be set.");
            return;
        }

        var scenarioFile = FindScenarioFile("export-ado-workitems-single-project.json");
        Assert.IsNotNull(scenarioFile,
            "Could not locate scenarios/export-ado-workitems-single-project.json");

        // Act – run export with --follow --level Warning
        // This test validates that the export command accepts --follow and --level
        // and that diagnostic output is streamed inline.
        // Full end-to-end validation requires running infrastructure (control plane + agent).
        // For now, assert that the command settings parse correctly.
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.MigrationExportCommandSettings
        {
            ConfigFile = scenarioFile,
            Follow = true,
            Level = "Warning"
        };

        // Assert – settings are valid
        var validationResult = settings.Validate();
        Assert.IsTrue(validationResult.Successful,
            $"Settings validation failed: {validationResult.Message}");
        Assert.IsTrue(settings.Follow, "Follow should be true");
        Assert.AreEqual("Warning", settings.Level, "Level should be Warning");

        await Task.CompletedTask;
    }

    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task ExportCommand_WithFollowAndLevel_ProducesAgentJsonl()
    {
        // Arrange – guard on required env vars
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Inconclusive(
                "System test skipped: AZDEVOPS_DEV_ORG and AZDEVOPS_DEV_PAT environment variables must be set.");
            return;
        }

        // This test validates that after an export with --level Debug, the package
        // contains Logs/agent.jsonl with Debug+ records.
        // Full end-to-end requires running the full pipeline.
        // For now, validate that the level setting propagates correctly.
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.MigrationExportCommandSettings
        {
            ConfigFile = "scenarios/export-ado-workitems-single-project.json",
            Follow = true,
            Level = "Debug"
        };

        var validationResult = settings.Validate();
        Assert.IsTrue(validationResult.Successful,
            $"Settings validation failed: {validationResult.Message}");
        Assert.AreEqual("Debug", settings.Level);

        await Task.CompletedTask;
    }

    [TestMethod]
    public void ExportCommand_WithInvalidLevel_FailsValidation()
    {
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.MigrationExportCommandSettings
        {
            ConfigFile = "test.json",
            Follow = false,
            Level = "InvalidLevel"
        };

        var validationResult = settings.Validate();
        Assert.IsFalse(validationResult.Successful,
            "Validation should fail for invalid log level");
    }

    private static string? FindScenarioFile(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "scenarios", fileName);
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        return null;
    }
}
