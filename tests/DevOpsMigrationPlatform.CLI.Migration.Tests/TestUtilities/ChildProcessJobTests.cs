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
        using var job = new ChildProcessJob();   // closed on every path, including a failed PID read
        job.Add(parent);

        var childPidLine = await parent.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20));
        using var child = Process.GetProcessById(int.Parse(childPidLine!.Trim()));

        job.Dispose();

        Assert.IsTrue(parent.WaitForExit(10_000), "Parent process should be killed when the job is disposed.");
        Assert.IsTrue(child.WaitForExit(10_000), "Descendant process should be killed when the job is disposed.");
    }

    [TestCategory("CodeTest")]
    [TestCategory("IntegrationTests")]
    [OSCondition(OperatingSystems.Windows)]
    [TestMethod]
    public void AddCurrentProcess_ProcessesStartedAfterwardsAreMembersFromBirth()
    {
        // CliRunner puts the test host itself in the job, so there is no window between Process.Start
        // and job assignment in which the CLI could spawn a child outside the job.
        // The job is deliberately NOT disposed here: disposing a job that contains the test host would
        // kill it. Its handle closes when the host exits, exactly like CliRunner's job.
        var job = new ChildProcessJob();
        job.AddCurrentProcess();

        using var child = Process.Start(new ProcessStartInfo("ping", "-n 30 127.0.0.1") { UseShellExecute = false, CreateNoWindow = true })!;
        try
        {
            Assert.IsTrue(job.Contains(child), "A process started after the host joined the job must already be a member.");
        }
        finally
        {
            child.Kill();
        }
    }
}
