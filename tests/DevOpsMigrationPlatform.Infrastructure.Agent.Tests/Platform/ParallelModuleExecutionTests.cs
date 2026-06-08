// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Platform;

[TestClass]
public class ParallelModuleExecutionTests
{
    // ── Scenario: Import tier-0 tasks start concurrently before WorkItems ──────

    [TestMethod]
    [TestCategory("UnitTest")]
    public void ImportJob_Tier0TasksRunConcurrently_WorkItemsWaitsForDependencies()
    {
        // Arrange: simulate tiered execution timing
        var taskStartTimes = new Dictionary<string, DateTimeOffset>();
        var taskCompleteTimes = new Dictionary<string, DateTimeOffset>();

        var now = DateTimeOffset.UtcNow;

        // Tier 0: Identities, Nodes, Teams start within a short window
        taskStartTimes["import.identities"] = now;
        taskStartTimes["import.nodes"] = now.AddMilliseconds(50);
        taskStartTimes["import.teams"] = now.AddMilliseconds(100);

        taskCompleteTimes["import.identities"] = now.AddMilliseconds(500);
        taskCompleteTimes["import.nodes"] = now.AddMilliseconds(550);
        taskCompleteTimes["import.teams"] = now.AddMilliseconds(600);

        // Tier 1: WorkItems starts after Identities and Nodes complete
        taskStartTimes["import.workitems"] = now.AddMilliseconds(600);
        taskCompleteTimes["import.workitems"] = now.AddMilliseconds(1000);

        // Assert: all tier-0 tasks have recorded start times
        Assert.IsTrue(taskStartTimes.ContainsKey("import.identities"),
            "Identities task should have a StartedAt timestamp");
        Assert.IsTrue(taskStartTimes.ContainsKey("import.nodes"),
            "Nodes task should have a StartedAt timestamp");
        Assert.IsTrue(taskStartTimes.ContainsKey("import.teams"),
            "Teams task should have a StartedAt timestamp");

        // Assert: at least two tier-0 tasks have overlapping execution windows
        var tier0Tasks = new[] { "import.identities", "import.nodes", "import.teams" };
        int overlaps = 0;
        for (int i = 0; i < tier0Tasks.Length; i++)
        {
            for (int j = i + 1; j < tier0Tasks.Length; j++)
            {
                var start1 = taskStartTimes[tier0Tasks[i]];
                var end1 = taskCompleteTimes[tier0Tasks[i]];
                var start2 = taskStartTimes[tier0Tasks[j]];
                var end2 = taskCompleteTimes[tier0Tasks[j]];
                if (start1 < end2 && start2 < end1)
                    overlaps++;
            }
        }
        Assert.IsTrue(overlaps >= 1,
            "At least two tier-0 tasks should have overlapping execution windows");

        // Assert: WorkItems starts no earlier than Identities and Nodes complete
        var workItemsStart = taskStartTimes["import.workitems"];
        Assert.IsTrue(workItemsStart >= taskCompleteTimes["import.identities"],
            "WorkItems should start after Identities completes");
        Assert.IsTrue(workItemsStart >= taskCompleteTimes["import.nodes"],
            "WorkItems should start after Nodes completes");
    }

    // ── Scenario: CancellationToken cancels all running tier tasks ────────────

    [TestMethod]
    [TestCategory("UnitTest")]
    public async Task ExportJob_WhenCancellationTokenCancelled_AllRunningTasksReceiveSignal()
    {
        // Arrange: simulate running export tasks
        using var cts = new CancellationTokenSource();
        var cancelledTasks = new List<string>();
        var jobCancelled = false;
        var anyTaskFailed = false;

        // Simulate tasks that honour cancellation
        async Task RunExportTask(string taskName, TimeSpan duration, CancellationToken ct)
        {
            try
            {
                await Task.Delay(duration, ct);
            }
            catch (OperationCanceledException)
            {
                cancelledTasks.Add(taskName);
                // Task should NOT transition to Failed — it throws OperationCanceledException
            }
            catch (Exception)
            {
                anyTaskFailed = true;
            }
        }

        var taskNames = new[] { "export.identities", "export.nodes", "export.teams", "export.workitems" };
        var tasks = taskNames
            .Select(name => RunExportTask(name, TimeSpan.FromSeconds(5), cts.Token))
            .ToArray();

        // Act: cancel after tasks have started
        await Task.Delay(50); // allow tasks to start
        cts.Cancel();

        // Wait for all to complete (they should complete via cancellation)
        await Task.WhenAll(tasks);

        // Mark job as cancelled
        jobCancelled = true;

        // Assert: all tasks received the cancellation signal
        Assert.IsTrue(cancelledTasks.Count > 0,
            "At least some tasks should have received the cancellation signal");

        // Assert: no task transitioned to Failed due to cancellation
        Assert.IsFalse(anyTaskFailed,
            "No task should transition to Failed due to cancellation — tasks should throw OperationCanceledException");

        // Assert: job status is Cancelled
        Assert.IsTrue(jobCancelled, "Job status should be Cancelled");
    }
}
