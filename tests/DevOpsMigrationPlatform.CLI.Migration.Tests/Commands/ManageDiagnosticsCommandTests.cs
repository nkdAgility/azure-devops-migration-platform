using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.Commands;

[TestClass]
[DoNotParallelize]
public class ManageDiagnosticsCommandTests
{
    // ── Unit tests ────────────────────────────────────────────────────────────────

    // ── System tests ─────────────────────────────────────────────────────────

    /// <summary>
    /// Starts the control plane (or reuses a running instance), submits a real export job
    /// via the CLI, then calls <c>manage diagnostics --job &lt;id&gt;</c> as a subprocess and
    /// asserts the exit code and observable output.
    /// </summary>
    [TestMethod]
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [Timeout(240_000)] // 4 minutes — export + diagnostics query
    public async Task ManageDiagnosticsCommand_SystemTest_AfterExport_ExitsZero_AndShowsDiagnosticOutput()
    {
        // ── Start control plane (reuse if already running) ──────────────────────
        await using var controlPlane = await ControlPlaneHostRunner.FindOrStartAsync(
            readyTimeout: TimeSpan.FromSeconds(30));

        // ── Run export to create a completed job ─────────────────────────────
        // Set Environment:Type=Hosted so the CLI connects to the shared control plane
        // instead of starting its own LocalStackHost.
        var hostedEnv = new Dictionary<string, string>
        {
            ["MigrationPlatform__Environment__Type"] = "Hosted",
            ["MigrationPlatform__Environment__ControlPlane__BaseUrl"] = ControlPlaneHostRunner.DefaultUrl,
        };

        var exportResult = await CliRunner.RunAsync(
            args: ["queue", "--config", "scenarios/queue-export-workitems-simulated-source.json", "--force-fresh"],
            env: hostedEnv,
            timeout: TimeSpan.FromMinutes(2));

        Console.WriteLine("=== EXPORT STDOUT ===");
        Console.WriteLine(exportResult.StandardOutput);
        if (!string.IsNullOrEmpty(exportResult.StandardError))
        {
            Console.WriteLine("=== EXPORT STDERR ===");
            Console.WriteLine(exportResult.StandardError);
        }

        Assert.IsFalse(exportResult.TimedOut, "Export CLI timed out.");
        Assert.AreEqual(0, exportResult.ExitCode,
            $"Export CLI exited with code {exportResult.ExitCode}.");

        // Parse the Job ID from the export output:
        // "  Job ID  : 505fbb5c-9e1a-430d-90e1-ca313dad21f3"
        var exportOutput = exportResult.StandardOutput + exportResult.StandardError;
        var jobIdMatch = Regex.Match(exportOutput,
            @"Job ID\s*:?\s*([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})",
            RegexOptions.IgnoreCase);
        Assert.IsTrue(jobIdMatch.Success,
            $"Could not find Job ID in export output. Output:\n{exportOutput}");
        var jobId = jobIdMatch.Groups[1].Value;
        Console.WriteLine($"Job ID: {jobId}");

        // ── Act — run manage diagnostics via CLI ────────────────────────────
        var result = await CliRunner.RunAsync(
            args: ["manage", "diagnostics", "--job", jobId, "--level", "Information"],
            env: hostedEnv,
            timeout: TimeSpan.FromMinutes(2));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        // ── Assert: process outcome ─────────────────────────────────────
        Assert.IsFalse(result.TimedOut, "manage diagnostics CLI timed out.");
        Assert.AreEqual(0, result.ExitCode,
            $"manage diagnostics exited with code {result.ExitCode}. Check STDOUT/STDERR above.");

        // The command either prints log records or a 'no logs' message.
        var combinedOutput = result.StandardOutput + result.StandardError;
        var hasOutput = combinedOutput.Contains("diagnostic", StringComparison.OrdinalIgnoreCase) ||
                        combinedOutput.Contains("log", StringComparison.OrdinalIgnoreCase) ||
                        combinedOutput.Contains("No ", StringComparison.OrdinalIgnoreCase) ||
                        combinedOutput.Length > 50;

        Console.WriteLine($"Output length: {combinedOutput.Length} chars");
        Assert.IsTrue(hasOutput,
            $"Expected some output from manage diagnostics. Got:\n{combinedOutput}");
    }
}
