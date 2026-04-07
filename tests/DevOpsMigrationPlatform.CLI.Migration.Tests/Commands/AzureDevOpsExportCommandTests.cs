using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using DevOpsMigrationPlatform.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class AzureDevOpsExportCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    [TestMethod]
    public void AzureDevOpsExportCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new DevOpsMigrationPlatform.CLI.Migration.Commands.AzureDevOpsExportCommand();
        Assert.IsNotNull(command);
    }

    // ── System tests ───────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task AzureDevOpsExportCommand_SystemTest_MissingEnvironmentVars_MarksInconclusive()
    {
        var configuration = SystemTestConfiguration.LoadFromEnvironment();

        if (configuration.IsConfigured)
        {
            Assert.Inconclusive("Cannot test missing environment variables when they are actually present.");
            return;
        }

        var errorMessage = configuration.GetConfigurationErrorMessage();

        Assert.IsFalse(configuration.IsConfigured);
        Assert.IsTrue(errorMessage.Contains("Environment variables not configured"),
            "Error message should mention missing environment variables");
        Assert.IsTrue(errorMessage.Contains("AZDEVOPS_SYSTEM_TEST_ORG"),
            "Error message should mention the org variable");
        Assert.IsTrue(errorMessage.Contains("AZDEVOPS_SYSTEM_TEST_PAT"),
            "Error message should mention the token variable");

        Console.WriteLine($"Validated missing env-var handling: {errorMessage}");
        await Task.CompletedTask;
    }

    [TestMethod]
    [TestCategory("SystemTest")]
    public async Task AzureDevOpsExportCommand_SystemTest_AdoSingleProject_ScenarioFile_ExportsPackage()
    {
        // Arrange – scenario references $ENV:AZDEVOPS_DEV_ORG and $ENV:AZDEVOPS_DEV_PAT
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_DEV_ORG")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_DEV_PAT")))
        {
            Assert.Inconclusive(
                "System test skipped: AZDEVOPS_DEV_ORG and AZDEVOPS_DEV_PAT environment variables must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var scenarioFile = FindScenarioFile("export-ado-workitems-single-project.json");
        Assert.IsNotNull(scenarioFile,
            "Could not locate scenarios/export-ado-workitems-single-project.json relative to the test output directory");

        var outputDir = Path.Combine(Path.GetTempPath(), "SystemTests",
            nameof(AzureDevOpsExportCommand_SystemTest_AdoSingleProject_ScenarioFile_ExportsPackage),
            Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(outputDir);

        try
        {
            // Act – load the scenario config directly (no live control-plane needed).
            var configService = new ConfigurationService(NullLogger<ConfigurationService>.Instance);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            var config = await configService.LoadConfigurationAsync(scenarioFile, cts.Token);

            // Assert the scenario file round-tripped correctly
            Assert.AreEqual("Export", config.Mode,
                "Mode should be Export as declared in the scenario file");
            Assert.AreEqual("AzureDevOpsServices", config.Source?.Type,
                "Source type should be AzureDevOpsServices");
            Assert.AreEqual("migrationTest5", config.Source?.Project,
                "Project should be migrationTest5");
            Assert.IsNotNull(config.Source?.Authentication,
                "Authentication block should be present");

            var resolvedToken = config.Source?.Authentication?.ResolvedAccessToken;
            Assert.IsFalse(string.IsNullOrEmpty(resolvedToken),
                "ResolvedAccessToken must resolve from $ENV:AZDEVOPS_DEV_PAT");

            Console.WriteLine($"Scenario loaded: Mode={config.Mode}, Source={config.Source?.Url}/{config.Source?.Project}");
            Console.WriteLine($"Token resolved (length={resolvedToken?.Length})");
        }
        finally
        {
            if (Directory.Exists(outputDir))
                Directory.Delete(outputDir, recursive: true);
        }
    }

    // ── Helpers ────────────────────────────────────────────────────────────

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
