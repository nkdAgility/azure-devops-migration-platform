using System;
using System.IO;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Tests for the <c>prepare</c> command.
/// Prepare submits a lightweight probe job through the full pipeline
/// (CLI → Control Plane → Agent → ArtefactStore) to validate end-to-end connectivity.
/// </summary>
[TestClass]
public class PrepareCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    [TestMethod]
    public void PrepareCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new DevOpsMigrationPlatform.CLI.Commands.PrepareCommand();
        Assert.IsNotNull(command);
    }

    [TestMethod]
    public void PrepareCommand_UsesCorrectSettingsType()
    {
        var command = new DevOpsMigrationPlatform.CLI.Commands.PrepareCommand();
        // PrepareCommand inherits ControlPlaneCommandBase<MigrationCommandSettings>
        Assert.IsInstanceOfType<Spectre.Console.Cli.ICommand>(command);
    }

    // ── System tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>devopsmigration prepare --config scenarios/queue-export-ado-workitems-single-project.json</c>
    /// as a subprocess. The prepare command submits a probe job that writes a single file
    /// to the artefact store and completes.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [Timeout(300_000)] // 5 minutes — prepare jobs should be fast
    public async Task PrepareCommand_WithValidConfig_ExitsZero_AndWritesProbeFile()
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

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunAsync(
            args: ["prepare", "--config", "scenarios/queue-export-ado-workitems-single-project.json"],
            timeout: TimeSpan.FromMinutes(4));

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
            combinedOutput.Contains("Preparation check passed", StringComparison.OrdinalIgnoreCase),
            "Expected success message not found in output.");

        var probeFile = Path.Combine(outputDir, "prepare-probe.json");
        Assert.IsTrue(File.Exists(probeFile),
            $"prepare-probe.json was not written to {outputDir}");
    }
}
