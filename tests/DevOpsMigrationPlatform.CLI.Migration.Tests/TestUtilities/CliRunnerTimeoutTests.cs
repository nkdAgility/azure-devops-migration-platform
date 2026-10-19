// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Guards #180 end to end: when <see cref="CliRunner"/>'s own timeout fires, the CLI and everything it
/// launched (ControlPlaneHost, MigrationAgent, TFS agent) must have exited while the test host is
/// still running, not only when the host exits.
/// </summary>
[TestClass]
[DoNotParallelize]
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
public class CliRunnerTimeoutTests
{
    private static readonly string[] LaunchedProcessNames =
    [
        "devopsmigration",
        "DevOpsMigrationPlatform.ControlPlaneHost",
        "DevOpsMigrationPlatform.MigrationAgent",
        "tfsmigration",
    ];

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [OSCondition(OperatingSystems.Windows)]
    [TestMethod]
    [Timeout(60_000)]
    public async Task RunAsync_WhenTimeoutFires_KillsCliAndEveryLaunchedProcess()
    {
        var startedAfter = DateTime.Now.AddSeconds(-1);

        // A short, deterministic runner timeout: the standalone stack is still starting (or the job
        // is running), so the timeout branch is guaranteed to execute.
        var result = await CliRunner.RunAsync(
            ["queue", "--config", "scenarios/SystemTest-Simulated-Export-WorkItems.json", "--force-fresh"],
            timeout: TimeSpan.FromSeconds(4));

        Assert.IsTrue(result.TimedOut, "The runner timeout should have fired for this test to be meaningful.");

        var survivors = LaunchedProcessNames
            .SelectMany(Process.GetProcessesByName)
            .Where(p => StartedAfter(p, startedAfter) && !p.WaitForExit(5_000))
            .Select(p => $"{p.ProcessName} ({p.Id})")
            .ToList();

        Assert.AreEqual(0, survivors.Count,
            "Processes launched by the timed-out CLI are still running: " + string.Join(", ", survivors));
    }

    private static bool StartedAfter(Process process, DateTime threshold)
    {
        try
        {
            return process.StartTime >= threshold;
        }
        catch (InvalidOperationException)
        {
            return false;   // already exited
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;   // not accessible: not one of ours
        }
    }
}
