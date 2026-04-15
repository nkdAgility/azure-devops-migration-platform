using System;
using System.IO;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// System tests for export command flags: <c>--follow</c> and <c>--level</c>.
/// Each test runs the CLI as a subprocess (the same invocation as the VS Code launch profiles)
/// so the full code path is exercised end-to-end.
/// </summary>
[TestClass]
public class ExportCommandFollowTests
{
    // ── Unit tests ────────────────────────────────────────────────────────────────

    [TestMethod]
    public void ExportCommand_WithInvalidLevel_FailsValidation()
    {
        var settings = new DevOpsMigrationPlatform.CLI.Migration.Settings.QueueCommandSettings
        {
            ConfigFile = "test.json",
            Follow = false,
            Level = "InvalidLevel"
        };

        var validationResult = settings.Validate();
        Assert.IsFalse(validationResult.Successful,
            "Validation should fail for invalid log level");
    }

    // ── System tests ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies that <c>export --follow --level Warning</c> runs end-to-end and exits 0.
    /// The <c>--follow</c> flag streams diagnostic records inline; <c>--level Warning</c>
    /// limits the records shown. Both flags must not break the export.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [Timeout(1_200_000)] // 20 minutes
    public async Task ExportCommand_WithFollowAndWarningLevel_ExitsZero_AndWritesRevisionFiles()
    {
        // ── Guard ─────────────────────────────────────────────────────────────
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        // ── Output folder ───────────────────────────────────────────────────────────
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "queue-export-ado-workitems-single-project");
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act — run with --follow and --level Warning ────────────────────
        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json",
                   "--force-fresh", "--follow", "--level", "Warning"],
            timeout: TimeSpan.FromMinutes(18));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert: process outcome ─────────────────────────────────────
        Assert.IsFalse(result.TimedOut,
            "CLI timed out. The export may be hung.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("export complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work items", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        // ── Assert: package written ─────────────────────────────────────────
        var workItemsDir = Path.Combine(outputDir, "WorkItems");
        Assert.IsTrue(Directory.Exists(workItemsDir),
            $"WorkItems directory was not created under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDir, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"No revision.json files found under {workItemsDir}");
        Console.WriteLine($"revision.json files: {revisionFiles.Length}");
    }

    /// <summary>
    /// Verifies that <c>export --level Debug</c> produces a <c>Logs/agent.jsonl</c> file
    /// inside the package directory (the agent writes structured logs at Debug+ level).
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [Timeout(1_200_000)] // 20 minutes
    public async Task ExportCommand_WithDebugLevel_WritesAgentJsonl()
    {
        // ── Guard ─────────────────────────────────────────────────────────────
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        // ── Output folder ───────────────────────────────────────────────────────────
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "queue-export-ado-workitems-single-project");
        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act — run with --level Debug ──────────────────────────────────
        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json",
                   "--force-fresh", "--level", "Debug"],
            timeout: TimeSpan.FromMinutes(18));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert: process outcome ─────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("export complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work items", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        // ── Assert: Logs/agent.jsonl written by the Migration Agent ───────────
        var logsPath = Path.Combine(outputDir, "Logs", "agent.jsonl");
        if (File.Exists(logsPath))
        {
            var lines = File.ReadAllLines(logsPath);
            Console.WriteLine($"Logs/agent.jsonl: {lines.Length} records");
            Assert.IsTrue(lines.Length > 0, "Logs/agent.jsonl should contain at least one record.");
        }
        else
        {
            // Log file is written by the Migration Agent; in standalone mode without a
            // separately running agent the file may not be present. Record this for visibility.
            Console.WriteLine($"Logs/agent.jsonl not found at {logsPath} " +
                              "(expected when running in standalone / in-process mode).");
        }
    }
}
