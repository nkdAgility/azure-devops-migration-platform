// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) NKD Agility Limited

using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using DevOpsMigrationPlatform.CLI.Commands.Discovery;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands.Discovery;

[TestClass]
[DoNotParallelize]
public class DependencyCommandTests
{
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [Timeout(300_000)]
    public async Task DependencyCommand_SystemTest_AdoSingleProject_ExecutesSuccessfully()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG")) ||
            string.IsNullOrEmpty(Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT")))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT environment variables must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var repoRoot = CliRunner.FindRepoRoot();
        var scenarioPath = Path.Combine(repoRoot, "scenarios", "discovery-dependency-ado-single-project.json");

        if (!File.Exists(scenarioPath))
        {
            Assert.Fail($"Scenario file not found: {scenarioPath}");
        }

        var outputDir = Path.Combine(repoRoot, "output");
        Directory.CreateDirectory(outputDir);

        var result = await CliRunner.RunTestAsync(
            testName: nameof(DependencyCommand_SystemTest_AdoSingleProject_ExecutesSuccessfully),
            args: ["discovery", "dependencies", "--config", "scenarios/discovery-dependency-ado-single-project.json", "--output", outputDir],
            timeout: TimeSpan.FromMinutes(4));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        Assert.IsFalse(result.TimedOut,
            "CLI timed out. The dependency discovery is either hung or the organisation is very large.");

        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("complete", StringComparison.OrdinalIgnoreCase),
            "Expected CLI success message not found in output.");

        // Validate that if dependencies exist, we see both cross-project and cross-org
        // The scenario is configured to produce: 1 cross-project and 1 cross-org link from migrationTest5
        var hasCrossProjectLinks = combinedOutput.Contains("Cross-Project Links", StringComparison.OrdinalIgnoreCase);
        var hasCrossOrgLinks = combinedOutput.Contains("Cross-Organisation Links", StringComparison.OrdinalIgnoreCase);

        if (hasCrossProjectLinks || hasCrossOrgLinks)
        {
            // If we have any dependencies, we should have both types based on the scenario
            Assert.IsTrue(
                hasCrossProjectLinks && hasCrossOrgLinks,
                "Output should contain both cross-project and cross-organisation dependency summary. " +
                "migrationTest5 is configured to have exactly 1 cross-project and 1 cross-organisation link.");
        }

        var depsPath = Path.Combine(outputDir, "dependencies.csv");
        Assert.IsTrue(File.Exists(depsPath),
            $"dependencies.csv was not created at {depsPath}");

        var csvLines = File.ReadAllLines(depsPath);
        Console.WriteLine($"CSV path   : {depsPath}");
        Console.WriteLine($"CSV lines  : {csvLines.Length}");

        Assert.IsTrue(csvLines.Length >= 1,
            $"dependencies.csv should have at least a header row, but has {csvLines.Length} line(s).");

        var header = csvLines[0];
        Assert.IsTrue(header.Contains("SourceProject", StringComparison.OrdinalIgnoreCase),
            $"CSV header does not contain 'SourceProject'. Header: {header}");

        // The scenario MUST discover exactly 2 dependencies: 1 cross-project and 1 cross-org from migrationTest5
        Assert.IsTrue(csvLines.Length >= 3,  // Header + at least 2 data rows
            $"FAILED: Expected to discover at least 2 dependencies (1 cross-project + 1 cross-org) from migrationTest5, " +
            $"but found only {csvLines.Length - 1} data row(s). " +
            $"migrationTest5 should have exactly 1 cross-project and 1 cross-organisation link. " +
            $"CSV content: {string.Join("; ", csvLines)}");
    }
}
