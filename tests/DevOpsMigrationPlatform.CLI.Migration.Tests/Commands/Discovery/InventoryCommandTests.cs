using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands.Discovery;

[TestClass]
public class InventoryCommandTests
{
    // -- Unit tests --

    [TestMethod]
    public void InventoryCommand_CanBeConstructed_WithParameterlessConstructor()
    {
        var command = new InventoryCommand();
        Assert.IsNotNull(command);
    }

    // -- System tests --

    [TestMethod]
    [TestCategory("SystemTest")]
    [Timeout(300_000)]
    public async Task InventoryCommand_SystemTest_AdoSingleProject_ScenarioFile_ExecutesSuccessfully()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT environment variables must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var csvPath = Path.Combine(CliRunner.FindRepoRoot(), "output", "discovery-summary.csv");
        if (File.Exists(csvPath))
            File.Delete(csvPath);

        var result = await CliRunner.RunAsync(
            args: ["discovery", "inventory", "--config", "scenarios/inventory-ado-single-project.json"],
            timeout: TimeSpan.FromMinutes(4));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        Assert.IsFalse(result.TimedOut,
            "CLI timed out. The inventory is either hung or the organisation is very large.");

        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("Inventory complete", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message ('Inventory complete') not found in output.");

        Assert.IsTrue(File.Exists(csvPath),
            $"discovery-summary.csv was not created at {csvPath}");

        var csvLines = File.ReadAllLines(csvPath);
        Console.WriteLine($"CSV path   : {csvPath}");
        Console.WriteLine($"CSV lines  : {csvLines.Length}");

        Assert.IsTrue(csvLines.Length >= 2,
            $"discovery-summary.csv should have a header row and at least one data row, but has {csvLines.Length} line(s).");

        var header = csvLines[0];
        Assert.IsTrue(header.Contains("WorkItemsCount", StringComparison.OrdinalIgnoreCase),
            $"CSV header does not contain 'WorkItemsCount'. Header: {header}");
    }
}
