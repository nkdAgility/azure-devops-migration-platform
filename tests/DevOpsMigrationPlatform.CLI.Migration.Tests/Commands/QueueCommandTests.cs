using System;
using System.IO;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Tests for the unified <c>queue</c> command.
/// Mode-driven behaviour: the config <c>mode</c> field determines execution
/// (Export, Import, or Both).
/// </summary>
[TestClass]
public class QueueCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    [TestMethod]
    public void QueueCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new DevOpsMigrationPlatform.CLI.Migration.Commands.QueueCommand();
        Assert.IsNotNull(command);
    }

    [TestMethod]
    public void QueueCommandSettings_WithValidLevel_PassesValidation()
    {
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.QueueCommandSettings
        {
            ConfigFile = "test.json",
            Follow = false,
            Level = "Information"
        };

        var result = settings.Validate();
        Assert.IsTrue(result.Successful, "Validation should pass for valid log level.");
    }

    [TestMethod]
    public void QueueCommandSettings_WithInvalidLevel_FailsValidation()
    {
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.QueueCommandSettings
        {
            ConfigFile = "test.json",
            Follow = false,
            Level = "InvalidLevel"
        };

        var result = settings.Validate();
        Assert.IsFalse(result.Successful, "Validation should fail for invalid log level.");
    }

    // ── System tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>devopsmigration queue --config scenarios/queue-export-ado-workitems-single-project.json --force-fresh</c>
    /// as a subprocess. The scenario config has <c>mode: Export</c>, so this must behave
    /// identically to the former <c>export</c> command.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [Timeout(1_200_000)] // 20 minutes
    public async Task QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles()
    {
        // ── Guard ─────────────────────────────────────────────────────────
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var outputDir = Path.Combine(CliRunner.FindRepoRoot(), "storage", "queue-export-ado-workitems-single-project");
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(18));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert ────────────────────────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("export complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work items", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        var workItemsDir = Path.Combine(outputDir, "WorkItems");
        Assert.IsTrue(Directory.Exists(workItemsDir),
            $"WorkItems directory was not created under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDir, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"No revision.json files found under {workItemsDir}");
    }
}
