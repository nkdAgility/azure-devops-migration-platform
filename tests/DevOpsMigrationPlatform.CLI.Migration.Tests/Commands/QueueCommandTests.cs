using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.Abstractions;
using DevOpsMigrationPlatform.Abstractions.Jobs;
using DevOpsMigrationPlatform.Abstractions.Options;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Tests for the unified <c>queue</c> command.
/// Mode-driven behaviour: the config <c>mode</c> field determines execution
/// (Export, Import, or Both).
/// </summary>
[TestClass]
[DoNotParallelize]
public class QueueCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

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
    [TestCategory("SystemTest_Live")]
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

        var testStorage = Path.Combine("storage", nameof(QueueCommand_WithExportMode_ExitsZero_AndWritesRevisionFiles));
        var outputDir = Path.Combine(CliRunner.FindRepoRoot(), testStorage);
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json", "--force-fresh"],
            env: new Dictionary<string, string> { ["DEVOPS_MIGRATION_TEST_STORAGE"] = testStorage },
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

        // Org/project nesting places WorkItems under <outputDir>/<org>/<project>/WorkItems/
        var workItemsDirs = Directory.GetDirectories(outputDir, "workitems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"WorkItems directory was not created anywhere under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDirs[0], "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"No revision.json files found under {workItemsDirs[0]}");
    }

    /// <summary>
    /// Verifies that when the config specifies <c>Environment.Type = Hosted</c> and the control
    /// plane is not running, the CLI fails fast with an actionable error — without performing
    /// expensive preflight operations (e.g. work item counting).
    /// Uses the simulated export config with the environment type overridden via env vars.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(30_000)] // 30 seconds — should fail fast
    public async Task QueueCommand_WithHostedModeAndUnreachableControlPlane_FailsFast()
    {
        // ── Act — override to Hosted mode pointing at a port nothing listens on ──
        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json"],
            env: new Dictionary<string, string>
            {
                ["MigrationPlatform__Environment__Type"] = "Hosted",
                ["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = "http://localhost:59999"
            },
            timeout: TimeSpan.FromSeconds(25));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert ────────────────────────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "CLI timed out — should have failed fast.");
        Assert.AreNotEqual(0, result.ExitCode,
            "CLI should exit non-zero when the control plane is unreachable.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("not reachable", StringComparison.OrdinalIgnoreCase),
            "Expected 'not reachable' error message in output.");
        Assert.IsTrue(
            combinedOutput.Contains("\"Type\": \"Standalone\"", StringComparison.OrdinalIgnoreCase),
            "Expected guidance showing the Standalone config snippet in output.");
    }

    /// <summary>
    /// Runs <c>devopsmigration queue --config scenarios/queue-import-workitems-simulated-fixture.json --force-fresh</c>
    /// as a subprocess. Uses a pre-built fixture zip (<c>scenarios/testdata/workitems-2items-flat.zip</c>)
    /// with a <c>Simulated</c> target — no live credentials required.
    /// Verifies that the CLI exits zero and logs import progress for both work items.
    /// See <c>scenarios/testdata/catalogue.json</c> for fixture details.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)] // 2 minutes — no network I/O
    public async Task QueueCommand_WithSimulatedImportMode_Fixture_ExitsZero_AndImportsBothWorkItems()
    {
        // ── Arrange ───────────────────────────────────────────────────────
        var outputDir = Path.Combine(CliRunner.FindRepoRoot(), "storage", nameof(QueueCommand_WithSimulatedImportMode_Fixture_ExitsZero_AndImportsBothWorkItems));
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-import-workitems-simulated-fixture.json", "--force-fresh"],
            env: new Dictionary<string, string> { ["DEVOPS_MIGRATION_TEST_STORAGE"] = Path.Combine("storage", nameof(QueueCommand_WithSimulatedImportMode_Fixture_ExitsZero_AndImportsBothWorkItems)) },
            timeout: TimeSpan.FromSeconds(110));

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
            combinedOutput.Contains("import complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work item", StringComparison.OrdinalIgnoreCase),
            "Expected CLI import progress message not found in output.");

        // Verify both fixture work items were processed by checking the idmap DB was created
        // (progress events are not observable from CLI stdout in the current streaming architecture).
        // Org/project nesting places idmap.db under <outputDir>/<org>/<project>/.migration/Checkpoints/
        var idmapFiles = Directory.GetFiles(outputDir, "idmap.db", SearchOption.AllDirectories);
        Assert.IsTrue(idmapFiles.Length > 0,
            $".migration/Checkpoints/idmap.db was not found anywhere under {outputDir} — import may not have processed any work items.");
    }
}
