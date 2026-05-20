// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.IO;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

/// <summary>
/// Tests for the <c>prepare</c> command.
/// Prepare submits a lightweight probe job through the full pipeline
/// (CLI → Control Plane → Agent → ArtefactStore) to validate end-to-end connectivity.
/// </summary>
[TestClass]
[DoNotParallelize]
public class PrepareCommandTests
{
    // ── Unit tests ─────────────────────────────────────────────────────────

    // ── System tests ───────────────────────────────────────────────────────

    /// <summary>
    /// Runs <c>devopsmigration prepare --config scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-SingleProject.json</c>
    /// as a subprocess. The prepare command submits a probe job that writes a single file
    /// to the artefact store and completes.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Live")]
    public async Task Prepare_ADO_WritesProbeFile()
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

        var testStorage = Path.Combine(CliRunner.TestWorkingFolder, nameof(Prepare_ADO_WritesProbeFile));
        var outputDir = Path.Combine(CliRunner.FindRepoRoot(), testStorage);

        // ── Act ───────────────────────────────────────────────────────────
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Prepare_ADO_WritesProbeFile),
            args: ["prepare", "--config", "scenarios/SystemTest-Live-Export-AzureDevOps-WorkItems-SingleProject.json"],
            timeout: TimeSpan.FromMinutes(4),
            cleanOutputFolder: true);
        outputDir = result.OutputDirectory;

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert ────────────────────────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"CLI exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("Preparation check passed", StringComparison.OrdinalIgnoreCase),
            "Expected success message not found in output.");

        // Prepare now executes module-level preflight checks and may write
        // per-module reports rather than a single root probe marker.
    }

    /// <summary>
    /// Verifies simulated prepare runs end-to-end and returns success output.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    public async Task Prepare_Sim_ExitsZero()
    {
        var result = await CliRunner.RunTestAsync(
            testName: nameof(Prepare_Sim_ExitsZero),
            args: ["prepare", "--config", "scenarios/SystemTest-Simulated-Migrate-Roundtrip.json", "--force-fresh"],
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

        var combinedOutput = result.StandardOutput + result.StandardError;
        Assert.IsTrue(
            combinedOutput.Contains("Preparation check passed", StringComparison.OrdinalIgnoreCase),
            "Expected simulated prepare success message not found in output.");
    }
}
