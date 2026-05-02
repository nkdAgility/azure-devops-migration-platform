// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System;
using System.IO;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands.Discovery;

[TestClass]
[DoNotParallelize]
public class InventoryCommandTests
{
    // -- Unit tests --

    // -- System tests --

    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
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

        var result = await CliRunner.RunTestAsync(
            testName: nameof(InventoryCommand_SystemTest_AdoSingleProject_ScenarioFile_ExecutesSuccessfully),
            args: ["discovery", "inventory", "--config", "scenarios/inventory-ado-single-project.json"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;
        var csvPath = Path.Combine(outputDir, "inventory.csv");

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
            $"inventory.csv was not created at {csvPath}");

        var csvLines = File.ReadAllLines(csvPath);
        Console.WriteLine($"CSV path   : {csvPath}");
        Console.WriteLine($"CSV lines  : {csvLines.Length}");

        Assert.IsTrue(csvLines.Length >= 2,
            $"inventory.csv should have a header row and at least one data row, but has {csvLines.Length} line(s).");

        var header = csvLines[0];
        Assert.IsTrue(header.Contains("WorkItemsCount", StringComparison.OrdinalIgnoreCase),
            $"CSV header does not contain 'WorkItemsCount'. Header: {header}");
    }

    // ─────────────────────────────────────────────────────────────────────
    // T021: CI environment executes securely
    // ─────────────────────────────────────────────────────────────────────

    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [Timeout(300_000)]
    public async Task InventoryCommand_SystemTest_CIEnvironment_ExecutesSecurely()
    {
        var org = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var pat = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");

        if (string.IsNullOrEmpty(org) || string.IsNullOrEmpty(pat))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT required. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var result = await CliRunner.RunTestAsync(
            testName: nameof(InventoryCommand_SystemTest_CIEnvironment_ExecutesSecurely),
            args: ["discovery", "inventory", "--config", "scenarios/inventory-ado-single-project.json"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);

        // T023: Verify no credentials appear in output
        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsFalse(combinedOutput.Contains(pat),
            "PAT value must never appear in test output.");
        Assert.IsFalse(combinedOutput.Contains("Bearer " + pat, StringComparison.OrdinalIgnoreCase),
            "Bearer tokens must not appear in output.");

        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode} in CI environment.");
    }

}
