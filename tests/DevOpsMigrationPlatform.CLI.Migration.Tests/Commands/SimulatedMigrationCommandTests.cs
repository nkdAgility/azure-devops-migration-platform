using System;
using System.IO;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// System tests for the Simulated connector scenarios.
/// These tests run end-to-end against the real CLI using the Simulated source/target,
/// requiring no external credentials or network access.
/// They verify observable CLI output and package folder structure.
/// </summary>
[TestClass]
[DoNotParallelize]
public class SimulatedMigrationCommandTests
{
    /// <summary>
    /// Verifies the simulated export scenario produces a non-empty WorkItems folder.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)] // 2 minutes — fully offline, no network
    public async Task QueueExportSimulated_ExitsZeroAndWritesWorkItemRevisions()
    {
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "queue-export-workitems-simulated-source");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(1));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");

        // Verify WorkItems folder was created with at least one revision
        // (org/project nesting places it under <outputDir>/<org>/<project>/WorkItems/)
        var workItemsDirs = Directory.GetDirectories(outputDir, "WorkItems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"Expected WorkItems/ folder to exist somewhere under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDirs[0], "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"Expected at least one revision.json under {workItemsDirs[0]}. None found.");
    }

    /// <summary>
    /// Verifies the simulated import scenario exits zero when importing the test fixture.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)]
    public async Task QueueImportSimulated_ExitsZeroAndAcceptsWorkItems()
    {
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "queue-import-workitems-simulated-target");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-import-workitems-simulated-target.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(1));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");
    }

    /// <summary>
    /// Verifies the full roundtrip scenario (Simulated → Package → Simulated) exits zero.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)]
    public async Task QueueRoundtripSimulated_ExitsZeroAndProducesPackageWithRevisions()
    {
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "roundtrip-simulated");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/roundtrip-simulated.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(1));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");

        // Roundtrip should produce a package (Both mode = export + import)
        // (org/project nesting places it under <outputDir>/<org>/<project>/WorkItems/)
        var workItemsDirs = Directory.GetDirectories(outputDir, "WorkItems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"Expected WorkItems/ folder somewhere under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDirs[0], "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"Expected at least one revision.json under {workItemsDirs[0]}. None found.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spec 007 Observability verification (T010, T015, T055)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// T010: Verifies Logs/progress.jsonl exists in the package after export.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)]
    public async Task QueueExportSimulated_ProducesProgressJsonl()
    {
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "queue-export-workitems-simulated-source");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(1));

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}.");

        // Job-scoped log folder: Logs/<ticks>-<jobId>/progress.jsonl
        var progressFiles = Directory.GetFiles(outputDir, "progress.jsonl", SearchOption.AllDirectories);
        Assert.IsTrue(progressFiles.Length > 0,
            $"Expected progress.jsonl to exist somewhere under {outputDir}");

        var lines = File.ReadAllLines(progressFiles[0]);
        Assert.IsTrue(lines.Length >= 1,
            "progress.jsonl must contain at least one NDJSON record per module stage transition.");
    }

    /// <summary>
    /// T015: Verifies Logs/agent.jsonl exists in the package after export.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)]
    public async Task QueueExportSimulated_ProducesAgentJsonl()
    {
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "queue-export-workitems-simulated-source");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(1));

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}.");

        // Job-scoped log folder: Logs/<ticks>-<jobId>/agent.jsonl
        var agentFiles = Directory.GetFiles(outputDir, "agent.jsonl", SearchOption.AllDirectories);
        Assert.IsTrue(agentFiles.Length > 0,
            $"Expected agent.jsonl to exist somewhere under {outputDir}");

        var lines = File.ReadAllLines(agentFiles[0]);
        Assert.IsTrue(lines.Length >= 1,
            "agent.jsonl must contain at least one structured NDJSON record at Warning+ level.");
    }

    /// <summary>
    /// T055: Verifies both progress.jsonl and agent.jsonl are produced in a single run.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(120_000)]
    public async Task QueueExportSimulated_ProducesBothLogFiles()
    {
        var outputDir = Path.Combine(
            CliRunner.FindRepoRoot(), "storage", "queue-export-workitems-simulated-source");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        var result = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(1));

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}.");

        // Job-scoped log folder: Logs/<ticks>-<jobId>/{progress,agent}.jsonl
        var logsDirs = Directory.GetDirectories(outputDir, "Logs", SearchOption.AllDirectories);
        Assert.IsTrue(logsDirs.Length > 0,
            $"Expected Logs/ directory somewhere under {outputDir}");

        var progressFiles = Directory.GetFiles(outputDir, "progress.jsonl", SearchOption.AllDirectories);
        Assert.IsTrue(progressFiles.Length > 0,
            "progress.jsonl missing.");
        var agentFiles = Directory.GetFiles(outputDir, "agent.jsonl", SearchOption.AllDirectories);
        Assert.IsTrue(agentFiles.Length > 0,
            "agent.jsonl missing.");
    }
}
