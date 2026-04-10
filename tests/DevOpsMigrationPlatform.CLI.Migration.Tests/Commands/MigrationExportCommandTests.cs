using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
public class MigrationExportCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    [TestMethod]
    public void MigrationExportCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new DevOpsMigrationPlatform.CLI.Migration.Commands.MigrationExportCommand();
        Assert.IsNotNull(command);
    }

    // ── System tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>devopsmigration export --config scenarios/export-ado-workitems-single-project.json --force-fresh</c>
    /// as a subprocess — the exact same invocation as the VS Code launch profile — then
    /// asserts the exit code, the success message, and the output folder contents.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [Timeout(1_200_000)] // 20 minutes — full export of a dev project over real network
    public async Task MigrationExportCommand_SystemTest_AdoSingleProject_ExitsZero_AndWritesRevisionFiles()
    {
        // ── Guard ─────────────────────────────────────────────────────────
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_DEV_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Inconclusive(
                "System test skipped: AZDEVOPS_DEV_ORG and AZDEVOPS_DEV_PAT must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        // ── Output folder (matches scenario Artefacts.Path) ───────────────
        // The scenario has "Path": "%TEMP%\\SystemTests\\export-ado-workitems-single-project"
        var outputDir = Environment.ExpandEnvironmentVariables(
            @"%TEMP%\SystemTests\export-ado-workitems-single-project");

        if (Directory.Exists(outputDir))
            Directory.Delete(outputDir, recursive: true);

        // ── Act — run the CLI exactly as the launch profile does ──────────
        var result = await CliRunner.RunAsync(
            args: ["export", "--config", "scenarios/export-ado-workitems-single-project.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(18)); // generous — MSTest [Timeout] is the hard ceiling

        // Always dump output so failures are diagnosable in test results.
        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert: process outcome ───────────────────────────────────────
        Assert.IsFalse(result.TimedOut,
            "CLI timed out after 10 minutes. The export is either hung or the project is very large.");

        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        // ── Assert: success message printed by the CLI ────────────────────
        // MigrationExportCommand prints on success (after Spectre ANSI stripping):
        //   "Export complete — <N> work items / <M> revisions written to package."
        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("export complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work items", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message ('export complete' or 'work items') not found in output.");

        // Parse work item and revision counts from the success line if present.
        // Pattern: "N work items / M revisions written"
        var countMatch = Regex.Match(combinedOutput,
            @"(\d[\d,]*)\s+work items\s*/\s*(\d[\d,]*)\s+revisions",
            RegexOptions.IgnoreCase);
        if (countMatch.Success)
        {
            var workItems = int.Parse(countMatch.Groups[1].Value.Replace(",", ""));
            var revisions = int.Parse(countMatch.Groups[2].Value.Replace(",", ""));
            Console.WriteLine($"Parsed from CLI output: {workItems} work items, {revisions} revisions");
            Assert.IsTrue(workItems > 0, "CLI reported 0 work items exported.");
            Assert.IsTrue(revisions >= workItems, "Revisions should be >= work items.");
        }

        // ── Assert: output folder contains revision.json files ────────────
        var workItemsDir = Path.Combine(outputDir, "WorkItems");
        Assert.IsTrue(Directory.Exists(workItemsDir),
            $"WorkItems directory was not created under {outputDir}");

        var revisionFiles = Directory.GetFiles(workItemsDir, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"No revision.json files found under {workItemsDir}");

        // Count unique work items (each work item folder contains 1..N revision.json files).
        var workItemDirs = Directory.GetDirectories(workItemsDir, "*", SearchOption.TopDirectoryOnly);
        Console.WriteLine($"Unique work item folders : {workItemDirs.Length}");
        Console.WriteLine($"Total revision.json files: {revisionFiles.Length}");
        Console.WriteLine($"Output directory         : {outputDir}");

        Assert.IsTrue(workItemDirs.Length > 0,
            "Expected at least one work item sub-directory under WorkItems/");
    }
}
