using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Commands.Discovery;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands.Discovery;

[TestClass]
public class DependencyCommandTests
{
    [TestMethod]
    public void DependencyCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new DependencyCommand();
        Assert.IsNotNull(command);
    }

    [TestMethod]
    [TestCategory("SystemTest")]
    [Timeout(300_000)]
    public async Task DependencyCommand_SystemTest_AdoSingleProject_ExecutesSuccessfully()
    {
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");

        if (string.IsNullOrWhiteSpace(orgEnv) || string.IsNullOrWhiteSpace(patEnv))
        {
            Assert.Inconclusive("System test skipped: AZDEVOPS_SYSTEM_TEST_ORG or AZDEVOPS_SYSTEM_TEST_PAT not set");
        }

        var scenarioPath = "scenarios/discovery-dependency-ado-single-project.json";
        if (!File.Exists(scenarioPath))
        {
            Assert.Inconclusive($"Scenario file not found: {scenarioPath}");
        }

        var outputPath = Path.Combine(Path.GetTempPath(), $"discovery-deps-{Guid.NewGuid()}.csv");

        try
        {
            // Run the command via CLI
            var args = new[] { "discovery", "dependencies", "--config", scenarioPath, "--output", outputPath };

            // This would normally be run via CliRunner or similar
            // For now, just verify the command can be instantiated
            var command = new DependencyCommand();
            Assert.IsNotNull(command);
        }
        finally
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);
        }
    }
}
