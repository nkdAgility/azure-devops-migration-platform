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
        var workItemsDir = Path.Combine(outputDir, "WorkItems");
        Assert.IsTrue(Directory.Exists(workItemsDir),
            $"Expected WorkItems/ folder to exist under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDir, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"Expected at least one revision.json under {workItemsDir}. None found.");
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
        var workItemsDir = Path.Combine(outputDir, "WorkItems");
        Assert.IsTrue(Directory.Exists(workItemsDir),
            $"Expected WorkItems/ folder under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDir, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"Expected at least one revision.json under {workItemsDir}. None found.");
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

        var progressJsonl = Path.Combine(outputDir, "Logs", "progress.jsonl");
        Assert.IsTrue(File.Exists(progressJsonl),
            $"Expected Logs/progress.jsonl to exist at {progressJsonl}");

        var lines = File.ReadAllLines(progressJsonl);
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

        var agentJsonl = Path.Combine(outputDir, "Logs", "agent.jsonl");
        Assert.IsTrue(File.Exists(agentJsonl),
            $"Expected Logs/agent.jsonl to exist at {agentJsonl}");

        var lines = File.ReadAllLines(agentJsonl);
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

        var logsDir = Path.Combine(outputDir, "Logs");
        Assert.IsTrue(Directory.Exists(logsDir),
            $"Expected Logs/ directory at {logsDir}");

        Assert.IsTrue(File.Exists(Path.Combine(logsDir, "progress.jsonl")),
            "Logs/progress.jsonl missing.");
        Assert.IsTrue(File.Exists(Path.Combine(logsDir, "agent.jsonl")),
            "Logs/agent.jsonl missing.");
    }
}
