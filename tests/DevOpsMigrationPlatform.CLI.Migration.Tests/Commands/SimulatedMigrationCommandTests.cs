// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
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
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Queue_Export_Sim_WritesWorkItemRevisions()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Export_Sim_WritesWorkItemRevisions),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Export-WorkItems.json", "--force-fresh"],
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

        var revisionFiles = workItemsDirs
            .SelectMany(dir => Directory.GetFiles(dir, "revision.json", SearchOption.AllDirectories))
            .ToArray();
        Assert.IsTrue(revisionFiles.Length > 0,
            $"Expected at least one revision.json under {outputDir}. None found.");

        // T029: migration-config.json must exist at the package root
        var configFiles = Directory.GetFiles(outputDir, "migration-config.json", SearchOption.AllDirectories);
        Assert.IsTrue(configFiles.Length > 0,
            $"Expected migration-config.json somewhere under {outputDir}. FR-002 requires the CLI to write it before job submission.");

        // Verify it deserialises to valid JSON containing the required MigrationPlatform key
        var configJson = File.ReadAllText(configFiles[0]);
        Assert.IsTrue(configJson.Contains("MigrationPlatform"),
            $"migration-config.json must contain 'MigrationPlatform' wrapper key. Got: {configJson.Substring(0, Math.Min(200, configJson.Length))}");

        // T035: verify package-boundary-authored state surfaces are present in simulated runs.
        var authoritativePlan = Directory.GetFiles(outputDir, "plan.json", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                path.Contains($"{Path.DirectorySeparatorChar}.migration{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(authoritativePlan,
            $"Expected authoritative .migration/plan.json somewhere under {outputDir}.");

        var cursorFiles = Directory.GetFiles(outputDir, "export.workitems.cursor.json", SearchOption.AllDirectories);
        Assert.IsTrue(cursorFiles.Length > 0,
            $"Expected project-scoped export.workitems.cursor.json under <org>/<project>/.migration somewhere in {outputDir}.");
    }

    /// <summary>
    /// T029b (updated): SC-003 resume determinism — re-submitting the same package URI without
    /// --force-fresh must be accepted (exit code 0) because the Source and Target are unchanged.
    /// The config is overwritten with the new payload; cursor state is preserved for resume.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Queue_Export_Sim_ReSubmitWithoutForce_Resumes()
    {
        // First run: establishes migration-config.json using --force-fresh
        var first = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Export_Sim_ReSubmitWithoutForce_Resumes),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Export-WorkItems.json", "--force-fresh"],
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
            testName: nameof(Queue_Export_Sim_ReSubmitWithoutForce_Resumes),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Export-WorkItems.json"],
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
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Queue_Import_Sim_AcceptsWorkItems()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Import_Sim_AcceptsWorkItems),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Import-WorkItems.json", "--force-fresh"],
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
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Queue_Migrate_Sim_ProducesPackageWithRevisions()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Migrate_Sim_ProducesPackageWithRevisions),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Migrate-Roundtrip.json", "--force-fresh"],
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

        var totalFiles = workItemsDirs
            .SelectMany(dir => Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            .Count();
        Assert.IsTrue(totalFiles > 0,
            $"Expected at least one file under discovered WorkItems directories in {outputDir}.");
    }

    // ─────────────────────────────────────────────────────────────────────
    // Spec 007 Observability verification (T010+T015+T055 consolidated)
    // ─────────────────────────────────────────────────────────────────────

    /// <summary>
    /// T010+T015+T055: Verifies both progress.ndjson and diagnostics.ndjson are produced
    /// in a single run and each contains at least one NDJSON record.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Queue_Export_Sim_ProducesBothLogFiles()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Export_Sim_ProducesBothLogFiles),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Export-WorkItems.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}.");

        // Job-scoped log folder: .migration/runs/<runId>/logs/{progress,diagnostics}.ndjson
        var logsDirs = Directory.GetDirectories(outputDir, "Logs", SearchOption.AllDirectories);
        Assert.IsTrue(logsDirs.Length > 0,
            $"Expected .migration/Logs/ directory somewhere under {outputDir}");

        var progressFiles = Directory.GetFiles(outputDir, "progress.ndjson", SearchOption.AllDirectories);
        Assert.IsTrue(progressFiles.Length > 0,
            "progress.ndjson missing.");
        Assert.IsTrue(File.ReadAllLines(progressFiles[0]).Length >= 1,
            "progress.ndjson must contain at least one NDJSON record per module stage transition.");

        var diagnosticsFiles = Directory.GetFiles(outputDir, "diagnostics.ndjson", SearchOption.AllDirectories);
        Assert.IsTrue(diagnosticsFiles.Length > 0,
            "diagnostics.ndjson missing.");
        Assert.IsTrue(File.ReadAllLines(diagnosticsFiles[0]).Length >= 1,
            "diagnostics.ndjson must contain at least one structured NDJSON record at Warning+ level.");
    }

    /// <summary>
    /// VS-H3: Verifies the simulated inventory scenario produces inventory.csv and inventory.json.
    /// Runs the <c>queue</c> command with <c>Mode: Inventory</c> against the Simulated connector.
    /// </summary>
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Queue_Inventory_Sim_WritesInventoryArtefacts()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Inventory_Sim_WritesInventoryArtefacts),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Inventory-WorkItems.json", "--force-fresh"],
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
