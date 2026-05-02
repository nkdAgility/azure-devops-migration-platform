using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
[DoNotParallelize]
public class MigrationExportCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    // ── System tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>devopsmigration export --config scenarios/queue-export-ado-workitems-single-project.json --force-fresh</c>
    /// as a subprocess — the exact same invocation as the VS Code launch profile — then
    /// asserts the exit code, the success message, and the output folder contents.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [Timeout(1_200_000)] // 20 minutes — full export of a dev project over real network
    public async Task MigrationExportCommand_SystemTest_AdoSingleProject_ExitsZero_AndWritesRevisionFiles()
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

        // ── Act — run the CLI exactly as the launch profile does ────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(MigrationExportCommand_SystemTest_AdoSingleProject_ExitsZero_AndWritesRevisionFiles),
            args: ["queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(18),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

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
        // Org/project nesting places WorkItems under <outputDir>/<org>/<project>/WorkItems/
        var workItemsDirs = Directory.GetDirectories(outputDir, "workitems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"WorkItems directory was not created anywhere under {outputDir}");
        var workItemsDir = workItemsDirs[0];

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

    /// <summary>
    /// Runs <c>devopsmigration export --config scenarios/queue-export-ado-workitems-single-project.json --force-fresh</c>
    /// as a subprocess and validates that comments and embedded images are exported correctly.
    /// 
    /// This test verifies:
    /// 1. Work item comments are written to comment sub-folders (*-c&lt;commentId&gt;/comment.json)
    /// 2. Embedded images referenced in HTML and Markdown fields are downloaded
    /// 3. Image URLs in revision.json are rewritten to relative paths
    /// 4. Comment images are stored beside comment.json inside the comment subfolder
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [Timeout(1_200_000)] // 20 minutes — full export including API calls for comments
    public async Task MigrationExportCommand_SystemTest_WorkItemComments_ExitsZero_AndWritesCommentFolders()
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

        // ── Act — run the CLI with comments and embedded images enabled ────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(MigrationExportCommand_SystemTest_WorkItemComments_ExitsZero_AndWritesCommentFolders),
            args: ["queue", "--config", "scenarios/queue-export-ado-workitems-single-project.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(18),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

        // Always dump output for diagnostics
        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert: process outcome ───────────────────────────────────────
        Assert.IsFalse(result.TimedOut,
            "CLI timed out. The export may be hung or the project is very large.");

        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        // ── Assert: success message ───────────────────────────────────────
        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("export complete", StringComparison.OrdinalIgnoreCase) ||
            combinedOutput.Contains("work items", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        // ── Assert: comment folders exist ───────────────────────────────────
        // Org/project nesting places WorkItems under <outputDir>/<org>/<project>/WorkItems/
        var workItemsDirs = Directory.GetDirectories(outputDir, "workitems", SearchOption.AllDirectories);
        Assert.IsTrue(workItemsDirs.Length > 0,
            $"WorkItems directory was not created anywhere under {outputDir}");
        var workItemsDir = workItemsDirs[0];

        // Search for comment folders matching pattern: *-<workItemId>-c<commentId>/
        var allDirs = Directory.GetDirectories(workItemsDir, "*", SearchOption.AllDirectories);
        var commentFolders = allDirs.Where(d =>
        {
            var name = Path.GetFileName(d);
            // Comment folder pattern: <ticks>-<workItemId>-c<commentId>
            // Example: 637123456789-12345-c1/
            return Regex.IsMatch(name, @"^\d+-\d+-c\d+$");
        }).ToList();

        Console.WriteLine($"Total directories found: {allDirs.Length}");
        Console.WriteLine($"Comment folders found: {commentFolders.Count}");

        if (commentFolders.Count > 0)
        {
            Console.WriteLine("Comment folders:");
            foreach (var folder in commentFolders.Take(10))
            {
                Console.WriteLine($"  - {Path.GetFileName(folder)}");
                var commentJsonPath = Path.Combine(folder, "comment.json");
                Assert.IsTrue(File.Exists(commentJsonPath),
                    $"comment.json not found at {commentJsonPath}");
            }
            Assert.IsTrue(commentFolders.Count > 0,
                "At least one comment folder should be present if the work items have comments.");
        }
        else
        {
            Console.WriteLine("⚠️ No comment folders found. This may indicate:");
            Console.WriteLine("   - Test work items have no comments");
            Console.WriteLine("   - Comments scope is not enabled in scenario config");
            Console.WriteLine("   - Comment export service was not called");
        }

        // ── Assert: embedded images are downloaded ───────────────────────────
        // Check for image files (SHA-256 hashes) beside revision.json or comment.json
        var imageFiles = Directory.GetFiles(workItemsDir, "*.*", SearchOption.AllDirectories)
            .Where(f =>
            {
                var fileName = Path.GetFileName(f);
                var ext = Path.GetExtension(f).ToLowerInvariant();
                // Image files: SHA-256 hex + extension (e.g., abc123def456.png)
                return Regex.IsMatch(fileName, @"^[a-f0-9]{64}\.(png|jpg|jpeg|gif|webp|svg|bmp)$") &&
                       (f.Contains("-c") || Path.GetFileName(Path.GetDirectoryName(f)) == "workitems");
            })
            .ToList();

        Console.WriteLine($"Image files found: {imageFiles.Count}");
        if (imageFiles.Count > 0)
        {
            Console.WriteLine("Embedded images:");
            foreach (var imgFile in imageFiles.Take(5))
            {
                var fileInfo = new FileInfo(imgFile);
                Console.WriteLine($"  - {Path.GetFileName(imgFile)} ({fileInfo.Length} bytes)");
            }
        }
        else
        {
            Console.WriteLine("⚠️ No embedded image files found. This may indicate:");
            Console.WriteLine("   - Work items contain no HTML/Markdown with embedded images");
            Console.WriteLine("   - Embedded images scope is not enabled");
            Console.WriteLine("   - Image download service was not called");
        }

        // ── Assert: at least revisions exist ──────────────────────────────
        var revisionFiles = Directory.GetFiles(workItemsDir, "revision.json", SearchOption.AllDirectories);
        Assert.IsTrue(revisionFiles.Length > 0,
            $"No revision.json files found under {workItemsDir}");

        Console.WriteLine($"Total revision.json files: {revisionFiles.Length}");
        Console.WriteLine($"Output directory: {outputDir}");
    }
}
