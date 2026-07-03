// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.SystemTests;

/// <summary>
/// VS-M1: CLI-level system test for the work-items import slice.
/// Queues an import job from a prepared package (Files -&gt; Target) via the real CLI
/// subprocess and asserts observable target-side output of the import:
/// the source-to-target ID map (<c>Checkpoints/idmap.db</c>) written by the import
/// pipeline, and progress records evidencing WorkItems import activity.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class WorkItemsImportSliceSystemTests
{
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Queue_Import_FromPreparedPackage_WritesTargetSideIdMapAndProgress()
    {
        // Act — queue an import job from the prepared package fixture against the Simulated target.
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Import_FromPreparedPackage_WritesTargetSideIdMapAndProgress),
            args: ["queue", "--config", "scenarios/SystemTest-Simulated-Import-WorkItems.json", "--force-fresh"],
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

        // Assert — the import job completed successfully.
        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");

        // Assert — target-side output: the import slice must have written the
        // source-to-target work item ID map (Checkpoints/idmap.db) recording the
        // work items created on the target.
        var idMapFiles = Directory.GetFiles(outputDir, "idmap.db", SearchOption.AllDirectories);
        Assert.IsTrue(idMapFiles.Length > 0,
            $"Expected Checkpoints/idmap.db somewhere under {outputDir}. " +
            "The import slice must persist the source-to-target ID map as evidence of target-side creation.");
        Assert.IsTrue(new FileInfo(idMapFiles[0]).Length > 0,
            $"idmap.db at {idMapFiles[0]} must be non-empty after a successful import.");

        // Assert — progress records evidence WorkItems import activity.
        var progressFiles = Directory.GetFiles(outputDir, "progress.ndjson", SearchOption.AllDirectories);
        Assert.IsTrue(progressFiles.Length > 0,
            $"Expected progress.ndjson somewhere under {outputDir}.");

        var progressContent = string.Join(Environment.NewLine,
            progressFiles.Select(File.ReadAllText));
        Assert.IsTrue(progressContent.Contains("WorkItems", StringComparison.OrdinalIgnoreCase),
            "progress.ndjson must contain WorkItems module records for the import slice.");
    }
}
