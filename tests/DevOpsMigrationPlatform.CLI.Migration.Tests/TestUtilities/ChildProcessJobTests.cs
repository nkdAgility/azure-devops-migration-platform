// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.CLI.Migration.Tests.TestUtilities;

/// <summary>
/// Guards #180: a CLI started by a system test must never outlive the test host, even when MSTest
/// abandons a timed-out test method and <see cref="CliRunner"/>'s own cleanup never runs.
/// </summary>
[TestClass]
[TestCategory("CodeTest")]
[TestCategory("IntegrationTests")]
public class ChildProcessJobTests
{
    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [OSCondition(OperatingSystems.Windows)]
    [TestMethod]
    public async Task Dispose_KillsTrackedProcessAndItsDescendants()
    {
        // Parent PowerShell starts a grandchild (ping) and prints its PID, then waits.
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            ArgumentList =
            {
                "-NoProfile", "-NonInteractive", "-Command",
                "$c = Start-Process ping -ArgumentList '-n','120','127.0.0.1' -PassThru -WindowStyle Hidden; $c.Id; Start-Sleep -Seconds 120",
            },
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var parent = Process.Start(psi)!;
        var job = new ChildProcessJob();
        job.Add(parent);

        var childPidLine = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        using var child = Process.GetProcessById(int.Parse(childPidLine!.Trim()));

        job.Dispose();

        Assert.IsTrue(parent.WaitForExit(10_000), "Parent process should be killed when the job is disposed.");
        Assert.IsTrue(child.WaitForExit(10_000), "Descendant process should be killed when the job is disposed.");
    }
}
