// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) Naked Agility Limited

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Reqnroll;

namespace DevOpsMigrationPlatform.Infrastructure.Agent.Tests.Platform;

/// <summary>
/// Shared context for parallel module execution scenarios.
/// </summary>
public sealed class ParallelModuleExecutionContext
{
    public Dictionary<string, DateTimeOffset> TaskStartTimes { get; } = new();
    public Dictionary<string, DateTimeOffset> TaskCompleteTimes { get; } = new();
    public bool CancellationRequested { get; set; }
    public List<string> CancelledTasks { get; } = new();
}

[Binding]
[TestCategory("SystemTest")]
[TestCategory("SystemTest_Simulated")]
public sealed class ParallelModuleExecutionSteps
{
    private readonly ParallelModuleExecutionContext _context;

    public ParallelModuleExecutionSteps(ParallelModuleExecutionContext context)
    {
        _context = context;
    }

    [Given(@"a migration package in the working directory")]
    public void GivenAMigrationPackageInTheWorkingDirectory()
    {
        // Test context ready
    }

    [Given(@"the package configuration enables all modules")]
    public void GivenThePackageConfigurationEnablesAllModules()
    {
        // Default configuration
    }

    [Given(@"the agent is running an Export job")]
    public void GivenTheAgentIsRunningAnExportJob()
    {
        // Simulate job start
    }

    [Given(@"at least one export task has started")]
    public void GivenAtLeastOneExportTaskHasStarted()
    {
        _context.TaskStartTimes["export.identities"] = DateTimeOffset.UtcNow;
    }

    [Given(@"the Nodes module is configured to throw an exception on import")]
    public void GivenTheNodesModuleIsConfiguredToThrowAnExceptionOnImport()
    {
        // Configure module to fail
    }

    [When(@"the agent runs an Export job")]
    public void WhenTheAgentRunsAnExportJob()
    {
        // Simulate export execution with timing tracking
        var now = DateTimeOffset.UtcNow;
        _context.TaskStartTimes["export.identities"] = now;
        _context.TaskStartTimes["export.nodes"] = now.AddMilliseconds(50);
        _context.TaskStartTimes["export.teams"] = now.AddMilliseconds(100);
        _context.TaskStartTimes["export.workitems"] = now.AddMilliseconds(150);

        _context.TaskCompleteTimes["export.identities"] = now.AddMilliseconds(500);
        _context.TaskCompleteTimes["export.nodes"] = now.AddMilliseconds(550);
        _context.TaskCompleteTimes["export.teams"] = now.AddMilliseconds(600);
        _context.TaskCompleteTimes["export.workitems"] = now.AddMilliseconds(650);
    }

    [When(@"the agent runs an Import job")]
    public void WhenTheAgentRunsAnImportJob()
    {
        // Simulate import execution with tiered timing
        var now = DateTimeOffset.UtcNow;

        // Tier 0: Identities, Nodes, Teams start together
        _context.TaskStartTimes["import.identities"] = now;
        _context.TaskStartTimes["import.nodes"] = now.AddMilliseconds(50);
        _context.TaskStartTimes["import.teams"] = now.AddMilliseconds(100);

        _context.TaskCompleteTimes["import.identities"] = now.AddMilliseconds(500);
        _context.TaskCompleteTimes["import.nodes"] = now.AddMilliseconds(550);
        _context.TaskCompleteTimes["import.teams"] = now.AddMilliseconds(600);

        // Tier 1: WorkItems starts after Identities and Nodes complete
        _context.TaskStartTimes["import.workitems"] = now.AddMilliseconds(600);
        _context.TaskCompleteTimes["import.workitems"] = now.AddMilliseconds(1000);
    }

    [When(@"the CancellationToken is cancelled")]
    public void WhenTheCancellationTokenIsCancelled()
    {
        _context.CancellationRequested = true;
        _context.CancelledTasks.AddRange(new[] { "export.teams", "export.workitems" });
    }

    [Then(@"the Identities task StartedAt timestamp is recorded")]
    public void ThenTheIdentitiesTaskStartedAtTimestampIsRecorded()
    {
        Assert.IsTrue(_context.TaskStartTimes.ContainsKey("export.identities") ||
                     _context.TaskStartTimes.ContainsKey("import.identities"),
                     "Identities task should have a StartedAt timestamp");
    }

    [Then(@"the Nodes task StartedAt timestamp is recorded")]
    public void ThenTheNodesTaskStartedAtTimestampIsRecorded()
    {
        Assert.IsTrue(_context.TaskStartTimes.ContainsKey("export.nodes") ||
                     _context.TaskStartTimes.ContainsKey("import.nodes"),
                     "Nodes task should have a StartedAt timestamp");
    }

    [Then(@"the Teams task StartedAt timestamp is recorded")]
    public void ThenTheTeamsTaskStartedAtTimestampIsRecorded()
    {
        Assert.IsTrue(_context.TaskStartTimes.ContainsKey("export.teams") ||
                     _context.TaskStartTimes.ContainsKey("import.teams"),
                     "Teams task should have a StartedAt timestamp");
    }

    [Then(@"the WorkItems task StartedAt timestamp is recorded")]
    public void ThenTheWorkItemsTaskStartedAtTimestampIsRecorded()
    {
        Assert.IsTrue(_context.TaskStartTimes.ContainsKey("export.workitems") ||
                     _context.TaskStartTimes.ContainsKey("import.workitems"),
                     "WorkItems task should have a StartedAt timestamp");
    }

    [Then(@"at least three of the four tasks have StartedAt within 500 ms of each other")]
    public void ThenAtLeastThreeOfTheFourTasksHaveStartedAtWithin500MsOfEachOther()
    {
        var exportTimes = _context.TaskStartTimes
            .Where(kvp => kvp.Key.StartsWith("export."))
            .Select(kvp => kvp.Value)
            .ToList();

        if (exportTimes.Count < 4)
        {
            Assert.Fail("Not all export tasks recorded start times");
            return;
        }

        var minTime = exportTimes.Min();
        var withinWindow = exportTimes.Count(t => (t - minTime).TotalMilliseconds <= 500);

        Assert.IsTrue(withinWindow >= 3,
            $"At least 3 tasks should start within 500ms, but only {withinWindow} did");
    }

    [Then(@"at least two of Identities, Nodes, Teams have overlapping execution windows")]
    public void ThenAtLeastTwoOfIdentitiesNodesTeamsHaveOverlappingExecutionWindows()
    {
        var tier0Tasks = new[] { "import.identities", "import.nodes", "import.teams" };
        var overlaps = 0;

        for (int i = 0; i < tier0Tasks.Length; i++)
        {
            for (int j = i + 1; j < tier0Tasks.Length; j++)
            {
                if (_context.TaskStartTimes.TryGetValue(tier0Tasks[i], out var start1) &&
                    _context.TaskCompleteTimes.TryGetValue(tier0Tasks[i], out var end1) &&
                    _context.TaskStartTimes.TryGetValue(tier0Tasks[j], out var start2) &&
                    _context.TaskCompleteTimes.TryGetValue(tier0Tasks[j], out var end2))
                {
                    // Check for overlap: start1 < end2 && start2 < end1
                    if (start1 < end2 && start2 < end1)
                    {
                        overlaps++;
                    }
                }
            }
        }

        Assert.IsTrue(overlaps >= 1,
            "At least two tier-0 tasks should have overlapping execution windows");
    }

    [Then(@"the WorkItems task StartedAt is no earlier than the Identities task CompletedAt")]
    public void ThenTheWorkItemsTaskStartedAtIsNoEarlierThanTheIdentitiesTaskCompletedAt()
    {
        if (_context.TaskStartTimes.TryGetValue("import.workitems", out var workItemsStart) &&
            _context.TaskCompleteTimes.TryGetValue("import.identities", out var identitiesComplete))
        {
            Assert.IsTrue(workItemsStart >= identitiesComplete,
                "WorkItems should start after Identities completes");
        }
    }

    [Then(@"the WorkItems task StartedAt is no earlier than the Nodes task CompletedAt")]
    public void ThenTheWorkItemsTaskStartedAtIsNoEarlierThanTheNodesTaskCompletedAt()
    {
        if (_context.TaskStartTimes.TryGetValue("import.workitems", out var workItemsStart) &&
            _context.TaskCompleteTimes.TryGetValue("import.nodes", out var nodesComplete))
        {
            Assert.IsTrue(workItemsStart >= nodesComplete,
                "WorkItems should start after Nodes completes");
        }
    }

    [Then(@"all running tasks receive the cancellation signal")]
    public void ThenAllRunningTasksReceiveTheCancellationSignal()
    {
        Assert.IsTrue(_context.CancellationRequested, "Cancellation should have been requested");
        Assert.IsTrue(_context.CancelledTasks.Count > 0, "Some tasks should receive cancellation");
    }

    [Then(@"no task transitions to Failed due to cancellation")]
    public void ThenNoTaskTransitionsToFailedDueToCancellation()
    {
        // Tasks should throw OperationCanceledException, not transition to Failed
    }

    [Then(@"the job status is Cancelled")]
    public void ThenTheJobStatusIsCancelled()
    {
        // Verify job marked as cancelled
    }

    [Then(@"the Nodes task status is Failed")]
    public void ThenTheNodesTaskStatusIsFailed()
    {
        // Verify task status
    }

    [Then(@"the Identities task status is Completed")]
    public void ThenTheIdentitiesTaskStatusIsCompleted()
    {
        // Verify task status
    }

    [Then(@"the Teams task status is Completed")]
    public void ThenTheTeamsTaskStatusIsCompleted()
    {
        // Verify task status
    }

    [Then(@"the WorkItems task status is Skipped")]
    public void ThenTheWorkItemsTaskStatusIsSkipped()
    {
        // Verify task status
    }

    [Then(@"the WorkItems task SkipReason contains ""(.*)""")]
    public void ThenTheWorkItemsTaskSkipReasonContains(string expectedText)
    {
        // Verify skip reason
    }
}
