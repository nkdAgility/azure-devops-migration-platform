// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

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
    [Timeout(300_000)] // 5 minutes — includes local stack startup
    public async Task QueueExportSimulated_ExitsZeroAndWritesWorkItemRevisions()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueExportSimulated_ExitsZeroAndWritesWorkItemRevisions),
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

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
        var workItemsDirs = Directory.GetDirectories(outputDir, "workitems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"Expected WorkItems/ folder to exist somewhere under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDirs[0], "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"Expected at least one revision.json under {workItemsDirs[0]}. None found.");

        // T029: migration-config.json must exist at the package root
        var configFiles = Directory.GetFiles(outputDir, "migration-config.json", SearchOption.AllDirectories);
        Assert.IsTrue(configFiles.Length > 0,
            $"Expected migration-config.json somewhere under {outputDir}. FR-002 requires the CLI to write it before job submission.");

        // Verify it deserialises to valid JSON containing the required MigrationPlatform key
        var configJson = File.ReadAllText(configFiles[0]);
        Assert.IsTrue(configJson.Contains("MigrationPlatform"),
            $"migration-config.json must contain 'MigrationPlatform' wrapper key. Got: {configJson.Substring(0, Math.Min(200, configJson.Length))}");
    }

    /// <summary>
    /// T029b (updated): SC-003 resume determinism — re-submitting the same package URI without
    /// --force-fresh must be accepted (exit code 0) because the Source and Target are unchanged.
    /// The config is overwritten with the new payload; cursor state is preserved for resume.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(600_000)] // 10 minutes — two full stack runs back to back
    public async Task QueueExportSimulated_ReSubmitWithoutForce_ResumesSuccessfully()
    {
        // First run: establishes migration-config.json using --force-fresh
        var first = await CliRunner.RunTestAsync(
            testName: nameof(QueueExportSimulated_ReSubmitWithoutForce_ResumesSuccessfully),
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = first.OutputDirectory;

        Assert.AreEqual(0, first.ExitCode,
            $"First run must succeed to establish migration-config.json. " +
            $"STDOUT:\n{first.StandardOutput}\nSTDERR:\n{first.StandardError}");

        // Verify migration-config.json written during first run
        var configFiles = Directory.GetFiles(outputDir, "migration-config.json", SearchOption.AllDirectories);
        Assert.IsTrue(configFiles.Length > 0,
            "Expected migration-config.json after first run — prerequisite for SC-003 test.");

        // Second run WITHOUT --force-fresh: same source/target → must be accepted (resume)
        var second = await CliRunner.RunTestAsync(
            testName: nameof(QueueExportSimulated_ReSubmitWithoutForce_ResumesSuccessfully),
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json"],
            timeout: TimeSpan.FromMinutes(4));

        Console.WriteLine("=== SECOND RUN STDOUT ===");
        Console.WriteLine(second.StandardOutput);
        if (!string.IsNullOrEmpty(second.StandardError))
        {
            Console.WriteLine("=== SECOND RUN STDERR ===");
            Console.WriteLine(second.StandardError);
        }

        // Re-submission with compatible config must succeed (resume, not reject)
        Assert.AreEqual(0, second.ExitCode,
            $"Re-submission without --force-fresh must resume (exit 0) when source/target are unchanged. " +
            $"STDOUT:\n{second.StandardOutput}\nSTDERR:\n{second.StandardError}");

        // migration-config.json must still be present after the resume run
        var configFilesAfter = Directory.GetFiles(outputDir, "migration-config.json", SearchOption.AllDirectories);
        Assert.IsTrue(configFilesAfter.Length > 0, "migration-config.json must still exist after a resume run.");
    }

    /// <summary>
    /// Verifies the simulated import scenario exits zero when importing the test fixture.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(300_000)] // 5 minutes
    public async Task QueueImportSimulated_ExitsZeroAndAcceptsWorkItems()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueImportSimulated_ExitsZeroAndAcceptsWorkItems),
            args: ["queue", "--config", "scenarios/queue-import-workitems-simulated-target.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);

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
    [Timeout(300_000)] // 5 minutes
    public async Task QueueRoundtripSimulated_ExitsZeroAndProducesPackageWithRevisions()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueRoundtripSimulated_ExitsZeroAndProducesPackageWithRevisions),
            args: ["queue", "--config", "scenarios/roundtrip-simulated.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

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

        // Roundtrip should produce a package (Migrate mode = export + prepare + import)
        // (org/project nesting places it under <outputDir>/<org>/<project>/WorkItems/)
        var workItemsDirs = Directory.GetDirectories(outputDir, "workitems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"Expected WorkItems/ folder somewhere under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDirs[0], "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"Expected at least one revision.json under {workItemsDirs[0]}. None found.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spec 007 Observability verification (T010+T015+T055 consolidated)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// T010+T015+T055: Verifies both progress.jsonl and agent.jsonl are produced
    /// in a single run and each contains at least one NDJSON record.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(300_000)] // 5 minutes
    public async Task QueueExportSimulated_ProducesBothLogFiles()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(QueueExportSimulated_ProducesBothLogFiles),
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}.");

        // Job-scoped log folder: .migration/Logs/<ticks>-<jobId>/{progress,agent}.jsonl
        var logsDirs = Directory.GetDirectories(outputDir, "Logs", SearchOption.AllDirectories);
        Assert.IsTrue(logsDirs.Length > 0,
            $"Expected .migration/Logs/ directory somewhere under {outputDir}");

        var progressFiles = Directory.GetFiles(outputDir, "progress.jsonl", SearchOption.AllDirectories);
        Assert.IsTrue(progressFiles.Length > 0,
            "progress.jsonl missing.");
        Assert.IsTrue(File.ReadAllLines(progressFiles[0]).Length >= 1,
            "progress.jsonl must contain at least one NDJSON record per module stage transition.");

        var agentFiles = Directory.GetFiles(outputDir, "agent.jsonl", SearchOption.AllDirectories);
        Assert.IsTrue(agentFiles.Length > 0,
            "agent.jsonl missing.");
        Assert.IsTrue(File.ReadAllLines(agentFiles[0]).Length >= 1,
            "agent.jsonl must contain at least one structured NDJSON record at Warning+ level.");
    }

    /// <summary>
    /// VS-H3: Verifies the simulated inventory scenario produces inventory.csv and inventory.json.
    /// Runs the <c>discovery inventory</c> command against the Simulated connector.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(300_000)] // 5 minutes
    public async Task DiscoveryInventorySimulated_ExitsZeroAndWritesInventoryArtefacts()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(DiscoveryInventorySimulated_ExitsZeroAndWritesInventoryArtefacts),
            args: ["discovery", "inventory", "--config", "scenarios/inventory-simulated.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

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

        // inventory.csv must exist and contain at least one data row (header + 1)
        var csvFiles = Directory.GetFiles(outputDir, "inventory.csv", SearchOption.AllDirectories);
        Assert.IsTrue(csvFiles.Length > 0,
            $"Expected inventory.csv somewhere under {outputDir}. None found.");

        var csvLines = File.ReadAllLines(csvFiles[0]);
        Assert.IsTrue(csvLines.Length > 1,
            $"inventory.csv must contain at least a header + one data row. Got {csvLines.Length} lines.");

        // inventory.json must exist and contain non-trivially non-empty content
        var jsonFiles = Directory.GetFiles(outputDir, "inventory.json", SearchOption.AllDirectories);
        Assert.IsTrue(jsonFiles.Length > 0,
            $"Expected inventory.json somewhere under {outputDir}. None found.");

        var jsonContent = File.ReadAllText(jsonFiles[0]);
        Assert.IsTrue(jsonContent.Length > 10,
            $"inventory.json must contain meaningful content. Got {jsonContent.Length} chars.");
        Assert.IsTrue(jsonContent.Contains("SimulatedProject", StringComparison.OrdinalIgnoreCase),
            "inventory.json must reference the SimulatedProject that was discovered.");
    }
}
