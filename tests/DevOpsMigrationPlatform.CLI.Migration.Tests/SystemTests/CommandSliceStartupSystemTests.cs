// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Threading.Tasks;
using DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.SystemTests;

/// <summary>
/// VS-M2: CLI-level system tests for the command families wired in Program.cs that
/// previously had no SystemTest coverage: <c>config</c>, <c>controlplane start</c>,
/// and <c>agent start</c>. Each test invokes the real CLI entry point as a subprocess
/// (the same code path as production) and asserts observable output proving the
/// command slice is registered and reachable.
/// </summary>
[TestClass]
[DoNotParallelize]
public sealed class CommandSliceStartupSystemTests
{
    private static async Task<CliRunner.TestCliResult> RunAsync(string testName, string[] args)
    {
        var result = await CliRunner.RunTestAsync(
            testName: testName,
            args: args,
            timeout: TimeSpan.FromMinutes(2));

        Console.WriteLine("=== STDOUT ===");
        Console.WriteLine(result.StandardOutput);
        if (!string.IsNullOrEmpty(result.StandardError))
        {
            Console.WriteLine("=== STDERR ===");
            Console.WriteLine(result.StandardError);
        }

        Assert.IsFalse(result.TimedOut, "CLI timed out.");
        return result;
    }

    // ── config command family ───────────────────────────────────────────────

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Config_Help_ListsNewSetAndGetSubcommands()
    {
        var result = await RunAsync(
            nameof(Config_Help_ListsNewSetAndGetSubcommands),
            ["config", "--help"]);

        Assert.AreEqual(0, result.ExitCode,
            $"'config --help' exited with {result.ExitCode}.\nSTDERR:\n{result.StandardError}");
        StringAssert.Contains(result.StandardOutput, "new",
            "'config --help' must list the 'new' subcommand.");
        StringAssert.Contains(result.StandardOutput, "set",
            "'config --help' must list the 'set' subcommand.");
        StringAssert.Contains(result.StandardOutput, "get",
            "'config --help' must list the 'get' subcommand.");
    }

    // Note: 'config set' is deliberately not exercised here — the subprocess writes
    // to the real %APPDATA% preferences.json (UserPreferencesService.OverridePreferencesDirectory
    // is an internal static hook that cannot be reached across the process boundary),
    // so a set/get round-trip would mutate developer machine state.
    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Config_Get_KnownPreferenceKey_ExecutesSuccessfully()
    {
        var result = await RunAsync(
            nameof(Config_Get_KnownPreferenceKey_ExecutesSuccessfully),
            ["config", "get", "scenario-folder"]);

        Assert.AreEqual(0, result.ExitCode,
            $"'config get scenario-folder' exited with {result.ExitCode}.\nSTDERR:\n{result.StandardError}");
        StringAssert.Contains(result.StandardOutput, "scenario-folder",
            "'config get scenario-folder' must produce observable output naming the requested key.");
    }

    // ── controlplane command family ─────────────────────────────────────────

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task ControlPlane_Help_ListsStartSubcommand()
    {
        var result = await RunAsync(
            nameof(ControlPlane_Help_ListsStartSubcommand),
            ["controlplane", "--help"]);

        Assert.AreEqual(0, result.ExitCode,
            $"'controlplane --help' exited with {result.ExitCode}.\nSTDERR:\n{result.StandardError}");
        StringAssert.Contains(result.StandardOutput, "start",
            "'controlplane --help' must list the 'start' subcommand.");
        StringAssert.Contains(result.StandardOutput, "Control Plane",
            "'controlplane --help' must describe the Control Plane host branch.");
    }

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task ControlPlaneStart_Help_DocumentsUrlOption()
    {
        var result = await RunAsync(
            nameof(ControlPlaneStart_Help_DocumentsUrlOption),
            ["controlplane", "start", "--help"]);

        Assert.AreEqual(0, result.ExitCode,
            $"'controlplane start --help' exited with {result.ExitCode}.\nSTDERR:\n{result.StandardError}");
        StringAssert.Contains(result.StandardOutput, "--url",
            "'controlplane start --help' must document the --url option.");
    }

    // ── agent command family ────────────────────────────────────────────────

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task Agent_Help_ListsStartSubcommand()
    {
        var result = await RunAsync(
            nameof(Agent_Help_ListsStartSubcommand),
            ["agent", "--help"]);

        Assert.AreEqual(0, result.ExitCode,
            $"'agent --help' exited with {result.ExitCode}.\nSTDERR:\n{result.StandardError}");
        StringAssert.Contains(result.StandardOutput, "start",
            "'agent --help' must list the 'start' subcommand.");
        StringAssert.Contains(result.StandardOutput, "Migration Agent",
            "'agent --help' must describe the Migration Agent branch.");
    }

    [TestCategory("SystemTest")]
    [TestCategory("SystemTest_Simulated")]
    [TestMethod]
    public async Task AgentStart_Help_DocumentsUrlOption()
    {
        var result = await RunAsync(
            nameof(AgentStart_Help_DocumentsUrlOption),
            ["agent", "start", "--help"]);

        Assert.AreEqual(0, result.ExitCode,
            $"'agent start --help' exited with {result.ExitCode}.\nSTDERR:\n{result.StandardError}");
        StringAssert.Contains(result.StandardOutput, "--url",
            "'agent start --help' must document the --url option.");
    }
}
