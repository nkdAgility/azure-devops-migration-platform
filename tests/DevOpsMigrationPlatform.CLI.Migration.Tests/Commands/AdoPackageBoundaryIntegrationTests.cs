// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Linq;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
[DoNotParallelize]
public class AdoPackageBoundaryIntegrationTests
{
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    [Timeout(1_200_000)] // 20 minutes
    public async Task Queue_Export_ADO_WritesAuthoritativeAndProjectScopedPackageState()
    {
        var orgEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_ORG");
        var patEnv = Environment.GetEnvironmentVariable("AZDEVOPS_SYSTEM_TEST_PAT");
        if (string.IsNullOrEmpty(orgEnv) || string.IsNullOrEmpty(patEnv))
        {
            Assert.Fail(
                "System test skipped: AZDEVOPS_SYSTEM_TEST_ORG and AZDEVOPS_SYSTEM_TEST_PAT must be set. " +
                "See docs/contributors.md for setup instructions.");
            return;
        }

        var result = await CliRunner.RunTestAsync(
            testName: nameof(Queue_Export_ADO_WritesAuthoritativeAndProjectScopedPackageState),
            args: ["queue", "--config", "scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-SingleProject.json", "--force-fresh"],
            timeout: TimeSpan.FromMinutes(18),
            cleanOutputFolder: true);
        var outputDir = result.OutputDirectory;

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. STDOUT:\n{result.StandardOutput}\nSTDERR:\n{result.StandardError}");

        var rootMigrationConfig = Directory.GetFiles(outputDir, "migration-config.json", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                path.Contains($"{Path.DirectorySeparatorChar}.migration{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(rootMigrationConfig, "Expected root authoritative .migration/migration-config.json.");

        var rootPlan = Directory.GetFiles(outputDir, "plan.json", SearchOption.AllDirectories)
            .FirstOrDefault(path =>
                path.Contains($"{Path.DirectorySeparatorChar}.migration{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase) &&
                !path.Contains($"{Path.DirectorySeparatorChar}runs{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase));
        Assert.IsNotNull(rootPlan, "Expected root authoritative .migration/plan.json.");

        var projectCursorFiles = Directory.GetFiles(outputDir, "export.workitems.cursor.json", SearchOption.AllDirectories);
        Assert.IsTrue(projectCursorFiles.Length > 0,
            $"Expected project-scoped export.workitems.cursor.json under <org>/<project>/.migration somewhere in {outputDir}.");
    }
}
